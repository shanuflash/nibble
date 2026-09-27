using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Nibble
{
    static class IconArt
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        // ---------- tray ----------
        // Everything is drawn from Segoe Fluent Icons / hinted GDI text via Glyphs, like the shell's
        // own tray icons, so it stays crisp at 16 px.

        public static readonly string[] TrayStyles = { "Mouse", "Number", "Battery", "Tinted", "Minimal" };

        public static Icon TrayIcon(int style, int size, int percent, bool charging, bool asleep, bool lightTaskbar)
        {
            using (var bmp = TrayArt(style, size, percent, charging, asleep, lightTaskbar))
            {
                IntPtr h = bmp.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }

        // Also used at larger sizes for the style picker previews.
        public static Bitmap TrayArt(int style, int s, int percent, bool charging, bool asleep, bool light)
        {
            Color fg = light ? Color.FromArgb(255, 20, 20, 22) : Color.White;
            Color green = light ? Theme.Hex(0x1E9E48) : Theme.Hex(0x30D158);
            Color red = light ? Theme.Hex(0xE0352B) : Theme.Hex(0xFF453A);
            Color amber = light ? Theme.Hex(0xC98A00) : Theme.Hex(0xFFB020);
            bool low = percent >= 0 && percent <= 20;
            Color line = asleep ? Color.FromArgb(150, fg) : fg;
            Color state = asleep ? Color.FromArgb(150, fg) : charging ? green : low ? red : fg;
            string mouse = Glyphs.Mouse.ToString();

            switch (style)
            {
                case 1:
                {
                    string text = percent >= 0 ? percent.ToString() : "\u2013";
                    int px = text.Length >= 3 ? (int)Math.Round(s * 0.78) : s + 1;
                    return Glyphs.Compose(s, Glyphs.Coverage(text, Digits, s, px, 400, 0), state);
                }
                case 2:
                {
                    int step = Math.Max(0, Math.Min(10, (int)Math.Round(Math.Max(0, percent) / 10.0)));
                    char g = (char)((charging ? Glyphs.BatteryCharging0 : Glyphs.Battery0) + step);
                    return Glyphs.Compose(s, Glyphs.Coverage(g.ToString(), Glyphs.IconFace, s, s, 400, 0), charging || low || asleep ? state : fg);
                }
                case 3:
                {
                    Color tint = asleep ? line : charging || percent > 50 ? green : low ? red : amber;
                    var cov = Glyphs.Coverage(mouse, Glyphs.IconFace, s, s, 400, 0);
                    return Glyphs.Compose(s, Glyphs.Interior(cov, s), Color.FromArgb(asleep ? 60 : 110, tint), cov, tint);
                }
                case 4:
                    return Glyphs.Compose(s, Glyphs.Coverage(mouse, Glyphs.IconFace, s, s, 400, 0), state);
                default:
                {
                    // The mouse fills from the bottom like a battery; fill follows the glyph's own interior.
                    var cov = Glyphs.Coverage(mouse, Glyphs.IconFace, s, s, 400, 0);
                    var inside = Glyphs.Interior(cov, s);
                    int top = s, bottom = -1;
                    for (int i = 0; i < inside.Length; i++)
                        if (inside[i]) { int y = i / s; if (y < top) top = y; if (y > bottom) bottom = y; }
                    int cut = bottom - (int)Math.Round((bottom - top + 1) * Math.Max(0, Math.Min(100, percent)) / 100.0) + 1;
                    var level = new bool[s * s];
                    var empty = new bool[s * s];
                    for (int i = 0; i < inside.Length; i++) { level[i] = inside[i] && i / s >= cut; empty[i] = inside[i] && !level[i]; }
                    // A coloured fill over a faint empty part, so it reads as a level rather than a solid mouse.
                    Color fill = asleep ? Color.FromArgb(90, fg)
                        : charging ? green
                        : low ? red
                        : percent <= 50 ? amber
                        : green;
                    return Glyphs.Compose(s, empty, Color.FromArgb(light ? 30 : 45, fg), level, fill, cov, line);
                }
            }
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
