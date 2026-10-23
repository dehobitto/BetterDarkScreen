using System.Drawing;
using System.Drawing.Drawing2D;

namespace BetterDarkScreen.Ui;

/// <summary>Draws the tray icon at the right size and color (no image files needed).</summary>
internal static class TrayIcons
{
    public enum State { Active, Idle, Off }

    public static Icon Create(State state)
    {
        int size = SystemInformation.SmallIconSize.Width;
        bool darkBar = Theme.TaskbarDark;
        var fg = darkBar ? Color.White : Color.FromArgb(28, 28, 28);

        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float s = size / 16f;
            var outer = new RectangleF(1.5f * s, 1.5f * s, 13f * s, 13f * s);

            // Crescent = circle minus offset circle.
            using var moon = new GraphicsPath();
            moon.AddEllipse(outer);
            using var cut = new GraphicsPath();
            cut.AddEllipse(new RectangleF(5.5f * s, -1.5f * s, 12f * s, 12f * s));
            using var region = new Region(moon);
            region.Exclude(cut);

            switch (state)
            {
                case State.Active:
                    using (var b = new SolidBrush(fg)) g.FillRegion(b, region);
                    break;
                case State.Idle:
                    // Waiting for schedule: outlined crescent.
                    using (var b = new SolidBrush(Color.FromArgb(darkBar ? 110 : 140, fg))) g.FillRegion(b, region);
                    break;
                case State.Off:
                    using (var p = new Pen(Color.FromArgb(150, fg), 1.2f * s)) g.DrawEllipse(p, outer);
                    using (var p = new Pen(fg, 1.4f * s)) g.DrawLine(p, 3 * s, 13 * s, 13 * s, 3 * s);
                    break;
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
}
