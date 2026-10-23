using System.Drawing;

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
        public required Label2 Tag;
        public required Label2 WarnIcon;
        public required Label2 Warn;
        public bool WarnShown;
    }

    private readonly Settings _s;
    private readonly DimController _dim;
    private readonly Action _changed;               // settings changed -> save (debounced)
    private readonly Action<string?> _openSettings; // focus key, e.g. "unlock"
    private readonly List<Row> _rows = new();
    private Label2 _status = null!, _modeLbl = null!;
    private ToggleSwitch _master = null!;
    private FlatButton _pause = null!;
    private Segmented _mode = null!;
    private Panel _footer = null!;
    private bool _anchorTop;
    private int _edgeY;
    private PopupMenu? _menu;

    private float S => DeviceDpi / 96f;
    private int D(float dip) => (int)Math.Round(dip * S);
    private int W => D(380);
    private int Pad => D(16);

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
        BackColor = t.Bg;
        int inner = W - Pad * 2;

        // Header
        var icon = new Label2 { Text = "\uE708", Family = Theme.IconFont, Px = 16, Color = t.Accent, BackColor = t.Bg };
        icon.SetBounds(Pad, Pad, D(22), D(28));
        var title = new Label2 { Text = "BetterDarkScreen", Px = 15, Style = FontStyle.Bold, Family = Theme.DisplayFont, BackColor = t.Bg };
        title.SetBounds(Pad + D(28), Pad, inner - D(90), D(28));
        _master = new ToggleSwitch { BackColor = t.Bg, Checked = _s.Enabled, AccessibleName = "Dimming on/off" };
        _master.SetBounds(W - Pad - D(48), Pad, D(48), D(28));
        _master.CheckedChanged += (_, _) => { _s.Enabled = _master.Checked; if (_s.Enabled) _dim.Resume(); _dim.Refresh(); _changed(); };
        _status = new Label2 { Px = 12, Color = t.TextDim, BackColor = t.Bg };
        _status.SetBounds(Pad, Pad + D(30), inner, D(18));
        Controls.AddRange(new Control[] { icon, title, _master, _status });

        // Displays
        var displays = _dim.Displays;
        foreach (var d in displays)
        {
            var ms = _s.For(d.Id);
            var card = new Card();
            card.Width = inner;
            int cp = D(14), cw = inner - cp * 2;

            var glyph = new Label2 { Text = "\uE7F4", Family = Theme.IconFont, Px = 14, Color = t.TextDim, BackColor = t.Card };
            glyph.SetBounds(cp, D(12), D(20), D(24));
            var name = new Label2 { Text = d.Label, Px = 13, Style = FontStyle.Bold, BackColor = t.Card };
            name.SetBounds(cp + D(26), D(12), cw - D(200), D(24));
            var tag = new Label2 { Px = 12, Color = t.TextDim, BackColor = t.Card, Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis };
            tag.SetBounds(inner - cp - D(56) - D(110), D(12), D(110), D(24));
            var tog = new ToggleSwitch { Checked = ms.Enabled, BackColor = t.Card, AccessibleName = $"Dim {d.Label}" };
            tog.SetBounds(inner - cp - D(48), D(12), D(48), D(24));

            var sun = new Label2 { Text = "\uE706", Family = Theme.IconFont, Px = 14, Color = t.TextDim, BackColor = t.Card };
            sun.SetBounds(cp, D(50), D(20), D(28));
            var slider = new FlatSlider { Minimum = 0, Maximum = 95, LargeChange = 10, BackColor = t.Card, AccessibleName = $"Dim level {d.Label}" };
            slider.SetSilently(ms.Level);
            slider.SetBounds(cp + D(20), D(50), cw - D(70), D(28));
            var pct = new Label2 { Text = $"{ms.Level}%", Px = 13, BackColor = t.Card, Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding };
            pct.SetBounds(inner - cp - D(44), D(50), D(44), D(28));

            var warnIcon = new Label2 { Text = "\uE7BA", Family = Theme.IconFont, Px = 13, Color = t.Warning, BackColor = t.Card, Visible = false };
            warnIcon.SetBounds(cp, D(84), D(20), D(22));
            var warn = new Label2 { Px = 12, Color = t.Warning, BackColor = t.Card, Link = true, Visible = false,
                Text = "Gamma stops near 50%. Unlock deeper gamma in Settings" };
            warn.SetBounds(cp + D(24), D(84), cw - D(24), D(22));
            warn.Click += (_, _) => { Close(); _openSettings("unlock"); };

            var row = new Row { Id = d.Id, Card = card, Slider = slider, Pct = pct, Tag = tag, WarnIcon = warnIcon, Warn = warn };
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
        _modeLbl = new Label2 { Text = "Method", Px = 12, Color = t.TextDim, BackColor = t.Bg };
        _mode = new Segmented { Items = new[] { "Auto", "Overlay", "Gamma", "Hardware", "Custom" }, BackColor = t.Bg, AccessibleName = "Dimming method" };
        _mode.SetSilently((int)_s.Mode);
        _mode.SelectedChanged += (_, _) => { _s.Mode = (DimMode)_mode.Selected; _dim.Refresh(); _changed(); UpdateAll(); };
        Controls.AddRange(new Control[] { _modeLbl, _mode });

        // Footer
        _footer = new Panel { BackColor = t.Footer };
        var divider = new Panel { BackColor = t.Divider };
        divider.SetBounds(0, 0, W, 1);
        _pause = new FlatButton { BackColor = t.Footer, Glyph = "\uE769", Text = "Pause" };
        _pause.SetBounds(Pad, D(10), D(120), D(32));
        _pause.Click += (_, _) => OnPauseClick();
        var gear = new FlatButton { BackColor = t.Footer, Glyph = "\uE713", Subtle = true, AccessibleName = "Settings" };
        gear.SetBounds(W - Pad - D(36), D(10), D(36), D(32));
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
        int inner = W - Pad * 2;
        int y = Pad + D(58);
        foreach (var r in _rows)
        {
            int h = D(r.WarnShown ? 114 : 92);
            r.Card.SetBounds(Pad, y, inner, h);
            r.Warn.Visible = r.WarnIcon.Visible = r.WarnShown;
            y += h + D(8);
        }
        y += D(4);
        _modeLbl.SetBounds(Pad, y, D(60), D(32));
        _mode.SetBounds(Pad + D(58), y, inner - D(58), D(32));
        y += D(32) + Pad;
        int fh = D(52);
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
        if (_dim.IsPaused) { _dim.Resume(); UpdateStatus(); return; }
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
        string text;
        if (!_s.Enabled) text = "Off";
        else if (_dim.IsPaused)
            text = _dim.PausedUntil == DateTime.MaxValue ? "Paused" : $"Paused until {_dim.PausedUntil:HH:mm}";
        else
        {
            double f = _dim.ScheduleFactor;
            string state = f <= 0 ? "Waiting" : f < 1 ? $"Fading {f:P0}" : "Active";
            text = _s.Schedule == ScheduleKind.Always ? "Active" : $"{state}  ·  {Schedule.Describe(_s, DateTime.Now)}";
            if (_dim.FullscreenGame && _s.Mode is DimMode.Auto or DimMode.Custom) text += "  ·  game detected";
        }
        _status.Text = text;
        if (_pause != null)
        {
            bool p = _dim.IsPaused;
            _pause.Text = p ? "Resume" : "Pause";
            _pause.Glyph = p ? "\uE768" : "\uE769";
            _pause.Enabled = _s.Enabled;
            _pause.Invalidate();
        }
    }

    // ------------------------------------------------------------ window behavior

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(() =>
        {
            if (IsDisposed || _menu != null) return; // our pause menu took focus
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
