using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Nibble.UI
{
    // Synthetic, colourful backdrops for design snapshots, so the glass has something to frost.
    static class Wallpaper
    {
        public static Bitmap Small(Size s)
        {
            var b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new Rectangle(Point.Empty, s), Theme.Hex(0x1D2B64), Theme.Hex(0x7B3F9E), 60f))
                    g.FillRectangle(br, 0, 0, s.Width, s.Height);
                using (var br = new SolidBrush(Theme.Hex(0xFF7A45))) g.FillEllipse(br, s.Width * 0.55f, s.Height * 0.05f, s.Width * 0.6f, s.Width * 0.6f);
                using (var br = new SolidBrush(Theme.Hex(0x2EC4B6))) g.FillEllipse(br, -s.Width * 0.2f, s.Height * 0.55f, s.Width * 0.7f, s.Width * 0.7f);
                using (var br = new SolidBrush(Color.FromArgb(200, 255, 209, 102))) g.FillEllipse(br, s.Width * 0.25f, s.Height * 0.3f, s.Width * 0.3f, s.Width * 0.3f);
            }
            return b;
        }

        public static Bitmap Large(Size s)
        {
            var b = new Bitmap(s.Width, s.Height);
            using (var g = Graphics.FromImage(b))
            using (var br = new LinearGradientBrush(new Rectangle(Point.Empty, s), Theme.Hex(0x1D2B64), Theme.Hex(0xC06C84), 35f))
            {
                g.FillRectangle(br, 0, 0, s.Width, s.Height);
                using (var b2 = new SolidBrush(Theme.Hex(0x2EC4B6))) g.FillEllipse(b2, -s.Width * 0.1f, s.Height * 0.5f, s.Width * 0.5f, s.Width * 0.5f);
                using (var b3 = new SolidBrush(Theme.Hex(0xFF9F0A))) g.FillEllipse(b3, s.Width * 0.7f, -s.Height * 0.2f, s.Width * 0.4f, s.Width * 0.4f);
            }
            return b;
        }
    }
}
