using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Nibble.Devices;
using Nibble.Platform;
using Nibble.UI;
using Nibble.UI.Popups;
using Nibble.UI.Settings;

namespace Nibble
{
    // Owns the tray icon and the windows, and connects them to the mouse session and preferences.
    sealed class TrayApp : ApplicationContext
    {
        public readonly MouseSession Mouse;
        public readonly Preferences Prefs;

        public UpdateInfo Update { get; private set; }
        public bool UpdateChecking { get; private set; }

        public event EventHandler Changed;

        readonly SynchronizationContext ui;
        readonly NotifyIcon tray;
        readonly Flyout flyout;
        readonly BatteryAlerts alerts = new BatteryAlerts();
        SettingsWindow settings;
        Icon trayIcon;

        public TrayApp(bool showOnStart, Instance instance)
        {
            ui = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ui);
            Prefs = Preferences.Load();
            Theme.Override = Prefs.Appearance;
            Mouse = new MouseSession(Drivers.Detect());

            tray = new NotifyIcon();
            tray.MouseUp += OnTrayClick;
            flyout = new Flyout(this);

            Mouse.Changed += delegate { RaiseChanged(); };
            Mouse.StatusRead += OnStatusRead;
            Mouse.DpiSwitched += OnDpiSwitched;
            Mouse.Start(Prefs.IntervalSec, Prefs.LastPercent);

            UpdateIcon();
            tray.Visible = true;

            After(3000, PromoteTrayIcon);
            instance.Listen(ui, flyout.ShowFlyout, Quit);
            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.UserPreferenceChanged += OnSystemPrefs;
            if (showOnStart) After(400, flyout.ShowFlyout);
        }

        // Design previews: no tray, no device access.
        TrayApp(MouseSession mouse)
        {
            Mouse = mouse;
            Prefs = new Preferences();
        }

        public static TrayApp Preview(MouseSession mouse) { return new TrayApp(mouse); }

        public bool AutoStart
        {
            get { return Nibble.AutoStart.Enabled; }
            set { Nibble.AutoStart.Enabled = value; RaiseChanged(); }
        }

        public void UpdatePrefs(Action<Preferences> edit)
        {
            int interval = Prefs.IntervalSec;
            edit(Prefs);
            Prefs.Save();
            Theme.Override = Prefs.Appearance;
            if (Prefs.IntervalSec != interval) Mouse.SetInterval(Prefs.IntervalSec);
            UpdateIcon();
            RaiseChanged();
        }

        public void CheckUpdates()
        {
            if (UpdateChecking) return;
            UpdateChecking = true;
            RaiseChanged();
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo u = null;
                try { u = Updates.Check(); } catch { }
                ui.Post(delegate { Update = u; UpdateChecking = false; RaiseChanged(); }, null);
            });
        }

        // Only Nibble's releases and the mouse vendor's firmware pages, and only from a click.
        public void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (url.StartsWith(AppInfo.ReleasesPage) || url.StartsWith(Mouse.Device.VendorSite)) Shell.Open(url);
        }

        public void OpenSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsWindow(this);
                settings.FormClosed += delegate { settings.Dispose(); settings = null; Shell.TrimMemory(true); };
            }
            settings.ShowOn(Screen.FromPoint(Cursor.Position));
            if (Mouse.Settings == null || (DateTime.Now - Mouse.SyncedAt).TotalSeconds > 5) Mouse.Refresh();
        }

        public void Quit()
        {
            Mouse.Stop();
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.UserPreferenceChanged -= OnSystemPrefs;
            tray.Visible = false;
            tray.Dispose();
            flyout.Close();
            ExitThread();
        }

        void PromoteTrayIcon()
        {
            if (!Shell.PromoteTrayIcon(Application.ExecutablePath)) return;
            tray.Visible = false;   // re-add so Explorer applies the pin now
            tray.Visible = true;
        }

        void OnStatusRead(object sender, EventArgs e)
        {
            if (Mouse.Online && !Mouse.Charging) Prefs.SaveLastPercent(Mouse.Percent);
            alerts.Check(Mouse, Prefs, flyout.ShowFlyout);
            UpdateIcon();
            Shell.TrimMemory(false);
        }

        void OnDpiSwitched(MouseSettings s)
        {
            if (Prefs.DpiPopup) DpiHud.Show(s.CurrentDpi, s.Stage, s.StageCount, s.CurrentColor);
        }

        void UpdateIcon()
        {
            var old = trayIcon;
            bool asleep = !Mouse.Found || !Mouse.Online;
            trayIcon = IconArt.TrayIcon(Prefs.TrayStyle, SystemInformation.SmallIconSize.Width, Mouse.Found ? Mouse.Percent : -1,
                Mouse.Charging && Mouse.Online, asleep, Theme.TaskbarLight());
            tray.Icon = trayIcon;
            if (old != null) old.Dispose();

            string name = Mouse.Device.Name;
            string tip = Mouse.Percent >= 0 && Mouse.Found
                ? string.Format("{0} · {1} · {2}", name, Mouse.PercentText, Mouse.StatusText)
                : name + " · " + Mouse.StatusText;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            if (flyout.Visible) { flyout.HideFlyout(); return; }
            // The click that dismissed the flyout must not reopen it.
            if ((DateTime.Now - flyout.HiddenAt).TotalMilliseconds < 300) return;
            flyout.ShowFlyout();
            if ((DateTime.Now - Mouse.SyncedAt).TotalSeconds > 15) Mouse.Refresh();
        }

        void OnPower(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) Post(delegate { After(4000, Mouse.Refresh); });
        }

        void OnSystemPrefs(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                Post(delegate { UpdateIcon(); flyout.ThemeChanged(); });
        }

        void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        void Post(Action a) { ui.Post(delegate { a(); }, null); }

        static void After(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }
    }
}
