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
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

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
        // True if it changed anything; Explorer only reads the setting when the icon is (re-)added.
        public static bool PromoteTrayIcon(string exePath)
        {
            bool changed = false;
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                {
                    if (root == null) return false;
                    foreach (var name in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(name, true))
                        {
                            if (k == null) continue;
                            var exe = k.GetValue("ExecutablePath") as string;
                            if (!string.Equals(exe, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.GetValue("IsPromoted") != null) continue;
                            k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            changed = true;
                        }
                }
            }
            catch { }
            return changed;
        }

        // Windows only lets the foreground process hand focus on. Call this before starting or signalling
        // another Nibble so its flyout can take focus (and close again on an outside click).
        public static void AllowForeground() { AllowSetForegroundWindow(-1); }

        public static void TakeForeground(IntPtr hwnd) { SetForegroundWindow(hwnd); }

        public static void Open(string url)
        {
            try { Process.Start(url); } catch { }
        }
    }
}
