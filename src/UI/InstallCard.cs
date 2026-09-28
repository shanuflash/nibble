using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Nibble.UI
{
    enum InstallMode { Install, Update, Installed, Uninstall }
    enum InstallChoice { None, Primary, Secondary }

    // First-run sheet: install (or update) Nibble, or run this copy as is. Also the uninstall
    // confirmation that Settings → Apps opens. Busy and done states play in place.
    sealed class InstallCard : GlassCard
    {
        const float W = 400, Pad = 36, IconSize = 76, ButtonH = 44;
        const int DoneHoldMs = 1500, CheckMs = 460;

        readonly InstallMode mode;
        readonly Version installed;
        bool launchAtLogin = true;
        bool busy, done;
        int doneAt;
        string error;
        public InstallChoice Choice { get; private set; }

        public static InstallChoice Ask(InstallMode mode)
        {
            using (var card = new InstallCard(mode))
            {
                card.ShowOn(Screen.FromPoint(Cursor.Position));
                Application.Run(card);
                return card.Choice;
            }
        }

        public static InstallCard ForPreview(InstallMode mode) { return new InstallCard(mode); }

        InstallCard(InstallMode mode) : base(W, CardHeight(mode), 30)
        {
            this.mode = mode;
            installed = Installer.InstalledVersion;
            if (mode == InstallMode.Update) launchAtLogin = AutoStart.EnabledFor(Installer.InstalledExe);
        }

        static bool HasToggle(InstallMode m) { return m == InstallMode.Install || m == InstallMode.Update; }
        static bool HasFootnote(InstallMode m) { return m == InstallMode.Install; }

        static float CardHeight(InstallMode m)
        {
            float h = Pad + IconSize + 20 + 32 + 8 + 44 + 26 + ButtonH + 12 + 22 + 30;
            if (HasToggle(m)) h += 56 + 20;
            if (HasFootnote(m)) h += 26;
            return h;
        }

        protected override Color CardTint(bool dark) { return dark ? Color.FromArgb(205, 24, 24, 28) : Color.FromArgb(200, 250, 250, 252); }

        // ---------- copy ----------

        string Title
        {
            get
            {
                if (done) return mode == InstallMode.Uninstall ? "Nibble is uninstalled" : "You’re all set";
                switch (mode)
                {
                    case InstallMode.Update: return "Update Nibble";
                    case InstallMode.Installed: return "Nibble is installed";
                    case InstallMode.Uninstall: return "Uninstall Nibble?";
                    default: return "Nibble";
                }
            }
        }

        string Subtitle
        {
            get
            {
                if (done) return mode == InstallMode.Uninstall ? "Thanks for giving it a go." : "Nibble lives in your tray now, next to the clock.";
                string from = installed != null ? installed.ToString() : "the installed version";
                switch (mode)
                {
                    case InstallMode.Update: return "From " + from + " to " + AppInfo.Version + ". Your settings stay as they are.";
                    case InstallMode.Installed: return "Version " + from + " is already on this PC.";
                    case InstallMode.Uninstall: return "This removes Nibble, its Start menu entry and launch at login. Your mouse keeps its settings.";
                    default: return "Your mouse\u2019s battery and settings, in the tray.";
                }
            }
        }

        string PrimaryLabel
        {
            get
            {
                if (busy) return mode == InstallMode.Uninstall ? "Removing" : mode == InstallMode.Update ? "Updating" : "Installing";
                if (error != null) return "Try again";
                switch (mode)
                {
                    case InstallMode.Update: return "Update";
                    case InstallMode.Installed: return "Open Nibble";
                    case InstallMode.Uninstall: return "Uninstall";
                    default: return "Install";
                }
            }
        }

        string LinkLabel
        {
            get
            {
                switch (mode)
                {
                    case InstallMode.Uninstall: return "Cancel";
                    case InstallMode.Install: return "Run without installing";
                    default: return "Run this copy instead";
                }
            }
        }

        // ---------- painting ----------

        protected override void PaintCard()
        {
            if (done) { PaintDone(); return; }
            float cx = CardW / 2, y = Pad;

            PaintIcon(new RectangleF(cx - IconSize / 2, y, IconSize, IconSize));
            y += IconSize + 20;
            C.Text(Title, C.Big, Th.Label, C.CardFlat, new RectangleF(F(Pad), F(y), F(CardW - 2 * Pad), F(32)), TextAlign.Center);
            y += 32 + 8;
            var sub = new RectangleF(F(Pad - 12), F(y), F(CardW - 2 * (Pad - 12)), 0);
            C.Paragraph(error ?? Subtitle, C.Row, error != null ? Th.Red : Th.Secondary, C.CardFlat, sub, TextAlign.Center, F(21));
            y += 44 + 26;

            if (HasToggle(mode))
            {
                var row = new RectangleF(F(Pad - 12), F(y), F(CardW - 2 * (Pad - 12)), F(56));
                C.Platter(row);
                C.SwitchRow(row, 0, F(56), "Launch at login", "Start Nibble with Windows", launchAtLogin, "login",
                    delegate { if (!busy) launchAtLogin = !launchAtLogin; });
                y += 56 + 20;
            }

            PaintPrimary(new RectangleF(F(Pad - 12), F(y), F(CardW - 2 * (Pad - 12)), F(ButtonH)));
            y += ButtonH + 12;
            if (!busy) PaintLink(LinkLabel, y, Secondary);
            y += 22;

            if (HasFootnote(mode))
                C.Text("Just for you · No admin needed · Remove in Settings → Apps", C.Cap, Th.Tertiary, C.CardFlat,
                    new RectangleF(F(Pad - 12), F(y + 8), F(CardW - 2 * (Pad - 12)), F(16)), TextAlign.Center);
        }

        void PaintIcon(RectangleF box)
        {
            // Soft drop shadow so the icon sits on the glass rather than being pasted onto it.
            for (int i = 3; i >= 1; i--)
            {
                var sh = new RectangleF(F(box.X - i * 1.5f), F(box.Y + 3 + i * 1.5f), F(box.Width + i * 3), F(box.Height + i * 3));
                Draw.FillRound(C.G, Color.FromArgb(Th.Dark ? 20 : 6, 0, 0, 0), sh, F(box.Width * 0.26f + i * 1.5f));
            }
            using (var art = IconArt.AppArt(Pi(box.Width))) C.G.DrawImageUnscaled(art, Pi(box.X), Pi(box.Y));
        }

        void PaintPrimary(RectangleF r)
        {
            const string id = "primary";
            Color fill = mode == InstallMode.Uninstall ? Th.Red : Th.Blue;
            if (!busy && C.Pressed == id) fill = Draw.Lerp(fill, Color.Black, 0.12f);
            else if (!busy && C.Hover == id) fill = Draw.Lerp(fill, Color.White, 0.1f);
            Draw.FillRound(C.G, fill, r, r.Height / 2);

            string label = PrimaryLabel;
            var size = C.Measure(label, C.Semi);
            float spin = busy ? F(14 + 8) : 0;
            float x = r.X + (r.Width - size.Width - spin) / 2;
            if (busy) Spinner(new RectangleF(x, r.Y + (r.Height - F(14)) / 2, F(14), F(14)));
            C.Text(label, C.Semi, Color.White, Draw.Flatten(fill, C.CardFlat), new RectangleF(x + spin, r.Y, size.Width + F(4), r.Height), TextAlign.Left);
            if (!busy) C.Hit(id, r, Primary);
        }

        void Spinner(RectangleF r)
        {
            float angle = (Environment.TickCount % 900) / 900f * 360;
            using (var track = new Pen(Color.FromArgb(70, 255, 255, 255), F(2))) C.G.DrawEllipse(track, r);
            using (var p = Draw.RoundPen(Color.White, F(2))) C.G.DrawArc(p, r, angle, 90);
        }

        void PaintLink(string label, float y, Action a)
        {
            const string id = "link";
            var size = C.Measure(label, C.Sub);
            var r = new RectangleF(F(CardW / 2) - size.Width / 2 - F(8), F(y), size.Width + F(16), F(22));
            C.Text(label, C.Sub, C.Hover == id ? Draw.Lerp(Th.Blue, Th.Label, 0.25f) : Th.Blue, C.CardFlat, r, TextAlign.Center);
            C.Hit(id, r, a);
        }

        // A mark that draws itself in, with the title and subtitle, centred in the card.
        void PaintDone()
        {
            float t = Math.Min(1, (Environment.TickCount - doneAt) / (float)CheckMs);
            float ease = 1 - (float)Math.Pow(1 - t, 3);
            float block = IconSize + 20 + 32 + 8 + 44;
            float y = (CardH - block) / 2, cx = CardW / 2;

            float d = IconSize * (0.8f + 0.2f * ease);
            var circle = new RectangleF(F(cx - d / 2), F(y + (IconSize - d) / 2), F(d), F(d));
            Color mark = mode == InstallMode.Uninstall ? Th.Secondary : Th.Green;
            using (var b = new SolidBrush(Color.FromArgb((int)(mark.A * ease), mark))) C.G.FillEllipse(b, circle);
            PaintCheck(circle, Math.Max(0, (t - 0.25f) / 0.75f));

            y += IconSize + 20;
            C.Text(Title, C.Big, Th.Label, C.CardFlat, new RectangleF(F(Pad), F(y), F(CardW - 2 * Pad), F(32)), TextAlign.Center);
            y += 32 + 8;
            C.Paragraph(Subtitle, C.Row, Th.Secondary, C.CardFlat, new RectangleF(F(Pad), F(y), F(CardW - 2 * Pad), 0), TextAlign.Center, F(21));
        }

        void PaintCheck(RectangleF circle, float progress)
        {
            if (progress <= 0) return;
            PointF[] pts =
            {
                new PointF(circle.X + circle.Width * 0.29f, circle.Y + circle.Height * 0.52f),
                new PointF(circle.X + circle.Width * 0.44f, circle.Y + circle.Height * 0.66f),
                new PointF(circle.X + circle.Width * 0.72f, circle.Y + circle.Height * 0.37f)
            };
            float a = Dist(pts[0], pts[1]), b = Dist(pts[1], pts[2]), len = (a + b) * Math.Min(1, progress);
            using (var p = Draw.RoundPen(Color.White, circle.Width * 0.085f))
            {
                if (len <= a) C.G.DrawLine(p, pts[0], Lerp(pts[0], pts[1], len / a));
                else C.G.DrawLines(p, new[] { pts[0], pts[1], Lerp(pts[1], pts[2], (len - a) / b) });
            }
        }

        static float Dist(PointF a, PointF b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        static PointF Lerp(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        // ---------- behaviour ----------

        protected override bool Step(int now)
        {
            if (done && now - doneAt > DoneHoldMs) { Finish(InstallChoice.Primary); return false; }
            return busy || done;
        }

        void Primary()
        {
            if (mode == InstallMode.Installed) { Finish(InstallChoice.Primary); return; }
            busy = true; error = null;
            Kick();
            bool login = launchAtLogin;
            var ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                if (mode == InstallMode.Uninstall) Installer.Uninstall();
                else err = Installer.Install(login);
                ui.Post(delegate
                {
                    busy = false;
                    if (err == null) { done = true; doneAt = Environment.TickCount; }
                    else error = "Couldn’t install: " + err;
                    Kick();
                }, null);
            });
        }

        void Secondary() { Finish(mode == InstallMode.Uninstall ? InstallChoice.None : InstallChoice.Secondary); }

        void Finish(InstallChoice c)
        {
            Choice = c;
            Dismiss();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !busy && !done) Primary();
            base.OnKeyDown(e);
        }

        // ---------- design review ----------

        public void Snapshot(string path, bool dark, string state)
        {
            if (state == "busy") busy = true;
            else if (state == "done") { done = true; doneAt = Environment.TickCount - 10000; }
            else if (state == "error") error = "Couldn’t install: the file is in use by another program.";
            SnapshotTo(path, dark);
        }
    }
}
