using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Nibble.UI
{
    // Backdrops for design snapshots, so the glass has something to frost: an image passed with --bg,
    // otherwise a neutral studio gradient.
    static class Wallpaper
    {
        public static string File;

        public static Bitmap For(bool dark, Size s)
        {
            if (File != null && System.IO.File.Exists(File)) return FromFile(File, s);
            return dark ? Studio(s, 0x2E3035, 0x111214, 0xFFFFFF, 34) : Studio(s, 0xF2F0EC, 0xD9D5CE, 0xFFFFFF, 150);
        }

        // Scales the image to cover the frame and crops a band just above the middle.
        static Bitmap FromFile(string path, Size s)
        {
            using (var src = new Bitmap(path))
            {
                float k = Math.Max(s.Width / (float)src.Width, s.Height / (float)src.Height);
                float w = src.Width * k, h = src.Height * k;
                var b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new RectangleF((s.Width - w) / 2, (s.Height - h) * 0.42f, w, h));
                }
                return b;
            }
        }

        // A photo-studio sweep: vertical gradient, one soft key light, a gentle vignette, fine grain.
        static Bitmap Studio(Size s, int top, int bottom, int light, int lightAlpha)
        {
            var b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                using (var br = new LinearGradientBrush(new Rectangle(Point.Empty, s), Theme.Hex(top), Theme.Hex(bottom), 90f))
                    g.FillRectangle(br, 0, 0, s.Width, s.Height);
                Glow(g, s, 0.25f, 0.1f, 0.9f, Theme.Hex(light), lightAlpha);
                Vignette(g, s, Theme.Hex(bottom), 110);
            }
            return Grain(b, 2);
        }

        static void Glow(Graphics g, Size s, float fx, float fy, float r, Color c, int alpha)
        {
            float rad = r * Math.Max(s.Width, s.Height);
            var rect = new RectangleF(fx * s.Width - rad, fy * s.Height - rad, rad * 2, rad * 2);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(alpha, c);
                    br.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillEllipse(br, rect);
                }
            }
        }

        static void Vignette(Graphics g, Size s, Color edge, int alpha)
        {
            var rect = new RectangleF(-s.Width * 0.35f, -s.Height * 0.35f, s.Width * 1.7f, s.Height * 1.7f);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(0, edge);
                    br.SurroundColors = new[] { Color.FromArgb(alpha, edge) };
                    g.FillRectangle(br, 0, 0, s.Width, s.Height);
                }
            }
        }

        // Fine grain so the gradient doesn't band.
        static Bitmap Grain(Bitmap b, int amount)
        {
            var rect = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            var rnd = new Random(7);
            for (int i = 0; i < px.Length; i++)
            {
                int n = rnd.Next(-amount, amount + 1), c = px[i];
                px[i] = (255 << 24) | (Clamp(((c >> 16) & 255) + n) << 16) | (Clamp(((c >> 8) & 255) + n) << 8) | Clamp((c & 255) + n);
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            b.UnlockBits(data);
            return b;
        }

        static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }
    }
}
