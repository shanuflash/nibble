using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Nibble")]
[assembly: AssemblyDescription("Battery tray for the Royal Kludge M3 mouse")]
[assembly: AssemblyProduct("Nibble")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Nibble
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--make-icon")
            {
                IconArt.WriteIco(args[1]);
                return;
            }

            if (args.Length >= 3 && args[0] == "--snapshot")
            {
                SetProcessDPIAware();
                int pct = args.Length > 3 ? int.Parse(args[3]) : 57;
                bool chg = args.Length > 4 && args[4] == "charging";
                bool on = !(args.Length > 4 && args[4] == "asleep");
                new TrayApp(args[1], args[2] == "dark", pct, chg, on, Array.IndexOf(args, "settings") >= 0);
                return;
            }

            if (args.Length >= 4 && args[0] == "--snapshot-settings")
            {
                SetProcessDPIAware();
                if (args.Length >= 5) SettingsWindow.SideStyle = int.Parse(args[4]);
                new TrayApp(args[1], args[2] == "dark", int.Parse(args[3]));
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Local\Nibble.RK-M3", out created))
            {
                if (!created) return;
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool show = Array.IndexOf(args, "--show") >= 0;
                var ctx = new TrayApp(show);
                if (Array.IndexOf(args, "--settings") >= 0) { var t = new System.Windows.Forms.Timer { Interval = 1500 }; t.Tick += delegate { t.Stop(); ctx.OpenSettings(); }; t.Start(); }
                Application.Run(ctx);
            }
        }
    }
}
