using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Nibble.UI
{
    enum TrayStyle { Mouse, Number, Battery, Ring, Badge }

    // The tray icon, drawn per pixel so it stays crisp at 16 px next to the shell's own icons. Glyphs come
    // from Segoe Fluent Icons through hinted GDI (see Glyphs). Also drawn larger for the style picker.
    //
    // Colours follow the system's lead: the taskbar's own colour while the battery is fine, amber from a
    // third, red from a fifth, green only while charging, dimmed while the mouse sleeps.
    static class TrayArt
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static readonly string[] Names = { "Mouse", "Number", "Battery", "Ring", "Badge" };

        public static Icon Icon(TrayStyle style, int size, int percent, bool charging, bool asleep, bool lightTaskbar)
        {
            using (var bmp = Render(style, size, percent, charging, asleep, lightTaskbar))
            {
                IntPtr h = bmp.GetHicon();
                var ic = (Icon)System.Drawing.Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }

        struct Palette
        {
            public Color Fg, Track, Level, Green;
            public bool Hidden;      // charging with the level hidden by the firmware
            public float Frac;       // share of the meter to fill
        }

        static Palette Colors(int pct, bool charging, bool asleep, bool light)
        {
            Color fg = light ? Color.FromArgb(255, 24, 24, 26) : Color.White;
            Color green = light ? Theme.Hex(0x1E9E48) : Theme.Hex(0x30D158);
            Color red = light ? Theme.Hex(0xE0352B) : Theme.Hex(0xFF453A);
            Color amber = light ? Theme.Hex(0xC98A00) : Theme.Hex(0xFFB020);
            var p = new Palette { Fg = asleep ? Color.FromArgb(120, fg) : fg, Green = green };
            p.Hidden = charging && pct < 0 && !asleep;
            p.Frac = p.Hidden ? 1 : Math.Max(0, Math.Min(100, pct)) / 100f;
            p.Track = Color.FromArgb(asleep ? 38 : 70, fg);
            p.Level = asleep ? Color.FromArgb(120, fg) : charging ? green : pct >= 0 && pct <= 20 ? red : pct >= 0 && pct <= 33 ? amber : fg;
            return p;
        }

        public static Bitmap Render(TrayStyle style, int s, int pct, bool charging, bool asleep, bool light)
        {
            var p = Colors(pct, charging, asleep, light);
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                switch (style)
                {
                    case TrayStyle.Number: Number(g, b, s, p, pct); break;
                    case TrayStyle.Battery: Battery(b, s, p, charging); break;
                    case TrayStyle.Ring: Ring(g, s, p); break;
                    case TrayStyle.Badge: Badge(g, b, s, p, pct); break;
                    default: Mouse(b, s, p, light); break;
                }
            }
            return b;
        }

        // ---------- styles ----------

        // The mouse fills from the bottom like a battery, following the glyph's own interior.
        static void Mouse(Bitmap b, int s, Palette p, bool light)
        {
            var mouse = Glyph(Glyphs.Mouse, s);
            if (p.Hidden) { Paint(b, mouse, p.Green); return; }
            var inside = Glyphs.Interior(mouse, s);
            int top = s, bottom = -1;
            for (int i = 0; i < inside.Length; i++)
                if (inside[i]) { int y = i / s; if (y < top) top = y; if (y > bottom) bottom = y; }
            int cut = bottom - (int)Math.Round((bottom - top + 1) * p.Frac) + 1;
            var level = new byte[s * s];
            var empty = new byte[s * s];
            for (int i = 0; i < inside.Length; i++)
                if (inside[i]) { if (i / s >= cut) level[i] = 255; else empty[i] = 255; }
            // A neutral fill would merge with the outline, so a healthy level is green here.
            Color fill = p.Level == p.Fg ? p.Green : p.Level;
            Paint(b, empty, Color.FromArgb(light ? 30 : 45, p.Fg));
            Paint(b, level, fill);
            Paint(b, mouse, p.Fg);
        }

        // The number, with a level bar underneath.
        static void Number(Graphics g, Bitmap b, int s, Palette p, int pct)
        {
            int bar = Math.Max(2, s / 8), barY = s - bar;
            g.SmoothingMode = SmoothingMode.None;
            using (var t = new SolidBrush(p.Track)) g.FillRectangle(t, 1, barY, s - 2, bar);
            using (var l = new SolidBrush(p.Level)) if (p.Frac > 0.01f) g.FillRectangle(l, 1, barY, Math.Max(1, (int)Math.Round((s - 2) * p.Frac)), bar);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (p.Hidden) { Icons.Fill(g, Symbol.Bolt, new PointF(s / 2f, (barY - 1) / 2f + 0.5f), barY - 2, p.Green); return; }
            g.Flush();
            string text = pct >= 0 ? pct.ToString() : "–";
            int area = barY - 1;
            var cov = Glyphs.Coverage(text, DigitsFace, s, (int)Math.Round(area * (text.Length >= 3 ? 0.95f : 1.12f)));
            Paint(b, Shift(cov, s, 0, -(int)Math.Round((s - area) / 2f)), p.Fg);
        }

        // The Windows battery glyph in ten steps.
        static void Battery(Bitmap b, int s, Palette p, bool charging)
        {
            if (p.Hidden) { Paint(b, Glyph(Glyphs.BatteryCharging0, s), p.Green); return; }
            int step = Math.Max(0, Math.Min(10, (int)Math.Round(p.Frac * 10)));
            char glyph = (char)((charging ? Glyphs.BatteryCharging0 : Glyphs.Battery0) + step);
            Paint(b, Glyph(glyph, s), p.Level);
        }

        // An arc filling clockwise from the top over a faint track.
        static void Ring(Graphics g, int s, Palette p)
        {
            float w = Math.Max(2f, s / 5.3f);
            var r = new RectangleF(w / 2 + 0.5f, w / 2 + 0.5f, s - w - 1, s - w - 1);
            using (var t = new Pen(p.Track, w)) g.DrawEllipse(t, r);
            using (var a = new Pen(p.Level, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (p.Frac > 0.01f) g.DrawArc(a, r, -90, 360 * p.Frac);
            if (p.Hidden) Icons.Fill(g, Symbol.Bolt, new PointF(s / 2f, s / 2f), s * 0.4f, p.Green);
        }

        // The number cut out of a tile whose colour carries the state. Regular-width digits keep strokes
        // thick enough to survive being cut out at 16 px.
        static void Badge(Graphics g, Bitmap b, int s, Palette p, int pct)
        {
            Draw.FillRound(g, p.Level, new RectangleF(0, 0, s, s), s * 0.22f);
            g.Flush();
            if (p.Hidden) { Knock(b, Mask(s, mg => Icons.Fill(mg, Symbol.Bolt, new PointF(s / 2f, s / 2f), s * 0.64f, Color.White))); return; }
            string text = pct >= 0 ? pct.ToString() : "–";
            Knock(b, Glyphs.Coverage(text, BadgeFace, s, (int)Math.Round(s * (text.Length >= 3 ? 0.64f : 0.86f))));
        }

        // ---------- pixels ----------

        static string digits, badge;
        static string BadgeFace { get { return badge ?? (badge = Draw.PickFont("Bahnschrift SemiBold", "Segoe UI Semibold")); } }
        static string DigitsFace { get { return digits ?? (digits = Draw.PickFont("Bahnschrift SemiBold Condensed", "Bahnschrift", "Segoe UI Semibold")); } }

        static byte[] Glyph(char c, int s) { return Glyphs.Coverage(c.ToString(), Glyphs.IconFace, s, s); }

        static byte[] Shift(byte[] cov, int s, int dx, int dy)
        {
            var o = new byte[cov.Length];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int sx = x - dx, sy = y - dy;
                    if (sx >= 0 && sy >= 0 && sx < s && sy < s) o[y * s + x] = cov[sy * s + sx];
                }
            return o;
        }

        // Coverage of whatever draw() paints, from its alpha.
        static byte[] Mask(int s, Action<Graphics> draw)
        {
            using (var m = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(m)) { g.SmoothingMode = SmoothingMode.AntiAlias; draw(g); }
                var px = Pixels(m);
                var cov = new byte[s * s];
                for (int i = 0; i < cov.Length; i++) cov[i] = (byte)((px[i] >> 24) & 255);
                return cov;
            }
        }

        // Source-over of colour c through a coverage map.
        static void Paint(Bitmap b, byte[] cov, Color c)
        {
            var px = Pixels(b);
            for (int i = 0; i < px.Length; i++)
            {
                int sa = cov[i] * c.A / 255;
                if (sa == 0) continue;
                int da = (px[i] >> 24) & 255, oa = sa + da * (255 - sa) / 255;
                int r = (c.R * sa + ((px[i] >> 16) & 255) * da * (255 - sa) / 255) / oa;
                int g = (c.G * sa + ((px[i] >> 8) & 255) * da * (255 - sa) / 255) / oa;
                int bl = (c.B * sa + (px[i] & 255) * da * (255 - sa) / 255) / oa;
                px[i] = (oa << 24) | (r << 16) | (g << 8) | bl;
            }
            SetPixels(b, px);
        }

        // Cuts a coverage map out of what's drawn.
        static void Knock(Bitmap b, byte[] cov)
        {
            var px = Pixels(b);
            for (int i = 0; i < px.Length; i++)
            {
                int a = ((px[i] >> 24) & 255) * (255 - cov[i]) / 255;
                px[i] = (a << 24) | (px[i] & 0xFFFFFF);
            }
            SetPixels(b, px);
        }

        static int[] Pixels(Bitmap b)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            b.UnlockBits(d);
            return px;
        }

        static void SetPixels(Bitmap b, int[] px)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, d.Scan0, px.Length);
            b.UnlockBits(d);
        }
    }
}
