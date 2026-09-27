using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Nibble
{
    // Presents a Surface on a WS_EX_LAYERED window with per-pixel alpha.
    static class Layered
    {
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT srcPos, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        public static void Push(IntPtr hwnd, Surface s, int x, int y, byte alpha)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            var size = new SZ { W = s.W, H = s.H };
            var src = new PT();
            var pos = new PT { X = x, Y = y };
            var blend = new BLEND { Alpha = alpha, Format = 1 }; // AC_SRC_ALPHA
            UpdateLayeredWindow(hwnd, screen, ref pos, ref size, s.Dc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // A 32bpp DIB section shared by GDI+ (shapes) and GDI (text), so text gets native ClearType
    // without any copies. A precomputed alpha mask cuts the rounded panel out afterwards.
    sealed class Surface : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class LOGFONT
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr dc, string s, int n, ref RECT r, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, int usage, out IntPtr bits, IntPtr section, int offset);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] static extern int SetTextCharacterExtra(IntPtr dc, int extra);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE size);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontIndirectW(LOGFONT lf);

        const int DT_CENTER = 1, DT_RIGHT = 2, DT_VCENTER = 4, DT_SINGLELINE = 0x20, DT_NOCLIP = 0x100, DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000;

        public readonly int W, H;
        public readonly IntPtr Dc;
        public readonly Bitmap Bmp;       // GDI+ view over the same pixels
        readonly IntPtr bits, hbmp, oldBmp;
        byte[] mask;

        // HFONTs live for the process lifetime; there are only a handful.
        static readonly Dictionary<Font, IntPtr> fonts = new Dictionary<Font, IntPtr>();

        public Surface(int w, int h)
        {
            W = w; H = h;
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = GetDC(IntPtr.Zero);
            Dc = CreateCompatibleDC(screen);
            hbmp = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
            ReleaseDC(IntPtr.Zero, screen);
            oldBmp = SelectObject(Dc, hbmp);
            SetBkMode(Dc, 1); // TRANSPARENT
            Bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        }

        // Anti-aliased rounded-rect coverage, computed once per open.
        public void SetMask(RectangleF r, float radius)
        {
            using (var m = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(m))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Draw.FillRound(g, Color.White, r, radius);
                }
                var data = m.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new int[W * H];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                m.UnlockBits(data);
                mask = new byte[px.Length];
                for (int i = 0; i < px.Length; i++) mask[i] = (byte)((px[i] >> 24) & 255);
            }
        }

        // GDI zeroes alpha wherever it draws text, so alpha is rebuilt from the mask after each frame.
        public unsafe void ApplyMask()
        {
            if (mask == null) return;
            byte* p = (byte*)bits;
            for (int i = 0, n = mask.Length; i < n; i++, p += 4)
            {
                int m = mask[i];
                if (m == 255) { p[3] = 255; }
                else if (m == 0) { *(int*)p = 0; }
                else
                {
                    p[0] = (byte)(p[0] * m / 255); p[1] = (byte)(p[1] * m / 255); p[2] = (byte)(p[2] * m / 255);
                    p[3] = (byte)m;
                }
            }
        }

        public void Text(string s, Font f, Color c, Rectangle r, int align)
        {
            SelectObject(Dc, HFont(f));
            SetTextCharacterExtra(Dc, Tracking(f));
            SetTextColor(Dc, c.R | (c.G << 8) | (c.B << 16));
            var rc = new RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
            int flags = DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS | DT_NOCLIP;
            if (align == 1) flags |= DT_CENTER; else if (align == 2) flags |= DT_RIGHT;
            DrawTextW(Dc, s, s.Length, ref rc, flags);
        }

        public SizeF Measure(string s, Font f)
        {
            SelectObject(Dc, HFont(f));
            SetTextCharacterExtra(Dc, Tracking(f));
            SIZE sz;
            GetTextExtentPoint32W(Dc, s, s.Length, out sz);
            return new SizeF(sz.cx, sz.cy);
        }

        // Display sizes get slightly tighter letter-spacing, as Apple does with SF Display.
        public static int Tracking(Font f)
        {
            return f.Size >= 22 ? -(int)Math.Round(f.Size * 0.025f) : 0;
        }

        static IntPtr HFont(Font f)
        {
            IntPtr h;
            if (fonts.TryGetValue(f, out h)) return h;
            var lf = new LOGFONT();
            f.ToLogFont(lf);
            // Greyscale AA rather than ClearType: fuller, smoother glyphs, closer to how macOS renders type.
            lf.lfQuality = 4; // ANTIALIASED_QUALITY
            h = CreateFontIndirectW(lf);
            fonts[f] = h;
            return h;
        }

        public void Dispose()
        {
            Bmp.Dispose();
            SelectObject(Dc, oldBmp);
            DeleteObject(hbmp);
            DeleteDC(Dc);
        }
    }
}
