using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Nibble
{
    class HidDev
    {
        public string Path;
        public int Pid;
        public ushort UsagePage, Usage;
        public int InLen, FeatLen;
    }

    class Reading
    {
        public bool Found, Online, Charging, Wired;
        public int Percent = -1;
        public MouseConfig Config;
    }

    static class Hid
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, int size, out int req, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int n, out int read, IntPtr ov);

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort a, b, c, d, e, f, g, h, i, j;
        }

        public static SafeFileHandle Open(string path, bool rw)
        {
            return CreateFile(path, rw ? 0xC0000000 : 0u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }

        // Enumerates HID collections whose path contains the given vid/pid filter.
        public static List<HidDev> Enumerate(int vid, int[] pids)
        {
            var list = new List<HidDev>();
            Guid hid; HidD_GetHidGuid(out hid);
            IntPtr set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            try
            {
                string vtag = string.Format("vid_{0:x4}", vid);
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d); i++)
                {
                    int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    string path;
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero)) continue;
                        path = Marshal.PtrToStringUni(buf + 4);
                    }
                    finally { Marshal.FreeHGlobal(buf); }

                    string lp = path.ToLowerInvariant();
                    if (lp.IndexOf(vtag) < 0) continue;
                    int pi = lp.IndexOf("pid_");
                    if (pi < 0) continue;
                    int pid;
                    try { pid = Convert.ToInt32(lp.Substring(pi + 4, 4), 16); } catch { continue; }
                    if (Array.IndexOf(pids, pid) < 0) continue;

                    using (var h = Open(path, false))
                    {
                        if (h.IsInvalid) continue;
                        IntPtr pp;
                        if (!HidD_GetPreparsedData(h, out pp)) continue;
                        HIDP_CAPS caps;
                        HidP_GetCaps(pp, out caps);
                        HidD_FreePreparsedData(pp);
                        list.Add(new HidDev
                        {
                            Path = path, Pid = pid,
                            UsagePage = caps.UsagePage, Usage = caps.Usage,
                            InLen = caps.InputReportByteLength, FeatLen = caps.FeatureReportByteLength
                        });
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }
    }

    // Royal Kludge M3 protocol, reverse engineered from drive.rkgaming.com ("BeiYing" protocol) and
    // probing. Every command is a 64-byte feature report (ID 3) on the 0xFF00 collection:
    //   [3][crc][0x50][len][sn][cmd][offset][payload...]   crc = sum(bytes 2..62) & 0xFF
    static class RkM3
    {
        public const int Vid = 0x372E;
        public const int PidDongle = 0x1019;
        public const int PidWired = 0x102A;
        static readonly int[] Pids = { PidDongle, PidWired };

        const byte FeatureReportId = 3;   // config collection, usage page 0xFF00
        const byte EventReportId = 9;     // event collection, usage page 0xFF06
        const byte EventMarker = 0xFA;
        const byte Fixed = 0x50;

        const byte CmdRead = 0x4F;        // read 10 bytes; offset 0x40+n = config chunk n, 0x81 = status
        const byte CmdParam = 0x35;       // write config bytes at an offset
        const byte CmdDpi = 0x3A;         // write one DPI stage (also sets active stage and stage count)
        const byte CmdReset = 0x06;       // factory reset

        public const int EvtConnect = 2, EvtDpi = 3, EvtBattery = 5;

        // One transaction at a time: the poller, the event reader and the settings UI all share the device.
        static readonly object io = new object();
        static byte readSn = 1;

        public static List<HidDev> Devices() { return Hid.Enumerate(Vid, Pids); }

        // ---------- framing ----------

        static byte[] Frame(int len, byte sn, byte cmd, byte offset, byte[] payload)
        {
            var p = new byte[len];
            p[0] = FeatureReportId;
            p[2] = Fixed;
            p[3] = (byte)(payload == null ? 0 : payload.Length);
            p[4] = sn;
            p[5] = cmd;
            p[6] = offset;
            if (payload != null) Buffer.BlockCopy(payload, 0, p, 7, payload.Length);
            int sum = 0;
            for (int k = 2; k < Math.Min(63, len); k++) sum += p[k];
            p[1] = (byte)sum;
            return p;
        }

        // Sends a frame and waits for the reply that echoes our sn and offset.
        static byte[] Exchange(SafeFileHandle h, int len, byte sn, byte cmd, byte offset, byte[] payload, int firstWait)
        {
            var frame = Frame(len, sn, cmd, offset, payload);
            if (!Hid.HidD_SetFeature(h, frame, frame.Length)) return null;
            Thread.Sleep(firstWait);
            for (int tries = 0; tries < 12; tries++)
            {
                var q = new byte[len];
                q[0] = FeatureReportId;
                if (Hid.HidD_GetFeature(h, q, q.Length) && q[1] == Fixed && q[4] == sn && q[5] == (offset & 0x3F))
                    return q;
                Thread.Sleep(15);
            }
            return null;
        }

        static byte[] Read(SafeFileHandle h, int len, byte offset)
        {
            // sn is echoed but otherwise ignored by reads; rotating it keeps stale replies from matching.
            readSn = (byte)(readSn % 200 + 1);
            var q = Exchange(h, len, readSn, CmdRead, offset, null, 25);
            return q != null && q[2] == 0 ? q : null;
        }

        static T WithDevice<T>(Func<SafeFileHandle, HidDev, T> fn, T fallback)
        {
            lock (io)
            {
                var devs = Devices();
                // Prefer the cable when both are present: it reports charge state directly.
                devs.Sort((a, b) => (b.Pid == PidWired ? 1 : 0) - (a.Pid == PidWired ? 1 : 0));
                foreach (var d in devs)
                {
                    if (d.UsagePage != 0xFF00 || d.FeatLen < 16) continue;
                    using (var h = Hid.Open(d.Path, true))
                    {
                        if (h.IsInvalid) continue;
                        T r = fn(h, d);
                        if (r != null && !r.Equals(fallback)) return r;
                    }
                }
                return fallback;
            }
        }

        // ---------- reads ----------

        public static Reading Query(bool withConfig)
        {
            bool found = Devices().Exists(d => d.UsagePage == 0xFF00);
            var r = WithDevice<Reading>(delegate (SafeFileHandle h, HidDev d)
            {
                var q = Read(h, d.FeatLen, 0x81);
                if (q == null || (q[6] & 0x7F) > 100) return null;
                var res = new Reading { Found = true, Online = true, Percent = q[6] & 0x7F, Charging = (q[6] & 0x80) != 0, Wired = d.Pid == PidWired };
                if (withConfig) res.Config = ReadConfig(h, d.FeatLen);
                return res;
            }, null);
            return r ?? new Reading { Found = found };
        }

        static MouseConfig ReadConfig(SafeFileHandle h, int len)
        {
            var buf = new byte[MouseConfig.Size + 4];
            for (int chunk = 0; chunk * 10 < MouseConfig.Size; chunk++)
            {
                var q = Read(h, len, (byte)(0x40 + chunk));
                if (q == null) return null;
                Buffer.BlockCopy(q, 6, buf, chunk * 10, 10);   // reply: [3][0x50][status][len][sn][offset][data]
            }
            var raw = new byte[MouseConfig.Size];
            Buffer.BlockCopy(buf, 0, raw, 0, raw.Length);
            // The table ends with a fixed A5 5A marker; anything else is a bad read.
            return raw[54] == 0xA5 && raw[55] == 0x5A ? new MouseConfig(raw) : null;
        }

        public static MouseConfig ReadConfig()
        {
            return WithDevice<MouseConfig>((h, d) => ReadConfig(h, d.FeatLen), null);
        }

        // ---------- writes ----------

        static bool Write(byte sn, byte cmd, byte offset, byte[] payload)
        {
            return WithDevice<string>(delegate (SafeFileHandle h, HidDev d)
            {
                var frame = Frame(d.FeatLen, sn, cmd, offset, payload);
                if (!Hid.HidD_SetFeature(h, frame, frame.Length)) return null;
                Thread.Sleep(40); // the site fires and forgets; give the firmware a moment before the next command
                var q = new byte[d.FeatLen];
                q[0] = FeatureReportId;
                Hid.HidD_GetFeature(h, q, q.Length);
                return "ok";
            }, null) != null;
        }

        public static bool WriteDpiStage(int stage, int stages, int dpi, Color color)
        {
            int enc = MouseConfig.EncodeDpi(dpi);
            var payload = new byte[] { (byte)((stage << 4) | stages), (byte)(enc & 255), (byte)(enc >> 8), color.R, color.G, color.B };
            return Write(0x50, CmdDpi, 0, payload);
        }

        public static bool WriteReportRate(int rateId)
        {
            return Write(0x31, CmdParam, 1, new[] { (byte)((rateId & 7) | ((rateId & 7) << 4)) });
        }

        public static bool WriteSleep(int seconds)
        {
            return Write(0x31, CmdParam, 8, new[] { (byte)(seconds / 30) });
        }

        public static bool WritePerformance(int lod, int debounce, int perfBits)
        {
            return Write(0x32, CmdParam, 3, new[] { (byte)lod, (byte)debounce, (byte)perfBits });
        }

        public static bool FactoryReset()
        {
            return Write(0, CmdReset, 0, new byte[] { 0xFF });
        }

        // ---------- firmware ----------

        // Get-version uses the OTA frame family: [3][crc][0x65][58][sn][device][0x92][rfSn][len][0x92 0x00].
        // Over the receiver only the receiver answers; the mouse answers when it's on the cable.
        // Returns e.g. "0109", or null.
        public static string ReadFirmwareVersion(out bool wired)
        {
            bool w = false;
            string v = WithDevice<string>(delegate (SafeFileHandle h, HidDev d)
            {
                var p = new byte[d.FeatLen];
                p[0] = FeatureReportId; p[2] = 0x65; p[3] = 58; p[4] = 7; p[5] = 0; p[6] = 0x92; p[7] = 1; p[8] = 2; p[9] = 0x92;
                int sum = 0;
                for (int k = 2; k < Math.Min(63, p.Length); k++) sum += p[k];
                p[1] = (byte)sum;
                if (!Hid.HidD_SetFeature(h, p, p.Length)) return null;
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(20);
                    var q = new byte[d.FeatLen];
                    q[0] = FeatureReportId;
                    // reply: [3][0x65][status][len][sn][otaRsp][result=0x0E][payloadLen][payload...], version at payload[18],[17]
                    if (Hid.HidD_GetFeature(h, q, q.Length) && q[1] == 0x65 && q[4] == 7 && q[6] == 0x0E && q.Length > 26)
                    {
                        w = d.Pid == PidWired;
                        return string.Format("{0:x2}{1:x2}", q[8 + 18], q[8 + 17]);
                    }
                }
                return null;
            }, null);
            wired = w;
            return v;
        }

        // ---------- events ----------

        public static HidDev EventDevice()
        {
            foreach (var d in Devices())
                if (d.UsagePage == 0xFF06 && d.InLen >= 4) return d;
            return null;
        }

        public static bool ParseEvent(byte[] b, int n, out int type, out int value)
        {
            type = value = 0;
            if (n < 4 || b[0] != EventReportId || b[1] != EventMarker) return false;
            type = b[2]; value = b[3];
            return true;
        }
    }
}
