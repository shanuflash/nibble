using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nibble.UI
{
    // Presents a Surface on a WS_EX_LAYERED window with per-pixel alpha.
    static class Layered
    {
        // Where every popup (flyout, notifications, DPI popup) sits: the corner next to the tray, `gap` px in
        // from the screen edge and the taskbar. Returns the panel's top-left in screen px.
        public static Point Corner(Screen screen, Size panel, int gap)
        {
            Rectangle wa = screen.WorkingArea, b = screen.Bounds;
            int x = wa.Right - panel.Width - gap, y = wa.Bottom - panel.Height - gap;
            if (wa.Top > b.Top) y = wa.Top + gap;           // taskbar on top
            else if (wa.Left > b.Left) x = wa.Left + gap;   // taskbar on the left
            return new Point(x, y);
        }

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
}
