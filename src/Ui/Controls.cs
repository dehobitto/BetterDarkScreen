using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace BetterDarkScreen.Ui;

/// <summary>Base for owner-drawn controls: double buffered, DPI aware, hover tracking.</summary>
internal abstract class FlatControl : Control
{
    protected bool Hover, Pressed;
    protected Theme T => Theme.Current;
    protected float S => DeviceDpi / 96f;

    protected FlatControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected Graphics Prep(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        return g;
    }

    protected void DrawFocus(Graphics g, RectangleF r, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        r.Inflate(2 * S, 2 * S);
        Theme.StrokeRound(g, T.Text, r, radius + 2 * S, 1.5f * S);
    }
}

/// <summary>Win11 toggle switch (40×20, knob 12). Optional "On"/"Off" caption on the left.</summary>
internal sealed class ToggleSwitch : FlatControl
{
    private bool _on;
    public event EventHandler? CheckedChanged;
    /// <summary>Draw "On"/"Off" left of the switch (settings rows).</summary>
    public bool ShowState { get; set; }

    public ToggleSwitch() { TabStop = true; }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SetSilently(bool v) { _on = v; Invalidate(); }

    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { Checked = !Checked; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S, w = 40 * s, h = 20 * s;
        var r = new RectangleF(Width - w - 1, (Height - h) / 2f, w, h);
        float knob = (Hover || Pressed ? 14 : 12) * s;
        var off = Enabled ? T.TextDim : T.TextDisabled;

        if (ShowState)
        {
            var tr = new Rectangle(0, 0, (int)(r.X - 12 * s), Height);
            TextRenderer.DrawText(g, _on ? "On" : "Off", Fonts.Get(Theme.TextFont, 14 * s), tr, Enabled ? T.Text : T.TextDisabled,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        if (_on)
        {
            var fill = Enabled ? (Hover ? T.AccentHover : T.Accent) : T.TextDisabled;
            Theme.FillRound(g, fill, r, h / 2);
            var k = new RectangleF(r.Right - 3 * s - 6 * s - knob / 2, r.Y + (h - knob) / 2, knob, knob);
            using var b = new SolidBrush(T.OnAccent);
            g.FillEllipse(b, k);
        }
        else
        {
            if (Hover && Enabled) Theme.FillRound(g, T.ControlHover, r, h / 2);
            var rr = r; rr.Inflate(-0.5f * s, -0.5f * s);
            Theme.StrokeRound(g, off, rr, h / 2, 1f * s);
            var k = new RectangleF(r.X + 3 * s + 6 * s - knob / 2, r.Y + (h - knob) / 2, knob, knob);
            using var b = new SolidBrush(off);
            g.FillEllipse(b, k);
        }
        DrawFocus(g, r, h / 2);
    }
}

/// <summary>Win11 slider with value range and wheel/keyboard support.</summary>
internal sealed class FlatSlider : FlatControl
{
    private int _value, _min, _max = 100;
    private bool _drag;
    public int SmallChange { get; set; } = 1;
    public int LargeChange { get; set; } = 5;
    public event EventHandler? ValueChanged;
    /// <summary>Raised when the user releases the thumb (good moment to save).</summary>
    public event EventHandler? Committed;

    public FlatSlider() { TabStop = true; }

    public int Minimum { get => _min; set { _min = value; Value = _value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Value = _value; Invalidate(); } }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSilently(int v) { _value = Math.Clamp(v, _min, _max); Invalidate(); }

    private RectangleF Track
    {
        get
        {
            float pad = 10 * S;
            return new RectangleF(pad, Height / 2f - 2 * S, Width - pad * 2, 4 * S);
        }
    }

    private float Frac => _max == _min ? 0 : (float)(_value - _min) / (_max - _min);

    private void SetFromX(int x)
    {
        var t = Track;
        float f = Math.Clamp((x - t.X) / t.Width, 0, 1);
        Value = _min + (int)Math.Round(f * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus(); _drag = true; Capture = true; SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag) { _drag = false; Capture = false; Committed?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta) * SmallChange * 2;
        Committed?.Invoke(this, EventArgs.Empty);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int d = e.KeyCode switch
        {
            Keys.Left or Keys.Down => -SmallChange,
            Keys.Right or Keys.Up => SmallChange,
            Keys.PageDown => -LargeChange,
            Keys.PageUp => LargeChange,
            Keys.Home => int.MinValue / 2,
            Keys.End => int.MaxValue / 2,
            _ => 0,
        };
        if (d != 0) { Value = (int)Math.Clamp((long)_value + d, _min, _max); Committed?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var t = Track;
        float s = S;
        Theme.FillRound(g, T.SliderTrack, t, t.Height / 2);
        var fill = new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * Frac), t.Height);
        Theme.FillRound(g, Enabled ? T.Accent : T.TextDisabled, fill, t.Height / 2);

        float cx = t.X + t.Width * Frac, cy = Height / 2f;
        float outer = 20 * s;
        var o = new RectangleF(cx - outer / 2, cy - outer / 2, outer, outer);
        using (var b = new SolidBrush(T.Thumb)) g.FillEllipse(b, o);
        using (var p = new Pen(T.ThumbBorder, 1 * s)) g.DrawEllipse(p, RectangleF.Inflate(o, -0.5f * s, -0.5f * s));
        float inner = (_drag ? 10 : Hover ? 14 : 12) * s;
        using (var b = new SolidBrush(Enabled ? (Hover ? T.AccentHover : T.Accent) : T.TextDisabled))
            g.FillEllipse(b, cx - inner / 2, cy - inner / 2, inner, inner);
        if (Focused && ShowFocusCues)
        {
            var f = o; f.Inflate(3 * s, 3 * s);
            using var p = new Pen(T.Text, 1.5f * s);
            g.DrawEllipse(p, f);
        }
    }
}

/// <summary>Row of mutually exclusive options (Win11 selector: raised pill + accent underline).</summary>
internal sealed class Segmented : FlatControl
{
    private string[] _items = Array.Empty<string>();
    private bool[] _disabled = Array.Empty<bool>();
    private int _sel, _hot = -1;
    public event EventHandler? SelectedChanged;

    public Segmented() { TabStop = true; }

    public string[] Items { get => _items; set { _items = value; _disabled = new bool[value.Length]; Invalidate(); } }

    public void SetDisabled(int index, bool disabled)
    {
        if (index >= 0 && index < _disabled.Length) { _disabled[index] = disabled; Invalidate(); }
    }

    public int Selected
    {
        get => _sel;
        set
        {
            value = Math.Clamp(value, 0, Math.Max(0, _items.Length - 1));
            if (_sel == value || IsDisabled(value)) return;
            _sel = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsDisabled(int i) => i >= 0 && i < _disabled.Length && _disabled[i];

    public void SetSilently(int v) { _sel = v; Invalidate(); }

    private RectangleF Cell(int i)
    {
        // Track has 1px border + 2px padding, 2px gap between cells.
        float s = S, pad = 3 * s, gap = 2 * s;
        int n = Math.Max(1, _items.Length);
        float w = (Width - pad * 2 - gap * (n - 1)) / n;
        return new RectangleF(pad + i * (w + gap), pad, w, Height - pad * 2);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Length; i++) if (Cell(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != _hot) { _hot = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { Focus(); int h = HitTest(e.Location); if (h >= 0) Selected = h; base.OnMouseClick(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int dir = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
        if (dir != 0)
            for (int i = _sel + dir; i >= 0 && i < _items.Length; i += dir)
                if (!IsDisabled(i)) { Selected = i; break; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S, rad = 6 * s;
        var all = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, T.SegTrack, all, rad);
        Theme.StrokeRound(g, T.SegBorder, all, rad, 1f);

        for (int i = 0; i < _items.Length; i++)
        {
            var c = Cell(i);
            bool dis = IsDisabled(i), sel = i == _sel;
            Color fg = dis ? T.TextDisabled : (sel ? T.Text : T.TextDim);
            if (sel)
            {
                Theme.FillRound(g, T.SegSelected, c, 4 * s);
                if (!T.Dark) Theme.StrokeRound(g, T.CardBorder, c, 4 * s, 1f);
                float uw = 16 * s, uh = 3 * s;
                Theme.FillRound(g, T.Accent, new RectangleF(c.X + (c.Width - uw) / 2, c.Bottom - uh - 2 * s, uw, uh), uh / 2);
            }
            else if (i == _hot && !dis)
            {
                Theme.FillRound(g, Theme.Blend(T.SegTrack, T.Text, 0.06), c, 4 * s);
                fg = T.Text;
            }
            var font = Fonts.Get(Theme.TextFont, 14 * s, sel ? FontStyle.Bold : FontStyle.Regular);
            var tr = Rectangle.Round(new RectangleF(c.X + 2 * s, c.Y, c.Width - 4 * s, c.Height - (sel ? 2 * s : 0)));
            TextRenderer.DrawText(g, _items[i], font, tr, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        DrawFocus(g, all, rad);
    }
}

/// <summary>Click, then press a key combination. Backspace/Delete clears, Esc cancels.</summary>
internal sealed class HotkeyBox : FlatControl
{
    private Hotkey _value;
    private bool _capturing;
    private string? _error;
    public event EventHandler? ValueChanged;
    /// <summary>Raised when capture starts (true) / ends (false), so global hotkeys can be paused.</summary>
    public event Action<bool>? Capturing;

    public HotkeyBox() { TabStop = true; Cursor = Cursors.Hand; }

    public Hotkey Value { get => _value; set { _value = value; Invalidate(); } }
    /// <summary>Shown under the field when the hotkey couldn't be registered.</summary>
    public string? Error { get => _error; set { _error = value; Invalidate(); } }

    protected override void OnClick(EventArgs e) { Focus(); Start(); base.OnClick(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Stop(); base.OnLostFocus(e); }

    private void Start() { if (_capturing) return; _capturing = true; Capturing?.Invoke(true); Invalidate(); }
    private void Stop() { if (!_capturing) return; _capturing = false; Capturing?.Invoke(false); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing)
        {
            if ((keyData & Keys.KeyCode) is Keys.Enter or Keys.Space) { Start(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape) { Stop(); return true; }
        if (key is Keys.Back or Keys.Delete && (keyData & Keys.Modifiers) == 0)
        {
            _value = default; Stop(); ValueChanged?.Invoke(this, EventArgs.Empty); return true;
        }
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None) { Invalidate(); return true; }
        if (key == Keys.Tab && (keyData & Keys.Modifiers) == 0) { Stop(); return base.ProcessCmdKey(ref msg, keyData); }

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((keyData & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
        if (Native.GetKeyState(0x5B) < 0 || Native.GetKeyState(0x5C) < 0) mods |= Native.MOD_WIN;
        var hk = new Hotkey(mods, key);
        if (!hk.IsValid) { _error = "Add Ctrl, Alt, Shift or Win"; Invalidate(); return true; }
        _value = hk; _error = null;
        Stop();
        ValueChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S, rad = 4 * s;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var small = Fonts.Get(Theme.TextFont, 12 * s);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        if (_capturing)
        {
            Theme.FillRound(g, T.Control, r, rad);
            Theme.StrokeRound(g, T.Text, r, rad, 1f * s);
            var live = new List<string>();
            if ((ModifierKeys & Keys.Control) != 0) live.Add("Ctrl");
            if ((ModifierKeys & Keys.Alt) != 0) live.Add("Alt");
            if ((ModifierKeys & Keys.Shift) != 0) live.Add("Shift");
            if (live.Count > 0) { live.Add("…"); DrawCaps(g, live, r); }
            else TextRenderer.DrawText(g, "Press keys…", Fonts.Get(Theme.TextFont, 13 * s), Rectangle.Round(r), T.Text, flags | TextFormatFlags.HorizontalCenter);
        }
        else if (_value.IsEmpty)
        {
            using (var path = Theme.RoundRect(r, rad))
            using (var pen = new Pen(Hover && Enabled ? T.TextDim : T.Dashed, 1f) { DashPattern = new[] { 3f, 2f } })
                g.DrawPath(pen, path);
            const string label = "Set shortcut";
            int tw = TextRenderer.MeasureText(g, label, small, Size.Empty, flags).Width, iw = (int)(12 * s), gap = (int)(6 * s);
            int x = (Width - tw - iw - gap) / 2;
            TextRenderer.DrawText(g, "\uE710", Fonts.Get(Theme.IconFont, 10 * s), new Rectangle(x, 0, iw, Height), T.TextDim, flags | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, label, small, new Rectangle(x + iw + gap, 0, tw + 2, Height), T.TextDim, flags);
        }
        else
        {
            Theme.FillRound(g, Hover && Enabled ? T.ControlHover : T.Control, r, rad);
            Theme.StrokeRound(g, T.ControlBorder, r, rad, 1f);
            DrawCaps(g, _value.Display().Split(" + ").ToList(), r);
        }

        if (!Enabled)
            using (var b = new SolidBrush(Color.FromArgb(153, BackColor))) g.FillRectangle(b, ClientRectangle);
        DrawFocus(g, r, rad);
    }

    /// <summary>Keycaps, right-aligned: Ctrl  Alt  PgDn.</summary>
    private void DrawCaps(Graphics g, List<string> keys, RectangleF r)
    {
        float s = S;
        var font = Fonts.Get(Theme.TextFont, 12 * s);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var widths = keys.Select(k => TextRenderer.MeasureText(g, k, font, Size.Empty, flags).Width + 14 * s).ToList();
        float gap = 4 * s, h = 18 * s;
        float x = r.Right - 8 * s - widths.Sum() - gap * (keys.Count - 1);
        float y = r.Y + (r.Height - h) / 2;
        for (int i = 0; i < keys.Count; i++)
        {
            var cap = new RectangleF(x, y, widths[i], h);
            Theme.FillRound(g, T.KeycapEdge, new RectangleF(cap.X, cap.Y + 1 * s, cap.Width, cap.Height), 3 * s);
            Theme.FillRound(g, T.Keycap, cap, 3 * s);
            TextRenderer.DrawText(g, keys[i], font, Rectangle.Round(cap), T.Text, flags);
            x += widths[i] + gap;
        }
    }
}

/// <summary>Left navigation item (Win11 settings style).</summary>
internal sealed class NavItem : FlatControl
{
    public string Glyph { get; set; } = "";
    public bool Selected { get; set; }

    public NavItem() { TabStop = true; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        if (Selected) Theme.FillRound(g, T.NavSelected, r, 4 * s);
        else if (Hover) Theme.FillRound(g, Theme.Blend(BackColor, T.Text, 0.04), r, 4 * s);
        if (Selected)
            Theme.FillRound(g, T.Accent, new RectangleF(0, (Height - 16 * s) / 2, 3 * s, 16 * s), 1.5f * s);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Glyph, Fonts.Get(Theme.IconFont, 16 * s), new Rectangle((int)(12 * s), 0, (int)(18 * s), Height), T.Text, flags);
        TextRenderer.DrawText(g, Text, Fonts.Get(Theme.TextFont, 14 * s), new Rectangle((int)(42 * s), 0, Width - (int)(46 * s), Height), T.Text, flags | TextFormatFlags.EndEllipsis);
        DrawFocus(g, r, 4 * s);
    }
}

/// <summary>Rounded button; optional Fluent icon glyph; Accent style.</summary>
internal sealed class FlatButton : FlatControl
{
    public string Glyph { get; set; } = "";
    public bool Accent { get; set; }
    public bool Subtle { get; set; }   // no background until hover (toolbar style)

    public FlatButton() { TabStop = true; }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    /// <summary>Width that fits glyph + text with Win11 padding (14 px each side).</summary>
    public int PreferredWidth()
    {
        float s = S;
        int textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Fonts.Get(Theme.TextFont, 14 * s)).Width;
        int iconW = string.IsNullOrEmpty(Glyph) ? 0 : (int)(16 * s);
        int gap = textW > 0 && iconW > 0 ? (int)(8 * s) : 0;
        return textW + iconW + gap + (int)(28 * s);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S, rad = 4 * s;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fg;
        if (Accent)
        {
            Theme.FillRound(g, Pressed ? Theme.Blend(T.Accent, T.Bg, 0.2) : Hover ? T.AccentHover : T.Accent, r, rad);
            fg = T.OnAccent;
        }
        else
        {
            if (!Subtle || Hover || Pressed)
            {
                var bg = Pressed ? Theme.Blend(T.Control, BackColor, 0.4) : Hover ? T.ControlHover : T.Control;
                Theme.FillRound(g, bg, r, rad);
                if (!Subtle)
                {
                    Theme.StrokeRound(g, T.ControlBorder, r, rad, 1f);
                    using var p = new Pen(T.ControlBottom, 1f);
                    g.DrawLine(p, r.X + rad, r.Bottom, r.Right - rad, r.Bottom);
                }
            }
            fg = Enabled ? (Pressed ? T.TextDim : T.Text) : T.TextDisabled;
        }

        var font = Fonts.Get(Theme.TextFont, 14 * s);
        var icon = Fonts.Get(Theme.IconFont, 16 * s);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        int textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, font, Size.Empty, flags).Width;
        int iconW = string.IsNullOrEmpty(Glyph) ? 0 : (int)(16 * s);
        int gap = textW > 0 && iconW > 0 ? (int)(8 * s) : 0;
        int x = (Width - (textW + iconW + gap)) / 2;
        if (iconW > 0)
            TextRenderer.DrawText(g, Glyph, icon, new Rectangle(x, 0, iconW, Height), fg, flags | TextFormatFlags.HorizontalCenter);
        if (textW > 0)
            TextRenderer.DrawText(g, Text, font, new Rectangle(x + iconW + gap, 0, textW + 2, Height), fg, flags);
        DrawFocus(g, r, rad);
    }
}

/// <summary>Plain text label drawn with GDI (crisp, no background artifacts).</summary>
internal sealed class Label2 : FlatControl
{
    public Color? Color { get; set; }
    public float Px { get; set; } = 13;
    public FontStyle Style { get; set; } = FontStyle.Regular;
    public string Family { get; set; } = Theme.TextFont;
    public TextFormatFlags Flags { get; set; } = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    /// <summary>Acts like a hyperlink: hand cursor, underline on hover.</summary>
    public bool Link { get; set; }

    public Label2() { TabStop = false; SetStyle(ControlStyles.Selectable, false); }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnMouseEnter(EventArgs e) { if (Link) Cursor = Cursors.Hand; base.OnMouseEnter(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var style = Link && Hover ? Style | FontStyle.Underline : Style;
        TextRenderer.DrawText(e.Graphics, Text, Fonts.Get(Family, Px * S, style), ClientRectangle, Color ?? T.Text, Flags);
    }
}

/// <summary>Rounded text field hosting a borderless TextBox.</summary>
internal sealed class FieldBox : FlatControl
{
    public readonly TextBox Box = new() { BorderStyle = BorderStyle.None };
    public string Suffix { get; set; } = "";

    public FieldBox()
    {
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.MouseEnter += (_, _) => { Hover = true; Invalidate(); };
        Box.MouseLeave += (_, _) => { Hover = false; Invalidate(); };
        TabStop = false;
    }

    [AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value; }

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        float s = S;
        Box.Font = Fonts.Get(Theme.TextFont, 13 * s);
        Box.BackColor = T.Control;
        Box.ForeColor = T.Text;
        int sufW = string.IsNullOrEmpty(Suffix) ? 0 : TextRenderer.MeasureText(Suffix, Fonts.Get(Theme.TextFont, 13 * s)).Width;
        int pad = (int)(10 * s);
        Box.SetBounds(pad, (Height - Box.PreferredHeight) / 2, Math.Max(10, Width - pad * 2 - sufW), Box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S, rad = 4 * s;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, T.Control, r, rad);
        Theme.StrokeRound(g, T.ControlBorder, r, rad, 1f);
        if (Box.Focused)
        {
            using var pen = new Pen(T.Accent, 2 * s);
            g.SmoothingMode = SmoothingMode.None;
            g.DrawLine(pen, rad, Height - 1.5f * s, Width - rad, Height - 1.5f * s);
        }
        if (!string.IsNullOrEmpty(Suffix))
            TextRenderer.DrawText(g, Suffix, Fonts.Get(Theme.TextFont, 13 * s),
                new Rectangle(0, 0, Width - (int)(10 * s), Height), T.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Rounded card container. Cards in a group get square inner corners (6 on top / bottom only).</summary>
internal sealed class Card : Panel
{
    /// <summary>Accent outline to draw attention (e.g. after a "go to setting" link).</summary>
    public bool Highlight { get; set; }
    public float RadiusTop { get; set; } = 6;
    public float RadiusBottom { get; set; } = 6;
    public Color? Fill { get; set; }
    public Color? Border { get; set; }

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public Color FillColor => Fill ?? Theme.Current.Card;

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? t.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundRect(r, RadiusTop * s, RadiusBottom * s))
        {
            using (var b = new SolidBrush(FillColor)) g.FillPath(b, path);
            using var p = Highlight ? new Pen(t.Accent, 2 * s) : new Pen(Border ?? t.CardBorder, 1f);
            g.DrawPath(p, path);
        }
    }
}

/// <summary>Small rounded tag: filled ("Waiting", "Gamma") or outlined ("Main").</summary>
internal sealed class Chip : FlatControl
{
    public bool Outline { get; set; }
    public Color? Fore { get; set; }
    public Chip() { TabStop = false; SetStyle(ControlStyles.Selectable, false); }

    protected override void OnTextChanged(EventArgs e) { Width = PreferredWidth(); Invalidate(); base.OnTextChanged(e); }

    public int PreferredWidth() => string.IsNullOrEmpty(Text) ? 0
        : TextRenderer.MeasureText(Text, Fonts.Get(Theme.TextFont, 12 * S), Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(12 * S) + 2;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        if (string.IsNullOrEmpty(Text)) return;
        float s = S, h = 18 * s;
        var r = new RectangleF(0.5f, (Height - h) / 2, Width - 1.5f, h);
        if (Outline) Theme.StrokeRound(g, T.Thumb, r, 4 * s, 1f);
        else Theme.FillRound(g, T.Chip, r, 4 * s);
        TextRenderer.DrawText(g, Text, Fonts.Get(Theme.TextFont, 12 * s), Rectangle.Round(r), Fore ?? T.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

/// <summary>Win11 combo box: current value + chevron, opens a menu of choices.</summary>
internal sealed class DropDown : FlatControl
{
    private string[] _items = Array.Empty<string>();
    private bool[] _disabled = Array.Empty<bool>();
    private int _sel;
    private PopupMenu? _menu;
    public event EventHandler? SelectedChanged;

    public DropDown() { TabStop = true; }

    public string[] Items { get => _items; set { _items = value; _disabled = new bool[value.Length]; Invalidate(); } }
    public void SetDisabled(int i, bool d) { if (i >= 0 && i < _disabled.Length) _disabled[i] = d; }
    public int Selected
    {
        get => _sel;
        set { if (value == _sel || value < 0 || value >= _items.Length) return; _sel = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public void SetSilently(int v) { _sel = Math.Clamp(v, 0, Math.Max(0, _items.Length - 1)); Invalidate(); }
    /// <summary>True while the choice menu is open (owner popups should not close).</summary>
    public bool MenuOpen => _menu != null;
    public event EventHandler? MenuClosed;

    protected override void OnClick(EventArgs e) { Focus(); Open(); base.OnClick(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Up or Keys.Down || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter || (e.KeyCode == Keys.Down && e.Alt)) { Open(); e.Handled = true; }
        else if (e.KeyCode is Keys.Up or Keys.Down)
        {
            int dir = e.KeyCode == Keys.Up ? -1 : 1;
            for (int i = _sel + dir; i >= 0 && i < _items.Length; i += dir) if (!_disabled[i]) { Selected = i; break; }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void Open()
    {
        if (_menu != null || _items.Length == 0) return;
        _menu = new PopupMenu();
        for (int i = 0; i < _items.Length; i++)
        {
            int idx = i;
            _menu.Add(_items[i], "", () => Selected = idx, !_disabled[i], i == _sel);
        }
        _menu.FormClosed += (_, _) => { _menu = null; MenuClosed?.Invoke(this, EventArgs.Empty); };
        var p = PointToScreen(new Point(0, Height + (int)(2 * S)));
        _menu.ShowAt(p, above: false, owner: FindForm());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float s = S, rad = 4 * s;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Hover && Enabled ? T.ControlHover : T.Control, r, rad);
        Theme.StrokeRound(g, T.ControlBorder, r, rad, 1f);
        using (var p = new Pen(T.ControlBottom, 1f)) g.DrawLine(p, r.X + rad, r.Bottom, r.Right - rad, r.Bottom);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        string text = _sel >= 0 && _sel < _items.Length ? _items[_sel] : "";
        TextRenderer.DrawText(g, text, Fonts.Get(Theme.TextFont, 14 * s), new Rectangle((int)(12 * s), 0, Width - (int)(40 * s), Height),
            Enabled ? T.Text : T.TextDisabled, flags | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "\uE70D", Fonts.Get(Theme.IconFont, 10 * s), new Rectangle(Width - (int)(28 * s), 0, (int)(18 * s), Height),
            T.TextDim, flags | TextFormatFlags.HorizontalCenter);
        DrawFocus(g, r, rad);
    }
}

/// <summary>Brand art: lockup / raster mark (tinted mask with hard shadow) or the vector monitor icon.</summary>
internal sealed class BrandView : FlatControl
{
    public enum Kind { Lockup, MarkRaster, MarkVector }
    public Kind Art { get; set; }
    public float Shadow { get; set; }
    public Color? Fore { get; set; }
    public float Stroke { get; set; } = 1.9f;
    public BrandView() { TabStop = false; SetStyle(ControlStyles.Selectable, false); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var c = Fore ?? T.Text;
        float sh = Shadow * S;
        var box = new RectangleF(0, 0, Width - sh, Height - sh);
        switch (Art)
        {
            case Kind.Lockup: Brand.DrawMask(g, Brand.Lockup, box, c, sh); break;
            case Kind.MarkRaster: Brand.DrawMask(g, Brand.MarkRaster, box, c, sh); break;
            default: Brand.DrawMark(g, new RectangleF(0, 0, Width, Height), c, Stroke); break;
        }
    }
}
