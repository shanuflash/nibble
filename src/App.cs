using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Nibble
{
    class TrayApp : ApplicationContext
    {
        public const string AppName = "Nibble";
        const string SettingsKey = @"Software\Nibble";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Device state (UI thread only)
        public int Percent = -1;
        public bool Found, Online, Charging, Wired, Busy;
        public MouseConfig Config;         // settings table read from the mouse

        public int CurrentStage { get { return Config != null ? Config.Stage : -1; } }
        public int CurrentDpi { get { return Config != null ? Config.CurrentDpi : -1; } }
        public DateTime Updated = DateTime.MinValue;

        // Settings
        public int IntervalSec = 60;
        public bool LowAlert = true;
        public int TrayStyle;              // index into IconArt.TrayStyles

        public int Appearance;             // 0 = follow Windows, 1 = light, 2 = dark

        public void SetTrayStyle(int style)
        {
            TrayStyle = style;
            SaveSettings();
            UpdateIcon();
            Changed();
        }

        public void SetAppearance(int mode)
        {
            Appearance = mode;
            Theme.Override = mode;
            SaveSettings();
            Changed();
        }

        public event EventHandler StateChanged;

        readonly SynchronizationContext ui;
        readonly NotifyIcon tray;
        readonly Flyout fly;
        readonly System.Windows.Forms.Timer poll;
        Icon trayIcon;
        int polling;
        volatile bool exiting;
        bool lowWarned, fullWarned;

        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

        public TrayApp(bool showOnStart)
        {
            ui = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ui);
            LoadSettings();

            tray = new NotifyIcon();
            tray.MouseUp += OnTrayClick;
            fly = new Flyout(this);
            UpdateIcon();
            tray.Visible = true;

            poll = new System.Windows.Forms.Timer();
            poll.Interval = IntervalSec * 1000;
            poll.Tick += delegate { RefreshNow(); };
            poll.Start();

            var reader = new Thread(ReaderLoop);
            reader.IsBackground = true;
            reader.Name = "hid-events";
            reader.Start();

            After(3000, PromoteTrayIcon);
            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.UserPreferenceChanged += OnPrefs;

            RefreshNow();
            if (showOnStart) After(400, fly.ShowFlyout);
        }

        // Design-review mode: renders the flyout with sample state, no tray or device access.
        public TrayApp(string snapshotPath, bool dark, int percent, bool charging, bool online, bool settings)
        {
            Found = true; Online = online; Percent = percent; Charging = charging;
            Config = SampleConfig(); Updated = DateTime.Now.AddSeconds(-4);
            fly = new Flyout(this);
            fly.Snapshot(snapshotPath, dark, settings);
        }

        // Design-review mode for the full-screen settings: Nibble.exe --snapshot-settings out.png dark|light page
        public TrayApp(string snapshotPath, bool dark, int page)
        {
            Found = true; Online = true; Percent = 55;
            Config = SampleConfig(); Updated = DateTime.Now;
            using (var w = new SettingsWindow(this)) w.Snapshot(snapshotPath, dark, page);
        }

        // ---------- writing settings ----------

        public bool Saving;
        public DateTime SavedAt = DateTime.MinValue;
        public bool SaveFailed;
        readonly Queue<Func<bool>> jobs = new Queue<Func<bool>>();
        bool jobRunning;
        SettingsWindow settings;

        // Optimistically applies an edit, queues the device write, then re-reads the mouse to confirm.
        public void Change(Action<MouseConfig> edit, Func<MouseConfig, bool> write)
        {
            if (Config == null) return;
            var c = Config.Clone();
            edit(c);
            Config = c;
            lock (jobs) jobs.Enqueue(delegate { return write(c); });
            Saving = true;
            Changed();
            RunJobs();
        }

        void RunJobs()
        {
            lock (jobs) { if (jobRunning) return; jobRunning = true; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = true;
                while (true)
                {
                    Func<bool> job;
                    lock (jobs)
                    {
                        if (jobs.Count == 0) { jobRunning = false; break; }
                        job = jobs.Dequeue();
                    }
                    try { ok &= job(); } catch { ok = false; }
                }
                MouseConfig fresh = null;
                try { fresh = RkM3.ReadConfig(); } catch { }
                ui.Post(delegate
                {
                    lock (jobs) if (jobs.Count > 0 || jobRunning) return; // newer edits still in flight
                    Saving = false;
                    SaveFailed = !ok || fresh == null;
                    if (fresh != null) Config = fresh;
                    SavedAt = DateTime.Now;
                    AfterStateUpdate();
                }, null);
            });
        }

        // ---------- firmware ----------

        public FirmwareInfo Fw;
        public bool FwChecking;

        public void CheckFirmware()
        {
            if (FwChecking) return;
            FwChecking = true;
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                FirmwareInfo f = null;
                try { f = Firmware.Check(); } catch { }
                ui.Post(delegate { Fw = f; FwChecking = false; Changed(); }, null);
            });
        }

        // Opens RK's official updater download in the browser; only ever from an explicit click.
        public void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://drive.rkgaming.com/")) return;
            try { Process.Start(url); } catch { }
        }

        public void OpenSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsWindow(this);
                settings.FormClosed += delegate { settings.Dispose(); settings = null; Trim(true); };
            }
            settings.ShowOn(Screen.FromPoint(Cursor.Position));
            if (Config == null || (DateTime.Now - Updated).TotalSeconds > 5) RefreshNow();
        }

        // Plausible table for design snapshots: one 1200 DPI stage, 2000 Hz.
        static MouseConfig SampleConfig()
        {
            var raw = new byte[] { 2, 0x66, 0x11, 1, 5, 0x40, 32, 16, 2, 1, 0, 23, 0, 23, 0, 63, 0, 127, 0, 255, 0, 7, 2, 0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 255, 255, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 165, 90 };
            return new MouseConfig(raw);
        }

        // ---------- polling ----------

        public void RefreshNow()
        {
            if (Interlocked.CompareExchange(ref polling, 1, 0) != 0) return;
            Busy = true;
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                var sw = Stopwatch.StartNew();
                Reading r = null;
                try { r = RkM3.Query(true); } catch { }
                // Keep the spinner visible long enough to register as feedback.
                int rest = 550 - (int)sw.ElapsedMilliseconds;
                if (rest > 0) Thread.Sleep(rest);
                ui.Post(delegate { polling = 0; Busy = false; Apply(r); }, null);
            });
        }

        void Apply(Reading r)
        {
            if (exiting) return;
            if (r == null) r = new Reading();
            Found = r.Found;
            Online = r.Online;
            if (r.Online)
            {
                Percent = r.Percent;
                Charging = r.Charging;
                Wired = r.Wired;
                if (r.Config != null && !Saving) Config = r.Config;   // don't clobber an edit in flight
                Updated = DateTime.Now;
            }
            if (!Found) { Online = false; }
            AfterStateUpdate();
        }

        void ReaderLoop()
        {
            bool first = true;
            while (!exiting)
            {
                HidDev d = null;
                try { d = RkM3.EventDevice(); } catch { }
                if (d == null) { first = false; Thread.Sleep(4000); continue; }
                using (var h = Hid.Open(d.Path, true))
                {
                    if (!h.IsInvalid)
                    {
                        if (!first) ui.Post(delegate { RefreshNow(); }, null); // receiver (re)plugged
                        first = false;
                        var buf = new byte[d.InLen];
                        int n;
                        while (!exiting && Hid.ReadFile(h, buf, buf.Length, out n, IntPtr.Zero))
                        {
                            int type, value;
                            if (RkM3.ParseEvent(buf, n, out type, out value))
                                ui.Post(delegate { OnDeviceEvent(type, value); }, null);
                        }
                    }
                }
                Thread.Sleep(2000);
            }
        }

        void OnDeviceEvent(int type, int value)
        {
            if (exiting) return;
            switch (type)
            {
                case RkM3.EvtBattery:
                    if ((value & 0x7F) > 100) return;
                    Percent = value & 0x7F;
                    Charging = (value & 0x80) != 0;
                    Found = Online = true;
                    Updated = DateTime.Now;
                    break;
                case RkM3.EvtDpi:
                    // Stage switched with the DPI button; re-read the table for the new active stage.
                    After(150, RefreshNow);
                    break;
                case RkM3.EvtConnect:
                    Found = true;
                    Online = value == 1;
                    if (Online) After(600, RefreshNow);
                    break;
                default:
                    return;
            }
            AfterStateUpdate();
        }

        void AfterStateUpdate()
        {
            if (Online) SaveLastPercent();
            CheckAlerts();
            UpdateIcon();
            Changed();
            Trim();
        }

        // Hand unused pages back to the OS; keeps the idle tray footprint tiny.
        public void Trim() { Trim(false); }

        // collect: after big transient allocations (flyout/settings closed), return the memory for real.
        public void Trim(bool collect)
        {
            if (collect) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        }

        void Changed()
        {
            var h = StateChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------- alerts ----------

        void CheckAlerts()
        {
            if (!Online || Percent < 0) return;
            if (Charging || Percent > 25) lowWarned = false;
            if (!Charging) fullWarned = false;

            if (LowAlert && !Charging && Percent <= 20 && !lowWarned)
            {
                lowWarned = true;
                tray.ShowBalloonTip(6000, "Mouse battery low",
                    string.Format("RK M3 is at {0}%. Plug it in soon.", Percent), ToolTipIcon.Warning);
            }
            if (LowAlert && Charging && Percent >= 100 && !fullWarned)
            {
                fullWarned = true;
                tray.ShowBalloonTip(5000, "Fully charged", "RK M3 is at 100%. You can unplug it.", ToolTipIcon.Info);
            }
        }

        // ---------- tray icon ----------

        public string StatusText()
        {
            if (!Found) return "Receiver not found";
            if (!Online) return "Mouse asleep";
            if (Charging) return Percent >= 100 ? "Fully charged" : "Charging";
            if (Percent <= 20) return "Low battery";
            return "On battery";
        }

        void UpdateIcon()
        {
            int s = SystemInformation.SmallIconSize.Width;
            var old = trayIcon;
            int pct = Found ? Percent : -1;
            bool chg = Charging && Online, asleep = !Found || !Online, light = Theme.TaskbarLight();
            trayIcon = IconArt.TrayIcon(TrayStyle, s, pct, chg, asleep, light);
            tray.Icon = trayIcon;
            if (old != null) old.Dispose();

            string tip = Percent >= 0 && Found
                ? string.Format("RK M3 \u00B7 {0}% \u00B7 {1}", Percent, StatusText())
                : "RK M3 \u00B7 " + StatusText();
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            if (fly.Visible) { fly.HideFlyout(); return; }
            // The click that deactivated the flyout should not immediately reopen it.
            if ((DateTime.Now - fly.HiddenAt).TotalMilliseconds < 300) return;
            fly.ShowFlyout();
            if ((DateTime.Now - Updated).TotalSeconds > 15) RefreshNow();
        }

        // Windows 11 hides new tray icons in the overflow; promote ours once, unless the user chose otherwise.
        void PromoteTrayIcon()
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                {
                    if (root == null) return;
                    foreach (var name in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(name, true))
                        {
                            if (k == null) continue;
                            var exe = k.GetValue("ExecutablePath") as string;
                            if (exe == null || !string.Equals(exe, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.GetValue("IsPromoted") == null) k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        }
                }
            }
            catch { }
        }

        // ---------- settings ----------

        void LoadSettings()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k == null) return;
                    object v = k.GetValue("IntervalSec");
                    if (v is int && (int)v >= 10) IntervalSec = (int)v;
                    v = k.GetValue("LowAlert");
                    if (v is int) LowAlert = (int)v != 0;
                    v = k.GetValue("TrayStyle");
                    if (v is int && (int)v >= 0 && (int)v < IconArt.TrayStyles.Length) TrayStyle = (int)v;
                    v = k.GetValue("Appearance");
                    if (v is int && (int)v >= 0 && (int)v <= 2) { Appearance = (int)v; Theme.Override = Appearance; }
                    // Last known level, so a sleeping mouse still shows (dimmed) after a restart.
                    v = k.GetValue("LastPercent");
                    if (v is int && (int)v >= 0 && (int)v <= 100) { Percent = (int)v; savedPercent = Percent; }
                }
            }
            catch { }
        }

        int savedPercent = -1;

        void SaveLastPercent()
        {
            if (Percent < 0 || Percent == savedPercent) return;
            savedPercent = Percent;
            try { using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue("LastPercent", Percent, RegistryValueKind.DWord); }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    k.SetValue("IntervalSec", IntervalSec, RegistryValueKind.DWord);
                    k.SetValue("LowAlert", LowAlert ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("TrayStyle", TrayStyle, RegistryValueKind.DWord);
                    k.SetValue("Appearance", Appearance, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public void SetInterval(int sec)
        {
            IntervalSec = sec;
            poll.Stop(); poll.Interval = sec * 1000; poll.Start();
            SaveSettings();
            Changed();
        }

        public void SetLowAlert(bool on)
        {
            LowAlert = on;
            SaveSettings();
            Changed();
        }

        public bool AutoStart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        var v = k == null ? null : k.GetValue(AppName) as string;
                        return v != null && v.Trim('"').Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
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
                        if (value) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue(AppName, false);
                    }
                }
                catch { }
                Changed();
            }
        }
        public void Quit()
        {
            exiting = true;
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.UserPreferenceChanged -= OnPrefs;
            tray.Visible = false;
            tray.Dispose();
            fly.Close();
            ExitThread();
        }

        void OnPower(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ui.Post(delegate { After(4000, RefreshNow); }, null);
        }

        void OnPrefs(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                ui.Post(delegate { UpdateIcon(); fly.ThemeChanged(); }, null);
        }

        static void After(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer();
            t.Interval = ms;
            t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }
    }
}
