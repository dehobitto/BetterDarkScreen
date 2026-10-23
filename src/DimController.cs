using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace BetterDarkScreen;

/// <summary>How a monitor is dimmed right now.</summary>
public enum Channel { Overlay, Gamma, Hardware }

/// <summary>Read-only view of one monitor's state for the UI.</summary>
internal sealed record MonitorState(
    DisplayInfo Display,
    MonitorMethod Requested,   // method asked for (global or per monitor)
    Channel Channel,           // method in use
    bool Fallback,             // requested method unsupported -> using overlay
    bool? HwSupported,         // null = still checking
    bool GammaSupported,
    bool GammaLimited);        // gamma can't reach the requested level (Windows cap)

/// <summary>
/// Owns overlays, gamma and hardware brightness for every display and drives them from settings,
/// schedule and pause state. UI thread only (hardware writes go to a worker).
/// Idle cost: one timer tick every 15-30 s.
/// </summary>
internal sealed class DimController : IDisposable
{
    private sealed class Slot
    {
        public required DisplayInfo Display;
        public OverlayWindow? Overlay;
        public double Current;   // 0..0.95 dim currently shown
        public double Target;    // 0..0.95 dim we animate toward
        public Channel Ch = Channel.Overlay;
        public bool ChSet;
        public MonitorMethod Requested;
        public bool Fallback;
        public bool GammaSupported = true;
        public bool GammaLimited;
    }

    private readonly Settings _s;
    private readonly List<Slot> _slots = new();
    private readonly HwBrightness _hw = new();
    private readonly Timer _tick = new();
    private readonly Timer _anim = new() { Interval = 16 };
    private readonly Timer _rebuild = new() { Interval = 700 };
    private readonly Native.WinEventDelegate _winEvent;
    private readonly IntPtr _hook;
    private DateTime _lastGammaKick;
    private bool _fullscreen;
    private IntPtr _fullscreenMonitor;
    private bool _gammaUnlocked = GammaRamp.IsRangeUnlocked();

    public DateTime? PausedUntil { get; private set; }
    public bool IsPaused => PausedUntil is { } p && p > DateTime.Now;
    public double ScheduleFactor { get; private set; }
    public IReadOnlyList<DisplayInfo> Displays => _slots.Select(s => s.Display).ToList();
    public bool FullscreenGame => _fullscreen;
    public bool GammaUnlocked => _gammaUnlocked;

    /// <summary>UI should refresh.</summary>
    public event Action? StateChanged;
    /// <summary>Settings were changed by the controller (hardware state) and should be saved.</summary>
    public event Action? Dirty;

    public DimController(Settings s)
    {
        _s = s;
        _tick.Tick += (_, _) => Update(animate: true);
        _anim.Tick += (_, _) => Animate();
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Rebuild(); };

        _hw.Probed += (_, _) => Update(animate: true);
        _hw.StateChanged += (id, normal, dimmed) =>
        {
            var ms = _s.For(id);
            if (ms.HwNormal == normal && ms.HwDimmed == dimmed) return;
            ms.HwNormal = normal; ms.HwDimmed = dimmed;
            Dirty?.Invoke();
        };

        _winEvent = OnWinEvent; // keep delegate alive
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEvent, 0, 0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.SessionSwitch += OnSession;

        Rebuild();
    }

    // ------------------------------------------------------------ public API

    public void Pause(TimeSpan? span)
    {
        PausedUntil = span is null ? DateTime.MaxValue : DateTime.Now + span.Value;
        Update(animate: true);
    }

    public void Resume()
    {
        PausedUntil = null;
        Update(animate: true);
    }

    /// <summary>Call after any settings change.</summary>
    public void Refresh(bool animate = true)
    {
        foreach (var sl in _slots)
            if (sl.Overlay != null) sl.Overlay.ExcludeFromCapture = _s.HideFromCapture;
        Update(animate);
    }

    /// <summary>Re-read the gamma unlock registry flag (after the user unlocked it).</summary>
    public void RecheckGammaUnlock() { _gammaUnlocked = GammaRamp.IsRangeUnlocked(); StateChanged?.Invoke(); }

    public MonitorState? State(string displayId)
    {
        var sl = _slots.FirstOrDefault(x => x.Display.Id == displayId);
        return sl == null ? null : ToState(sl);
    }

    public IReadOnlyList<MonitorState> States => _slots.Select(ToState).ToList();

    private MonitorState ToState(Slot sl) => new(sl.Display, sl.Requested, sl.Ch, sl.Fallback,
        _hw.IsSupported(sl.Display.Id), sl.GammaSupported, sl.GammaLimited);

    /// <summary>Would this level hit the gamma cap on this monitor (for warnings before it happens)?</summary>
    public bool GammaWouldLimit(string displayId, int level)
    {
        if (_gammaUnlocked) return false;
        var sl = _slots.FirstOrDefault(x => x.Display.Id == displayId);
        if (sl == null || sl.Ch != Channel.Gamma) return false;
        double floor = GammaRamp.KnownFloor(sl.Display.GdiName) ?? GammaRamp.DefaultFloor;
        return 1.0 - level / 100.0 < floor - 0.005;
    }

    // ------------------------------------------------------------ core

    private void Rebuild()
    {
        foreach (var sl in _slots) sl.Overlay?.Dispose();
        GammaRamp.Invalidate();
        _slots.Clear();

        var specs = new List<HwBrightness.DisplaySpec>();
        foreach (var d in BetterDarkScreen.Displays.Enumerate())
        {
            var ms = _s.For(d.Id);
            _slots.Add(new Slot { Display = d, GammaSupported = GammaRamp.Probe(d.GdiName) });
            specs.Add(new(d.Id, d.GdiName, d.Internal, ms.HwNormal, ms.HwDimmed));
        }
        _hw.SetDisplays(specs);
        Update(animate: false);
    }

    private MonitorMethod RequestedFor(Slot sl) => _s.Mode switch
    {
        DimMode.Overlay => MonitorMethod.Overlay,
        DimMode.Gamma => MonitorMethod.Gamma,
        DimMode.Hardware => MonitorMethod.Hardware,
        DimMode.Custom => _s.For(sl.Display.Id).Method,
        _ => MonitorMethod.Auto,
    };

    private Channel Resolve(Slot sl, out bool fallback)
    {
        fallback = false;
        sl.Requested = RequestedFor(sl);
        switch (sl.Requested)
        {
            case MonitorMethod.Gamma:
                if (sl.GammaSupported) return Channel.Gamma;
                fallback = true; return Channel.Overlay;
            case MonitorMethod.Hardware:
                // While probing (null) stay on overlay without flagging.
                var hw = _hw.IsSupported(sl.Display.Id);
                if (hw == true) return Channel.Hardware;
                fallback = hw == false; return Channel.Overlay;
            case MonitorMethod.Overlay:
                return Channel.Overlay;
            default: // Auto: gamma only for an exclusive-fullscreen game on this monitor
                return _fullscreen && sl.GammaSupported && MonitorOf(sl.Display) == _fullscreenMonitor ? Channel.Gamma : Channel.Overlay;
        }
    }

    private void Update(bool animate)
    {
        var now = DateTime.Now;
        if (PausedUntil is { } p && p <= now) PausedUntil = null;

        ScheduleFactor = Schedule.Factor(_s, now);
        bool active = _s.Enabled && !IsPaused;
        DetectFullscreen();

        bool anyDim = false, anyGamma = false;
        foreach (var sl in _slots)
        {
            var ms = _s.For(sl.Display.Id);
            double target = active && ms.Enabled ? Math.Clamp(ms.Level, 0, 95) / 100.0 * ScheduleFactor : 0;
            sl.Target = target;
            anyDim |= target > 0;

            var ch = Resolve(sl, out sl.Fallback);
            if (!sl.ChSet || ch != sl.Ch)
            {
                // Switch channel instantly: clear the old one, show current level on the new one.
                if (sl.ChSet) Clear(sl, sl.Ch);
                sl.Ch = ch; sl.ChSet = true;
                sl.GammaLimited = false;
                Push(sl, force: true);
            }
            if (!animate) { sl.Current = target; Push(sl, force: false); }
            anyGamma |= sl.Ch == Channel.Gamma && sl.Current > 0;
        }

        if (animate && _slots.Any(x => Math.Abs(x.Current - x.Target) > 0.0005)) _anim.Start();

        // Keep gamma alive: games and Windows reset it on mode switches.
        if (anyGamma && (now - _lastGammaKick).TotalSeconds >= 3)
        {
            _lastGammaKick = now;
            foreach (var sl in _slots.Where(x => x.Ch == Channel.Gamma && x.Current > 0)) Push(sl, force: true);
        }

        // Tick fast only when something can change soon.
        bool fading = _s.Schedule != ScheduleKind.Always && ScheduleFactor > 0 && ScheduleFactor < 1;
        int interval = anyGamma || fading || (anyDim && _s.Mode is DimMode.Auto or DimMode.Custom) ? 2000
                     : anyDim ? 5000 : 15000;
        if (_s.Schedule == ScheduleKind.Always && !anyDim && PausedUntil is null) interval = 30000;
        if (_tick.Interval != interval || !_tick.Enabled) { _tick.Interval = interval; _tick.Start(); }

        StateChanged?.Invoke();
    }

    private void Animate()
    {
        bool moving = false;
        foreach (var sl in _slots)
        {
            double diff = sl.Target - sl.Current;
            if (Math.Abs(diff) <= 0.004) sl.Current = sl.Target;
            else
            {
                sl.Current += diff * 0.22 + Math.Sign(diff) * 0.002; // ~250 ms ease
                moving = true;
            }
            Push(sl, force: false);
        }
        if (!moving) { _anim.Stop(); StateChanged?.Invoke(); }
    }

    private void Clear(Slot sl, Channel ch)
    {
        switch (ch)
        {
            case Channel.Overlay: sl.Overlay?.SetAlpha(0); break;
            case Channel.Gamma: GammaRamp.Reset(sl.Display.GdiName); break;
            case Channel.Hardware: _hw.SetDim(sl.Display.Id, 0); break;
        }
    }

    private void Push(Slot sl, bool force)
    {
        switch (sl.Ch)
        {
            case Channel.Gamma:
                if (sl.Current <= 0.0005) { GammaRamp.Reset(sl.Display.GdiName); sl.GammaLimited = false; }
                else
                {
                    double want = 1.0 - sl.Current;
                    double got = GammaRamp.Apply(sl.Display.GdiName, want, force);
                    bool limited = got > want + 0.01;
                    if (limited != sl.GammaLimited) { sl.GammaLimited = limited; StateChanged?.Invoke(); }
                }
                return;

            case Channel.Hardware:
                _hw.SetDim(sl.Display.Id, sl.Current);
                return;

            default:
                byte a = (byte)Math.Round(Math.Clamp(sl.Current, 0, 0.95) * 255);
                if (a > 0 && sl.Overlay == null)
                    sl.Overlay = new OverlayWindow(sl.Display.Bounds, _s.HideFromCapture); // lazy: no windows when unused
                sl.Overlay?.SetAlpha(a);
                return;
        }
    }

    private static IntPtr MonitorOf(DisplayInfo d)
    {
        foreach (var s in Screen.AllScreens)
            if (string.Equals(s.DeviceName, d.GdiName, StringComparison.OrdinalIgnoreCase))
                return Native.MonitorFromPoint(new System.Drawing.Point(s.Bounds.X + 1, s.Bounds.Y + 1), Native.MONITOR_DEFAULTTONEAREST);
        return IntPtr.Zero;
    }

    private void DetectFullscreen()
    {
        bool fs = Native.SHQueryUserNotificationState(out int st) == 0 && st == Native.QUNS_RUNNING_D3D_FULL_SCREEN;
        _fullscreen = fs;
        _fullscreenMonitor = fs ? Native.MonitorFromWindow(Native.GetForegroundWindow(), Native.MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;
    }

    // ------------------------------------------------------------ events

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        foreach (var sl in _slots) sl.Overlay?.BringToTop();
        bool wasFs = _fullscreen; var wasMon = _fullscreenMonitor;
        DetectFullscreen();
        if (wasFs != _fullscreen || wasMon != _fullscreenMonitor) Update(animate: true);
    }

    private void OnDisplayChanged(object? sender, EventArgs e) { _rebuild.Stop(); _rebuild.Start(); }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) { _rebuild.Stop(); _rebuild.Start(); }
    }

    private void OnSession(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        { _rebuild.Stop(); _rebuild.Start(); }
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.SessionSwitch -= OnSession;
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        _tick.Dispose(); _anim.Dispose(); _rebuild.Dispose();
        foreach (var sl in _slots) sl.Overlay?.Dispose();
        GammaRamp.ResetAll();
        _hw.Dispose(); // restores monitor brightness
        foreach (var ms in _s.Monitors.Values) ms.HwDimmed = false;
    }
}
