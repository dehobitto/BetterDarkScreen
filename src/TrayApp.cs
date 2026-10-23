using BetterDarkScreen.Ui;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace BetterDarkScreen;

internal sealed class TrayApp : ApplicationContext
{
    private readonly Settings _s;
    private readonly DimController _dim;
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _keys;
    private readonly Timer _save = new() { Interval = 800 };
    private readonly Timer _trim = new() { Interval = 4000 };
    private readonly HashSet<HotkeyAction> _hotkeyFailed = new();
    private FlyoutForm? _flyout;
    private SettingsForm? _settings;
    private PopupMenu? _menu;
    private TrayIcons.State _iconState = (TrayIcons.State)(-1);
    private DateTime _flyoutClosed;
    private bool _hotkeysSuspended;

    public TrayApp(bool openSettings)
    {
        _s = Settings.Load();
        _dim = new DimController(_s);
        _dim.Dirty += () => { _save.Stop(); _save.Start(); };

        _tray = new NotifyIcon { Text = "BetterDarkScreen", Visible = true };
        _tray.MouseUp += OnTrayMouse;
        UpdateIcon();

        _keys = new HotkeyWindow(OnHotkey);
        ApplyHotkeys();

        _save.Tick += (_, _) => { _save.Stop(); _s.Save(); };
        _trim.Tick += (_, _) => { _trim.Stop(); if (_flyout == null && _settings == null && _menu == null) Native.TrimWorkingSet(); };

        _dim.StateChanged += UpdateIcon;
        SystemEvents.UserPreferenceChanged += OnPrefs;

        if (_s.StartWithWindows) SetStartup(true); // refresh path if exe moved

        if (_s.FirstRun || openSettings)
        {
            _s.FirstRun = false; Changed();
            var t = new Timer { Interval = 600 };
            t.Tick += (_, _) => { t.Dispose(); if (openSettings) OpenSettings(null); else ShowFlyout(); };
            t.Start();
        }
        else _trim.Start();
    }

    /// <summary>
    /// Another instance was launched: show the panel.
    /// </summary>
    public void Activate() => ShowFlyout();

    private void Changed()
    {
        _save.Stop(); _save.Start();
        UpdateIcon();
    }

    // ------------------------------------------------------------ tray

    private void OnTrayMouse(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_flyout != null) { _flyout.Close(); return; }
            // Clicking the icon deactivates (closes) the flyout first; don't reopen instantly.
            if ((DateTime.Now - _flyoutClosed).TotalMilliseconds < 250) return;
            ShowFlyout();
        }
        else if (e.Button == MouseButtons.Right) ShowMenu();
    }

    private void ShowFlyout()
    {
        if (_flyout != null) { _flyout.Activate(); return; }
        _menu?.Close();
        _trim.Stop();
        _flyout = new FlyoutForm(_s, _dim, Changed, OpenSettings);
        _flyout.FormClosed += (_, _) =>
        {
            _flyoutClosed = DateTime.Now;
            _flyout = null;
            _trim.Start();
        };
        _flyout.ShowNearTray();
    }

    private void ShowMenu()
    {
        _menu?.Close();
        _flyout?.Close();
        bool paused = _dim.IsPaused;
        var m = new PopupMenu()
            .Add("Open panel", "\uE7F4", ShowFlyout)
            .Add(_s.Enabled ? "Turn off" : "Turn on", _s.Enabled ? "\uE7E8" : "\uE706", ToggleEnabled);
        if (_s.Enabled)
        {
            if (paused) m.Add("Resume", "\uE768", () => _dim.Resume());
            else
            {
                m.Add("Pause 1 hour", "\uE769", () => _dim.Pause(TimeSpan.FromHours(1)));
                m.Add("Pause until I resume", "\uE769", () => _dim.Pause(null));
            }
        }
        m.Separator()
         .Add("Settings", "\uE713", () => OpenSettings(null))
         .Add("Exit", "\uE8BB", ExitThread);
        _menu = m;
        m.FormClosed += (_, _) => { if (_menu == m) _menu = null; _trim.Start(); };
        _trim.Stop();
        m.ShowAt(Cursor.Position, above: true);
    }

    private void ToggleEnabled()
    {
        _s.Enabled = !_s.Enabled;
        if (_s.Enabled) _dim.Resume();
        _dim.Refresh();
        Changed();
    }

    private void OpenSettings(string? focus)
    {
        _trim.Stop();
        if (_settings == null)
        {
            _settings = new SettingsForm(_s, _dim, Changed, SetStartup, a => !_hotkeyFailed.Contains(a), SuspendHotkeys, ApplyHotkeys);
            _settings.FormClosed += (_, _) =>
            {
                _settings = null;
                _s.Save();
                _trim.Start();
            };
            _settings.Open(SettingsForm.PageFor(focus), focus);
            _settings.Show();
        }
        else _settings.Open(SettingsForm.PageFor(focus), focus);
        if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
        _settings.Activate();
    }

    private void UpdateIcon()
    {
        var st = !_s.Enabled ? TrayIcons.State.Off
               : !_dim.IsPaused && _dim.ScheduleFactor > 0 ? TrayIcons.State.Active : TrayIcons.State.Idle;
        string tip = st switch
        {
            TrayIcons.State.Off => "BetterDarkScreen · off",
            TrayIcons.State.Idle => _dim.IsPaused ? "BetterDarkScreen · paused" : "BetterDarkScreen · waiting for schedule",
            _ => "BetterDarkScreen · dimming",
        };
        if (_tray.Text != tip) _tray.Text = tip;
        if (st == _iconState) return;
        _iconState = st;
        var old = _tray.Icon;
        _tray.Icon = TrayIcons.Create(st);
        old?.Dispose();
    }

    private void OnPrefs(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        Theme.Invalidate();
        _iconState = (TrayIcons.State)(-1);
        UpdateIcon();
        _flyout?.Close();
        _menu?.Close();
    }

    // ------------------------------------------------------------ hotkeys

    private void SuspendHotkeys(bool on)
    {
        if (_hotkeysSuspended == on) return;
        _hotkeysSuspended = on;
        ApplyHotkeys();
    }

    private void ApplyHotkeys()
    {
        _keys.UnregisterAll();
        _hotkeyFailed.Clear();
        if (!_s.Hotkeys || _hotkeysSuspended) return;
        foreach (var a in HotkeyActions.All)
        {
            var hk = Hotkey.Parse(HotkeyActions.Get(_s, a));
            if (!hk.IsValid) continue;
            bool repeat = a is HotkeyAction.Darker or HotkeyAction.Brighter; // hold to keep changing
            if (!_keys.Register((int)a, hk.Mods | (repeat ? 0 : Native.MOD_NOREPEAT), (uint)hk.Key))
                _hotkeyFailed.Add(a);
        }
    }

    private void OnHotkey(int id)
    {
        switch ((HotkeyAction)id)
        {
            case HotkeyAction.Toggle:
                ToggleEnabled();
                return;
            case HotkeyAction.Pause:
                if (_dim.IsPaused) _dim.Resume(); else _dim.Pause(TimeSpan.FromHours(1));
                UpdateIcon();
                return;
            case HotkeyAction.Panel:
                if (_flyout != null) _flyout.Close(); else ShowFlyout();
                return;
            case HotkeyAction.Darker:
            case HotkeyAction.Brighter:
                int step = Math.Clamp(_s.HotkeyStep, 1, 50);
                int delta = (HotkeyAction)id == HotkeyAction.Darker ? step : -step;
                if (!_s.Enabled) { _s.Enabled = true; _dim.Resume(); }
                foreach (var d in _dim.Displays)
                {
                    var ms = _s.For(d.Id);
                    if (ms.Enabled) ms.Level = Math.Clamp(ms.Level + delta, 0, 95);
                }
                _dim.Refresh();
                Changed();
                _flyout?.Close();
                return;
        }
    }

    // ------------------------------------------------------------ startup

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue("BetterDarkScreen", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("BetterDarkScreen", false);
        }
        catch { }
    }

    // ------------------------------------------------------------ exit

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= OnPrefs;
        _menu?.Close();
        _flyout?.Close();
        _settings?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _keys.Dispose();
        _dim.Dispose();   // restores gamma + monitor brightness, removes overlays
        _s.Save();
        base.ExitThreadCore();
    }
}

/// <summary>Message-only window receiving WM_HOTKEY.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Action<int> _onHotkey;
    private readonly List<int> _ids = new();

    public HotkeyWindow(Action<int> onHotkey)
    {
        _onHotkey = onHotkey;
        CreateHandle(new CreateParams { Caption = "BetterDarkScreen.Hotkeys", Parent = new IntPtr(-3) }); // HWND_MESSAGE
    }

    public bool Register(int id, uint mods, uint vk)
    {
        if (!Native.RegisterHotKey(Handle, id, mods, vk)) return false;
        _ids.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids) Native.UnregisterHotKey(Handle, id);
        _ids.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY) { _onHotkey((int)m.WParam); return; }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
