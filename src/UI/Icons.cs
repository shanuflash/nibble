using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Nibble.UI
{
    enum Symbol { Scope, Bolt, Battery, Sliders, Mouse }

    // Vector symbols on a 24×24 grid. Strokes are widened into filled outlines; each part is
    // filled on its own (so overlaps never cancel out) and the union of parts is measured for centring.
    static class Icons
    {
        const float W = 2f;   // stroke width in grid units

        public static List<GraphicsPath> Parts(Symbol symbol)
        {
            var p = new List<GraphicsPath>();
            switch (symbol)
            {
                case Symbol.Scope: Scope(p); break;
                case Symbol.Bolt: Bolt(p); break;
                case Symbol.Battery: Battery(p); break;
                case Symbol.Sliders: Sliders(p); break;
                default: Mouse(p); break;
            }
            return p;
        }

        // Fills the symbol with its geometric centre on `center` and its longest side `ink` px.
        public static void Fill(Graphics g, Symbol symbol, PointF center, float ink, Color c)
        {
            var parts = Parts(symbol);
            var b = parts[0].GetBounds();
            foreach (var part in parts) b = RectangleF.Union(b, part.GetBounds());
            float k = ink / System.Math.Max(b.Width, b.Height);
            var mode = g.PixelOffsetMode;
            var smooth = g.SmoothingMode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var m = new Matrix())
            using (var brush = new SolidBrush(c))
            {
                m.Translate(center.X, center.Y);
                m.Scale(k, k);
                m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
                foreach (var part in parts) { part.Transform(m); g.FillPath(brush, part); part.Dispose(); }
            }
            g.PixelOffsetMode = mode;
            g.SmoothingMode = smooth;
        }

        static void Add(List<GraphicsPath> parts, System.Action<GraphicsPath> build)
        {
            var g = new GraphicsPath();
            build(g);
            parts.Add(g);
        }

        // Ring with four ticks and a centre dot.
        static void Scope(List<GraphicsPath> p)
        {
            Stroke(p, s => s.AddEllipse(5.5f, 5.5f, 13, 13));
            Stroke(p, s => { s.AddLine(12, 1.5f, 12, 5.5f); s.StartFigure(); s.AddLine(12, 18.5f, 12, 22.5f); });
            Stroke(p, s => { s.AddLine(1.5f, 12, 5.5f, 12); s.StartFigure(); s.AddLine(18.5f, 12, 22.5f, 12); });
            Add(p, s => s.AddEllipse(10, 10, 4, 4));
        }

        static void Bolt(List<GraphicsPath> p)
        {
            Add(p, s => s.AddPolygon(new[]
            {
                new PointF(13.8f, 1.5f), new PointF(4.5f, 13.6f), new PointF(11f, 13.6f),
                new PointF(10.2f, 22.5f), new PointF(19.5f, 10.4f), new PointF(13f, 10.4f)
            }));
        }

        static void Battery(List<GraphicsPath> p)
        {
            Stroke(p, s => s.AddPath(Round(new RectangleF(1.5f, 6.5f, 18, 11), 3.2f), false));
            Add(p, s => s.AddPath(Round(new RectangleF(20.5f, 10, 2.5f, 4), 1.1f), false));
            Add(p, s => s.AddPath(Round(new RectangleF(4.5f, 9.5f, 9, 5), 1.2f), false));
        }

        static void Sliders(List<GraphicsPath> p)
        {
            float[] ys = { 5, 12, 19 }, ks = { 15.5f, 8f, 13.5f };
            Stroke(p, s => { for (int i = 0; i < 3; i++) { s.StartFigure(); s.AddLine(2, ys[i], 22, ys[i]); } });
            for (int i = 0; i < 3; i++) { int k = i; Add(p, s => s.AddEllipse(ks[k] - 3.2f, ys[k] - 3.2f, 6.4f, 6.4f)); }
        }

        static void Mouse(List<GraphicsPath> p)
        {
            Stroke(p, s => s.AddPath(Round(new RectangleF(5.5f, 1.5f, 13, 21), 6.5f), false));
            Stroke(p, s => s.AddLine(12, 2, 12, 9));
        }

        // Adds a stroked outline (round caps and joins) as filled geometry.
        static void Stroke(List<GraphicsPath> parts, System.Action<GraphicsPath> build)
        {
            var s = new GraphicsPath();
            using (var pen = new Pen(Color.Black, W) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                build(s);
                s.Widen(pen);
            }
            s.FillMode = FillMode.Winding;
            parts.Add(s);
        }

        static GraphicsPath Round(RectangleF r, float rad) { return Draw.Round(r, rad); }
    }
}
