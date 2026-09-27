using System;
using System.Drawing;
using System.Windows.Forms;
using Nibble.Devices;
using Nibble.UI;
using Nibble.UI.Popups;
using Nibble.UI.Settings;

namespace Nibble
{
    // Developer modes for reviewing the UI without a mouse:
    //   --snapshot out.png dark|light [percent] [charging|asleep] [settings]    flyout
    //   --snapshot-settings out.png dark|light page [charging]                 settings window
    //   --test-dpi-hud                                                         DPI popup, three presses
    //   --test-notifications                                                   each battery alert
    static class Previews
    {
        public static bool Run(string[] a)
        {
            if (a.Length >= 3 && a[0] == "--snapshot")
            {
                Program.InitPreview();
                int percent = a.Length > 3 ? int.Parse(a[3]) : 57;
                bool charging = a.Length > 4 && a[4] == "charging";
                bool online = !(a.Length > 4 && a[4] == "asleep");
                var m = Session(online, percent, charging, DateTime.Now.AddSeconds(-4));
                new Flyout(TrayApp.Preview(m)).Snapshot(a[1], a[2] == "dark", Program.Has(a, "settings"));
                return true;
            }
            if (a.Length >= 4 && a[0] == "--snapshot-settings")
            {
                Program.InitPreview();
                bool charging = Program.Has(a, "charging");
                var m = Session(true, charging ? -1 : 55, charging, DateTime.Now);
                using (var w = new SettingsWindow(TrayApp.Preview(m))) w.Snapshot(a[1], a[2] == "dark", int.Parse(a[3]));
                return true;
            }
            if (a.Length >= 1 && a[0] == "--test-dpi-hud") { TestDpiHud(); return true; }
            if (a.Length >= 1 && a[0] == "--test-notifications") { TestNotifications(); return true; }
            return false;
        }

        static MouseSession Session(bool online, int percent, bool charging, DateTime synced)
        {
            var m = new MouseSession(Drivers.All[0]);
            m.Simulate(online, percent, charging, SampleSettings(m.Device.Caps), synced);
            return m;
        }

        // One 1200 DPI stage at 2000 Hz.
        static MouseSettings SampleSettings(MouseCaps caps)
        {
            var s = new MouseSettings(caps) { PollingHz = 2000, SleepSeconds = 60, Debounce = 5, SensorMode = 1 };
            int[] dpi = { 1200, 1200, 3200, 6400, 12800, 26000 };
            int[] led = { 0x00FF00, 0xFF0000, 0x00FFFF, 0xFF00FF, 0x0000FF, 0xFFFFFF };
            for (int i = 1; i <= caps.MaxStages; i++) { s.SetDpi(i, dpi[i - 1]); s.SetStageColor(i, Theme.Hex(led[i - 1])); }
            s.SetStages(1, 1);
            return s;
        }

        static void TestDpiHud()
        {
            Program.InitPreview();
            int[] dpis = { 800, 1600, 3200 };
            Color[] leds = { Theme.Hex(0xFF0000), Theme.Hex(0x00FF00), Theme.Hex(0x00FFFF) };
            int i = 0;
            var t = new Timer { Interval = 400 };
            t.Tick += delegate
            {
                t.Interval = 900;
                if (i < 3) { DpiHud.Show(dpis[i], i + 1, 3, leds[i]); i++; }
                else if (i++ > 6) { t.Stop(); Application.ExitThread(); }
            };
            t.Start();
            Application.Run();
        }

        static void TestNotifications()
        {
            Program.InitPreview();
            string name = Drivers.All[0].Name;
            var steps = new Action[]
            {
                delegate { BatteryAlerts.ShowLow(name, 18, null, true); },
                delegate { BatteryAlerts.ShowFull(name, null, true); },
                delegate { Application.ExitThread(); }
            };
            int i = 0;
            var t = new Timer { Interval = 500 };
            t.Tick += delegate { t.Interval = 7500; steps[i++](); if (i == steps.Length) t.Stop(); };
            t.Start();
            Application.Run();
        }
    }
}
