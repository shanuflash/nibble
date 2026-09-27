using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace Nibble
{
    // Liquid Glass palette. Fills are translucent so the frosted backdrop shows through.
    class Theme
    {
        public bool Dark;
        public Color Tint;                       // laid over the blurred backdrop
        public Color Label, Secondary, Tertiary;
        public Color Platter, PlatterRimTop, PlatterRimBottom;
        public Color Control, ControlHover, ControlPress;
        public Color RimTop, RimBottom;          // panel specular edge
        public Color Track, SwitchOff, SegTrack, SegThumb, Separator;
        public Color Green, Red, Blue, Orange;
        public Color Fallback;                   // backdrop when the screen can't be captured

        public static bool AppsDark() { return ReadPersonalize("AppsUseLightTheme", 1) == 0; }
        public static bool TaskbarLight() { return ReadPersonalize("SystemUsesLightTheme", 0) == 1; }

        static int ReadPersonalize(string name, int def)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return v is int ? (int)v : def;
                }
            }
            catch { return def; }
        }

        // 0 = follow Windows, 1 = light, 2 = dark (Nibble's Appearance setting)
        public static int Override;

        public static Theme Current() { return Current(Override == 0 ? AppsDark() : Override == 2); }

        public static Theme Current(bool dark)
        {
            var t = new Theme { Dark = dark };
            if (dark)
            {
                t.Tint = Color.FromArgb(150, 18, 18, 22);
                t.Label = Color.FromArgb(255, 255, 255, 255);
                t.Secondary = Color.FromArgb(158, 235, 235, 245);
                t.Tertiary = Color.FromArgb(80, 235, 235, 245);
                t.Platter = Color.FromArgb(22, 255, 255, 255);
                t.PlatterRimTop = Color.FromArgb(46, 255, 255, 255);
                t.PlatterRimBottom = Color.FromArgb(8, 255, 255, 255);
                t.Control = Color.FromArgb(30, 255, 255, 255);
                t.ControlHover = Color.FromArgb(44, 255, 255, 255);
                t.ControlPress = Color.FromArgb(64, 255, 255, 255);
                t.RimTop = Color.FromArgb(120, 255, 255, 255);
                t.RimBottom = Color.FromArgb(26, 255, 255, 255);
                t.Track = Color.FromArgb(34, 255, 255, 255);
                t.SwitchOff = Color.FromArgb(40, 255, 255, 255);
                t.SegTrack = Color.FromArgb(24, 255, 255, 255);
                t.SegThumb = Color.FromArgb(60, 255, 255, 255);
                t.Separator = Color.FromArgb(22, 255, 255, 255);
                t.Green = Hex(0x30D158); t.Red = Hex(0xFF453A); t.Blue = Hex(0x0A84FF); t.Orange = Hex(0xFF9F0A);
                t.Fallback = Hex(0x2A2A30);
            }
            else
            {
                t.Tint = Color.FromArgb(150, 250, 250, 252);
                t.Label = Color.FromArgb(255, 17, 17, 20);
                t.Secondary = Color.FromArgb(140, 40, 40, 50);
                t.Tertiary = Color.FromArgb(70, 40, 40, 50);
                t.Platter = Color.FromArgb(110, 255, 255, 255);
                t.PlatterRimTop = Color.FromArgb(200, 255, 255, 255);
                t.PlatterRimBottom = Color.FromArgb(50, 255, 255, 255);
                t.Control = Color.FromArgb(140, 255, 255, 255);
                t.ControlHover = Color.FromArgb(185, 255, 255, 255);
                t.ControlPress = Color.FromArgb(230, 255, 255, 255);
                t.RimTop = Color.FromArgb(230, 255, 255, 255);
                t.RimBottom = Color.FromArgb(70, 255, 255, 255);
                t.Track = Color.FromArgb(22, 0, 0, 0);
                t.SwitchOff = Color.FromArgb(26, 0, 0, 0);
                t.SegTrack = Color.FromArgb(16, 0, 0, 0);
                t.SegThumb = Color.FromArgb(255, 255, 255, 255);
                t.Separator = Color.FromArgb(18, 0, 0, 0);
                t.Green = Hex(0x34C759); t.Red = Hex(0xFF3B30); t.Blue = Hex(0x007AFF); t.Orange = Hex(0xFF9500);
                t.Fallback = Hex(0xE8E8EE);
            }
            return t;
        }

        public static Color Hex(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }
    }

    static class Draw
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        // Specular edge: bright at the top, fading toward the bottom, like light catching glass.
        public static void Rim(Graphics g, RectangleF r, float rad, Color top, Color bottom, float width)
        {
            var rr = new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width);
            using (var p = Round(rr, Math.Max(0, rad - width / 2)))
            using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, 90f))
            {
                var blend = new ColorBlend(4);
                blend.Colors = new[] { top, Lerp(top, bottom, 0.75f), bottom, Lerp(bottom, top, 0.35f) };
                blend.Positions = new[] { 0f, 0.35f, 0.8f, 1f };
                br.InterpolationColors = blend;
                using (var pen = new Pen(br, width)) g.DrawPath(pen, p);
            }
        }

        public static void Bolt(Graphics g, Color c, RectangleF r)
        {
            PointF[] u =
            {
                new PointF(0.62f, 0.00f), new PointF(0.08f, 0.58f), new PointF(0.46f, 0.58f),
                new PointF(0.36f, 1.00f), new PointF(0.92f, 0.40f), new PointF(0.54f, 0.40f)
            };
            var pts = new PointF[u.Length];
            for (int i = 0; i < u.Length; i++) pts[i] = new PointF(r.X + u[i].X * r.Width, r.Y + u[i].Y * r.Height);
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, float k)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * k))), c.R, c.G, c.B);
        }

        public static string PickFont(params string[] names)
        {
            using (var ifc = new InstalledFontCollection())
            {
                foreach (var n in names)
                    foreach (var f in ifc.Families)
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f.Name;
            }
            return "Segoe UI";
        }
    }
}
