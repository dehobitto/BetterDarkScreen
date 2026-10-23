namespace BetterDarkScreen.Ui;

/// <summary>Quick panel shown on tray click (like Win11 quick settings). Created on demand, disposed on close.</summary>
internal sealed class FlyoutForm : PopupForm
{
    private sealed class Row
    {
        public required string Id;
        public required Card Card;
        public required FlatSlider Slider;
        public required Label2 Pct;
        public required Chip Tag;
        public required Label2 Name;
        public required Label2 WarnIcon;
        public required Label2 Warn;
        public bool WarnShown;
    }

    private static readonly string[] ModeItems = { "Auto", "Overlay", "Gamma", "Hardware", "Per monitor" };

    private readonly Settings _s;
    private readonly DimController _dim;
    private readonly Action _changed;               // settings changed -> save (debounced)
    private readonly Action<string?> _openSettings; // focus key, e.g. "unlock"
    private readonly List<Row> _rows = new();
    private Label2 _status = null!, _modeLbl = null!;
    private Chip _stateChip = null!;
    private ToggleSwitch _master = null!;
    private FlatButton _pause = null!;
    private DropDown _mode = null!;
    private Panel _footer = null!;
    private bool _anchorTop;
    private int _edgeY;
    private PopupMenu? _menu;

    private float S => DeviceDpi / 96f;
    private int D(float dip) => (int)Math.Round(dip * S);
    private int W => D(380);
    private int Pad => D(16);
    private int CardX => D(12);

    public FlyoutForm(Settings s, DimController dim, Action changed, Action<string?> openSettings)
    {
        _s = s; _dim = dim; _changed = changed; _openSettings = openSettings;
        Text = "BetterDarkScreen";
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    /// <summary>Show near the tray on the monitor under the cursor.</summary>
    public void ShowNearTray()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea; var b = screen.Bounds;
        Bounds = new Rectangle(wa.Right - 420, wa.Bottom - 320, 380, 300); // fully on target monitor => right DPI
        _ = Handle;

        int m = D(12);
        _anchorTop = wa.Top > b.Top;                       // taskbar on top
        _edgeY = _anchorTop ? wa.Top + m : wa.Bottom - m;
        Build();
        int x = wa.Left > b.Left ? wa.Left + m : wa.Right - Width - m; // taskbar on left?
        Location = new Point(x, Top);

        Show();
        Activate();
        _dim.StateChanged += OnState;
    }

    // ------------------------------------------------------------ build

    private void Build()
    {
        SuspendLayout();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        _rows.Clear();

        var t = Theme.Current;
        BackColor = t.Flyout;

        // Header: mark · name · master switch, then state chip + schedule text.
        var mark = new BrandView { Art = BrandView.Kind.MarkVector, BackColor = t.Flyout, Stroke = 1.9f };
        mark.SetBounds(Pad, Pad, D(20), D(20));
        var title = new Label2 { Text = "BetterDarkScreen", Px = 14, Style = FontStyle.Bold, BackColor = t.Flyout };
        title.SetBounds(Pad + D(30), Pad, W - Pad * 2 - D(80), D(20));
        _master = new ToggleSwitch { BackColor = t.Flyout, Checked = _s.Enabled, AccessibleName = "Dimming on/off" };
        _master.SetBounds(W - Pad - D(42), Pad, D(42), D(20));
        _master.CheckedChanged += (_, _) => { _s.Enabled = _master.Checked; if (_s.Enabled) _dim.Resume(); _dim.Refresh(); _changed(); };
        _stateChip = new Chip { BackColor = t.Flyout, Fore = t.Text };
        _stateChip.SetBounds(Pad + D(30), Pad + D(26), D(60), D(18));
        _status = new Label2 { Px = 12, Color = t.TextDim, BackColor = t.Flyout };
        _status.SetBounds(Pad + D(100), Pad + D(26), W - Pad * 2 - D(100), D(18));
        Controls.AddRange(new Control[] { mark, title, _master, _stateChip, _status });

        // Displays
        int inner = W - CardX * 2;
        foreach (var d in _dim.Displays)
        {
            var ms = _s.For(d.Id);
            var card = new Card { Fill = t.FlyoutCard, Border = t.FlyoutCardBorder, BackColor = t.Flyout };
            card.Width = inner;
            var bg = t.FlyoutCard;
            int cp = D(14);

            var glyph = new Label2 { Text = "\uE7F4", Family = Theme.IconFont, Px = 16, Color = t.TextDim, BackColor = bg };
            glyph.SetBounds(cp, D(12), D(16), D(20));
            var nameFont = Fonts.Get(Theme.TextFont, 14 * S, FontStyle.Bold);
            int nameW = Math.Min(TextRenderer.MeasureText(d.Label, nameFont).Width, inner - D(220));
            var name = new Label2 { Text = d.Label, Px = 14, Style = FontStyle.Bold, BackColor = bg };
            name.SetBounds(cp + D(26), D(12), nameW, D(20));
            var tag = new Chip { BackColor = bg };
            tag.SetBounds(cp + D(26) + nameW + D(10), D(13), D(80), D(18));
            var tog = new ToggleSwitch { Checked = ms.Enabled, BackColor = bg, AccessibleName = $"Dim {d.Label}" };
            tog.SetBounds(inner - cp - D(42), D(12), D(42), D(20));

            var sun = new Label2 { Text = "\uE706", Family = Theme.IconFont, Px = 16, Color = t.TextDim, BackColor = bg };
            sun.SetBounds(cp, D(42), D(16), D(20));
            var slider = new FlatSlider { Minimum = 0, Maximum = 95, LargeChange = 10, BackColor = bg, AccessibleName = $"Dim level {d.Label}" };
            slider.SetSilently(ms.Level);
            slider.SetBounds(cp + D(28), D(42), inner - cp * 2 - D(28) - D(52), D(20));
            var pct = new Label2 { Text = $"{ms.Level}%", Px = 14, BackColor = bg, Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding };
            pct.SetBounds(inner - cp - D(40), D(42), D(40), D(20));

            var warnIcon = new Label2 { Text = "\uE7BA", Family = Theme.IconFont, Px = 13, Color = t.Warning, BackColor = bg, Visible = false };
            warnIcon.SetBounds(cp, D(70), D(20), D(20));
            var warn = new Label2
            {
                Px = 12, Color = t.Warning, BackColor = bg, Link = true, Visible = false,
                Text = "Gamma stops near 50%. Unlock deeper gamma in Settings"
            };
            warn.SetBounds(cp + D(24), D(70), inner - cp * 2 - D(24), D(20));
            warn.Click += (_, _) => { Close(); _openSettings("unlock"); };

            var row = new Row { Id = d.Id, Card = card, Slider = slider, Pct = pct, Tag = tag, Name = name, WarnIcon = warnIcon, Warn = warn };
            string id = d.Id;
            slider.ValueChanged += (_, _) =>
            {
                ms.Level = slider.Value;
                pct.Text = $"{slider.Value}%";
                if (_s.LinkMonitors)
                    foreach (var r in _rows.Where(r => r.Id != id))
                    { _s.For(r.Id).Level = slider.Value; r.Slider.SetSilently(slider.Value); r.Pct.Text = $"{slider.Value}%"; }
                _dim.Refresh();
                UpdateRows();
            };
            slider.Committed += (_, _) => _changed();
            tog.CheckedChanged += (_, _) =>
            {
                ms.Enabled = tog.Checked;
                slider.Enabled = tog.Checked;
                _dim.Refresh(); _changed();
            };
            slider.Enabled = ms.Enabled;

            card.Controls.AddRange(new Control[] { glyph, name, tag, tog, sun, slider, pct, warnIcon, warn });
            Controls.Add(card);
            _rows.Add(row);
        }

        // Method
        _modeLbl = new Label2 { Text = "Method", Px = 14, BackColor = t.Flyout };
        _mode = new DropDown { Items = ModeItems, BackColor = t.Flyout, AccessibleName = "Dimming method" };
        _mode.SetSilently((int)_s.Mode);
        _mode.SelectedChanged += (_, _) => { _s.Mode = (DimMode)_mode.Selected; _dim.Refresh(); _changed(); UpdateAll(); };
        _mode.MenuClosed += (_, _) => BeginInvoke(() => { if (!IsDisposed && Form.ActiveForm != this) Close(); });
        Controls.AddRange(new Control[] { _modeLbl, _mode });

        // Footer
        _footer = new Panel { BackColor = t.Bg };
        var divider = new Panel { BackColor = t.Divider };
        divider.SetBounds(0, 0, W, 1);
        _pause = new FlatButton { BackColor = t.Bg, Glyph = "\uE769", Text = "Pause for 1 hour" };
        _pause.SetBounds(Pad, D(12), _pause.PreferredWidth(), D(32));
        _pause.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowPauseMenu(); };
        _pause.Click += (_, _) => OnPauseClick();
        var gear = new FlatButton { BackColor = t.Bg, Glyph = "\uE713", Subtle = true, AccessibleName = "Open settings" };
        gear.SetBounds(W - Pad - D(36), D(12), D(36), D(32));
        gear.Click += (_, _) => { Close(); _openSettings(null); };
        _footer.Controls.AddRange(new Control[] { divider, _pause, gear });
        Controls.Add(_footer);

        ResumeLayout();
        UpdateAll();
    }

    /// <summary>Positions everything; called when a warning appears/disappears.</summary>
    private void Relayout()
    {
        SuspendLayout();
        int inner = W - CardX * 2;
        int y = Pad + D(26) + D(18) + D(12);
        foreach (var r in _rows)
        {
            int h = D(r.WarnShown ? 102 : 76);
            r.Card.SetBounds(CardX, y, inner, h);
            r.Warn.Visible = r.WarnIcon.Visible = r.WarnShown;
            y += h + D(4);
        }
        y += D(10);
        _modeLbl.SetBounds(Pad, y, D(120), D(32));
        _mode.SetBounds(W - Pad - D(168), y, D(168), D(32));
        y += D(32) + D(14);
        int fh = D(56);
        _footer.SetBounds(0, y, W, fh);
        int height = y + fh;
        var top = _anchorTop ? _edgeY : _edgeY - height;
        Bounds = new Rectangle(Left, top, W, height);
        ResumeLayout();
        Invalidate(true);
    }

    // ------------------------------------------------------------ state

    private void UpdateAll()
    {
        UpdateStatus();
        UpdateRows(force: true);
    }

    private void UpdateRows(bool force = false)
    {
        bool layout = force;
        foreach (var r in _rows)
        {
            var st = _dim.State(r.Id);
            if (st == null) continue;
            r.Tag.Text = TagText(st);
            bool warn = st.Channel == Channel.Gamma && _s.For(r.Id).Enabled
                        && (st.GammaLimited || _dim.GammaWouldLimit(r.Id, r.Slider.Value));
            if (warn)
                r.Warn.Text = _dim.GammaUnlocked
                    ? "Gamma limit still active. Sign out to apply the unlock"
                    : "Gamma stops near 50%. Unlock deeper gamma in Settings";
            if (warn != r.WarnShown) { r.WarnShown = warn; layout = true; }
        }
        if (layout) Relayout();
    }

    internal static string TagText(MonitorState st)
    {
        string ch = st.Channel switch { Channel.Gamma => "Gamma", Channel.Hardware => "Hardware", _ => "Overlay" };
        if (st.Requested == MonitorMethod.Hardware && st.HwSupported == null) return "Checking hardware…";
        if (st.Fallback) return st.Requested == MonitorMethod.Hardware ? "Overlay · no DDC/CI" : "Overlay · no gamma";
        return st.Requested == MonitorMethod.Auto ? $"Auto · {ch}" : ch;
    }

    private void OnPauseClick()
    {
        if (_dim.IsPaused) _dim.Resume();
        else _dim.Pause(TimeSpan.FromHours(1));
        UpdateStatus();
    }

    /// <summary>Right-click on Pause: other durations.</summary>
    private void ShowPauseMenu()
    {
        if (_menu != null || !_s.Enabled) return;
        _menu = new PopupMenu()
            .Add("For 15 minutes", "\uE916", () => Pause(TimeSpan.FromMinutes(15)))
            .Add("For 1 hour", "\uE823", () => Pause(TimeSpan.FromHours(1)))
            .Add("For 3 hours", "\uE823", () => Pause(TimeSpan.FromHours(3)))
            .Add("Until I resume", "\uE769", () => Pause(null));
        _menu.FormClosed += (_, _) =>
        {
            _menu = null;
            // Clicked outside both windows -> close the panel too.
            BeginInvoke(() => { if (!IsDisposed && Form.ActiveForm != this) Close(); });
        };
        var p = _pause.PointToScreen(new Point(0, 0));
        _menu.ShowAt(new Point(p.X, p.Y - D(4)), above: !_anchorTop, owner: this);
        if (_anchorTop) _menu.Location = new Point(_menu.Left, p.Y + _pause.Height + D(4));
    }

    private void Pause(TimeSpan? span) { _dim.Pause(span); UpdateStatus(); Activate(); }

    private void OnState()
    {
        if (IsDisposed) return;
        if (_dim.Displays.Count != _rows.Count) { Close(); return; } // display list changed while open
        UpdateStatus();
        UpdateRows();
    }

    private void UpdateStatus()
    {
        if (_status == null) return;
        string chip, text;
        if (!_s.Enabled) { chip = "Off"; text = "Dimming is turned off"; }
        else if (_dim.IsPaused)
        {
            chip = "Paused";
            text = _dim.PausedUntil == DateTime.MaxValue ? "Until you resume" : $"Until {_dim.PausedUntil:HH:mm}";
        }
        else
        {
            double f = _dim.ScheduleFactor;
            chip = _s.Schedule == ScheduleKind.Always ? "Active" : f <= 0 ? "Waiting" : f < 1 ? $"Fading {f:P0}" : "Active";
            text = Schedule.DescribeShort(_s, DateTime.Now);
            if (_dim.FullscreenGame && _s.Mode is DimMode.Auto or DimMode.Custom) text += " · game detected";
        }
        _stateChip.Text = chip;
        _status.Left = _stateChip.Right + D(8);
        _status.Width = W - Pad - _status.Left;
        _status.Text = text;
        if (_mode != null && _mode.Selected != (int)_s.Mode) _mode.SetSilently((int)_s.Mode);
        if (_pause != null)
        {
            bool p = _dim.IsPaused;
            _pause.Text = p ? "Resume" : "Pause for 1 hour";
            _pause.Glyph = p ? "\uE768" : "\uE769";
            _pause.Enabled = _s.Enabled;
            _pause.Width = _pause.PreferredWidth();
            _pause.Invalidate();
        }
    }

    // ------------------------------------------------------------ window behavior

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(() =>
        {
            if (IsDisposed || _menu != null || (_mode != null && _mode.MenuOpen)) return; // our own menu took focus
            if (!ContainsFocus && Form.ActiveForm != this) Close();
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Build();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _dim.StateChanged -= OnState;
        _menu?.Close();
        base.OnFormClosed(e);
    }
}
