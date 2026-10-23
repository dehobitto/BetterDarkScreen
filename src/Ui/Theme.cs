using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace BetterDarkScreen.Ui;

/// <summary>Windows 11-like palette that follows system light/dark mode and accent color.</summary>
internal sealed class Theme
{
    public bool Dark { get; private init; }
    public Color Bg { get; private init; }
    public Color Footer { get; private init; }
    public Color Card { get; private init; }
    public Color CardBorder { get; private init; }
    public Color Control { get; private init; }
    public Color ControlHover { get; private init; }
    public Color ControlBorder { get; private init; }
    public Color Text { get; private init; }
    public Color TextDim { get; private init; }
    public Color TextDisabled { get; private init; }
    public Color Accent { get; private init; }
    public Color AccentHover { get; private init; }
    public Color OnAccent { get; private init; }
    public Color Divider { get; private init; }
    public Color Track { get; private init; }
    public Color Menu { get; private init; }
    public Color MenuHover { get; private init; }
    public Color Selected { get; private init; }
    public Color Warning { get; private init; }
    // Redesign tokens
    public Color TextTertiary { get; private init; }
    public Color ControlBottom { get; private init; }
    public Color Flyout { get; private init; }
    public Color FlyoutBorder { get; private init; }
    public Color FlyoutCard { get; private init; }
    public Color FlyoutCardBorder { get; private init; }
    public Color SegTrack { get; private init; }
    public Color SegBorder { get; private init; }
    public Color SegSelected { get; private init; }
    public Color NavSelected { get; private init; }
    public Color SliderTrack { get; private init; }
    public Color Thumb { get; private init; }
    public Color ThumbBorder { get; private init; }
    public Color Chip { get; private init; }
    public Color Keycap { get; private init; }
    public Color KeycapEdge { get; private init; }
    public Color Note { get; private init; }
    public Color Dashed { get; private init; }

    // GDI (TextRenderer) draws the variable "Segoe UI Variable" font with poor hinting at small sizes,
    // so stick to classic Segoe UI, which is hinted for GDI and looks crisp.
    public static string TextFont { get; } = Pick("Segoe UI", "Tahoma");
    public static string DisplayFont { get; } = Pick("Segoe UI", "Tahoma");
    public static string IconFont { get; } = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
    public static string MonoFont { get; } = Pick("Cascadia Mono", "Consolas");

    private static Theme? _cur;
    public static Theme Current => _cur ??= Load();
    public static void Invalidate() => _cur = null;

    private static Theme Load()
    {
        bool dark = SystemUsesDark("AppsUseLightTheme");
        var (accDark, accLight) = ReadAccent();
        var accent = EnsureContrast(dark ? accLight : accDark, dark);
        var onAccent = Luma(accent) > 0.5 ? Hex(0x000000) : Hex(0xFFFFFF);
        return dark
            ? new Theme
            {
                Dark = true,
                Bg = Hex(0x202020), Footer = Hex(0x1C1C1C), Card = Hex(0x2B2B2B), CardBorder = Hex(0x1D1D1D),
                Control = Hex(0x373737), ControlHover = Hex(0x3C3C3C), ControlBorder = Hex(0x454545),
                Text = Hex(0xFFFFFF), TextDim = Hex(0xC5C5C5), TextDisabled = Hex(0x787878),
                Accent = accent, AccentHover = Blend(accent, Color.Black, 0.1), OnAccent = onAccent,
                Divider = Hex(0x3A3A3A), Track = Hex(0x9A9A9A),
                Menu = Hex(0x2C2C2C), MenuHover = Hex(0x3A3A3A), Selected = Hex(0x4A4A4A), Warning = Hex(0xFCE100),
                TextTertiary = Hex(0x9D9D9D), ControlBottom = Hex(0x5A5A5A),
                Flyout = Hex(0x2C2C2C), FlyoutBorder = Hex(0x3C3C3C), FlyoutCard = Hex(0x333333), FlyoutCardBorder = Hex(0x3A3A3A),
                SegTrack = Hex(0x1C1C1C), SegBorder = Hex(0x383838), SegSelected = Hex(0x3A3A3A), NavSelected = Hex(0x2D2D2D),
                SliderTrack = Hex(0x5C5C5C), Thumb = Hex(0x454545), ThumbBorder = Hex(0x575757),
                Chip = Hex(0x3D3D3D), Keycap = Hex(0x4A4A4A), KeycapEdge = Hex(0x1F1F1F), Note = Hex(0x272727), Dashed = Hex(0x5C5C5C),
            }
            : new Theme
            {
                Dark = false,
                Bg = Hex(0xF3F3F3), Footer = Hex(0xEEEEEE), Card = Hex(0xFBFBFB), CardBorder = Hex(0xE5E5E5),
                Control = Hex(0xFDFDFD), ControlHover = Hex(0xF6F6F6), ControlBorder = Hex(0xD5D5D5),
                Text = Hex(0x1B1B1B), TextDim = Hex(0x5D5D5D), TextDisabled = Hex(0xA0A0A0),
                Accent = accent, AccentHover = Blend(accent, Color.White, 0.1), OnAccent = onAccent,
                Divider = Hex(0xE5E5E5), Track = Hex(0x8A8A8A),
                Menu = Hex(0xF9F9F9), MenuHover = Hex(0xEAEAEA), Selected = Hex(0xFFFFFF), Warning = Hex(0x9D5D00),
                TextTertiary = Hex(0x707070), ControlBottom = Hex(0xBDBDBD),
                Flyout = Hex(0xEEEEEE), FlyoutBorder = Hex(0xD8D8D8), FlyoutCard = Hex(0xFBFBFB), FlyoutCardBorder = Hex(0xE3E3E3),
                SegTrack = Hex(0xEDEDED), SegBorder = Hex(0xD9D9D9), SegSelected = Hex(0xFFFFFF), NavSelected = Hex(0xEAEAEA),
                SliderTrack = Hex(0xC2C2C2), Thumb = Hex(0xFFFFFF), ThumbBorder = Hex(0xD0D0D0),
                Chip = Hex(0xE6E6E6), Keycap = Hex(0xFFFFFF), KeycapEdge = Hex(0xCFCFCF), Note = Hex(0xF7F7F7), Dashed = Hex(0xA8A8A8),
            };
    }

    public static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>Grey or very dark accents vanish on the dark background; lift them so controls stay readable.</summary>
    private static Color EnsureContrast(Color c, bool dark)
    {
        if (dark) { while (Luma(c) < 0.45) c = Blend(c, Color.White, 0.15); }
        else { while (Luma(c) > 0.45) c = Blend(c, Color.Black, 0.15); }
        return c;
    }

    /// <summary>True when the taskbar is dark (for picking tray icon color).</summary>
    public static bool TaskbarDark => SystemUsesDark("SystemUsesLightTheme");

    private static bool SystemUsesDark(string value)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue(value) is int v && v == 0;
        }
        catch { return false; }
    }

    private static (Color dark, Color light) ReadAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (k?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
            {
                // Palette: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, ... (RGBA)
                Color At(int i) => Color.FromArgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
                return (At(4), At(1));
            }
        }
        catch { }
        return (Hex(0x005FB8), Hex(0x60CDFF));
    }

    private static string Pick(string preferred, string fallback)
    {
        try
        {
            using var f = new Font(preferred, 10f);
            return string.Equals(f.Name, preferred, StringComparison.OrdinalIgnoreCase) ? preferred : fallback;
        }
        catch { return fallback; }
    }

    public static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // ---- drawing helpers ----
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var path = RoundRect(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    /// <summary>Rounded rect with separate top / bottom radius (grouped cards).</summary>
    public static GraphicsPath RoundRect(RectangleF r, float top, float bottom)
    {
        var p = new GraphicsPath();
        float dt = Math.Min(top * 2, Math.Min(r.Width, r.Height)), db = Math.Min(bottom * 2, Math.Min(r.Width, r.Height));
        if (dt > 0.5f) { p.AddArc(r.X, r.Y, dt, dt, 180, 90); p.AddArc(r.Right - dt, r.Y, dt, dt, 270, 90); }
        else { p.AddLine(r.X, r.Y, r.Right, r.Y); }
        if (db > 0.5f) { p.AddArc(r.Right - db, r.Bottom - db, db, db, 0, 90); p.AddArc(r.X, r.Bottom - db, db, db, 90, 90); }
        else { p.AddLine(r.Right, r.Bottom, r.X, r.Bottom); }
        p.CloseFigure();
        return p;
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
    {
        using var path = RoundRect(r, radius);
        using var pen = new Pen(c, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Borderless popup look: rounded corners, thin border and a real DWM shadow (Win11).</summary>
    public static void ApplyPopupChrome(IntPtr hwnd, Theme t)
    {
        ApplyWindowChrome(hwnd, t.Dark, true);
        int border = (t.FlyoutBorder.B << 16) | (t.FlyoutBorder.G << 8) | t.FlyoutBorder.R; // COLORREF 0x00BBGGRR (grey: same either way)
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, 4);
    }

    /// <summary>Dark title bar + rounded corners (Win11). No-op where unsupported.</summary>
    public static void ApplyWindowChrome(IntPtr hwnd, bool dark, bool round = true)
    {
        int v = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
        if (round)
        {
            int c = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref c, 4);
        }
    }
}

internal static class Fonts
{
    private static readonly Dictionary<(string, float, FontStyle), Font> _cache = new();

    /// <summary>Pixel-sized font (already DPI scaled by caller).</summary>
    public static Font Get(string family, float px, FontStyle style = FontStyle.Regular)
    {
        px = MathF.Round(px * 2) / 2;
        var key = (family, px, style);
        if (!_cache.TryGetValue(key, out var f)) _cache[key] = f = new Font(family, px, style, GraphicsUnit.Pixel);
        return f;
    }

    public static void Clear()
    {
        foreach (var f in _cache.Values) f.Dispose();
        _cache.Clear();
    }
}
