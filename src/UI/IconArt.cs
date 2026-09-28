using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Nibble.UI
{
    // The app icon, in the Liquid Glass style: a top-lit green squircle holding a frosted glass mouse
    // with a green bolt.
    static class IconArt
    {
        public static Bitmap AppArt(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float inset = s * 0.03f;
                var tile = new RectangleF(inset, inset, s - 2 * inset, s - 2 * inset);
                Tile(g, tile);
                float mw = tile.Width * 0.36f, mh = tile.Height * 0.62f;
                var mouse = new RectangleF(tile.X + (tile.Width - mw) / 2, tile.Y + (tile.Height - mh) / 2 + tile.Height * 0.01f, mw, mh);
                GlassMouse(g, mouse, s);
                var bolt = new PointF(mouse.X + mouse.Width / 2, mouse.Y + mouse.Height * 0.66f);
                Glow(g, bolt, mouse.Width * 0.45f, Theme.Hex(0x5BE37D), 70);
                Icons.Fill(g, Symbol.Bolt, bolt, mouse.Height * 0.3f, Theme.Hex(0x22B24C));
            }
            return bmp;
        }

        // Continuous-corner squircle (superellipse, n = 5), the shape of Apple's icons.
        static GraphicsPath Squircle(RectangleF r)
        {
            const int N = 160;
            const double n = 5;
            var pts = new PointF[N];
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, a = r.Width / 2, b = r.Height / 2;
            for (int i = 0; i < N; i++)
            {
                double t = 2 * Math.PI * i / N, c = Math.Cos(t), sn = Math.Sin(t);
                pts[i] = new PointF(cx + (float)(a * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n)), cy + (float)(b * Math.Sign(sn) * Math.Pow(Math.Abs(sn), 2 / n)));
            }
            var p = new GraphicsPath();
            p.AddPolygon(pts);
            return p;
        }

        // Green gradient, a soft light across the top and a specular rim.
        static void Tile(Graphics g, RectangleF tile)
        {
            using (var shape = Squircle(tile))
            {
                using (var br = new LinearGradientBrush(tile, Color.Black, Color.Black, 90f))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { Theme.Hex(0x5BE37D), Theme.Hex(0x1C9A45), Theme.Hex(0x0E6B2E) };
                    cb.Positions = new[] { 0f, 0.55f, 1f };
                    br.InterpolationColors = cb;
                    g.FillPath(br, shape);
                }
                var st = g.Save();
                g.SetClip(shape);
                var light = new RectangleF(tile.X - tile.Width * 0.2f, tile.Y - tile.Height * 0.55f, tile.Width * 1.4f, tile.Height * 0.95f);
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(light);
                    using (var pb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                        g.FillEllipse(pb, light);
                }
                g.Restore(st);
                float w = Math.Max(1f, tile.Width / 90f);
                using (var rim = new LinearGradientBrush(tile, Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                using (var p = new Pen(rim, w))
                using (var inner = Squircle(RectangleF.Inflate(tile, -w / 2, -w / 2)))
                    g.DrawPath(p, inner);
            }
        }

        // A frosted, top-lit mouse body with a soft shadow, a lit edge and the button seam.
        static void GlassMouse(Graphics g, RectangleF m, int s)
        {
            using (var path = Draw.Round(m, m.Width / 2))
            {
                using (var shadow = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                {
                    using (var sg = Graphics.FromImage(shadow))
                    using (var sp = Draw.Round(new RectangleF(m.X, m.Y + m.Height * 0.05f, m.Width, m.Height), m.Width / 2))
                    using (var sb = new SolidBrush(Color.FromArgb(90, 0, 20, 10)))
                    {
                        sg.SmoothingMode = SmoothingMode.AntiAlias;
                        sg.FillPath(sb, sp);
                    }
                    Glass.BoxBlur(shadow, Math.Max(1, s / 40), 3);
                    g.DrawImageUnscaled(shadow, 0, 0);
                }

                var st = g.Save();
                g.SetClip(path);
                using (var br = new LinearGradientBrush(m, Color.FromArgb(242, 255, 255, 255), Color.FromArgb(204, 220, 227, 234), 90f))
                    g.FillRectangle(br, RectangleF.Inflate(m, 1, 1));
                // Gradient brushes are larger than what they fill, so GDI+ never wraps a stray row.
                using (var spec = new LinearGradientBrush(new RectangleF(m.X, m.Y - 2, m.Width, m.Height * 0.5f), Color.FromArgb(200, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(spec, m.X, m.Y, m.Width, m.Height * 0.46f);
                using (var shade = new LinearGradientBrush(new RectangleF(m.X, m.Y + m.Height * 0.5f, m.Width, m.Height * 0.55f), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(45, 0, 20, 40), 90f))
                    g.FillRectangle(shade, m.X, m.Y + m.Height * 0.54f, m.Width, m.Height * 0.47f);
                g.Restore(st);

                float w = Math.Max(1f, s / 110f);
                using (var rb = new LinearGradientBrush(new RectangleF(m.X, m.Y - 1, m.Width, m.Height + 2), Color.FromArgb(230, 255, 255, 255), Color.FromArgb(60, 0, 30, 20), 90f))
                using (var p = new Pen(rb, w)) g.DrawPath(p, path);
            }
            using (var seam = new Pen(Color.FromArgb(55, 0, 40, 30), Math.Max(1f, s / 64f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(seam, m.X + m.Width / 2, m.Y + m.Height * 0.05f, m.X + m.Width / 2, m.Y + m.Height * 0.34f);
        }

        static void Glow(Graphics g, PointF c, float r, Color color, int alpha)
        {
            var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(rect);
                using (var pb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(alpha, color), SurroundColors = new[] { Color.FromArgb(0, color) } })
                    g.FillEllipse(pb, rect);
            }
        }

        // Writes a multi-size PNG-compressed .ico.
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var b = AppArt(sizes[i]))
                using (var ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    pngs[i] = ms.ToArray();
                }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off);
                    off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }
}
