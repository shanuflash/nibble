using System.Drawing;
using System.Drawing.Drawing2D;

namespace Nibble.UI
{
    // Small stroked icons for the flyout. `k` is px per logical px; colours arrive with fades applied.
    static class LineIcons
    {
        public static void Mouse(Graphics g, float cx, float cy, Color c, float k)
        {
            var body = new RectangleF(cx - 13 * k, cy - 20 * k, 26 * k, 40 * k);
            using (var p = Draw.RoundPen(c, 2.2f * k))
            using (var path = Draw.Round(body, 13 * k))
            {
                g.DrawPath(p, path);
                g.DrawLine(p, cx, body.Y + k, cx, body.Y + 13 * k);
            }
            Draw.FillRound(g, c, new RectangleF(cx - 1.6f * k, body.Y + 6 * k, 3.2f * k, 6 * k), 1.6f * k);
        }

        public static void Refresh(Graphics g, RectangleF r, Color c, float k, float angle)
        {
            var st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.RotateTransform(angle);
            float rad = 7 * k;
            using (var p = Draw.RoundPen(c, 1.9f * k))
            using (var cap = new AdjustableArrowCap(2.3f, 2.3f, true))
            {
                p.CustomEndCap = cap;
                g.DrawArc(p, -rad, -rad, rad * 2, rad * 2, -60, 290);
            }
            g.Restore(st);
        }

        public static void Sliders(Graphics g, RectangleF r, Color c, Color hole, float k)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, hw = 8 * k;
            float[] ys = { -5 * k, 0, 5 * k };
            float[] ks = { 3 * k, -3.5f * k, 1.5f * k };
            using (var p = Draw.RoundPen(c, 1.8f * k))
                for (int i = 0; i < 3; i++) g.DrawLine(p, cx - hw, cy + ys[i], cx + hw, cy + ys[i]);
            using (var b = new SolidBrush(c))
            using (var h = new SolidBrush(hole))
                for (int i = 0; i < 3; i++)
                {
                    g.FillEllipse(b, cx + ks[i] - 3 * k, cy + ys[i] - 3 * k, 6 * k, 6 * k);
                    g.FillEllipse(h, cx + ks[i] - 1.4f * k, cy + ys[i] - 1.4f * k, 2.8f * k, 2.8f * k);
                }
        }

        public static void Chevron(Graphics g, RectangleF r, Color c, float k)
        {
            float cx = r.X + r.Width / 2 - k, cy = r.Y + r.Height / 2;
            using (var p = Draw.RoundPen(c, 2.2f * k))
                g.DrawLines(p, new[] { new PointF(cx + 3.5f * k, cy - 7 * k), new PointF(cx - 3.5f * k, cy), new PointF(cx + 3.5f * k, cy + 7 * k) });
        }

        public static void Arrow(Graphics g, RectangleF r, Color c, float k)
        {
            using (var p = Draw.RoundPen(c, 1.8f * k))
            {
                g.DrawLine(p, r.X, r.Bottom, r.Right, r.Y);
                g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.3f, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Y + r.Height * 0.7f) });
            }
        }

        public static void Wave(Graphics g, RectangleF r, Color c, float k)
        {
            float cx = r.X + r.Width / 2, by = r.Bottom - k;
            using (var p = Draw.RoundPen(c, 1.8f * k))
            {
                g.DrawArc(p, cx - 8 * k, by - 8 * k - 4 * k, 16 * k, 16 * k, 225, 90);
                g.DrawArc(p, cx - 4.5f * k, by - 4.5f * k - 4 * k, 9 * k, 9 * k, 225, 90);
            }
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 1.8f * k, by - 4.6f * k, 3.6f * k, 3.6f * k);
        }

        public static void Plug(Graphics g, RectangleF r, Color c, float k)
        {
            using (var p = Draw.RoundPen(c, 1.8f * k))
            {
                g.DrawLine(p, r.X + 5 * k, r.Y + k, r.X + 5 * k, r.Y + 5 * k);
                g.DrawLine(p, r.X + 11 * k, r.Y + k, r.X + 11 * k, r.Y + 5 * k);
                using (var path = Draw.Round(new RectangleF(r.X + 2.5f * k, r.Y + 5 * k, 11 * k, 6 * k), 2 * k)) g.DrawPath(p, path);
                g.DrawLine(p, r.X + 8 * k, r.Y + 11 * k, r.X + 8 * k, r.Bottom);
            }
        }

        public static void Crosshair(Graphics g, RectangleF r, Color c, float k)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var p = Draw.RoundPen(c, 1.8f * k))
            {
                g.DrawEllipse(p, cx - 5.5f * k, cy - 5.5f * k, 11 * k, 11 * k);
                g.DrawLine(p, cx, r.Y, cx, cy - 3 * k); g.DrawLine(p, cx, cy + 3 * k, cx, r.Bottom);
                g.DrawLine(p, r.X, cy, cx - 3 * k, cy); g.DrawLine(p, cx + 3 * k, cy, r.Right, cy);
            }
        }

        public static void Clock(Graphics g, RectangleF r, Color c, float k)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var p = Draw.RoundPen(c, 1.8f * k))
            {
                g.DrawEllipse(p, r.X + 0.5f * k, r.Y + 0.5f * k, r.Width - k, r.Height - k);
                g.DrawLines(p, new[] { new PointF(cx, cy - 4 * k), new PointF(cx, cy), new PointF(cx + 3 * k, cy + 2 * k) });
            }
        }
    }
}
