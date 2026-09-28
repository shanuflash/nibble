using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Nibble.UI
{
    static class IconArt
    {
        // App icon: green squircle with a white mouse and a charge bolt.
        public static Bitmap AppArt(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                var r = new RectangleF(s * 0.04f, s * 0.04f, s * 0.92f, s * 0.92f);
                using (var p = Draw.Round(r, s * 0.23f))
                using (var br = new LinearGradientBrush(r, Theme.Hex(0x4CD964), Theme.Hex(0x1FA347), 90f))
                    g.FillPath(br, p);

                var m = new RectangleF(s * 0.32f, s * 0.20f, s * 0.36f, s * 0.60f);
                using (var pen = new Pen(Color.White, Math.Max(1.2f, s * 0.065f)))
                {
                    using (var p = Draw.Round(m, m.Width / 2)) g.DrawPath(pen, p);
                    g.DrawLine(pen, s * 0.5f, m.Y + s * 0.02f, s * 0.5f, m.Y + m.Height * 0.30f);
                }
                if (s >= 24)
                    Draw.Bolt(g, Color.White, new RectangleF(s * 0.445f, s * 0.47f, s * 0.11f, s * 0.2f));
            }
            return bmp;
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
