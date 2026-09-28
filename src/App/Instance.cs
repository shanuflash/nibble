using System;
using System.Threading;

namespace Nibble
{
    // One tray instance per user. Launching Nibble again asks the running one to show its flyout;
    // the installer asks it to quit before replacing the exe.
    sealed class Instance : IDisposable
    {
        const string MutexName = @"Local\Nibble.RK-M3";
        const string ShowName = @"Local\Nibble.Show";
        const string QuitName = @"Local\Nibble.Quit";

        readonly Mutex mutex;
        public readonly bool IsFirst;

        public Instance()
        {
            bool created;
            mutex = new Mutex(true, MutexName, out created);
            IsFirst = created;
        }

        public static bool Running()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(MutexName, out m)) return false;
            m.Dispose();
            return true;
        }

        public static void SignalShow()
        {
            Platform.Shell.AllowForeground();
            Signal(ShowName);
        }

        // Asks the running instance to exit; true once it has.
        public static bool RequestQuit(int timeoutMs)
        {
            if (!Running()) return true;
            Signal(QuitName);
            int until = Environment.TickCount + timeoutMs;
            while (Running() && Environment.TickCount < until) Thread.Sleep(50);
            return !Running();
        }

        // Runs onShow / onQuit on the UI thread when another process signals.
        public void Listen(SynchronizationContext ui, Action onShow, Action onQuit)
        {
            Watch(ShowName, ui, onShow);
            Watch(QuitName, ui, onQuit);
        }

        static void Watch(string name, SynchronizationContext ui, Action a)
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            var t = new Thread(delegate ()
            {
                while (true) { ev.WaitOne(); ui.Post(delegate { a(); }, null); }
            });
            t.IsBackground = true;
            t.Name = "instance-" + name.Substring(name.LastIndexOf('.') + 1).ToLowerInvariant();
            t.Start();
        }

        static void Signal(string name)
        {
            EventWaitHandle ev;
            if (!EventWaitHandle.TryOpenExisting(name, out ev)) return;
            using (ev) ev.Set();
        }

        public void Dispose()
        {
            if (IsFirst) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
