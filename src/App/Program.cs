using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nibble.UI;

namespace Nibble
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--make-icon") { IconArt.WriteIco(args[1]); return; }
            if (Previews.Run(args)) return;

            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Has(args, "--uninstall")) { InstallCard.Ask(InstallMode.Uninstall); return; }
            if (!Has(args, "--portable") && Installer.ShouldOffer() && !OfferInstall()) return;

            using (var instance = new Instance())
            {
                if (!instance.IsFirst) { Instance.SignalShow(); return; }
                var app = new TrayApp(Has(args, "--show"), instance);
                if (Has(args, "--settings"))
                {
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += delegate { t.Stop(); app.OpenSettings(); };
                    t.Start();
                }
                Application.Run(app);
            }
        }

        // Returns true to keep running this copy.
        static bool OfferInstall()
        {
            var mode = !Installer.IsInstalled ? InstallMode.Install
                : Installer.NewerThanInstalled() ? InstallMode.Update
                : InstallMode.Installed;
            switch (InstallCard.Ask(mode))
            {
                case InstallChoice.Primary:
                    if (Instance.Running()) Instance.SignalShow();
                    else Installer.LaunchInstalled("--show");
                    return false;
                case InstallChoice.Secondary:
                    Installer.RememberPortable();
                    return true;
                default:
                    return false;
            }
        }

        public static void InitPreview()
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
        }

        public static bool Has(string[] args, string flag) { return Array.IndexOf(args, flag) >= 0; }
    }
}
