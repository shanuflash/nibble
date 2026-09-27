using System;
using System.Drawing;
using Microsoft.Win32;

namespace Nibble.UI
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

        // Preferences.Appearance: 0 = follow Windows, 1 = light, 2 = dark
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
}
