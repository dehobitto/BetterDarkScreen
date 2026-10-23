using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BetterDarkScreen.Ui;

/// <summary>
/// Brand assets: the monochrome logo lockup and monitor mark (embedded PNG masks, tinted at draw time
/// so the color follows the theme) and the simplified vector monitor icon used at 16–48 px.
/// </summary>
internal static class Brand
{
    // Simplified monitor (24×24 viewBox): tilted screen, short neck, flat base, glare line.
    private static readonly string[] MarkPaths =
    {
        "M3.6 4.4 19.6 2.9c.8-.1 1.3.4 1.3 1.2l-.4 10.7c0 .7-.5 1.1-1.2 1.1L4.7 16.2c-.7 0-1.2-.4-1.3-1.1L2.6 5.6c-.1-.7.3-1.1 1-1.2Z",
        "M10.3 16.1 9.5 19.6M14.7 15.9l.9 3.5",
        "M6.6 20.4c2.2-.9 9.4-1.1 11.8-.2",
        "M6.4 7.3 12.2 6.7",
    };

    private static GraphicsPath? _mark;

    /// <summary>Stroke the vector monitor into <paramref name="r"/>. <paramref name="stroke"/> is in 24-unit viewBox space.</summary>
    public static void DrawMark(Graphics g, RectangleF r, Color c, float stroke = 1.9f)
    {
        _mark ??= BuildMark();
        float k = Math.Min(r.Width, r.Height) / 24f;
        using var path = (GraphicsPath)_mark.Clone();
        using (var m = new Matrix())
        {
            m.Translate(r.X + (r.Width - 24 * k) / 2, r.Y + (r.Height - 24 * k) / 2);
            m.Scale(k, k);
            path.Transform(m);
        }
        var oldSm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, stroke * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawPath(pen, path);
        g.SmoothingMode = oldSm;
    }

    /// <summary>Monochrome monitor icon (window title bar, tray).</summary>
    public static Icon MarkIcon(int size, Color c, bool slash = false)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawMark(g, new RectangleF(0, 0, size, size), c, size <= 16 ? 2f : 1.9f);
            if (slash)
            {
                float s = size / 16f;
                using var p = new Pen(c, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(p, 2.5f * s, 14f * s, 13.5f * s, 2f * s);
            }
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally { Native.DestroyIcon(h); }
    }

    // ---- raster masks (white + alpha) ----

    private static Bitmap? _lockup, _markRaster;
    public static Bitmap? Lockup => _lockup ??= Load("BetterDarkScreen.Assets.lockup.png");
    public static Bitmap? MarkRaster => _markRaster ??= Load("BetterDarkScreen.Assets.mark.png");

    private static Bitmap? Load(string name)
    {
        try
        {
            using var s = typeof(Brand).Assembly.GetManifestResourceStream(name);
            return s == null ? null : new Bitmap(s);
        }
        catch { return null; }
    }

    /// <summary>Draw a white-on-alpha mask tinted with <paramref name="fill"/>, with a hard (no blur) black shadow.</summary>
    public static void DrawMask(Graphics g, Bitmap? mask, RectangleF box, Color fill, float shadow)
    {
        if (mask == null) return;
        // Fit (contain) and center.
        float k = Math.Min(box.Width / mask.Width, box.Height / mask.Height);
        var dst = new RectangleF(box.X + (box.Width - mask.Width * k) / 2, box.Y + (box.Height - mask.Height * k) / 2, mask.Width * k, mask.Height * k);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (shadow > 0) Blit(g, mask, RectangleF.Inflate(new RectangleF(dst.X + shadow, dst.Y + shadow, dst.Width, dst.Height), 0, 0), Color.Black);
        Blit(g, mask, dst, fill);
    }

    private static void Blit(Graphics g, Bitmap mask, RectangleF dst, Color c)
    {
        var cm = new ColorMatrix(new[]
        {
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, c.A / 255f, 0 },
            new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 },
        });
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(cm);
        ia.SetWrapMode(WrapMode.TileFlipXY); // no edge bleed
        var pts = new[] { new PointF(dst.Left, dst.Top), new PointF(dst.Right, dst.Top), new PointF(dst.Left, dst.Bottom) };
        g.DrawImage(mask, pts, new RectangleF(0, 0, mask.Width, mask.Height), GraphicsUnit.Pixel, ia);
    }

    // ---- tiny SVG path parser (M L H V C Z, absolute + relative) ----

    private static GraphicsPath BuildMark()
    {
        var gp = new GraphicsPath();
        foreach (var d in MarkPaths) AddSvg(gp, d);
        return gp;
    }

    private static void AddSvg(GraphicsPath gp, string d)
    {
        var tok = Regex.Matches(d, @"[MmLlHhVvCcZz]|-?(?:\d+\.?\d*|\.\d+)").Select(m => m.Value).ToList();
        int i = 0;
        char cmd = 'M';
        PointF cur = default, start = default;
        float N() => float.Parse(tok[i++], CultureInfo.InvariantCulture);
        bool IsNum() => i < tok.Count && !char.IsLetter(tok[i][0]);

        while (i < tok.Count)
        {
            if (char.IsLetter(tok[i][0])) cmd = tok[i++][0];
            bool rel = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    {
                        float x = N(), y = N();
                        cur = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        start = cur;
                        gp.StartFigure();
                        cmd = rel ? 'l' : 'L'; // following pairs are line-tos
                        break;
                    }
                case 'L':
                    {
                        float x = N(), y = N();
                        var p = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        gp.AddLine(cur, p); cur = p;
                        break;
                    }
                case 'H':
                    {
                        float x = N(); var p = new PointF(rel ? cur.X + x : x, cur.Y);
                        gp.AddLine(cur, p); cur = p;
                        break;
                    }
                case 'V':
                    {
                        float y = N(); var p = new PointF(cur.X, rel ? cur.Y + y : y);
                        gp.AddLine(cur, p); cur = p;
                        break;
                    }
                case 'C':
                    {
                        float x1 = N(), y1 = N(), x2 = N(), y2 = N(), x = N(), y = N();
                        PointF P(float a, float b) => rel ? new PointF(cur.X + a, cur.Y + b) : new PointF(a, b);
                        var c1 = P(x1, y1); var c2 = P(x2, y2); var e = P(x, y);
                        gp.AddBezier(cur, c1, c2, e); cur = e;
                        break;
                    }
                case 'Z':
                    gp.CloseFigure(); cur = start;
                    if (IsNum()) cmd = 'L';
                    break;
                default:
                    i++; break;
            }
        }
    }
}
