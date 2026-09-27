using System.Windows.Forms;
using Microsoft.Win32;
using Nibble.UI;

namespace Nibble
{
    // Nibble's own settings, stored under HKCU\Software\Nibble.
    sealed class Preferences
    {
        const string Key = @"Software\Nibble";

        public static readonly int[] Intervals = { 30, 60, 300, 900 };
        public static readonly string[] IntervalNames = { "30s", "1m", "5m", "15m" };

        public int IntervalSec = 60;
        public bool BatteryAlerts = true;
        public bool AlertsOverGames = true;   // click-through over borderless and fullscreen-optimised games
        public bool DpiPopup = true;
        public TrayStyle TrayStyle = TrayStyle.Mouse;
        public int Appearance;                // 0 = follow Windows, 1 = light, 2 = dark
        public int LastPercent = -1;          // so a sleeping mouse still shows its level after a restart

        public static Preferences Load()
        {
            var p = new Preferences();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    if (k == null) return p;
                    int v;
                    if (Read(k, "IntervalSec", out v) && v >= 10) p.IntervalSec = v;
                    if (Read(k, "LowAlert", out v)) p.BatteryAlerts = v != 0;
                    if (Read(k, "AlertsOverGames", out v)) p.AlertsOverGames = v != 0;
                    if (Read(k, "ShowDpiHud", out v)) p.DpiPopup = v != 0;
                    if (Read(k, "TrayStyle", out v) && v >= 0 && v < IconArt.StyleNames.Length) p.TrayStyle = (TrayStyle)v;
                    if (Read(k, "Appearance", out v) && v >= 0 && v <= 2) p.Appearance = v;
                    if (Read(k, "LastPercent", out v) && v >= 0 && v <= 100) p.LastPercent = v;
                }
            }
            catch { }
            return p;
        }

        public void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    Write(k, "IntervalSec", IntervalSec);
                    Write(k, "LowAlert", BatteryAlerts ? 1 : 0);
                    Write(k, "AlertsOverGames", AlertsOverGames ? 1 : 0);
                    Write(k, "ShowDpiHud", DpiPopup ? 1 : 0);
                    Write(k, "TrayStyle", (int)TrayStyle);
                    Write(k, "Appearance", Appearance);
                }
            }
            catch { }
        }

        public void SaveLastPercent(int percent)
        {
            if (percent < 0 || percent == LastPercent) return;
            LastPercent = percent;
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) Write(k, "LastPercent", percent); }
            catch { }
        }

        static bool Read(RegistryKey k, string name, out int value)
        {
            object v = k.GetValue(name);
            value = v is int ? (int)v : 0;
            return v is int;
        }

        static void Write(RegistryKey k, string name, int value) { k.SetValue(name, value, RegistryValueKind.DWord); }
    }

    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        var v = k == null ? null : k.GetValue(AppInfo.Name) as string;
                        return v != null && v.Trim('"').Equals(Application.ExecutablePath, System.StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(AppInfo.Name, "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue(AppInfo.Name, false);
                    }
                }
                catch { }
            }
        }
    }
}
