using System;
using System.IO;
using System.Threading;

namespace Nibble
{
    // Logs HID event reports and the battery feature response for two minutes,
    // to find where the M3 reports its DPI stage.
    static class DpiProbe
    {
        static readonly object gate = new object();
        static StreamWriter log;

        static void Write(string s)
        {
            lock (gate) { log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); log.Flush(); }
        }

        static void Main(string[] args)
        {
            log = new StreamWriter(args.Length > 0 ? args[0] : "dpiprobe.log");
            var ev = RkM3.EventDevice();
            Write("event device: " + (ev == null ? "none" : ev.Path));
            if (ev != null)
            {
                var t = new Thread(delegate ()
                {
                    using (var h = Hid.Open(ev.Path, true))
                    {
                        var buf = new byte[ev.InLen];
                        int n;
                        while (Hid.ReadFile(h, buf, buf.Length, out n, IntPtr.Zero))
                            Write("EVENT " + BitConverter.ToString(buf, 0, Math.Min(n, 16)));
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            HidDev cfg = null;
            foreach (var d in RkM3.Devices()) if (d.UsagePage == 0xFF00 && d.FeatLen >= 8) cfg = d;
            var end = DateTime.Now.AddSeconds(120);
            while (DateTime.Now < end)
            {
                if (cfg != null)
                    using (var h = Hid.Open(cfg.Path, true))
                    {
                        byte[] p = new byte[cfg.FeatLen];
                        p[0] = 3; p[2] = 0x50; p[4] = 0x02; p[5] = 0x4F; p[6] = 0x81;
                        int sum = 0; for (int k = 2; k < 63; k++) sum += p[k];
                        p[1] = (byte)sum;
                        Hid.HidD_SetFeature(h, p, p.Length);
                        Thread.Sleep(200);
                        byte[] q = new byte[cfg.FeatLen]; q[0] = 3;
                        if (Hid.HidD_GetFeature(h, q, q.Length)) Write("FEAT  " + BitConverter.ToString(q, 0, 20));
                    }
                Thread.Sleep(1300);
            }
            Write("done");
        }
    }
}
