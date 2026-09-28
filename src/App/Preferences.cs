using System;
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

    // Launch at login: a value under HKCU\...\Run, plus its StartupApproved entry, which is where
    // Task Manager's Startup apps toggle lives. Writing both keeps the two switches in agreement.
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // First byte even = enabled (02), odd = disabled by the user (03). The rest is a timestamp, zero is fine.
        static readonly byte[] Approved = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        public static bool Enabled
        {
            get { return Registered() && Approval() != Disabled; }
            set
            {
                try
                {
                    using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                    using (var ok = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                    {
                        if (value)
                        {
                            run.SetValue(AppInfo.Name, "\"" + Application.ExecutablePath + "\"");
                            ok.SetValue(AppInfo.Name, Approved, RegistryValueKind.Binary);
                        }
                        else
                        {
                            run.DeleteValue(AppInfo.Name, false);
                            ok.DeleteValue(AppInfo.Name, false);
                        }
                    }
                }
                catch { }
            }
        }

        const int Missing = 0, On = 1, Disabled = 2;

        static bool Registered()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    var v = k == null ? null : k.GetValue(AppInfo.Name) as string;
                    return v != null && v.Trim('"').Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        static int Approval()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    var b = k == null ? null : k.GetValue(AppInfo.Name) as byte[];
                    if (b == null || b.Length == 0) return Missing;
                    return (b[0] & 1) == 0 ? On : Disabled;
                }
            }
            catch { return Missing; }
        }
    }
}
