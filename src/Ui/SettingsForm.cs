using System.Drawing;
using System.Globalization;

namespace BetterDarkScreen.Ui;

/// <summary>Settings window with left navigation (Win11 style). All changes apply live; no Save button.</summary>
internal sealed class SettingsForm : Form
{
    public enum Page { Schedule, Dimming, Displays, Hotkeys, General }

    private readonly Settings _s;
    private readonly DimController _dim;
    private readonly Action _changed;
    private readonly Action<bool> _setStartup;
    private readonly Func<HotkeyAction, bool> _hotkeyOk;
    private readonly Action<bool> _suspendHotkeys;
    private readonly Action _hotkeysChanged;

    private Page _page = Page.Schedule;
    private string? _highlight;
    private Panel _content = null!;
    private Label2? _preview;
    private string _displaySig = "";
    private int _y;

    private float S => DeviceDpi / 96f;
    private int D(float dip) => (int)Math.Round(dip * S);
    private int NavW => D(232);
    private int ContentW => D(600);
    private int CPad => D(28);
    private int RowW => ContentW - CPad * 2;

    public SettingsForm(Settings s, DimController dim, Action changed, Action<bool> setStartup,
                        Func<HotkeyAction, bool> hotkeyOk, Action<bool> suspendHotkeys, Action hotkeysChanged)
    {
        _s = s; _dim = dim; _changed = changed; _setStartup = setStartup;
        _hotkeyOk = hotkeyOk; _suspendHotkeys = suspendHotkeys; _hotkeysChanged = hotkeysChanged;
        Text = "BetterDarkScreen settings";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        _dim.StateChanged += OnDimState;
    }

    /// <summary>Jump to a page and flash one setting (e.g. "unlock").</summary>
    public void Open(Page page, string? highlight)
    {
        _page = page; _highlight = highlight;
        if (IsHandleCreated) Build();
        if (highlight != null)
        {
            var t = new System.Windows.Forms.Timer { Interval = 2500 };
            t.Tick += (_, _) => { t.Dispose(); _highlight = null; if (!IsDisposed) foreach (var c in _content.Controls.OfType<Card>()) { c.Highlight = false; c.Invalidate(); } };
            t.Start();
        }
    }

    public static Page PageFor(string? key) => key switch
    {
        "unlock" => Page.Dimming,
        "hotkeys" => Page.Hotkeys,
        "displays" => Page.Displays,
        _ => Page.Schedule,
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(Handle, Theme.Current.Dark);
        Build();
        CenterToScreen();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Build(); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && ActiveControl is not HotkeyBox) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _dim.StateChanged -= OnDimState;
        _suspendHotkeys(false);
        base.OnFormClosed(e);
    }

    private void OnDimState()
    {
        if (IsDisposed || _page != Page.Displays) return;
        string sig = DisplaySignature();
        if (sig != _displaySig) BeginInvoke(Build);
    }

    private string DisplaySignature() => string.Join("|", _dim.States.Select(st => $"{st.Display.Id}:{st.HwSupported}:{st.GammaSupported}:{st.Channel}:{st.Fallback}"));

    // ------------------------------------------------------------ shell

    private void Build()
    {
        SuspendLayout();
        var focusName = ActiveControl?.AccessibleName;
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        var t = Theme.Current;
        BackColor = t.Bg;

        // Navigation
        var nav = new Panel { BackColor = t.Bg };
        var appIcon = new Label2 { Text = "\uE708", Family = Theme.IconFont, Px = 16, Color = t.Accent, BackColor = t.Bg };
        appIcon.SetBounds(D(20), D(18), D(22), D(28));
        var appName = new Label2 { Text = "BetterDarkScreen", Px = 13, BackColor = t.Bg };
        appName.SetBounds(D(48), D(18), NavW - D(56), D(28));
        nav.Controls.AddRange(new Control[] { appIcon, appName });
        int ny = D(64);
        foreach (var (pg, glyph, title) in new[]
                 {
                     (Page.Schedule, "\uE823", "Schedule"),
                     (Page.Dimming, "\uE706", "Dimming"),
                     (Page.Displays, "\uE7F4", "Displays"),
                     (Page.Hotkeys, "\uE765", "Hotkeys"),
                     (Page.General, "\uE713", "General"),
                 })
        {
            var item = new NavItem { Text = title, Glyph = glyph, Selected = pg == _page, BackColor = t.Bg, AccessibleName = "nav:" + title };
            item.SetBounds(D(8), ny, NavW - D(16), D(40));
            var target = pg;
            item.Click += (_, _) => { if (_page != target) { _page = target; Build(); } };
            nav.Controls.Add(item);
            ny += D(42);
        }

        // Content
        _content = new Panel { BackColor = t.Bg };
        _preview = null;
        _y = D(24);
        switch (_page)
        {
            case Page.Schedule: BuildSchedule(); break;
            case Page.Dimming: BuildDimming(); break;
            case Page.Displays: BuildDisplays(); break;
            case Page.Hotkeys: BuildHotkeys(); break;
            default: BuildGeneral(); break;
        }
        int h = Math.Max(D(600), Math.Max(_y + D(24), ny + D(24)));
        nav.SetBounds(0, 0, NavW, h);
        _content.SetBounds(NavW, 0, ContentW, h);
        Controls.Add(nav);
        Controls.Add(_content);
        ClientSize = new Size(NavW + ContentW, h);
        ResumeLayout();

        if (focusName != null)
            foreach (var c in AllControls(this))
                if (c.AccessibleName == focusName && c.CanFocus) { c.Focus(); break; }
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var cc in AllControls(c)) yield return cc;
        }
    }

    // ------------------------------------------------------------ pages

    private void BuildSchedule()
    {
        PageTitle("Schedule");
        var sch = new Segmented { Items = new[] { "Always", "Custom hours", "Sunset to sunrise" }, AccessibleName = "Schedule" };
        sch.SetSilently((int)_s.Schedule);
        sch.SelectedChanged += (_, _) => { _s.Schedule = (ScheduleKind)sch.Selected; Apply(); BeginInvoke(Build); };
        Row("When to dim", null, sch, D(340), D(34));

        if (_s.Schedule == ScheduleKind.Fixed)
        {
            var from = Field(_s.FixedStart, "");
            var to = Field(_s.FixedEnd, "");
            from.Box.TextChanged += (_, _) => { if (Schedule.TryParseTime(from.Text, out _)) { _s.FixedStart = from.Text.Trim(); Apply(); UpdatePreview(); } };
            to.Box.TextChanged += (_, _) => { if (Schedule.TryParseTime(to.Text, out _)) { _s.FixedEnd = to.Text.Trim(); Apply(); UpdatePreview(); } };
            Row("Start", "24-hour time, e.g. 21:30", from, D(96), D(32));
            Row("End", "Can be after midnight", to, D(96), D(32));
        }
        else if (_s.Schedule == ScheduleKind.Sun)
        {
            var lat = Field(_s.Latitude.ToString("0.####", CultureInfo.InvariantCulture), "°");
            var lon = Field(_s.Longitude.ToString("0.####", CultureInfo.InvariantCulture), "°");
            lat.Box.TextChanged += (_, _) => { if (TryNum(lat.Text, -90, 90, out var v)) { _s.Latitude = v; Apply(); UpdatePreview(); } };
            lon.Box.TextChanged += (_, _) => { if (TryNum(lon.Text, -180, 180, out var v)) { _s.Longitude = v; Apply(); UpdatePreview(); } };
            Row("Latitude", "North is positive. Kyiv ≈ 50.45", lat, D(110), D(32));
            Row("Longitude", "East is positive. Kyiv ≈ 30.52", lon, D(110), D(32));

            var so = Field(_s.SunsetOffsetMin.ToString(CultureInfo.InvariantCulture), "min");
            var ro = Field(_s.SunriseOffsetMin.ToString(CultureInfo.InvariantCulture), "min");
            so.Box.TextChanged += (_, _) => { if (int.TryParse(so.Text, out var v) && Math.Abs(v) <= 300) { _s.SunsetOffsetMin = v; Apply(); UpdatePreview(); } };
            ro.Box.TextChanged += (_, _) => { if (int.TryParse(ro.Text, out var v) && Math.Abs(v) <= 300) { _s.SunriseOffsetMin = v; Apply(); UpdatePreview(); } };
            Row("Sunset offset", "Minutes. Negative starts earlier", so, D(110), D(32));
            Row("Sunrise offset", "Minutes. Negative stops earlier", ro, D(110), D(32));
        }

        if (_s.Schedule != ScheduleKind.Always)
        {
            Row("Fade", "Smooth transition at start and end", SliderWithValue(0, 90, _s.FadeMinutes, v => $"{v} min",
                v => { _s.FadeMinutes = v; _dim.Refresh(); }), D(270), D(32));

            _preview = new Label2 { Px = 12, Color = Theme.Current.TextDim, BackColor = Theme.Current.Bg };
            _preview.SetBounds(CPad + D(4), _y + D(4), RowW, D(20));
            _content.Controls.Add(_preview);
            _y += D(30);
            UpdatePreview();
        }
    }

    private void BuildDimming()
    {
        PageTitle("Dimming");
        var mode = new Segmented { Items = new[] { "Auto", "Overlay", "Gamma", "Hardware", "Custom" }, AccessibleName = "Method" };
        mode.SetSilently((int)_s.Mode);
        mode.SelectedChanged += (_, _) => { _s.Mode = (DimMode)mode.Selected; Apply(); BeginInvoke(Build); };
        string help = _s.Mode switch
        {
            DimMode.Auto => "Overlay normally. Gamma in exclusive-fullscreen games.",
            DimMode.Overlay => "Click-through dark layer. Smooth, deep, works on every monitor.",
            DimMode.Gamma => "Graphics card gamma. Works in fullscreen games, depth limited by Windows.",
            DimMode.Hardware => "Monitor's own backlight via DDC/CI. Best image, needs monitor support.",
            _ => "Each monitor uses its own method. Set them on the Displays page.",
        };
        BlockRow("Method", help, mode, D(36));

        if (_s.Mode == DimMode.Custom)
        {
            var go = new FlatButton { Text = "Open Displays", Glyph = "\uE7F4" };
            go.Click += (_, _) => { _page = Page.Displays; Build(); };
            Row("Per-monitor methods", string.Join("  ·  ", _dim.States.Select(st => $"{st.Display.Label}: {FlyoutForm.TagText(st)}")), go, D(150), D(32));
        }

        Row("Link monitor levels", "Moving one slider moves all", Toggle(_s.LinkMonitors, v => _s.LinkMonitors = v), D(48), D(28));
        Row("Hide from screenshots", "Recordings and streams stay bright (overlay only)",
            Toggle(_s.HideFromCapture, v => { _s.HideFromCapture = v; _dim.Refresh(false); }), D(48), D(28));

        bool unlocked = GammaRamp.IsRangeUnlocked();
        var unlock = new FlatButton { Text = unlocked ? "Unlocked" : "Unlock", Accent = !unlocked, Enabled = !unlocked, AccessibleName = "unlock" };
        unlock.Click += (_, _) =>
        {
            if (!GammaRamp.UnlockRange()) return;
            unlock.Text = "Sign out to apply"; unlock.Accent = false; unlock.Enabled = false; unlock.Invalidate();
            GammaRamp.ClearLimits();
            _dim.RecheckGammaUnlock();
        };
        Row("Deeper gamma dimming", unlocked ? "Windows gamma limit is lifted" : "Windows stops gamma near 50%. Needs admin, then sign out",
            unlock, D(150), D(32));
    }

    private void BuildDisplays()
    {
        PageTitle("Displays");
        _displaySig = DisplaySignature();
        var hint = new Label2 { Px = 12, Color = Theme.Current.TextDim, BackColor = Theme.Current.Bg,
            Text = _s.Mode == DimMode.Custom ? "Each monitor uses the method chosen here." : "Picking a method here switches Method to Custom." };
        hint.SetBounds(CPad + D(4), _y - D(6), RowW, D(20));
        _content.Controls.Add(hint);
        _y += D(22);

        foreach (var st in _dim.States)
        {
            var ms = _s.For(st.Display.Id);
            var seg = new Segmented { Items = new[] { "Auto", "Overlay", "Gamma", "Hardware" }, AccessibleName = "method:" + st.Display.Id };
            seg.SetSilently((int)ms.Method);
            if (!st.GammaSupported) seg.SetDisabled(2, true);
            if (st.HwSupported == false) seg.SetDisabled(3, true);
            string id = st.Display.Id;
            seg.SelectedChanged += (_, _) =>
            {
                _s.For(id).Method = (MonitorMethod)seg.Selected;
                _s.Mode = DimMode.Custom;
                Apply();
                BeginInvoke(Build);
            };

            string hw = st.HwSupported switch { true => "Hardware ✓", false => "Hardware ✗ (DDC/CI off or unsupported)", _ => "Hardware: checking…" };
            string gm = st.GammaSupported ? "Gamma ✓" : "Gamma ✗";
            string now = _s.Mode == DimMode.Custom ? $"  ·  now {FlyoutForm.TagText(st)}" : "";
            BlockRow(st.Display.Label + (st.Display.Primary ? "  (main)" : ""), $"{hw}  ·  {gm}{now}", seg, D(36));
        }

        var note = new Label2 { Px = 12, Color = Theme.Current.TextDisabled, BackColor = Theme.Current.Bg,
            Text = "No hardware? Turn on DDC/CI in the monitor's on-screen menu, then reconnect it." };
        note.SetBounds(CPad + D(4), _y + D(4), RowW, D(20));
        _content.Controls.Add(note);
        _y += D(30);
    }

    private void BuildHotkeys()
    {
        PageTitle("Hotkeys");
        Row("Use hotkeys", "Work everywhere, also in games", Toggle(_s.Hotkeys, v => { _s.Hotkeys = v; _hotkeysChanged(); BeginInvoke(Build); }), D(48), D(28));

        foreach (var a in HotkeyActions.All)
        {
            var box = new HotkeyBox { Value = Hotkey.Parse(HotkeyActions.Get(_s, a)), Enabled = _s.Hotkeys, AccessibleName = "hk:" + a };
            var action = a;
            box.Capturing += on => _suspendHotkeys(on);
            box.ValueChanged += (_, _) =>
            {
                string v = box.Value.ToString();
                // One combo per action: clear duplicates.
                if (v.Length > 0)
                    foreach (var other in HotkeyActions.All.Where(o => o != action && HotkeyActions.Get(_s, o) == v))
                        HotkeyActions.Set(_s, other, "");
                HotkeyActions.Set(_s, action, v);
                _hotkeysChanged();
                _changed();
                BeginInvoke(Build);
            };
            string? desc = null;
            if (_s.Hotkeys && !box.Value.IsEmpty && !_hotkeyOk(a)) desc = "⚠ Used by another app. Pick another combo";
            else if (box.Value.IsEmpty) desc = "Click the field and press keys";
            Row(HotkeyActions.Title(a), desc, box, D(210), D(32));
        }

        Row("Step", "How much one press of Dim more / less changes",
            SliderWithValue(1, 20, _s.HotkeyStep, v => $"{v}%", v => _s.HotkeyStep = v), D(270), D(32));
        var tip = new Label2 { Px = 12, Color = Theme.Current.TextDisabled, BackColor = Theme.Current.Bg,
            Text = "Backspace clears a field. Esc cancels." };
        tip.SetBounds(CPad + D(4), _y + D(4), RowW, D(20));
        _content.Controls.Add(tip);
        _y += D(30);
    }

    private void BuildGeneral()
    {
        PageTitle("General");
        Row("Start with Windows", null, Toggle(_s.StartWithWindows, v => { _s.StartWithWindows = v; _setStartup(v); }), D(48), D(28));
        var folder = new FlatButton { Text = "Open folder", Glyph = "\uE8B7" };
        folder.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(Settings.Dir); System.Diagnostics.Process.Start("explorer.exe", Settings.Dir); } catch { }
        };
        Row("Settings file", "%AppData%\\BetterDarkScreen\\settings.json", folder, D(150), D(32));

        var ver = new Label2 { Text = $"BetterDarkScreen {Application.ProductVersion.Split('+')[0]}", Px = 12, Color = Theme.Current.TextDisabled, BackColor = Theme.Current.Bg };
        ver.SetBounds(CPad + D(4), _y + D(8), RowW, D(20));
        _content.Controls.Add(ver);
        _y += D(36);
    }

    // ------------------------------------------------------------ building blocks

    private void PageTitle(string title)
    {
        var h = new Label2 { Text = title, Px = 28, Style = FontStyle.Bold, Family = Theme.DisplayFont, BackColor = Theme.Current.Bg };
        h.SetBounds(CPad, _y, RowW, D(44));
        _content.Controls.Add(h);
        _y += D(60);
    }

    private Card NewCard(int h, Control control)
    {
        var card = new Card { Highlight = _highlight != null && control.AccessibleName == _highlight };
        card.SetBounds(CPad, _y, RowW, h);
        _content.Controls.Add(card);
        _y += h + D(4);
        return card;
    }

    /// <summary>Win11 settings card: title + description left, control right.</summary>
    private void Row(string title, string? desc, Control control, int cw, int ch)
    {
        var t = Theme.Current;
        control.AccessibleName ??= title;
        int h = D(desc == null ? 60 : 68);
        var card = NewCard(h, control);
        int ip = D(18);
        int textW = card.Width - ip * 3 - cw;
        var tl = new Label2 { Text = title, Px = 14, BackColor = t.Card };
        tl.SetBounds(ip, desc == null ? (h - D(22)) / 2 : D(13), textW, D(22));
        card.Controls.Add(tl);
        if (desc != null)
        {
            var dl = new Label2 { Text = desc, Px = 12, Color = desc.StartsWith("⚠") ? t.Warning : t.TextDim, BackColor = t.Card };
            dl.SetBounds(ip, D(36), textW, D(20));
            card.Controls.Add(dl);
        }
        SetBg(control, t.Card);
        control.SetBounds(card.Width - ip - cw, (h - ch) / 2, cw, ch);
        card.Controls.Add(control);
    }

    /// <summary>Card with the control on its own line under the text (for wide selectors).</summary>
    private void BlockRow(string title, string desc, Control control, int ch)
    {
        var t = Theme.Current;
        control.AccessibleName ??= title;
        int h = D(68) + ch + D(14);
        var card = NewCard(h, control);
        int ip = D(18);
        var tl = new Label2 { Text = title, Px = 14, BackColor = t.Card };
        tl.SetBounds(ip, D(13), card.Width - ip * 2, D(22));
        var dl = new Label2 { Text = desc, Px = 12, Color = t.TextDim, BackColor = t.Card };
        dl.SetBounds(ip, D(36), card.Width - ip * 2, D(20));
        SetBg(control, t.Card);
        control.SetBounds(ip, D(66), card.Width - ip * 2, ch);
        card.Controls.AddRange(new[] { tl, dl, control });
    }

    private static void SetBg(Control c, Color bg)
    {
        c.BackColor = bg;
        foreach (Control k in c.Controls) if (k is not TextBox) SetBg(k, bg);
    }

    private Control SliderWithValue(int min, int max, int value, Func<int, string> fmt, Action<int> set)
    {
        var wrap = new Panel();
        var lbl = new Label2 { Text = fmt(value), Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding };
        var sl = new FlatSlider { Minimum = min, Maximum = max, LargeChange = Math.Max(1, (max - min) / 9) };
        sl.SetSilently(value);
        sl.ValueChanged += (_, _) => { lbl.Text = fmt(sl.Value); set(sl.Value); };
        sl.Committed += (_, _) => _changed();
        wrap.Resize += (_, _) =>
        {
            sl.SetBounds(0, 0, wrap.Width - D(64), wrap.Height);
            lbl.SetBounds(wrap.Width - D(60), 0, D(60), wrap.Height);
        };
        wrap.Controls.AddRange(new Control[] { sl, lbl });
        return wrap;
    }

    private FieldBox Field(string text, string suffix)
    {
        var f = new FieldBox { Suffix = suffix };
        f.Box.Text = text;
        return f;
    }

    private ToggleSwitch Toggle(bool value, Action<bool> set)
    {
        var tg = new ToggleSwitch();
        tg.SetSilently(value);
        tg.CheckedChanged += (_, _) => { set(tg.Checked); Apply(); };
        return tg;
    }

    private void Apply()
    {
        _dim.Refresh();
        _changed();
    }

    private static bool TryNum(string s, double min, double max, out double v)
        => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;

    private void UpdatePreview()
    {
        if (_preview == null || _preview.IsDisposed) return;
        var now = DateTime.Now;
        var w = Schedule.Window(_s, now.AddDays(-1)) is { } y && now < y.end ? y : Schedule.Window(_s, now);
        _preview.Text = w is { } x
            ? $"Dims from {x.start:HH:mm} until {x.end:HH:mm}" + (_s.Schedule == ScheduleKind.Sun ? " today" : "")
            : "No dimming period with these values";
    }
}
