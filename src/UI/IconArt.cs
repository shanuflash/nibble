using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Nibble.UI
{
    enum TrayStyle { Mouse, Number, Battery, Tinted, Minimal }

    static class IconArt
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static readonly string[] StyleNames = { "Mouse", "Number", "Battery", "Tinted", "Minimal" };

        public static Icon TrayIcon(TrayStyle style, int size, int percent, bool charging, bool asleep, bool lightTaskbar)
        {
            using (var bmp = TrayArt(style, size, percent, charging, asleep, lightTaskbar))
            {
                IntPtr h = bmp.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }

        // Tray glyphs come from Segoe Fluent Icons through hinted GDI (see Glyphs), like the shell's own tray
        // icons, so they stay crisp at 16 px. Also drawn larger for the style picker.
        public static Bitmap TrayArt(TrayStyle style, int s, int percent, bool charging, bool asleep, bool light)
        {
            Color fg = light ? Color.FromArgb(255, 20, 20, 22) : Color.White;
            Color green = light ? Theme.Hex(0x1E9E48) : Theme.Hex(0x30D158);
            Color red = light ? Theme.Hex(0xE0352B) : Theme.Hex(0xFF453A);
            Color amber = light ? Theme.Hex(0xC98A00) : Theme.Hex(0xFFB020);
            bool low = percent >= 0 && percent <= 20;
            Color dim = Color.FromArgb(150, fg);
            Color state = asleep ? dim : charging ? green : low ? red : fg;
            var mouse = Glyph(Glyphs.Mouse, s);

            // Charging with the level hidden: show "charging" without faking a level.
            if (charging && percent < 0 && !asleep)
            {
                if (style == TrayStyle.Number) return SolidBolt(s, green);
                if (style == TrayStyle.Battery) return Glyphs.Compose(s, Glyph(Glyphs.BatteryCharging0, s), green);
                return Glyphs.Compose(s, mouse, green);
            }

            switch (style)
            {
                case TrayStyle.Number:
                {
                    string text = percent >= 0 ? percent.ToString() : "–";
                    int px = text.Length >= 3 ? (int)Math.Round(s * 0.78) : s + 1;
                    return Glyphs.Compose(s, Glyphs.Coverage(text, Digits, s, px), state);
                }
                case TrayStyle.Battery:
                {
                    int step = Math.Max(0, Math.Min(10, (int)Math.Round(Math.Max(0, percent) / 10.0)));
                    char g = (char)((charging ? Glyphs.BatteryCharging0 : Glyphs.Battery0) + step);
                    return Glyphs.Compose(s, Glyph(g, s), charging || low || asleep ? state : fg);
                }
                case TrayStyle.Tinted:
                {
                    Color tint = asleep ? dim : charging || percent > 50 ? green : low ? red : amber;
                    return Glyphs.Compose(s, Glyphs.Interior(mouse, s), Color.FromArgb(asleep ? 60 : 110, tint), mouse, tint);
                }
                case TrayStyle.Minimal:
                    return Glyphs.Compose(s, mouse, state);
                default:
                {
                    // The mouse fills from the bottom like a battery, following the glyph's own interior.
                    var inside = Glyphs.Interior(mouse, s);
                    int top = s, bottom = -1;
                    for (int i = 0; i < inside.Length; i++)
                        if (inside[i]) { int y = i / s; if (y < top) top = y; if (y > bottom) bottom = y; }
                    int cut = bottom - (int)Math.Round((bottom - top + 1) * Math.Max(0, Math.Min(100, percent)) / 100.0) + 1;
                    var level = new bool[s * s];
                    var empty = new bool[s * s];
                    for (int i = 0; i < inside.Length; i++) { level[i] = inside[i] && i / s >= cut; empty[i] = inside[i] && !level[i]; }
                    Color fill = asleep ? Color.FromArgb(90, fg) : charging ? green : low ? red : percent <= 50 ? amber : green;
                    return Glyphs.Compose(s, empty, Color.FromArgb(light ? 30 : 45, fg), level, fill, mouse, asleep ? dim : fg);
                }
            }
        }

        static byte[] Glyph(char c, int s) { return Glyphs.Coverage(c.ToString(), Glyphs.IconFace, s, s); }

        static Bitmap SolidBolt(int s, Color c)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) Icons.Fill(g, Symbol.Bolt, new PointF(s / 2f, s / 2f), s - 1, c);
            return bmp;
        }

        static string digits;
        static string Digits { get { return digits ?? (digits = Draw.PickFont("Bahnschrift SemiBold Condensed", "Bahnschrift", "Segoe UI Semibold")); } }

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
