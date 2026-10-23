using System.Drawing;
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

    public static string TextFont { get; } = Pick("Segoe UI Variable Text", "Segoe UI");
    public static string DisplayFont { get; } = Pick("Segoe UI Variable Display", "Segoe UI");
    public static string IconFont { get; } = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");

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
        int border = t.Dark ? 0x00404040 : 0x00D8D8D8; // COLORREF 0x00BBGGRR (grey: same either way)
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
