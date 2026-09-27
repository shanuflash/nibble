using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Nibble.UI
{
    static class Draw
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        // Specular edge: bright at the top, fading toward the bottom, like light catching glass.
        public static void Rim(Graphics g, RectangleF r, float rad, Color top, Color bottom, float width)
        {
            var rr = new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width);
            using (var p = Round(rr, Math.Max(0, rad - width / 2)))
            using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, 90f))
            {
                var blend = new ColorBlend(4);
                blend.Colors = new[] { top, Lerp(top, bottom, 0.75f), bottom, Lerp(bottom, top, 0.35f) };
                blend.Positions = new[] { 0f, 0.35f, 0.8f, 1f };
                br.InterpolationColors = blend;
                using (var pen = new Pen(br, width)) g.DrawPath(pen, p);
            }
        }

        public static void Bolt(Graphics g, Color c, RectangleF r)
        {
            PointF[] u =
            {
                new PointF(0.62f, 0.00f), new PointF(0.08f, 0.58f), new PointF(0.46f, 0.58f),
                new PointF(0.36f, 1.00f), new PointF(0.92f, 0.40f), new PointF(0.54f, 0.40f)
            };
            var pts = new PointF[u.Length];
            for (int i = 0; i < u.Length; i++) pts[i] = new PointF(r.X + u[i].X * r.Width, r.Y + u[i].Y * r.Height);
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, float k)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * k))), c.R, c.G, c.B);
        }

        public static string PickFont(params string[] names)
        {
            using (var ifc = new InstalledFontCollection())
            {
                foreach (var n in names)
                    foreach (var f in ifc.Families)
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f.Name;
            }
            return "Segoe UI";
        }

        // Opaque equivalent of a translucent colour laid over `under`, faded by `fade`. GDI text can't blend,
        // so translucent text colours are flattened against the average backdrop.
        public static Color Flatten(Color c, Color under, float fade)
        {
            float k = c.A / 255f * fade;
            return Color.FromArgb(255, (int)(under.R + (c.R - under.R) * k), (int)(under.G + (c.G - under.G) * k), (int)(under.B + (c.B - under.B) * k));
        }

        public static Color Flatten(Color c, Color under) { return Flatten(c, under, 1); }

        // Pixels per logical pixel. The process is system-DPI aware, so the screen DC reports the right scale.
        public static float ScreenScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        public static Pen RoundPen(Color c, float width)
        {
            return new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        }
    }
}
