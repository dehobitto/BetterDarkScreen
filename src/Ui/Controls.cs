using System.ComponentModel;
using System.Drawing;
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

/// <summary>Win11 toggle switch.</summary>
internal sealed class ToggleSwitch : FlatControl
{
    private bool _on;
    public event EventHandler? CheckedChanged;

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
        float knob = (Hover ? 14 : 12) * s;
        if (Pressed) knob = 14 * s;

        if (_on)
        {
            Theme.FillRound(g, Enabled ? (Hover ? T.AccentHover : T.Accent) : T.TextDisabled, r, h / 2);
            var k = new RectangleF(r.Right - h / 2 - knob / 2, r.Y + (h - knob) / 2, knob, knob);
            using var b = new SolidBrush(T.OnAccent);
            g.FillEllipse(b, k);
        }
        else
        {
            if (Hover) Theme.FillRound(g, T.ControlHover, r, h / 2);
            var rr = r; rr.Inflate(-0.5f * s, -0.5f * s);
            Theme.StrokeRound(g, Enabled ? T.TextDim : T.TextDisabled, rr, h / 2, 1f * s);
            var k = new RectangleF(r.X + h / 2 - knob / 2, r.Y + (h - knob) / 2, knob, knob);
            using var b = new SolidBrush(Enabled ? T.TextDim : T.TextDisabled);
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
            float pad = 11 * S;
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
        Theme.FillRound(g, T.Track, t, t.Height / 2);
        var fill = new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * Frac), t.Height);
        Theme.FillRound(g, Enabled ? T.Accent : T.TextDisabled, fill, t.Height / 2);

        float cx = t.X + t.Width * Frac, cy = Height / 2f;
        float outer = 20 * s;
        var o = new RectangleF(cx - outer / 2, cy - outer / 2, outer, outer);
        using (var b = new SolidBrush(T.Control)) g.FillEllipse(b, o);
        using (var p = new Pen(T.ControlBorder, 1 * s)) g.DrawEllipse(p, o);
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
        float w = (Width - 1f) / Math.Max(1, _items.Length);
        return new RectangleF(i * w, 0, w, Height - 1f);
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
        float s = S, rad = 5 * s;
        var all = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, T.Dark ? Theme.Blend(T.Bg, Color.Black, 0.15) : Theme.Hex(0xEDEDED), all, rad);
        Theme.StrokeRound(g, T.ControlBorder, all, rad, 1f);

        var font = Fonts.Get(Theme.TextFont, 13 * s);
        for (int i = 0; i < _items.Length; i++)
        {
            var c = Cell(i);
            var inner = RectangleF.Inflate(c, -3 * s, -3 * s);
            bool dis = IsDisabled(i);
            Color fg = dis ? T.TextDisabled : (i == _sel ? T.Text : T.TextDim);
            if (i == _sel)
            {
                Theme.FillRound(g, T.Selected, inner, 4 * s);
                if (!T.Dark) Theme.StrokeRound(g, T.CardBorder, inner, 4 * s, 1f);
                float uw = 16 * s, uh = 3 * s;
                Theme.FillRound(g, T.Accent, new RectangleF(inner.X + (inner.Width - uw) / 2, inner.Bottom - uh - 1 * s, uw, uh), uh / 2);
            }
            else if (i == _hot && !dis)
            {
                Theme.FillRound(g, T.ControlHover, inner, 4 * s);
                fg = T.Text;
            }

            var tr = Rectangle.Round(new RectangleF(c.X + 2 * s, c.Y, c.Width - 4 * s, c.Height - (i == _sel ? 2 * s : 0)));
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
        Theme.FillRound(g, _capturing ? T.Card : Hover ? T.ControlHover : T.Control, r, rad);
        Theme.StrokeRound(g, _capturing ? T.Accent : T.ControlBorder, r, rad, _capturing ? 1.5f * s : 1f);

        string text; Color fg;
        if (_capturing)
        {
            var live = new List<string>();
            if ((ModifierKeys & Keys.Control) != 0) live.Add("Ctrl");
            if ((ModifierKeys & Keys.Alt) != 0) live.Add("Alt");
            if ((ModifierKeys & Keys.Shift) != 0) live.Add("Shift");
            text = live.Count > 0 ? string.Join(" + ", live) + " + …" : "Press keys…";
            fg = T.Accent;
        }
        else if (_value.IsEmpty) { text = "Not set"; fg = T.TextDisabled; }
        else { text = _value.Display(); fg = T.Text; }

        TextRenderer.DrawText(g, text, Fonts.Get(Theme.TextFont, 13 * s), Rectangle.Round(r), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
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
        var r = new RectangleF(0, 2 * s, Width - 1, Height - 4 * s);
        if (Selected || Hover) Theme.FillRound(g, Selected ? T.Card : T.ControlHover, r, 4 * s);
        if (Selected)
            Theme.FillRound(g, T.Accent, new RectangleF(0, r.Y + (r.Height - 16 * s) / 2, 3 * s, 16 * s), 1.5f * s);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Glyph, Fonts.Get(Theme.IconFont, 15 * s), new Rectangle((int)(14 * s), 0, (int)(20 * s), Height), T.Text, flags);
        TextRenderer.DrawText(g, Text, Fonts.Get(Theme.TextFont, 14 * s), new Rectangle((int)(46 * s), 0, Width - (int)(50 * s), Height), T.Text, flags | TextFormatFlags.EndEllipsis);
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
                Theme.FillRound(g, Pressed ? T.Control : Hover ? T.ControlHover : T.Control, r, rad);
                if (!Subtle) Theme.StrokeRound(g, T.ControlBorder, r, rad, 1f);
            }
            fg = Enabled ? (Pressed ? T.TextDim : T.Text) : T.TextDisabled;
        }

        var font = Fonts.Get(Theme.TextFont, 13 * s);
        var icon = Fonts.Get(Theme.IconFont, 14 * s);
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

/// <summary>Rounded card container.</summary>
internal sealed class Card : Panel
{
    /// <summary>Accent outline to draw attention (e.g. after a "go to setting" link).</summary>
    public bool Highlight { get; set; }

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? t.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, t.Card, r, 6 * s);
        if (Highlight)
        {
            var h = RectangleF.Inflate(r, -1 * s, -1 * s);
            Theme.StrokeRound(g, t.Accent, h, 6 * s, 2 * s);
        }
        else Theme.StrokeRound(g, t.CardBorder, r, 6 * s, 1f);
    }
}
