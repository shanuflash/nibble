using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Nibble.Devices;

namespace Nibble
{
    // Live state of the connected mouse: polls it, listens for its events and queues writes.
    // State is only touched on the UI thread; device I/O runs on background threads.
    sealed class MouseSession
    {
        public readonly IMouse Device;

        public bool Found { get; private set; }
        public bool Online { get; private set; }
        public bool Charging { get; private set; }
        public bool Wired { get; private set; }
        public bool Busy { get; private set; }
        public int Percent { get; private set; }
        public MouseSettings Settings { get; private set; }
        public DateTime SyncedAt { get; private set; }

        // Charging done: the firmware clears the charging bit at 100 while still on the cable.
        public bool FullyCharged { get; private set; }
        // While charging the M3 reports a flat 100 instead of its level, so there is no number to show.
        public bool LevelHidden { get; private set; }

        public bool Saving { get; private set; }
        public bool SaveFailed { get; private set; }
        public DateTime SavedAt { get; private set; }

        public FirmwareInfo Firmware { get; private set; }
        public bool FirmwareChecking { get; private set; }

        public event EventHandler Changed;            // anything shown on screen
        public event EventHandler StatusRead;         // fresh battery or settings from the device
        public event Action<MouseSettings> DpiSwitched;   // stage or DPI changed on the mouse itself

        readonly SynchronizationContext ui;
        readonly Queue<Func<bool>> jobs = new Queue<Func<bool>>();
        bool jobRunning;
        int polling;
        volatile bool stopped;
        System.Windows.Forms.Timer poll;

        int lastStage = -1, lastDpi = -1;
        bool dpiButtonPressed;

        public MouseSession(IMouse device)
        {
            Device = device;
            Percent = -1;
            SyncedAt = SavedAt = DateTime.MinValue;
            ui = SynchronizationContext.Current;
        }

        public void Start(int intervalSec, int lastKnownPercent)
        {
            Percent = lastKnownPercent;
            poll = new System.Windows.Forms.Timer { Interval = intervalSec * 1000 };
            poll.Tick += delegate { Refresh(); };
            poll.Start();
            var reader = new Thread(delegate () { Device.Listen(() => stopped, e => Post(() => OnEvent(e))); });
            reader.IsBackground = true;
            reader.Name = "device-events";
            reader.Start();
            Refresh();
        }

        public void Stop()
        {
            stopped = true;
            if (poll != null) poll.Stop();
        }

        public void SetInterval(int sec)
        {
            if (poll == null) return;
            poll.Stop(); poll.Interval = sec * 1000; poll.Start();
        }

        // Design previews: fixed state, no device access.
        public void Simulate(bool online, int percent, bool charging, MouseSettings settings, DateTime syncedAt)
        {
            Found = true; Online = online; Percent = percent; Charging = charging;
            LevelHidden = charging && percent < 0;
            Settings = settings; SyncedAt = syncedAt;
        }

        public string PercentText { get { return Percent + "%"; } }

        public string StatusText
        {
            get
            {
                if (!Found) return "Receiver not found";
                if (!Online) return "Mouse asleep";
                if (FullyCharged) return "Fully charged";
                if (Charging) return "Charging";
                if (Percent <= 20) return "Low battery";
                return "On battery";
            }
        }

        // ---------- reading ----------

        public void Refresh()
        {
            if (Interlocked.CompareExchange(ref polling, 1, 0) != 0) return;
            Busy = true;
            RaiseChanged();
            ThreadPool.QueueUserWorkItem(delegate
            {
                var sw = Stopwatch.StartNew();
                MouseStatus r = null;
                try { r = Device.Query(true); } catch { }
                // Keep the spinner up long enough to read as feedback.
                int rest = 550 - (int)sw.ElapsedMilliseconds;
                if (rest > 0) Thread.Sleep(rest);
                Post(delegate { polling = 0; Busy = false; Apply(r ?? new MouseStatus()); });
            });
        }

        void Apply(MouseStatus r)
        {
            if (stopped) return;
            Found = r.Found;
            Online = r.Online && r.Found;
            if (r.Online)
            {
                Wired = r.Wired;
                SetBattery(r.Percent, r.Charging);
                if (r.Settings != null && !Saving) { Settings = r.Settings; TrackDpi(true); }   // don't clobber an edit in flight
                SyncedAt = DateTime.Now;
            }
            Publish();
        }

        void OnEvent(DeviceEvent e)
        {
            if (stopped) return;
            switch (e.Kind)
            {
                case DeviceEventKind.Battery:
                    SetBattery(e.Percent, e.Charging);
                    Found = Online = true;
                    SyncedAt = DateTime.Now;
                    break;
                case DeviceEventKind.DpiButton:
                    // Re-read the table for the new active stage; the popup shows once it's in.
                    dpiButtonPressed = true;
                    After(150, Refresh);
                    return;
                case DeviceEventKind.Link:
                    Found = true;
                    Online = e.Online;
                    if (Online) After(600, Refresh);
                    break;
                case DeviceEventKind.Reattached:
                    Refresh();
                    return;
            }
            Publish();
        }

        void SetBattery(int raw, bool charging)
        {
            Charging = charging;
            FullyCharged = !charging && raw >= 100 && Wired;
            LevelHidden = charging && raw >= 100;
            Percent = LevelHidden ? -1 : raw;
        }

        // The popup is for changes made on the mouse (its DPI button), not for edits from Settings.
        void TrackDpi(bool fromMouse)
        {
            if (Settings == null) return;
            int s = Settings.Stage, d = Settings.CurrentDpi;
            bool changed = lastStage > 0 && (s != lastStage || d != lastDpi);
            if (fromMouse && (changed || dpiButtonPressed) && DpiSwitched != null) DpiSwitched(Settings);
            lastStage = s; lastDpi = d; dpiButtonPressed = false;
        }

        // ---------- writing ----------

        // Applies an edit optimistically, queues the device write, then re-reads the mouse to confirm.
        public void Change(Action<MouseSettings> edit, SettingGroup group)
        {
            if (Settings == null) return;
            var s = Settings.Clone();
            edit(s);
            Settings = s;
            Enqueue(() => Device.Write(s, group));
        }

        public void FactoryReset()
        {
            if (Settings == null) return;
            Enqueue(Device.FactoryReset);
        }

        void Enqueue(Func<bool> job)
        {
            lock (jobs) jobs.Enqueue(job);
            Saving = true;
            RaiseChanged();
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
                MouseSettings fresh = null;
                try { fresh = Device.ReadSettings(); } catch { }
                Post(delegate
                {
                    lock (jobs) if (jobs.Count > 0 || jobRunning) return;   // newer edits still in flight
                    Saving = false;
                    SaveFailed = !ok || fresh == null;
                    if (fresh != null) Settings = fresh;
                    TrackDpi(false);
                    SavedAt = DateTime.Now;
                    Publish();
                });
            });
        }

        // ---------- firmware ----------

        public void CheckFirmware()
        {
            if (FirmwareChecking) return;
            FirmwareChecking = true;
            RaiseChanged();
            ThreadPool.QueueUserWorkItem(delegate
            {
                FirmwareInfo f = null;
                try { f = Device.CheckFirmware(); } catch { }
                Post(delegate { Firmware = f; FirmwareChecking = false; RaiseChanged(); });
            });
        }

        // ---------- plumbing ----------

        void Publish()
        {
            if (StatusRead != null) StatusRead(this, EventArgs.Empty);
            RaiseChanged();
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
