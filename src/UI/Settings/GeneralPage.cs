using System;
using System.Drawing;

namespace Nibble.UI.Settings
{
    sealed class GeneralPage : SettingsPage
    {
        public GeneralPage(TrayApp app) : base(app) { }

        public override string Title { get { return "General"; } }
        public override Symbol Icon { get { return Symbol.Sliders; } }
        public override Color Tint { get { return Theme.Hex(0xAF52DE); } }

        public override void Opened() { if (App.Update == null) App.CheckUpdates(); }

        public override void Paint(Canvas c, RectangleF area)
        {
            var th = c.Th;
            float y = c.Header(area, Title, "How Nibble looks and behaves.");
            float rh = c.F(58);

            y = PaintTrayGallery(c, area, y) + c.F(14);

            var p = new RectangleF(area.X, y, area.Width, rh * 4);
            c.Platter(p);
            PaintUpdateRow(c, p, 3, rh);
            c.Sep(p, rh * 3);
            c.RowLabel(p, 0, rh, "Appearance", "Nibble’s panels and this window");
            c.Segmented(c.RowControl(p, 0, rh, 270, 32, 14), new[] { "System", "Light", "Dark" }, App.Prefs.Appearance, "appearance",
                delegate (int i) { App.UpdatePrefs(x => x.Appearance = i); });
            c.Sep(p, rh);
            c.SwitchRow(p, 1, rh, "Launch at login", "Start Nibble in the tray with Windows", App.AutoStart, "login",
                delegate { App.AutoStart = !App.AutoStart; });
            c.Sep(p, rh * 2);
            c.RowLabel(p, 2, rh, "Check battery every", "Live changes still arrive instantly");
            c.Segmented(c.RowControl(p, 2, rh, 240, 32, 14), Preferences.IntervalNames, Array.IndexOf(Preferences.Intervals, App.Prefs.IntervalSec), "interval",
                delegate (int i) { App.UpdatePrefs(x => x.IntervalSec = Preferences.Intervals[i]); });
        }

        // Live previews of each tray style at the current level, on a taskbar-coloured well.
        float PaintTrayGallery(Canvas c, RectangleF area, float y)
        {
            var th = c.Th;
            var gp = new RectangleF(area.X, y, area.Width, c.F(176));
            c.Platter(gp);
            var pf = c.PlatterFlat();
            c.Text("Tray icon", c.Row, th.Label, pf, new RectangleF(gp.X + c.F(18), gp.Y + c.F(12), c.F(200), c.F(22)), TextAlign.Left);
            c.Text("Hover it any time for the exact percentage", c.Sub, th.Secondary, pf, new RectangleF(gp.X + c.F(18), gp.Y + c.F(32), c.F(360), c.F(18)), TextAlign.Left);
            int n = TrayArt.Names.Length;
            float gap = c.F(10), tw = (gp.Width - c.F(36) - gap * (n - 1)) / n, tileH = c.F(104);
            int pct = Mouse.Percent >= 0 ? Mouse.Percent : 60;
            bool lightBar = Theme.TaskbarLight();
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(gp.X + c.F(18) + i * (tw + gap), gp.Y + c.F(58), tw, tileH);
                var style = (TrayStyle)i;
                bool sel = App.Prefs.TrayStyle == style;
                string id = "tray" + i;
                var well = new RectangleF(r.X + c.F(8), r.Y + c.F(8), r.Width - c.F(16), c.F(60));
                Color wellColor = lightBar ? Color.FromArgb(255, 238, 238, 240) : Color.FromArgb(255, 40, 40, 44);
                Draw.FillRound(c.G, c.Av(sel ? th.Blue : c.Hover == id ? th.ControlHover : th.SegTrack), r, c.F(16));
                Draw.FillRound(c.G, c.Av(wellColor), well, c.F(10));
                int ps = c.Pi(32);
                using (var art = TrayArt.Render(style, ps, pct, Mouse.Charging, !Mouse.Online && Mouse.Found, lightBar))
                    c.G.DrawImageUnscaled(art, (int)(well.X + (well.Width - ps) / 2), (int)(well.Y + (well.Height - ps) / 2));
                c.Text(TrayArt.Names[i], c.SmallSemi, sel ? Color.White : th.Label, Draw.Flatten(sel ? th.Blue : th.SegTrack, pf),
                    new RectangleF(r.X, well.Bottom + c.F(6), r.Width, c.F(22)), TextAlign.Center);
                if (!sel) c.Hit(id, r, delegate { App.UpdatePrefs(x => x.TrayStyle = style); });
            }
            return gp.Bottom;
        }

        // Nibble's own version vs. the latest GitHub release.
        void PaintUpdateRow(Canvas c, RectangleF p, int i, float rh)
        {
            var u = App.Update;
            string sub = App.UpdateChecking ? "Checking GitHub…"
                : u == null ? "Latest releases from GitHub"
                : u.Available ? "Version " + u.Tag + " is available"
                : u.NoReleases ? "No releases published yet"
                : u.Failed ? "Couldn’t reach GitHub"
                : "You’re up to date";
            c.RowLabel(p, i, rh, AppInfo.Name + " " + AppInfo.Version, sub);
            if (App.UpdateChecking) return;
            var r = c.RowControl(p, i, rh, 130, 30, 18);
            if (u != null && u.Available)
                c.Button(r, "Get " + u.Tag, Color.White, "upd", delegate { App.OpenUrl(u.Url ?? AppInfo.ReleasesPage); }, c.Th.Blue);
            else
                c.Button(r, u == null || u.Failed ? "Check now" : "Check again", c.Th.Label, "upd", App.CheckUpdates);
        }
    }
}
