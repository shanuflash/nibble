using System.Drawing;
using Nibble.Devices;

namespace Nibble.UI.Settings
{
    sealed class PowerPage : SettingsPage
    {
        public PowerPage(TrayApp app) : base(app) { }

        public override string Title { get { return "Power"; } }
        public override Symbol Icon { get { return Symbol.Battery; } }
        public override Color Tint { get { return Theme.Hex(0x34C759); } }

        public override void Paint(Canvas c, RectangleF area)
        {
            var th = c.Th;
            float y = c.Header(area, Title, "Battery, sleep, and how Nibble keeps you posted.");
            float rh = c.F(58);

            var hero = new RectangleF(area.X, y, area.Width, c.F(92));
            c.Platter(hero);
            var pf = c.PlatterFlat();
            float d = c.F(72);
            c.BatteryRing(Mouse, new RectangleF(hero.X + c.F(22), hero.Y + (hero.Height - d) / 2, d, d), c.F(8), false);
            bool levelHidden = Mouse.Online && Mouse.LevelHidden;
            string level = Mouse.Percent >= 0 ? Mouse.PercentText : levelHidden ? "Charging" : "—";
            c.Text(level, c.Big, th.Label, pf, new RectangleF(hero.X + c.F(114), hero.Y + c.F(16), c.F(200), c.F(34)), TextAlign.Left);
            string link = Mouse.Wired ? "USB cable" : "2.4 GHz";
            string status = !Mouse.Found ? "Receiver not connected" : !Mouse.Online ? "Asleep"
                : levelHidden ? (Mouse.Wired ? "Over USB cable" : "Plugged in")
                : Mouse.StatusText + " · " + link;
            c.Text(status, c.Sub, Mouse.Charging || Mouse.FullyCharged ? th.Green : th.Secondary, pf,
                new RectangleF(hero.X + c.F(116), hero.Y + c.F(52), c.F(300), c.F(18)), TextAlign.Left);
            y = hero.Bottom + c.F(14);

            var sp = new RectangleF(area.X, y, area.Width, c.F(132));
            c.Platter(sp);
            c.RowLabel(sp, 0, rh, "Sleep after", "The mouse powers down after sitting still this long");
            if (Mouse.Settings != null)
            {
                var options = Mouse.Settings.Caps.SleepOptions;
                const int perRow = 5;
                float cw = (sp.Width - c.F(28) - c.F(8) * (perRow - 1)) / perRow;
                for (int i = 0; i < options.Length; i++)
                {
                    var chip = new RectangleF(sp.X + c.F(14) + (i % perRow) * (cw + c.F(8)), sp.Y + c.F(58) + (i / perRow) * c.F(36), cw, c.F(30));
                    int secs = options[i];
                    c.Chip(chip, SleepName(secs), Mouse.Settings.SleepSeconds == secs, "sleep" + i,
                        delegate { Mouse.Change(x => x.SleepSeconds = secs, SettingGroup.Sleep); });
                }
            }
            y = sp.Bottom + c.F(14);

            var ap = new RectangleF(area.X, y, area.Width, rh * 2);
            c.Platter(ap);
            var prefs = App.Prefs;
            c.SwitchRow(ap, 0, rh, "Battery alerts", "Notify at 20% and when fully charged", prefs.BatteryAlerts, "alerts",
                delegate { App.UpdatePrefs(p => p.BatteryAlerts = !p.BatteryAlerts); });
            c.Sep(ap, rh);
            c.SwitchRow(ap, 1, rh, "Show alerts over games", "Click-through, so aim and clicks still reach the game", prefs.AlertsOverGames, "overgames",
                delegate { App.UpdatePrefs(p => p.AlertsOverGames = !p.AlertsOverGames); });
        }

        static string SleepName(int seconds)
        {
            if (seconds == 0) return "Never";
            return seconds < 60 ? seconds + " s" : seconds / 60 + " min";
        }
    }
}
