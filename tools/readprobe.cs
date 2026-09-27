using System;
using System.Threading;

namespace Nibble
{
    // Read-only probe: sends the 0x4F "read" frame with different sn/offset pairs and dumps replies.
    static class ReadProbe
    {
        static void Main(string[] args)
        {
            HidDev cfg = null;
            foreach (var d in RkM3.Devices()) if (d.UsagePage == 0xFF00 && d.FeatLen >= 8 && d.Pid == RkM3.PidDongle) cfg = d;
            if (cfg == null) { Console.WriteLine("no device"); return; }
            int[][] tries =
            {
                new[] { 2, 0x81 }, new[] { 1, 0x00 }, new[] { 1, 0x0B }, new[] { 1, 0x20 },
                new[] { 2, 0x00 }, new[] { 0, 0x00 }, new[] { 3, 0x00 }, new[] { 1, 0x80 }
            };
            using (var h = Hid.Open(cfg.Path, true))
                foreach (var t in tries)
                {
                    byte[] p = new byte[cfg.FeatLen];
                    p[0] = 3; p[2] = 0x50; p[3] = 0; p[4] = (byte)t[0]; p[5] = 0x4F; p[6] = (byte)t[1];
                    int sum = 0; for (int k = 2; k < 63; k++) sum += p[k];
                    p[1] = (byte)sum;
                    bool ok = Hid.HidD_SetFeature(h, p, p.Length);
                    Thread.Sleep(200);
                    byte[] q = new byte[cfg.FeatLen]; q[0] = 3;
                    bool got = Hid.HidD_GetFeature(h, q, q.Length);
                    Console.WriteLine("sn={0} off=0x{1:X2} set={2} get={3}: {4}", t[0], t[1], ok, got, BitConverter.ToString(q, 0, 64));
                }
        }
    }
}
