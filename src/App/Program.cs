using System;
using System.Runtime.InteropServices;
using System.Threading;
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

            bool created;
            using (var mutex = new Mutex(true, @"Local\Nibble.RK-M3", out created))
            {
                if (!created) return;
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var app = new TrayApp(Has(args, "--show"));
                if (Has(args, "--settings"))
                {
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += delegate { t.Stop(); app.OpenSettings(); };
                    t.Start();
                }
                Application.Run(app);
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
