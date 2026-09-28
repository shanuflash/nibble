using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Nibble
{
    // Per-user install without admin: the exe goes to %LOCALAPPDATA%\Programs\Nibble, with a Start menu
    // shortcut and an entry in Settings → Apps whose uninstall runs "Nibble.exe --uninstall".
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;
        const string PrefsKey = @"Software\Nibble";
        const string PortableValue = "PortablePath";

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name); }
        }

        public static string InstalledExe { get { return Path.Combine(Dir, AppInfo.Name + ".exe"); } }

        static string Shortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk"); }
        }

        public static bool RunningInstalled { get { return SamePath(Application.ExecutablePath, InstalledExe); } }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        public static Version InstalledVersion
        {
            get
            {
                try
                {
                    var v = FileVersionInfo.GetVersionInfo(InstalledExe);
                    return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
                }
                catch { return null; }
            }
        }

        // Offer to install when running a copy from somewhere else, unless the user chose to keep
        // running that copy.
        public static bool ShouldOffer()
        {
            if (RunningInstalled) return false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(PrefsKey))
                {
                    var p = k == null ? null : k.GetValue(PortableValue) as string;
                    return !SamePath(p, Application.ExecutablePath);
                }
            }
            catch { return true; }
        }

        public static bool NewerThanInstalled()
        {
            var installed = InstalledVersion;
            return installed != null && AppInfo.Version > installed;
        }

        public static void RememberPortable()
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(PrefsKey)) k.SetValue(PortableValue, Application.ExecutablePath); }
            catch { }
        }

        // Copies this exe into place (replacing a running older copy) and registers it. Null on success.
        public static string Install(bool launchAtLogin)
        {
            try
            {
                if (!Instance.RequestQuit(4000)) StopProcesses(InstalledExe);
                Directory.CreateDirectory(Dir);
                if (!RunningInstalled) CopyWithRetry(Application.ExecutablePath, InstalledExe);
                CreateShortcut(Shortcut, InstalledExe);
                Register();
                AutoStart.Set(launchAtLogin, InstalledExe);
                using (var k = Registry.CurrentUser.CreateSubKey(PrefsKey)) k.DeleteValue(PortableValue, false);
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        public static void LaunchInstalled(string args)
        {
            try { Process.Start(new ProcessStartInfo(InstalledExe, args) { UseShellExecute = true }); }
            catch { }
        }

        // Removes everything Install added, plus Nibble's settings. The folder is deleted once this
        // process has exited, since a running exe can't delete itself.
        public static void Uninstall()
        {
            if (!Instance.RequestQuit(4000)) StopProcesses(InstalledExe);
            AutoStart.Set(false, InstalledExe);
            TryDelete(Shortcut);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(PrefsKey, false); } catch { }
            if (!Directory.Exists(Dir)) return;
            var cmd = string.Format("/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{0}\"", Dir);
            try { Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden }); }
            catch { }
        }

        // ---------- pieces ----------

        static void Register()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                string exe = "\"" + InstalledExe + "\"";
                k.SetValue("DisplayName", AppInfo.Name);
                k.SetValue("DisplayVersion", AppInfo.Version.ToString());
                k.SetValue("Publisher", Company());
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", exe + " --uninstall");
                k.SetValue("URLInfoAbout", "https://github.com/" + AppInfo.Repo);
                k.SetValue("HelpLink", "https://github.com/" + AppInfo.Repo + "/issues");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        static string Company()
        {
            var a = (AssemblyCompanyAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyCompanyAttribute));
            return a != null ? a.Company : AppInfo.Name;
        }

        // Via WScript.Shell, which every Windows has; avoids a COM interop assembly.
        static void CreateShortcut(string path, string target)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            try
            {
                object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var lt = link.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Battery and settings for your mouse" });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.ReleaseComObject(link);
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        // The old copy can hold its file for a moment after exiting.
        static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(from, to, true); return; }
                catch (IOException) { if (i >= 20) throw; Thread.Sleep(150); }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; Thread.Sleep(150); }
            }
        }

        static void StopProcesses(string exePath)
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName(AppInfo.Name))
            {
                try
                {
                    if (p.Id == self || !SamePath(p.MainModule.FileName, exePath)) continue;
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }
}
