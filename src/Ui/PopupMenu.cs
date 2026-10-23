using System.Drawing;
using System.Drawing.Drawing2D;

namespace BetterDarkScreen.Ui;

/// <summary>
/// Lightweight Win11-style context menu: one owner-drawn window (no child controls),
/// Fluent icons, rounded corners, DWM shadow, keyboard navigation.
/// </summary>
internal sealed class PopupMenu : PopupForm
{
    private sealed record Item(string Text, string Glyph, Action? Click, bool Enabled, bool Separator, bool Checked);

    private readonly List<Item> _items = new();
    private int _hot = -1;
    private float S => DeviceDpi / 96f;
    private int D(float v) => (int)Math.Round(v * S);
    private int ItemH => D(34);
    private int SepH => D(9);
    private int Pad => D(4);
    private bool HasGlyphs => _items.Any(i => i.Glyph.Length > 0 || i.Checked);

    public PopupMenu()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public PopupMenu Add(string text, string glyph, Action click, bool enabled = true, bool isChecked = false)
    {
        _items.Add(new Item(text, glyph, click, enabled, false, isChecked));
        return this;
    }

    public PopupMenu Separator()
    {
        _items.Add(new Item("", "", null, false, true, false));
        return this;
    }

    /// <summary>Show with the top-left (or bottom-left when <paramref name="above"/>) at a screen point, kept inside the work area.</summary>
    public void ShowAt(Point pt, bool above, Form? owner = null)
    {
        var screen = Screen.FromPoint(pt);
        Bounds = new Rectangle(screen.WorkingArea.X + 10, screen.WorkingArea.Y + 10, 100, 100); // create on that monitor for DPI
        if (owner != null) Owner = owner;
        _ = Handle;

        var font = Fonts.Get(Theme.TextFont, 14 * S);
        int textW = _items.Where(i => !i.Separator).Select(i => TextRenderer.MeasureText(i.Text, font).Width).DefaultIfEmpty(0).Max();
        int left = HasGlyphs ? D(44) : D(16);
        int w = Math.Max(D(200), left + textW + D(28));
        int h = Pad * 2 + _items.Sum(i => i.Separator ? SepH : ItemH);
        var wa = screen.WorkingArea;
        int x = pt.X, y = above ? pt.Y - h : pt.Y;
        if (x + w > wa.Right) x = pt.X - w;            // flip left
        if (y + h > wa.Bottom) y = pt.Y - h;           // flip up
        if (y < wa.Top) y = Math.Min(pt.Y, wa.Bottom - h);
        x = Math.Clamp(x, wa.Left, wa.Right - w);
        Bounds = new Rectangle(x, y, w, h);
        BackColor = Theme.Current.Menu;

        Show();
        Native.SetForegroundWindow(Handle); // required for tray menus to close on outside click
        Activate();
    }

    private Rectangle ItemRect(int index)
    {
        int y = Pad;
        for (int i = 0; i < index; i++) y += _items[i].Separator ? SepH : ItemH;
        return new Rectangle(Pad, y, Width - Pad * 2, _items[index].Separator ? SepH : ItemH);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
            if (!_items[i].Separator && ItemRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(t.Menu);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (!Native.IsWin11)
        {
            using var p = new Pen(t.ControlBorder);
            g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
        var font = Fonts.Get(Theme.TextFont, 14 * S);
        var icon = Fonts.Get(Theme.IconFont, 15 * S);
        int left = HasGlyphs ? D(44) : D(16);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = ItemRect(i);
            if (it.Separator)
            {
                using var p = new Pen(t.Divider);
                int y = r.Y + r.Height / 2;
                g.DrawLine(p, r.X - Pad, y, r.Right + Pad, y);
                continue;
            }
            if (i == _hot && it.Enabled)
                Theme.FillRound(g, t.MenuHover, new RectangleF(r.X + 0.5f, r.Y + 1, r.Width - 1, r.Height - 2), 4 * S);

            var fg = it.Enabled ? t.Text : t.TextDisabled;
            string glyph = it.Checked ? "\uE73E" : it.Glyph;
            if (glyph.Length > 0)
                TextRenderer.DrawText(g, glyph, icon, new Rectangle(r.X + D(12), r.Y, D(20), r.Height), it.Checked ? t.Accent : fg, flags);
            TextRenderer.DrawText(g, it.Text, font, new Rectangle(r.X + left - Pad, r.Y, r.Width - left, r.Height), fg, flags | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int h = HitTest(e.Location);
        if (h >= 0) Invoke(h);
    }

    private void Invoke(int i)
    {
        var it = _items[i];
        if (!it.Enabled || it.Separator) return;
        Close();
        it.Click?.Invoke();
    }

    private void MoveHot(int dir)
    {
        int n = _items.Count;
        for (int k = 1; k <= n; k++)
        {
            int i = ((_hot < 0 ? (dir > 0 ? -1 : 0) : _hot) + dir * k + n * 2) % n;
            if (!_items[i].Separator && _items[i].Enabled) { _hot = i; Invalidate(); return; }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Down: MoveHot(1); return true;
            case Keys.Up: MoveHot(-1); return true;
            case Keys.Enter: case Keys.Space: if (_hot >= 0) Invoke(_hot); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(() => { if (!IsDisposed) Close(); });
    }
}
