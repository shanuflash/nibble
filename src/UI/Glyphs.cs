using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Nibble.UI
{
    // Renders Segoe Fluent Icons glyphs the way the shell does: GDI with font hinting and greyscale
    // anti-aliasing, then coverage becomes alpha. Crisp at 16 px, matching the other tray icons.
    static class Glyphs
    {
        public const char Mouse = '';
        public const char Battery0 = '';          // EBA0..EBAA: 0..100% in 10% steps
        public const char BatteryCharging0 = '';  // EBAB..EBB5: charging, same steps

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
            uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr dc, int c);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int m);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr dc, string s, int n, ref RECT r, int f);

        static string face;
        public static string IconFace
        {
            get { return face ?? (face = Draw.PickFont("Segoe Fluent Icons", "Segoe MDL2 Assets")); }
        }

        // Coverage (0..255) of text drawn centred in an s×s cell with GDI's hinted greyscale AA.
        public static byte[] Coverage(string text, string fontFace, int s, int px)
        {
            using (var bmp = new Bitmap(s, s, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    IntPtr dc = g.GetHdc();
                    IntPtr font = CreateFontW(-px, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED_QUALITY */, 0, fontFace);
                    IntPtr old = SelectObject(dc, font);
                    SetTextColor(dc, 0xFFFFFF);
                    SetBkMode(dc, 1);
                    var rc = new RECT { L = 0, T = 0, R = s, B = s };
                    DrawTextW(dc, text, text.Length, ref rc, 0x1 | 0x4 | 0x20 | 0x100); // CENTER | VCENTER | SINGLELINE | NOCLIP
                    SelectObject(dc, old);
                    DeleteObject(font);
                    g.ReleaseHdc(dc);
                }
                var data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                var raw = new byte[data.Stride * s];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                bmp.UnlockBits(data);
                var cov = new byte[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                        cov[y * s + x] = raw[y * data.Stride + x * 3 + 1]; // green channel = coverage
                return cov;
            }
        }

        // Pixels enclosed by a glyph's outline, found by flood-filling the background from the edges.
        public static bool[] Interior(byte[] cov, int s)
        {
            var outside = new bool[s * s];
            var q = new Queue<int>();
            for (int i = 0; i < s; i++)
            {
                foreach (int p in new[] { i, (s - 1) * s + i, i * s, i * s + s - 1 })
                    if (!outside[p] && cov[p] < 96) { outside[p] = true; q.Enqueue(p); }
            }
            while (q.Count > 0)
            {
                int p = q.Dequeue(), x = p % s, y = p / s;
                int[] nb = { x > 0 ? p - 1 : -1, x < s - 1 ? p + 1 : -1, y > 0 ? p - s : -1, y < s - 1 ? p + s : -1 };
                foreach (int n in nb)
                    if (n >= 0 && !outside[n] && cov[n] < 96) { outside[n] = true; q.Enqueue(n); }
            }
            var inside = new bool[s * s];
            for (int i = 0; i < inside.Length; i++) inside[i] = !outside[i] && cov[i] < 96;
            return inside;
        }

        // Composites layers into an ARGB bitmap: each layer is (coverage or mask, colour).
        public static Bitmap Compose(int s, params object[] layers)
        {
            var px = new int[s * s];
            for (int l = 0; l + 1 < layers.Length; l += 2)
            {
                var c = (Color)layers[l + 1];
                var cov = layers[l] as byte[];
                var mask = layers[l] as bool[];
                for (int i = 0; i < px.Length; i++)
                {
                    int a = cov != null ? cov[i] : (mask[i] ? 255 : 0);
                    a = a * c.A / 255;
                    if (a == 0) continue;
                    px[i] = Over(px[i], c, a);
                }
            }
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        // Non-premultiplied "source over" for one pixel.
        static int Over(int dst, Color c, int a)
        {
            int da = (dst >> 24) & 255;
            int oa = a + da * (255 - a) / 255;
            if (oa == 0) return 0;
            int r = (c.R * a + ((dst >> 16) & 255) * da * (255 - a) / 255) / oa;
            int g = (c.G * a + ((dst >> 8) & 255) * da * (255 - a) / 255) / oa;
            int b = (c.B * a + (dst & 255) * da * (255 - a) / 255) / oa;
            return (oa << 24) | (r << 16) | (g << 8) | b;
        }
    }
}
