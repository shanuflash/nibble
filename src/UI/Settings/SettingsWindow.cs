using System;
using System.Drawing;
using System.Windows.Forms;

namespace Nibble.UI.Settings
{
    // The settings card: a sidebar of pages, the current page, and a save status pill.
    sealed class SettingsWindow : GlassCard
    {
        const float SideW = 236;

        readonly TrayApp app;
        readonly SettingsPage[] pages;
        readonly string[] groupTitles = { "Mouse", "App" };
        readonly SettingsPage[][] groups;

        Color sideFlat;
        int page;
        float pageT = 1; int pageT0;

        public SettingsWindow(TrayApp app) : base(980, 640, 30)
        {
            this.app = app;
            var dpi = new DpiPage(app);
            var perf = new PerformancePage(app);
            var power = new PowerPage(app);
            var general = new GeneralPage(app);
            var device = new DevicePage(app);
            pages = new SettingsPage[] { dpi, perf, power, general, device };
            groups = new[] { new SettingsPage[] { dpi, perf, power, device }, new SettingsPage[] { general } };
            app.Changed += OnState;
        }

        void OnState(object s, EventArgs e) { AppChanged(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            app.Changed -= OnState;
            base.OnFormClosed(e);
        }

        protected override Color CardTint(bool dark) { return dark ? Color.FromArgb(205, 24, 24, 28) : Color.FromArgb(200, 250, 250, 252); }
        Color SideColor() { return Th.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255); }

        protected override void GlassBuilt() { sideFlat = Draw.Flatten(SideColor(), C.CardFlat); }

        protected override void Opening()
        {
            page = 0; pageT = 1;
            pages[0].Opened();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode >= Keys.D1 && e.KeyCode < Keys.D1 + pages.Length) GoTo(e.KeyCode - Keys.D1);
            else if (e.KeyCode == Keys.Down) GoTo(Math.Min(pages.Length - 1, page + 1));
            else if (e.KeyCode == Keys.Up) GoTo(Math.Max(0, page - 1));
            base.OnKeyDown(e);
        }

        void GoTo(int p)
        {
            if (p == page) return;
            page = p; pageT = 0; pageT0 = Environment.TickCount;
            CancelDrag();
            pages[p].Opened();
            Kick();
        }

        protected override bool Step(int now)
        {
            bool more = false;
            if (pageT < 1) { pageT = Math.Min(1, (now - pageT0) / 180f); more = true; }
            return more || app.Mouse.Saving;
        }

        // ---------- painting ----------

        protected override void PaintCard()
        {
            PaintSidebar();

            var content = new RectangleF(F(SideW + 34), F(34), F(CardW - SideW - 34 - 34), F(CardH - 68));
            C.Fade = pageT;
            C.Interactive = pageT >= 1;
            pages[page].Paint(C, content);
            C.Fade = 1;
            C.Interactive = true;

            PaintClose(CardW - 34, 26);
            PaintStatus(CardW - 34 - 32);
        }

        void PaintSidebar()
        {
            var side = new RectangleF(F(12), F(12), F(SideW - 12), F(CardH - 24));
            Draw.FillRound(C.G, SideColor(), side, F(30 - 12));
            Draw.Rim(C.G, side, F(30 - 12), Th.PlatterRimTop, Th.PlatterRimBottom, Math.Max(1f, F(0.8f)));

            int ai = Pi(28);
            using (var art = IconArt.AppArt(ai)) C.G.DrawImageUnscaled(art, (int)(side.X + F(16)), (int)(side.Y + F(18)));
            C.Text(AppInfo.Name, C.Head, Th.Label, sideFlat, new RectangleF(side.X + F(16) + ai + F(10), side.Y + F(18), F(140), ai), TextAlign.Left);

            var dev = new RectangleF(side.X + F(10), side.Y + F(62), side.Width - F(20), F(60));
            Draw.FillRound(C.G, Th.Platter, dev, F(14));
            Draw.Rim(C.G, dev, F(14), Th.PlatterRimTop, Th.PlatterRimBottom, Math.Max(1f, F(0.8f)));
            DeviceBadge(new RectangleF(dev.X + F(11), dev.Y + F(11), F(38), F(38)), Draw.Flatten(Th.Platter, sideFlat), dev.X + F(60), dev.Width - F(66));

            float y = dev.Bottom + F(18);
            for (int gi = 0; gi < groups.Length; gi++)
            {
                C.Text(groupTitles[gi], C.Cap, Th.Secondary, sideFlat, new RectangleF(side.X + F(18), y, F(160), F(16)), TextAlign.Left);
                y += F(20);
                foreach (var pg in groups[gi])
                {
                    int i = Array.IndexOf(pages, pg);
                    var r = new RectangleF(side.X + F(8), y, side.Width - F(16), F(36));
                    string id = "nav" + i;
                    bool sel = i == page;
                    Color rowFill = sel ? Th.Blue : C.Hover == id ? (Th.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255)) : Color.Empty;
                    if (!rowFill.IsEmpty) Draw.FillRound(C.G, rowFill, r, F(10));
                    var tile = new RectangleF(r.X + F(7), r.Y + F(6), F(24), F(24));
                    C.Tile(tile, pg.Tint);
                    C.SymbolTile(pg.Icon, tile, Color.White);
                    C.Text(pg.Title, sel ? C.Semi : C.Nav, sel ? Color.White : Th.Label, sideFlat,
                        new RectangleF(tile.Right + F(11), r.Y, r.Width - F(46), r.Height), TextAlign.Left);
                    C.Hit(id, r, delegate { GoTo(i); });
                    y += F(38);
                }
                y += F(14);
            }
        }

        // Battery ring around the mouse, with its name and status.
        void DeviceBadge(RectangleF box, Color under, float textX, float textW)
        {
            var m = app.Mouse;
            C.BatteryRing(m, box, Math.Max(2.5f, box.Width * 0.08f), true);
            string link = m.Wired ? "USB" : "2.4 GHz";
            string sub = !m.Found ? "Receiver not found"
                : !m.Online ? (m.Percent >= 0 ? m.Percent + "% · asleep" : "Asleep")
                : m.Percent < 0 ? (m.Charging ? "Charging" : "Connected")
                : m.PercentText + " · " + (m.FullyCharged ? "charged" : m.Charging ? "charging" : link);
            float cy = box.Y + box.Height / 2;
            C.Text(m.Device.Name, C.Semi, Th.Label, under, new RectangleF(textX, cy - F(19), textW, F(20)), TextAlign.Left);
            C.Text(sub, C.Sub, Th.Secondary, under, new RectangleF(textX, cy + F(1), textW, F(18)), TextAlign.Left);
        }

        // Save status pill, left of the close button.
        void PaintStatus(float right)
        {
            var m = app.Mouse;
            string msg = null; Color mc = Th.Secondary;
            if (m.Saving) msg = "Saving…";
            else if (m.SaveFailed && (DateTime.Now - m.SavedAt).TotalSeconds < 6) { msg = "Couldn’t reach the mouse"; mc = Th.Red; }
            else if ((DateTime.Now - m.SavedAt).TotalSeconds < 2.5) { msg = "Saved to mouse"; mc = Th.Green; }
            else if (!m.Online) { msg = m.Found ? "Mouse asleep — move it to wake" : "Receiver not connected"; mc = Th.Orange; }
            if (msg == null) return;
            var sz = C.Measure(msg, C.SmallSemi);
            var pill = new RectangleF(F(right) - F(12) - sz.Width - F(28), F(29), sz.Width + F(28), F(26));
            var fill = Th.Dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255);
            Draw.FillRound(C.G, fill, pill, pill.Height / 2);
            C.Text(msg, C.SmallSemi, mc, Draw.Flatten(fill, C.CardFlat), pill, TextAlign.Center);
        }

        // ---------- input ----------

        // Empty space in the header strip or the sidebar drags the window.
        protected override bool IsDragZone(float x, float y) { return y < F(70) || x < F(SideW); }

        protected override bool BeginDrag(string hitId, float x) { return pages[page].BeginDrag(hitId, x); }
        protected override void DragTo(float x) { pages[page].DragTo(x); }
        protected override void EndDrag() { pages[page].EndDrag(); }

        // ---------- design review ----------

        public void Snapshot(string path, bool dark, int pageIndex)
        {
            page = pageIndex; pageT = 1;
            SnapshotTo(path, dark);
        }
    }
}
