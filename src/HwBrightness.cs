namespace BetterDarkScreen;

/// <summary>
/// Monitor's own backlight ("hardware" / natural dimming):
/// DDC/CI for external monitors, the LCD brightness IOCTL for built-in panels.
/// DDC is slow (~50 ms per call) so everything runs on one background thread; requests are coalesced.
/// </summary>
internal sealed class HwBrightness : IDisposable
{
    public sealed record DisplaySpec(string Id, string GdiName, bool Internal, int SavedNormal, bool SavedDimmed);

    private sealed class Dev
    {
        public required string Id;
        public required string Gdi;
        public bool Internal;
        public IntPtr Phys;                 // DDC handle (one per display; first physical monitor)
        public Native.PHYSICAL_MONITOR[]? PhysArr;
        public byte[]? LcdLevels;           // built-in panel levels
        public bool? Supported;
        public uint Max = 100;
        public int Normal = -1;             // brightness to return to
        public bool Dimmed;                 // we currently hold it lowered
        public double WantDim;              // 0..0.95
        public int LastSet = -1;
        public bool Slow;                   // debounce + rate limit writes (fragile monitors)
        public long ChangedAt;              // Environment.TickCount64 of the last WantDim change
        public long WroteAt = long.MinValue / 2;
        public long RetryAt;                // >0: probe again at this TickCount64 (monitor didn't answer)
        public int ProbeTries;
    }

    /// <summary>Re-probe a monitor that didn't answer after 5 s, 15 s and 30 s (waking up, slow DDC).</summary>
    private static readonly int[] RetryDelays = { 5000, 10000, 15000 };
    private long _holdUntil;

    /// <summary>Slow mode: wait this long after the last change before writing (slider has stopped).</summary>
    private const int SlowSettleMs = 800;
    /// <summary>Slow mode: never write more often than this.</summary>
    private const int SlowMinGapMs = 1500;

    private readonly object _lock = new();
    private readonly Dictionary<string, Dev> _devs = new(StringComparer.OrdinalIgnoreCase);
    private List<DisplaySpec>? _pendingSpecs;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly SynchronizationContext _ui;
    private volatile bool _quit;

    /// <summary>(id, supported) after probing. Raised on UI thread.</summary>
    public event Action<string, bool>? Probed;
    /// <summary>(id, normal, dimmed) state to persist for crash recovery. Raised on UI thread.</summary>
    public event Action<string, int, bool>? StateChanged;

    public HwBrightness()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _thread = new Thread(Run) { IsBackground = true, Name = "HwBrightness", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public bool? IsSupported(string id) { lock (_lock) return _devs.TryGetValue(id, out var d) ? d.Supported : null; }

    /// <summary>Replace display set (after topology change). Re-probes; keeps normal/dimmed state per id.</summary>
    public void SetDisplays(IEnumerable<DisplaySpec> specs)
    {
        lock (_lock) _pendingSpecs = specs.ToList();
        _wake.Set();
    }

    public void SetDim(string id, double dim)
    {
        lock (_lock)
        {
            if (!_devs.TryGetValue(id, out var d)) return;
            if (Math.Abs(d.WantDim - dim) < 0.0001) return;
            d.WantDim = dim;
            d.ChangedAt = Environment.TickCount64;
        }
        _wake.Set();
    }

    /// <summary>Don't talk to any monitor for this long (e.g. right after resume, while monitors wake up).</summary>
    public void HoldOff(int ms)
    {
        Interlocked.Exchange(ref _holdUntil, Environment.TickCount64 + ms);
        _wake.Set();
    }

    private readonly Dictionary<string, bool> _slow = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Turn slow (debounced, rate-limited) writes on or off for one display.</summary>
    public void SetSlow(string id, bool slow)
    {
        lock (_lock)
        {
            if (_slow.TryGetValue(id, out var cur) && cur == slow) return;
            _slow[id] = slow;
            if (_devs.TryGetValue(id, out var d)) d.Slow = slow;
        }
        _wake.Set();
    }

    // ------------------------------------------------------------ worker

    private void Run()
    {
        int wait = Timeout.Infinite;
        while (!_quit)
        {
            _wake.WaitOne(wait);
            long hold = Interlocked.Read(ref _holdUntil) - Environment.TickCount64;
            if (hold > 0) { wait = (int)Math.Min(hold, int.MaxValue); continue; } // pending work runs after the hold

            List<DisplaySpec>? specs;
            lock (_lock) { specs = _pendingSpecs; _pendingSpecs = null; }
            if (specs != null) Reprobe(specs);
            RetryProbes();

            // Apply until nothing changes (coalesces fast slider moves).
            bool again = true;
            while (again && !_quit)
            {
                again = false;
                List<Dev> list;
                lock (_lock) list = _devs.Values.ToList();
                foreach (var d in list)
                    if (Step(d)) { again = true; Thread.Sleep(60); }
            }

            // Slow-mode writes and probe retries that are due later: wake up for them.
            wait = Timeout.Infinite;
            List<Dev> all;
            lock (_lock) all = _devs.Values.ToList();
            long now = Environment.TickCount64;
            foreach (var d in all)
            {
                int due = SlowDueIn(d);
                if (d.RetryAt > 0) { int r = (int)Math.Max(0, d.RetryAt - now); due = due < 0 ? r : Math.Min(due, r); }
                if (due >= 0 && (wait == Timeout.Infinite || due < wait)) wait = Math.Max(due, 20);
            }
        }
        RestoreAll();
    }

    private void Reprobe(List<DisplaySpec> specs)
    {
        Dictionary<string, Dev> old;
        lock (_lock) { old = new(_devs, StringComparer.OrdinalIgnoreCase); _devs.Clear(); }

        foreach (var sp in specs)
        {
            old.TryGetValue(sp.Id, out var prev);
            var d = new Dev { Id = sp.Id, Gdi = sp.GdiName, Internal = sp.Internal };
            lock (_lock) d.Slow = _slow.TryGetValue(sp.Id, out var slow) && slow;
            if (prev != null) { d.Normal = prev.Normal; d.Dimmed = prev.Dimmed; d.WantDim = prev.WantDim; }
            else if (sp.SavedDimmed && sp.SavedNormal >= 0) { d.Normal = sp.SavedNormal; d.Dimmed = true; } // crash recovery

            d.Supported = Open(d);
            if (d.Supported != true && !d.Internal) { d.ProbeTries = 0; d.RetryAt = Environment.TickCount64 + RetryDelays[0]; }
            lock (_lock) _devs[d.Id] = d;
            bool sup = d.Supported == true;
            _ui.Post(_ => Probed?.Invoke(sp.Id, sup), null);
        }
        foreach (var o in old.Values) Close(o);
    }

    /// <summary>Monitors that didn't answer the first probe get a few more chances before Hardware is given up.</summary>
    private void RetryProbes()
    {
        List<Dev> list;
        lock (_lock) list = _devs.Values.Where(d => d.RetryAt > 0 && Environment.TickCount64 >= d.RetryAt).ToList();
        foreach (var d in list)
        {
            Close(d);
            bool ok = Open(d);
            d.Supported = ok;
            d.ProbeTries++;
            d.RetryAt = ok || d.ProbeTries >= RetryDelays.Length ? 0 : Environment.TickCount64 + RetryDelays[d.ProbeTries];
            string id = d.Id;
            _ui.Post(_ => Probed?.Invoke(id, ok), null);
        }
    }

    private static bool Open(Dev d)
    {
        // Built-in laptop panel
        if (d.Internal && OpenLcd(d)) return true;

        IntPtr hmon = HMonitorOf(d.Gdi);
        if (hmon == IntPtr.Zero) return false;
        try
        {
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint n) || n == 0) return false;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hmon, n, arr)) return false;
            d.PhysArr = arr;
            d.Phys = arr[0].hPhysicalMonitor;
            for (int i = 0; i < 3; i++) // DDC is flaky; retry
            {
                if (Native.GetVCPFeatureAndVCPFeatureReply(d.Phys, Native.VCP_BRIGHTNESS, IntPtr.Zero, out uint cur, out uint max) && max > 0)
                {
                    d.Max = max;
                    if (!d.Dimmed) d.Normal = (int)cur;
                    return true;
                }
                Thread.Sleep(40);
            }
        }
        catch { }
        return false;
    }

    private static bool OpenLcd(Dev d)
    {
        IntPtr h = Native.CreateFile(@"\\.\LCD", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == Native.INVALID_HANDLE || h == IntPtr.Zero) return false;
        try
        {
            var levels = new byte[256];
            if (!Native.DeviceIoControl(h, Native.IOCTL_VIDEO_QUERY_SUPPORTED_BRIGHTNESS, null, 0, levels, levels.Length, out int n, IntPtr.Zero) || n == 0)
                return false;
            d.LcdLevels = levels.Take(n).Distinct().OrderBy(b => b).ToArray();
            var cur = new byte[3];
            if (Native.DeviceIoControl(h, Native.IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, null, 0, cur, 3, out _, IntPtr.Zero))
            {
                if (!d.Dimmed) d.Normal = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? cur[2] : cur[1];
            }
            d.Max = 100;
            return true;
        }
        finally { Native.CloseHandle(h); }
    }

    private static IntPtr HMonitorOf(string gdi)
    {
        foreach (var s in Screen.AllScreens)
            if (string.Equals(s.DeviceName, gdi, StringComparison.OrdinalIgnoreCase))
                return Native.MonitorFromPoint(new System.Drawing.Point(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2), Native.MONITOR_DEFAULTTONEAREST);
        return IntPtr.Zero;
    }

    /// <summary>Slow mode: ms until a pending write may happen (0 = now), or -1 if nothing is pending.</summary>
    private int SlowDueIn(Dev d)
    {
        double dim; long changed;
        lock (_lock) { if (!d.Slow) return -1; dim = d.WantDim; changed = d.ChangedAt; }
        if (d.Supported != true) return -1;
        bool pending = dim <= 0.0005 ? d.Dimmed : !d.Dimmed || TargetFor(d, dim) != d.LastSet;
        if (!pending) return -1;
        long now = Environment.TickCount64;
        long due = Math.Max(changed + SlowSettleMs, d.WroteAt + SlowMinGapMs);
        return (int)Math.Max(0, due - now);
    }

    private static int TargetFor(Dev d, double dim) => Math.Clamp((int)Math.Round(d.Normal * (1 - dim)), 0, (int)d.Max);

    /// <summary>Moves one device toward its wanted level. Returns true if it wrote something.</summary>
    private bool Step(Dev d)
    {
        if (d.Supported != true) return false;
        double dim;
        bool slow;
        lock (_lock) { dim = d.WantDim; slow = d.Slow; }
        if (slow && SlowDueIn(d) != 0) return false; // wait until the slider settles / rate limit

        if (dim <= 0.0005)
        {
            if (!d.Dimmed) return false;
            if (!Write(d, d.Normal)) return false;
            d.Dimmed = false;
            Report(d);
            return true;
        }

        if (!d.Dimmed)
        {
            // Capture the user's current brightness right before lowering it.
            int cur = Read(d);
            if (cur >= 0) d.Normal = cur;
            if (d.Normal < 0) d.Normal = (int)d.Max;
            d.Dimmed = true;
            Report(d);
        }
        int target = TargetFor(d, dim);
        if (target == d.LastSet) return false;
        return Write(d, target);
    }

    private void Report(Dev d)
    {
        string id = d.Id; int n = d.Normal; bool dm = d.Dimmed;
        _ui.Post(_ => StateChanged?.Invoke(id, n, dm), null);
    }

    private static int Read(Dev d)
    {
        if (d.LcdLevels != null)
        {
            IntPtr h = Native.CreateFile(@"\\.\LCD", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return -1;
            try
            {
                var cur = new byte[3];
                return Native.DeviceIoControl(h, Native.IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, null, 0, cur, 3, out _, IntPtr.Zero)
                    ? (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? cur[2] : cur[1]) : -1;
            }
            finally { Native.CloseHandle(h); }
        }
        for (int i = 0; i < 2; i++)
        {
            if (Native.GetVCPFeatureAndVCPFeatureReply(d.Phys, Native.VCP_BRIGHTNESS, IntPtr.Zero, out uint cur, out _)) return (int)cur;
            Thread.Sleep(40);
        }
        return -1;
    }

    private static bool Write(Dev d, int value)
    {
        if (value < 0) return false;
        bool ok;
        if (d.LcdLevels != null)
        {
            // Snap to a supported level.
            byte v = d.LcdLevels.OrderBy(l => Math.Abs(l - value)).First();
            IntPtr h = Native.CreateFile(@"\\.\LCD", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return false;
            try
            {
                var buf = new byte[] { 3, v, v }; // DISPLAYPOLICY_BOTH, AC, DC
                ok = Native.DeviceIoControl(h, Native.IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS, buf, 3, null, 0, out _, IntPtr.Zero);
            }
            finally { Native.CloseHandle(h); }
        }
        else
        {
            ok = Native.SetVCPFeature(d.Phys, Native.VCP_BRIGHTNESS, (uint)value);
            if (!ok) { Thread.Sleep(50); ok = Native.SetVCPFeature(d.Phys, Native.VCP_BRIGHTNESS, (uint)value); }
        }
        if (ok) d.LastSet = value;
        d.WroteAt = Environment.TickCount64;
        return ok;
    }

    private void RestoreAll()
    {
        List<Dev> list;
        lock (_lock) list = _devs.Values.ToList();
        foreach (var d in list)
        {
            if (d.Supported == true && d.Dimmed && Write(d, d.Normal)) d.Dimmed = false;
            Close(d);
        }
    }

    private static void Close(Dev d)
    {
        if (d.PhysArr != null) { try { Native.DestroyPhysicalMonitors((uint)d.PhysArr.Length, d.PhysArr); } catch { } }
        d.PhysArr = null; d.Phys = IntPtr.Zero;
    }

    /// <summary>Restores normal brightness on every monitor and stops the thread (blocks up to 3 s).</summary>
    public void Dispose()
    {
        _quit = true;
        _wake.Set();
        _thread.Join(3000);
        _wake.Dispose();
    }
}
