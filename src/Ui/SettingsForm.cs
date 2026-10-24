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
    private List<Card>? _group;
    private Icon? _icon;
    private Color _iconColor;
    // Pages are rebuilt inside this panel; the form and its title bar are never touched.
    private readonly Panel _root = new() { Location = Point.Empty };

    private float S => DeviceDpi / 96f;
    private int D(float dip) => (int)Math.Round(dip * S);
    private int NavW => D(252);
    private int ClientW => D(880);
    private int CX => D(20);                     // content left padding (inside content panel)
    private int ContentW => ClientW - NavW;
    private int RowW => ContentW - CX - D(36);   // 36 right padding

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
        Controls.Add(_root);
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
        _icon?.Dispose();
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

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_COMPOSITED; // paint all children in one buffered pass: no flashing
            return cp;
        }
    }

    private void Build()
    {
        // Keep keyboard focus on the form while the old controls are destroyed. If the focused
        // control is disposed, Windows briefly drops focus and the title bar flashes inactive.
        var focusName = ActiveControl?.AccessibleName;
        if (IsHandleCreated) { ActiveControl = null; Focus(); }

        // Freeze painting of the page area only (not the window frame) until the new page is ready.
        bool freeze = _root.IsHandleCreated && Visible;
        if (freeze) Native.SendMessage(_root.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try { BuildCore(focusName); }
        finally
        {
            if (freeze)
            {
                Native.SendMessage(_root.Handle, Native.WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                Native.RedrawWindow(_root.Handle, IntPtr.Zero, IntPtr.Zero,
                    Native.RDW_ERASE | Native.RDW_INVALIDATE | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW);
            }
        }
    }

    private void BuildCore(string? focusName)
    {
        SuspendLayout();
        _root.SuspendLayout();
        var old = _root.Controls.Cast<Control>().ToList();
        _root.Controls.Clear();
        foreach (var c in old) c.Dispose();
        var t = Theme.Current;
        BackColor = _root.BackColor = t.Bg;

        // Title bar icon: monochrome monitor mark. Only replaced when the color changes,
        // because setting Form.Icon repaints the title bar.
        if (_icon == null || _iconColor != t.Text)
        {
            var oldIcon = _icon;
            _icon = Brand.MarkIcon(SystemInformation.SmallIconSize.Width, t.Text);
            _iconColor = t.Text;
            Icon = _icon;
            oldIcon?.Dispose();
        }

        // Navigation: lockup, then pages.
        var nav = new Panel { BackColor = t.Bg };
        var logo = new BrandView { Art = BrandView.Kind.Lockup, Shadow = 3, BackColor = t.Bg, AccessibleName = "BetterDarkScreen" };
        logo.SetBounds(D(24), D(12), D(153), D(88));
        nav.Controls.Add(logo);
        int ny = D(12 + 85 + 20);
        foreach (var (pg, glyph, title) in new[]
                 {
                     (Page.Schedule, "", "Schedule"),
                     (Page.Dimming, "", "Dimming"),
                     (Page.Displays, "", "Displays"),
                     (Page.Hotkeys, "", "Hotkeys"),
                     (Page.General, "", "General"),
                 })
        {
            var item = new NavItem { Text = title, Glyph = glyph, Selected = pg == _page, BackColor = t.Bg, AccessibleName = "nav:" + title };
            item.SetBounds(D(8), ny, NavW - D(16), D(36));
            var target = pg;
            item.Click += (_, _) => { if (_page != target) { _page = target; Build(); } };
            nav.Controls.Add(item);
            ny += D(38);
        }

        // Content
        _content = new Panel { BackColor = t.Bg };
        _preview = null;
        _group = null;
        _y = D(12);
        switch (_page)
        {
            case Page.Schedule: BuildSchedule(); break;
            case Page.Dimming: BuildDimming(); break;
            case Page.Displays: BuildDisplays(); break;
            case Page.Hotkeys: BuildHotkeys(); break;
            default: BuildGeneral(); break;
        }
        EndGroup();
        int h = Math.Max(D(688), Math.Max(_y + D(24), ny + D(12)));
        nav.SetBounds(0, 0, NavW, h);
        _content.SetBounds(NavW, 0, ContentW, h);
        _root.Controls.Add(nav);
        _root.Controls.Add(_content);
        _root.Size = new Size(ClientW, h);
        _root.ResumeLayout();
        var size = new Size(ClientW, h);
        if (ClientSize != size) ClientSize = size; // resizing repaints the frame, so only when needed
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
        BlockRow("When to dim", "Dimming turns on and off by itself", sch);

        if (_s.Schedule == ScheduleKind.Fixed)
        {
            Header("Hours");
            BeginGroup();
            var from = Field(_s.FixedStart, "");
            var to = Field(_s.FixedEnd, "");
            from.TextChanged += (_, _) => { if (Schedule.TryParseTime(from.Text, out _)) { _s.FixedStart = from.Text.Trim(); Apply(); UpdatePreview(); } };
            to.TextChanged += (_, _) => { if (Schedule.TryParseTime(to.Text, out _)) { _s.FixedEnd = to.Text.Trim(); Apply(); UpdatePreview(); } };
            Row("Start", "24-hour time, e.g. 21:30", from, D(96), D(32));
            Row("End", "Can be after midnight", to, D(96), D(32));
            EndGroup();
        }
        else if (_s.Schedule == ScheduleKind.Sun)
        {
            Header("Location");
            BeginGroup();
            var lat = Field(_s.Latitude.ToString("0.####", CultureInfo.InvariantCulture), "°");
            var lon = Field(_s.Longitude.ToString("0.####", CultureInfo.InvariantCulture), "°");
            lat.TextChanged += (_, _) => { if (TryNum(lat.Text, -90, 90, out var v)) { _s.Latitude = v; Apply(); UpdatePreview(); } };
            lon.TextChanged += (_, _) => { if (TryNum(lon.Text, -180, 180, out var v)) { _s.Longitude = v; Apply(); UpdatePreview(); } };
            Row("Latitude", "North is positive. Kyiv ≈ 50.45", lat, D(110), D(32));
            Row("Longitude", "East is positive. Kyiv ≈ 30.52", lon, D(110), D(32));
            EndGroup();

            Header("Offsets");
            BeginGroup();
            var so = Field(_s.SunsetOffsetMin.ToString(CultureInfo.InvariantCulture), "min");
            var ro = Field(_s.SunriseOffsetMin.ToString(CultureInfo.InvariantCulture), "min");
            so.TextChanged += (_, _) => { if (int.TryParse(so.Text, out var v) && Math.Abs(v) <= 300) { _s.SunsetOffsetMin = v; Apply(); UpdatePreview(); } };
            ro.TextChanged += (_, _) => { if (int.TryParse(ro.Text, out var v) && Math.Abs(v) <= 300) { _s.SunriseOffsetMin = v; Apply(); UpdatePreview(); } };
            Row("Sunset offset", "Minutes. Negative starts earlier", so, D(110), D(32));
            Row("Sunrise offset", "Minutes. Negative stops earlier", ro, D(110), D(32));
            EndGroup();
        }

        if (_s.Schedule != ScheduleKind.Always)
        {
            Header("Transition");
            Row("Fade", "Smooth transition at start and end", SliderWithValue(0, 90, _s.FadeMinutes, v => $"{v} min",
                v => { _s.FadeMinutes = v; _dim.Refresh(); }, D(64)), D(180 + 16 + 64), D(32));

            _preview = Caption("");
            UpdatePreview();
        }
    }

    private void BuildDimming()
    {
        PageTitle("Dimming");
        var t = Theme.Current;
        BeginGroup();
        var mode = new Segmented { Items = new[] { "Auto", "Overlay", "Gamma", "Hardware", "Per monitor" }, AccessibleName = "Method" };
        mode.SetSilently((int)_s.Mode);
        mode.SelectedChanged += (_, _) => { _s.Mode = (DimMode)mode.Selected; Apply(); BeginInvoke(Build); };
        BlockRow("Method", "How the screens get darker", mode);

        if (_s.Mode == DimMode.Custom)
        {
            foreach (var st in _dim.States)
            {
                var ms = _s.For(st.Display.Id);
                var dd = new DropDown { Items = new[] { "Auto", "Overlay", "Gamma", "Hardware" }, AccessibleName = "method:" + st.Display.Id };
                dd.SetSilently((int)ms.Method);
                if (!st.GammaSupported) dd.SetDisabled(2, true);
                if (st.HwSupported == false) dd.SetDisabled(3, true);
                string id = st.Display.Id;
                dd.SelectedChanged += (_, _) => { _s.For(id).Method = (MonitorMethod)dd.Selected; Apply(); BeginInvoke(Build); };
                IndentRow(st.Display.Label, dd, D(148));
            }
            var link = new Label2 { Text = "What each monitor supports", Px = 14, Color = t.TextDim, Link = true };
            var chev = new Label2 { Text = "", Family = Theme.IconFont, Px = 10, Color = t.TextDim };
            link.Click += (_, _) => { _page = Page.Displays; Build(); };
            chev.Click += (_, _) => { _page = Page.Displays; Build(); };
            var card = NewCard(D(44), link);
            int lw = TextRenderer.MeasureText(link.Text, Fonts.Get(Theme.TextFont, 14 * S)).Width;
            link.BackColor = chev.BackColor = card.FillColor;
            link.SetBounds(D(46), D(12), lw + D(2), D(20));
            chev.SetBounds(D(46) + lw + D(6), D(12), D(12), D(20));
            card.Controls.AddRange(new Control[] { link, chev });
        }
        else
        {
            string help = _s.Mode switch
            {
                DimMode.Auto => "Overlay normally. Gamma for the game's monitor in exclusive fullscreen.",
                DimMode.Overlay => "Click-through dark layer. Smooth, deep, works on every monitor.",
                DimMode.Gamma => "Graphics card gamma. Works in fullscreen games, depth limited by Windows.",
                _ => "Monitor's own backlight via DDC/CI. Best image, needs monitor support.",
            };
            var lbl = new Label2 { Text = help, Px = 12, Color = t.TextDim };
            var card = NewCard(D(40), lbl);
            lbl.BackColor = card.FillColor;
            lbl.SetBounds(D(16), D(10), card.Width - D(32), D(20));
            card.Controls.Add(lbl);
        }
        EndGroup();

        Header("Behavior");
        Row("Link monitor levels", "Moving one slider moves all", Toggle(_s.LinkMonitors, v => _s.LinkMonitors = v), D(84), D(24));
        bool anyOverlay = _dim.States.Any(st => st.Channel == Channel.Overlay);
        Row("Hide from screenshots", "Recordings and streams stay bright. Overlay method only" + (anyOverlay ? "" : " — no monitor uses it now"),
            Toggle(_s.HideFromCapture, v => { _s.HideFromCapture = v; _dim.Refresh(false); }), D(84), D(24));

        bool unlocked = GammaRamp.IsRangeUnlocked();
        Control right;
        if (unlocked) right = StatusText("", "Active");
        else
        {
            var unlock = new FlatButton { Text = "Unlock", AccessibleName = "unlock" };
            unlock.Click += (_, _) =>
            {
                if (!GammaRamp.UnlockRange()) return;
                unlock.Text = "Sign out to apply"; unlock.Enabled = false;
                unlock.Width = unlock.PreferredWidth(); unlock.Left = unlock.Parent!.Width - D(16) - unlock.Width;
                unlock.Invalidate();
                GammaRamp.ClearLimits();
                _dim.RecheckGammaUnlock();
            };
            right = unlock;
        }
        right.AccessibleName ??= "unlock";
        Row("Deeper gamma dimming", "Lifts the Windows gamma limit so Gamma can go darker", right,
            right is FlatButton fb ? fb.PreferredWidth() : D(90), D(32));
    }

    private void BuildDisplays()
    {
        var t = Theme.Current;
        PageTitle("Displays", _s.Mode == DimMode.Custom ? "Choose how each monitor gets darker."
                                                         : "Choose how each monitor gets darker. Picking one switches Method to Per monitor.");
        _displaySig = DisplaySignature();

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

            bool hwChosen = st.Requested == MonitorMethod.Hardware && st.HwSupported != false;
            int slowH = hwChosen ? D(12 + 52) : 0;
            int h = D(14 + 42 + 14 + 38 + 16) + slowH;
            var card = NewCard(h, seg, gap: D(8));
            var bg = card.FillColor;
            var glyph = new Label2 { Text = "", Family = Theme.IconFont, Px = 20, BackColor = bg };
            glyph.SetBounds(D(16), D(14), D(22), D(22));
            int x = D(16 + 20 + 14);
            var nameFont = Fonts.Get(Theme.TextFont, 14 * S, FontStyle.Bold);
            int nw = Math.Min(TextRenderer.MeasureText(st.Display.Label, nameFont).Width, card.Width - x - D(80));
            var name = new Label2 { Text = st.Display.Label, Px = 14, Style = FontStyle.Bold, BackColor = bg };
            name.SetBounds(x, D(14), nw, D(20));
            card.Controls.AddRange(new Control[] { glyph, name });
            if (st.Display.Primary)
            {
                var main = new Chip { Text = "Main", Outline = true, BackColor = bg };
                main.SetBounds(x + nw + D(8), D(15), main.PreferredWidth(), D(18));
                card.Controls.Add(main);
            }

            // Capabilities: ✓ Hardware (DDC/CI)   ✓ Gamma   ✓ Overlay
            int cx = x;
            foreach (var (ok, label) in new (bool? ok, string label)[]
                     {
                         (st.HwSupported, st.HwSupported == null ? "Hardware: checking…" : "Hardware (DDC/CI)"),
                         (st.GammaSupported, "Gamma"),
                         (true, "Overlay"),
                     })
            {
                string g = ok switch { true => "", false => "", _ => "" };
                var c = ok == false ? t.TextDisabled : t.TextDim;
                var ic = new Label2 { Text = g, Family = Theme.IconFont, Px = 10, Color = c, BackColor = bg };
                ic.SetBounds(cx, D(40), D(12), D(16));
                int lw = TextRenderer.MeasureText(label, Fonts.Get(Theme.TextFont, 12 * S)).Width;
                var lb = new Label2 { Text = label, Px = 12, Color = c, BackColor = bg };
                lb.SetBounds(cx + D(16), D(40), lw + D(2), D(16));
                card.Controls.AddRange(new Control[] { ic, lb });
                cx += D(16) + lw + D(14);
            }
            if (_s.Mode == DimMode.Custom && st.Fallback)
            {
                var now = new Label2 { Text = "now " + FlyoutForm.TagText(st), Px = 12, Color = t.Warning, BackColor = bg };
                now.SetBounds(cx, D(40), card.Width - cx - D(16), D(16));
                card.Controls.Add(now);
            }

            seg.BackColor = bg;
            seg.SetBounds(D(16), D(14 + 42 + 14), card.Width - D(32), D(38));
            card.Controls.Add(seg);

            if (hwChosen)
            {
                // Slow hardware mode for monitors that blink or restart on frequent DDC/CI writes.
                int sy = D(14 + 42 + 14 + 38 + 12);
                var sep = new Panel { BackColor = t.CardBorder };
                sep.SetBounds(D(16), sy, card.Width - D(32), 1);
                var tl = new Label2 { Text = "Slow mode", Px = 14, BackColor = bg };
                tl.SetBounds(D(16), sy + D(8), card.Width - D(130), D(20));
                var dl = new Label2
                {
                    Text = "Screen blinks or restarts? Brightness then jumps in one step, after you stop moving the slider",
                    Px = 12, Color = t.TextDim, BackColor = bg,
                    Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis,
                };
                dl.SetBounds(D(16), sy + D(28), card.Width - D(130), D(32));
                var tg = Toggle(ms.HwSlow, v => ms.HwSlow = v);
                tg.AccessibleName = "slow:" + st.Display.Id;
                tg.BackColor = bg;
                tg.SetBounds(card.Width - D(16) - D(84), sy + D(16), D(84), D(24));
                card.Controls.AddRange(new Control[] { sep, tl, dl, tg });
            }
        }

        // Note
        _y += D(8);
        var note = new Card { Fill = t.Note };
        note.SetBounds(CX, _y, RowW, D(64));
        var info = new Label2 { Text = "", Family = Theme.IconFont, Px = 16, Color = t.TextDim, BackColor = t.Note };
        info.SetBounds(D(16), D(14), D(16), D(18));
        var nt = new Label2 { Text = "No Hardware option?", Px = 14, Style = FontStyle.Bold, BackColor = t.Note };
        nt.SetBounds(D(44), D(12), note.Width - D(60), D(20));
        var nd = new Label2 { Text = "Turn on DDC/CI in the monitor’s on-screen menu, then reconnect the cable.", Px = 14, Color = t.TextDim, BackColor = t.Note };
        nd.SetBounds(D(44), D(32), note.Width - D(60), D(20));
        note.Controls.AddRange(new Control[] { info, nt, nd });
        _content.Controls.Add(note);
        _y += D(64) + D(4);
    }

    private void BuildHotkeys()
    {
        PageTitle("Hotkeys");
        var t = Theme.Current;
        Row("Use hotkeys", "Work everywhere, also in games", Toggle(_s.Hotkeys, v => { _s.Hotkeys = v; _hotkeysChanged(); BeginInvoke(Build); }), D(84), D(24));

        Header("Shortcuts");
        BeginGroup();
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
            string? desc = _s.Hotkeys && !box.Value.IsEmpty && !_hotkeyOk(a) ? "⚠ Used by another app. Pick another combo" : null;
            Row(HotkeyActions.Title(a), desc, box, D(200), D(32), compact: true, dim: !_s.Hotkeys);
        }
        EndGroup();
        Caption("Click a shortcut, then press the keys. Backspace clears it, Esc cancels.", D(8), t.TextTertiary);

        _y += D(16);
        Row("Step", "Change per press of Dim more / Dim less",
            SliderWithValue(1, 20, _s.HotkeyStep, v => $"{v}%", v => _s.HotkeyStep = v, D(36)), D(180 + 16 + 36), D(32));
    }

    private void BuildGeneral()
    {
        PageTitle("General");
        var t = Theme.Current;
        Row("Start with Windows", "Runs in the tray after you sign in",
            Toggle(_s.StartWithWindows, v => { _s.StartWithWindows = v; _setStartup(v); }), D(84), D(24));
        var folder = new FlatButton { Text = "Open folder", Glyph = "" };
        folder.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(Settings.Dir); System.Diagnostics.Process.Start("explorer.exe", Settings.Dir); } catch { }
        };
        Row("Settings file", "%AppData%\\BetterDarkScreen\\settings.json", folder, folder.PreferredWidth(), D(32), mono: true);

        Header("About");
        var name = new Label2 { Text = "BetterDarkScreen", Px = 14, Style = FontStyle.Bold };
        var card = NewCard(D(72), name);
        var bg = card.FillColor;
        var mark = new BrandView { Art = BrandView.Kind.MarkRaster, Shadow = 2, BackColor = bg };
        mark.SetBounds(D(16), D(16), D(37), D(42));
        name.BackColor = bg;
        name.SetBounds(D(16 + 35 + 16), D(18), card.Width - D(90), D(20));
        var ver = new Label2 { Text = $"Version {Application.ProductVersion.Split('+')[0]}", Px = 12, Color = t.TextDim, BackColor = bg };
        ver.SetBounds(D(16 + 35 + 16), D(38), card.Width - D(90), D(16));
        var open = new Label2
        {
            Text = "\uE8A7", Family = Theme.IconFont, Px = 14, Color = t.TextDim, BackColor = bg,
            Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
        };
        open.SetBounds(card.Width - D(40), D(26), D(24), D(20));
        card.Controls.AddRange(new Control[] { mark, name, ver, open });
        ver.Text += "  ·  github.com/dehobitto/BetterDarkScreen";
        foreach (Control c in card.Controls.Cast<Control>().Append(card))
        {
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => OpenUrl(RepoUrl);
        }
        card.AccessibleName = "Open BetterDarkScreen on GitHub";
    }

    private const string RepoUrl = "https://github.com/dehobitto/BetterDarkScreen";

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ------------------------------------------------------------ building blocks

    private void PageTitle(string title, string? subtitle = null)
    {
        var h = new Label2 { Text = title, Px = 28, Style = FontStyle.Bold, Family = Theme.DisplayFont, BackColor = Theme.Current.Bg };
        h.SetBounds(CX, _y, RowW, D(36));
        _content.Controls.Add(h);
        _y += D(36);
        if (subtitle == null) { _y += D(24); return; }
        _y += D(4);
        var sub = new Label2 { Text = subtitle, Px = 14, Color = Theme.Current.TextDim, BackColor = Theme.Current.Bg };
        sub.SetBounds(CX, _y, RowW, D(20));
        _content.Controls.Add(sub);
        _y += D(20) + D(20);
    }

    /// <summary>Group header ("Behavior", "Shortcuts", "About").</summary>
    private void Header(string text)
    {
        EndGroup();
        _y += D(28) - D(4); // previous card already added 4 below itself
        var h = new Label2 { Text = text, Px = 14, Style = FontStyle.Bold, BackColor = Theme.Current.Bg };
        h.SetBounds(CX + D(2), _y, RowW, D(20));
        _content.Controls.Add(h);
        _y += D(20) + D(8);
    }

    private Label2 Caption(string text, int top = 0, Color? color = null)
    {
        EndGroup();
        var t = Theme.Current;
        var l = new Label2 { Text = text, Px = 12, Color = color ?? t.TextDim, BackColor = t.Bg };
        _y += top;
        l.SetBounds(CX + D(2), _y, RowW, D(16));
        _content.Controls.Add(l);
        _y += D(16) + D(4);
        return l;
    }

    /// <summary>Cards added until <see cref="EndGroup"/> are stacked 2 px apart and share outer corners.</summary>
    private void BeginGroup() { EndGroup(); _group = new List<Card>(); }

    private void EndGroup()
    {
        if (_group == null) return;
        var g = _group; _group = null;
        for (int i = 0; i < g.Count; i++)
        {
            g[i].RadiusTop = i == 0 ? 6 : 0;
            g[i].RadiusBottom = i == g.Count - 1 ? 6 : 0;
        }
        if (g.Count > 0) _y += D(2); // groups end with the normal 4 px gap
    }

    private Card NewCard(int h, Control control, int? gap = null)
    {
        var card = new Card { Highlight = _highlight != null && control.AccessibleName == _highlight };
        card.SetBounds(CX, _y, RowW, h);
        _content.Controls.Add(card);
        _y += h + (gap ?? (_group != null ? D(2) : D(4)));
        _group?.Add(card);
        return card;
    }

    /// <summary>Settings card: title (+ description) left, control right. Min height 64 (52 when compact).</summary>
    private void Row(string title, string? desc, Control control, int cw, int ch, bool compact = false, bool mono = false, bool dim = false)
    {
        var t = Theme.Current;
        control.AccessibleName ??= title;
        int ip = D(16);
        int textW = RowW - ip * 2 - D(16) - cw;
        var descFont = Fonts.Get(mono ? Theme.MonoFont : Theme.TextFont, 12 * S);
        int descH = 0;
        if (desc != null)
        {
            var sz = TextRenderer.MeasureText(desc, descFont, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            descH = Math.Max(D(16), sz.Height);
        }
        int textH = D(20) + descH;
        int h = Math.Max(D(desc == null && compact ? 52 : 64), textH + D(24));
        var card = NewCard(h, control);
        var bg = card.FillColor;
        int ty = (h - textH) / 2;
        var tl = new Label2 { Text = title, Px = 14, BackColor = bg, Color = dim ? t.TextDisabled : null };
        tl.SetBounds(ip, ty, textW, D(20));
        card.Controls.Add(tl);
        if (desc != null)
        {
            var dl = new Label2
            {
                Text = desc, Px = 12, Family = mono ? Theme.MonoFont : Theme.TextFont, BackColor = bg,
                Color = desc.StartsWith("⚠") ? t.Warning : dim ? t.TextDisabled : t.TextDim,
                Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis,
            };
            dl.SetBounds(ip, ty + D(20), textW, descH);
            card.Controls.Add(dl);
        }
        SetBg(control, bg);
        control.SetBounds(card.Width - ip - cw, (h - ch) / 2, cw, ch);
        card.Controls.Add(control);
    }

    /// <summary>Card with title + description on top and a full-width selector under it.</summary>
    private void BlockRow(string title, string desc, Control control)
    {
        var t = Theme.Current;
        control.AccessibleName ??= title;
        int ip = D(16), ch = D(38);
        int h = D(14) + D(36) + D(12) + ch + D(16);
        var card = NewCard(h, control);
        var bg = card.FillColor;
        var tl = new Label2 { Text = title, Px = 14, BackColor = bg };
        tl.SetBounds(ip, D(14), card.Width - ip * 2, D(20));
        var dl = new Label2 { Text = desc, Px = 12, Color = t.TextDim, BackColor = bg };
        dl.SetBounds(ip, D(34), card.Width - ip * 2, D(16));
        SetBg(control, bg);
        control.SetBounds(ip, D(14 + 36 + 12), card.Width - ip * 2, ch);
        card.Controls.AddRange(new[] { tl, dl, control });
    }

    /// <summary>Indented sub-row inside a group: monitor glyph, name, control on the right.</summary>
    private void IndentRow(string title, Control control, int cw)
    {
        var t = Theme.Current;
        var card = NewCard(D(52), control);
        var bg = card.FillColor;
        var glyph = new Label2 { Text = "", Family = Theme.IconFont, Px = 16, Color = t.TextDim, BackColor = bg };
        glyph.SetBounds(D(46), D(16), D(16), D(20));
        var tl = new Label2 { Text = title, Px = 14, BackColor = bg };
        tl.SetBounds(D(74), D(16), card.Width - D(74) - cw - D(32), D(20));
        SetBg(control, bg);
        control.SetBounds(card.Width - D(16) - cw, D(10), cw, D(32));
        card.Controls.AddRange(new[] { glyph, tl, control });
    }

    /// <summary>"✓ Active" style status in place of a control.</summary>
    private Control StatusText(string glyph, string text)
    {
        var t = Theme.Current;
        var wrap = new Panel();
        var ic = new Label2 { Text = glyph, Family = Theme.IconFont, Px = 12, Color = t.TextDim, Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding };
        var lb = new Label2 { Text = text, Px = 14, Color = t.TextDim, Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding };
        wrap.Resize += (_, _) =>
        {
            int lw = TextRenderer.MeasureText(text, Fonts.Get(Theme.TextFont, 14 * S)).Width + D(2);
            lb.SetBounds(wrap.Width - lw, 0, lw, wrap.Height);
            ic.SetBounds(wrap.Width - lw - D(22), 0, D(14), wrap.Height);
        };
        wrap.Controls.AddRange(new Control[] { ic, lb });
        return wrap;
    }

    private static void SetBg(Control c, Color bg)
    {
        c.BackColor = bg;
        foreach (Control k in c.Controls) if (k is not TextBox) SetBg(k, bg);
    }

    private Control SliderWithValue(int min, int max, int value, Func<int, string> fmt, Action<int> set, int valueW)
    {
        var wrap = new Panel();
        var lbl = new Label2 { Text = fmt(value), Px = 14, Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding };
        var sl = new FlatSlider { Minimum = min, Maximum = max, LargeChange = Math.Max(1, (max - min) / 9) };
        sl.SetSilently(value);
        sl.ValueChanged += (_, _) => { lbl.Text = fmt(sl.Value); set(sl.Value); };
        sl.Committed += (_, _) => _changed();
        wrap.Resize += (_, _) =>
        {
            sl.SetBounds(0, 0, wrap.Width - valueW - D(16), wrap.Height);
            lbl.SetBounds(wrap.Width - valueW, 0, valueW, wrap.Height);
        };
        wrap.Controls.AddRange(new Control[] { sl, lbl });
        return wrap;
    }

    private FieldBox Field(string text, string suffix)
    {
        var f = new FieldBox { Suffix = suffix };
        f.Text = text;
        return f;
    }

    private ToggleSwitch Toggle(bool value, Action<bool> set)
    {
        var tg = new ToggleSwitch { ShowState = true };
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
