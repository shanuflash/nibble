using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nibble.Platform
{
    enum ScreenMode
    {
        Normal,
        Game,       // borderless or fullscreen-optimised app: a window on top still shows
        Exclusive   // exclusive fullscreen or presentation: nothing can be drawn over it
    }

    static class Shell
    {
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

        const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

        public static ScreenMode CurrentScreenMode()
        {
            int s;
            try { if (SHQueryUserNotificationState(out s) != 0) return ScreenMode.Normal; }
            catch { return ScreenMode.Normal; }
            if (s == QUNS_RUNNING_D3D_FULL_SCREEN || s == QUNS_PRESENTATION_MODE) return ScreenMode.Exclusive;
            return s == QUNS_BUSY ? ScreenMode.Game : ScreenMode.Normal;
        }

        // Hands unused pages back to the OS so the idle tray footprint stays tiny. `collect` after large
        // transient allocations (a panel was just closed).
        public static void TrimMemory(bool collect)
        {
            if (collect) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        }

        // Windows 11 puts new tray icons in the overflow. Promote ours once, unless the user already chose.
        public static void PromoteTrayIcon(string exePath)
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
                            if (!string.Equals(exe, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.GetValue("IsPromoted") == null) k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        }
                }
            }
            catch { }
        }

        public static void Open(string url)
        {
            try { Process.Start(url); } catch { }
        }
    }
}
