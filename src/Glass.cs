using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Nibble
{
    // Cheap frosted-glass building blocks. Everything runs once per panel open on tiny bitmaps.
    static class Glass
    {
        // Captures the screen behind the panel. Null if the desktop can't be read (secure desktop, etc).
        public static Bitmap Capture(Rectangle screenRect)
        {
            try
            {
                var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size);
                return bmp;
            }
            catch { return null; }
        }

        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        // Captures what's behind one of our own visible windows by briefly excluding it from capture
        // (Windows 10 2004+). Returns null if that isn't supported, so callers keep their old glass.
        public static Bitmap CaptureBehind(IntPtr hwnd, Rectangle screenRect)
        {
            if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)) return null;
            try
            {
                System.Threading.Thread.Sleep(50); // let DWM compose a frame without us
                return Capture(screenRect);
            }
            finally { SetWindowDisplayAffinity(hwnd, WDA_NONE); }
        }

        // Frosted backdrop: downscale 8x, box-blur, upscale smoothly, boost saturation, then tint.
        public static Bitmap Frost(Bitmap source, Size size, Color tint, Color fallback, float saturation)
        {
            var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(result))
            {
                if (source == null)
                {
                    g.Clear(fallback);
                }
                else
                {
                    int sw = Math.Max(4, size.Width / 8), sh = Math.Max(4, size.Height / 8);
                    using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
                    {
                        using (var sg = Graphics.FromImage(small))
                        {
                            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            sg.DrawImage(source, new Rectangle(0, 0, sw, sh));
                        }
                        BoxBlur(small, 2, 3);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(Saturation(saturation));
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(small, new Rectangle(0, 0, size.Width, size.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, ia);
                        }
                    }
                }
                using (var b = new SolidBrush(tint)) g.FillRectangle(b, 0, 0, size.Width, size.Height);
            }
            return result;
        }

        // Mean color of an opaque bitmap; GDI text can't blend alpha, so translucent text colors
        // are flattened against this.
        public static Color Average(Bitmap source)
        {
            // Downscale first so a full-screen backdrop doesn't cost a screen-sized managed array.
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(source, new Rectangle(0, 0, 32, 32));
                }
                return AverageSmall(bmp);
            }
        }

        static Color AverageSmall(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[bmp.Width * bmp.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            long r = 0, g = 0, b = 0;
            foreach (int c in px) { r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255; }
            int n = Math.Max(1, px.Length);
            return Color.FromArgb(255, (int)(r / n), (int)(g / n), (int)(b / n));
        }

        static ColorMatrix Saturation(float s)
        {
            float rw = 0.3086f, gw = 0.6094f, bw = 0.0820f;
            float a = (1 - s) * rw, b = (1 - s) * gw, c = (1 - s) * bw;
            return new ColorMatrix(new[]
            {
                new[] { a + s, a, a, 0, 0 },
                new[] { b, b + s, b, 0, 0 },
                new[] { c, c, c + s, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 },
                new[] { 0f, 0, 0, 0, 1 }
            });
        }

        // Separable box blur, repeated for a near-gaussian result. Only used on small bitmaps.
        public static void BoxBlur(Bitmap bmp, int radius, int passes)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int w = bmp.Width, h = bmp.Height;
            var a = new int[w * h];
            Marshal.Copy(data.Scan0, a, 0, a.Length);
            var b = new int[a.Length];
            for (int p = 0; p < passes; p++)
            {
                Pass(a, b, w, h, radius, true);
                Pass(b, a, w, h, radius, false);
            }
            Marshal.Copy(a, 0, data.Scan0, a.Length);
            bmp.UnlockBits(data);
        }

        static void Pass(int[] src, int[] dst, int w, int h, int r, bool horizontal)
        {
            int lines = horizontal ? h : w, len = horizontal ? w : h;
            int div = r * 2 + 1;
            for (int line = 0; line < lines; line++)
            {
                int sa = 0, sr = 0, sg = 0, sb = 0;
                for (int k = -r; k <= r; k++)
                {
                    int c = src[Index(line, Clamp(k, len), horizontal, w)];
                    sa += (c >> 24) & 255; sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255;
                }
                for (int i = 0; i < len; i++)
                {
                    dst[Index(line, i, horizontal, w)] = ((sa / div) << 24) | ((sr / div) << 16) | ((sg / div) << 8) | (sb / div);
                    int cOut = src[Index(line, Clamp(i - r, len), horizontal, w)];
                    int cIn = src[Index(line, Clamp(i + r + 1, len), horizontal, w)];
                    sa += ((cIn >> 24) & 255) - ((cOut >> 24) & 255);
                    sr += ((cIn >> 16) & 255) - ((cOut >> 16) & 255);
                    sg += ((cIn >> 8) & 255) - ((cOut >> 8) & 255);
                    sb += (cIn & 255) - (cOut & 255);
                }
            }
        }

        static int Clamp(int i, int len) { return i < 0 ? 0 : (i >= len ? len - 1 : i); }
        static int Index(int line, int i, bool horizontal, int w) { return horizontal ? line * w + i : i * w + line; }
    }
}
