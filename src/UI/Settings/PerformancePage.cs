using System;
using System.Collections.Generic;
using System.Drawing;
using Nibble.Devices;

namespace Nibble.UI.Settings
{
    sealed class PerformancePage : SettingsPage
    {
        sealed class Toggle
        {
            public Tuning Flag; public string Title, Sub;
            public Toggle(Tuning flag, string title, string sub) { Flag = flag; Title = title; Sub = sub; }
        }

        static readonly Toggle[] Toggles =
        {
            new Toggle(Tuning.MotionSync, "Motion Sync", "Aligns sensor frames with each report"),
            new Toggle(Tuning.RippleControl, "Ripple Control", "Smooths jitter above 9000 DPI"),
            new Toggle(Tuning.AngleSnapping, "Angle Snapping", "Straightens slightly off-axis lines"),
            new Toggle(Tuning.GlassMode, "Glass Mode", "Better tracking on glass surfaces")
        };

        public PerformancePage(TrayApp app) : base(app) { }

        public override string Title { get { return "Performance"; } }
        public override Symbol Icon { get { return Symbol.Bolt; } }
        public override Color Tint { get { return Theme.Hex(0x007AFF); } }

        public override void Paint(Canvas c, RectangleF area)
        {
            float y = c.Header(area, Title, "How the sensor and radio behave. Changes apply instantly.");
            if (!HaveSettings(c, area, y)) return;
            var cfg = Mouse.Settings;
            var caps = cfg.Caps;
            float rh = c.F(58);

            var p1 = new RectangleF(area.X, y, area.Width, rh * 2);
            c.Platter(p1);
            c.RowLabel(p1, 0, rh, "Polling rate", "Reports per second, in Hz");
            var rates = new string[caps.PollingRates.Length];
            for (int i = 0; i < rates.Length; i++) rates[i] = HzName(caps.PollingRates[i]);
            c.Segmented(c.RowControl(p1, 0, rh, 392, 32, 14), rates, Array.IndexOf(caps.PollingRates, cfg.PollingHz), "rate", delegate (int i)
            {
                int hz = caps.PollingRates[i];
                Mouse.Change(x => x.PollingHz = hz, SettingGroup.PollingRate);
            });
            c.Sep(p1, rh);
            c.RowLabel(p1, 1, rh, "Sensor mode", caps.SensorModeHints[cfg.SensorMode]);
            c.Segmented(c.RowControl(p1, 1, rh, 360, 32, 14), caps.SensorModes, cfg.SensorMode, "mode", delegate (int i)
            {
                Mouse.Change(x => x.SensorMode = i, SettingGroup.Sensor);
            });
            y = p1.Bottom + c.F(14);

            var toggles = new List<Toggle>();
            foreach (var t in Toggles) if ((caps.Tunings & t.Flag) != 0) toggles.Add(t);
            if (toggles.Count > 0)
            {
                var p2 = new RectangleF(area.X, y, area.Width, rh * toggles.Count);
                c.Platter(p2);
                for (int i = 0; i < toggles.Count; i++)
                {
                    var t = toggles[i];
                    string sub = t.Flag == Tuning.MotionSync && caps.MotionSyncMaxHz > 0 && cfg.PollingHz > caps.MotionSyncMaxHz
                        ? "Not supported at " + cfg.PollingHz + " Hz" : t.Sub;
                    if (i > 0) c.Sep(p2, rh * i);
                    c.SwitchRow(p2, i, rh, t.Title, sub, cfg.Has(t.Flag), "tune" + (int)t.Flag,
                        delegate { Mouse.Change(x => x.Set(t.Flag, !x.Has(t.Flag)), SettingGroup.Sensor); });
                }
                y = p2.Bottom + c.F(14);
            }

            var p3 = new RectangleF(area.X, y, area.Width, rh * 2);
            c.Platter(p3);
            c.RowLabel(p3, 0, rh, "Lift-off distance", "Height where tracking stops");
            c.Segmented(c.RowControl(p3, 0, rh, 270, 32, 14), caps.LiftOffNames, cfg.LiftOff, "lod", delegate (int i)
            {
                Mouse.Change(x => x.LiftOff = i, SettingGroup.Sensor);
            });
            c.Sep(p3, rh);
            c.RowLabel(p3, 1, rh, "Click debounce", "Lower is faster; raise it if clicks double");
            c.Stepper(c.RowControl(p3, 1, rh, 150, 32, 14), cfg.Debounce + " ms", "deb",
                delegate { Mouse.Change(x => x.Debounce = x.Debounce - 1, SettingGroup.Sensor); },
                delegate { Mouse.Change(x => x.Debounce = x.Debounce + 1, SettingGroup.Sensor); });
        }

        static string HzName(int hz) { return hz >= 1000 ? hz / 1000 + "K" : hz.ToString(); }
    }
}
