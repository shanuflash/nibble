using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Nibble.UI
{
    enum InstallMode { Install, Update, Installed, Uninstall }
    enum InstallChoice { None, Primary, Secondary }

    // First-run card: install (or update) Nibble into its own folder, or just run this copy.
    // Also the uninstall confirmation that Settings → Apps opens.
    sealed class InstallCard : GlassCard
    {
        readonly InstallMode mode;
        bool launchAtLogin = true;
        bool busy;
        string error;
        public InstallChoice Choice { get; private set; }

        // Shows the card modally and returns what the user picked.
        public static InstallChoice Ask(InstallMode mode)
        {
            using (var card = new InstallCard(mode))
            {
                card.Shown += delegate { card.Activate(); };
                card.ShowOn(Screen.FromPoint(Cursor.Position));
                Application.Run(card);
                return card.Choice;
            }
        }

        public static InstallCard ForPreview(InstallMode mode) { return new InstallCard(mode); }

        InstallCard(InstallMode mode) : base(460, HasSwitch(mode) ? 376 : 304, 30)
        {
            this.mode = mode;
            if (mode == InstallMode.Update) launchAtLogin = AutoStart.EnabledFor(Installer.InstalledExe);
        }

        static bool HasSwitch(InstallMode m) { return m == InstallMode.Install || m == InstallMode.Update; }

        protected override Color CardTint(bool dark) { return dark ? Color.FromArgb(205, 24, 24, 28) : Color.FromArgb(200, 250, 250, 252); }

        string Title
        {
            get
            {
                switch (mode)
                {
                    case InstallMode.Update: return "Update " + AppInfo.Name;
                    case InstallMode.Installed: return AppInfo.Name + " is installed";
                    case InstallMode.Uninstall: return "Uninstall " + AppInfo.Name + "?";
                    default: return "Install " + AppInfo.Name;
                }
            }
        }

        string[] Body
        {
            get
            {
                var installed = Installer.InstalledVersion;
                switch (mode)
                {
                    case InstallMode.Update:
                        return new[] { "Replaces version " + installed + " with " + AppInfo.Version + ".", "Your settings stay as they are." };
                    case InstallMode.Installed:
                        return new[] { "Version " + installed + " is already in your Start menu.", "Open it, or run this copy instead." };
                    case InstallMode.Uninstall:
                        return new[] { "Removes Nibble, its Start menu entry and launch at login.", "Your mouse keeps its settings." };
                    default:
                        return new[] { "Adds Nibble to Start and keeps it in one place,", "so launch at login and updates keep working." };
                }
            }
        }

        string PrimaryLabel
        {
            get
            {
                if (busy) return mode == InstallMode.Uninstall ? "Removing…" : "Installing…";
                switch (mode)
                {
                    case InstallMode.Update: return "Update";
                    case InstallMode.Installed: return "Open " + AppInfo.Name;
                    case InstallMode.Uninstall: return "Uninstall";
                    default: return "Install";
                }
            }
        }

        string SecondaryLabel
        {
            get
            {
                switch (mode)
                {
                    case InstallMode.Installed: return "Run this copy";
                    case InstallMode.Uninstall: return "Cancel";
                    default: return "Just run it";
                }
            }
        }

        protected override void PaintCard()
        {
            float pad = 32;
            int icon = Pi(64);
            using (var art = IconArt.AppArt(icon)) C.G.DrawImageUnscaled(art, Pi(pad), Pi(pad));
            PaintClose(CardW - 20, 20);

            float y = pad + 64 + 18;
            C.Text(Title, C.Big, Th.Label, C.CardFlat, new RectangleF(F(pad), F(y), F(CardW - 2 * pad), F(34)), TextAlign.Left);
            y += 42;
            foreach (var line in Body)
            {
                C.Text(line, C.Row, Th.Secondary, C.CardFlat, new RectangleF(F(pad), F(y), F(CardW - 2 * pad), F(20)), TextAlign.Left);
                y += 22;
            }

            if (HasSwitch(mode))
            {
                var row = new RectangleF(F(pad - 8), F(y + 14), F(CardW - 2 * (pad - 8)), F(58));
                C.Platter(row);
                C.SwitchRow(row, 0, F(58), "Launch at login", "Start Nibble in the tray with Windows", launchAtLogin, "login",
                    delegate { if (!busy) launchAtLogin = !launchAtLogin; });
            }

            float bh = 38, by = CardH - pad - bh;
            if (error != null)
                C.Text(error, C.Sub, Th.Red, C.CardFlat, new RectangleF(F(pad), F(by - 26), F(CardW - 2 * pad), F(18)), TextAlign.Left);

            float pw = Math.Max(120, C.Measure(PrimaryLabel, C.SmallSemi).Width / C.U + 40);
            var primary = new RectangleF(F(CardW - pad - pw), F(by), F(pw), F(bh));
            Color accent = mode == InstallMode.Uninstall ? Th.Red : Th.Blue;
            C.Button(primary, PrimaryLabel, Color.White, "primary", busy ? (Action)delegate { } : Primary, accent);
            float sw = C.Measure(SecondaryLabel, C.SmallSemi).Width / C.U + 40;
            var secondary = new RectangleF(primary.X - F(10 + sw), F(by), F(sw), F(bh));
            if (!busy) C.Button(secondary, SecondaryLabel, Th.Label, "secondary", Secondary);
        }

        void Primary()
        {
            if (mode == InstallMode.Installed) { Finish(InstallChoice.Primary); return; }
            busy = true; error = null;
            Render();
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
                    if (err == null) Finish(InstallChoice.Primary);
                    else { error = "Couldn’t install: " + err; Kick(); }
                }, null);
            });
        }

        void Secondary() { Finish(InstallChoice.Secondary); }

        void Finish(InstallChoice c)
        {
            Choice = c;
            Dismiss();
        }

        public void Snapshot(string path, bool dark) { SnapshotTo(path, dark); }
    }
}
