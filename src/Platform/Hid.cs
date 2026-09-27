using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nibble.Platform
{
    // One HID top-level collection. A single mouse usually exposes several.
    sealed class HidDeviceInfo
    {
        public string Path;
        public int Pid;
        public ushort UsagePage, Usage;
        public int InputLength, FeatureLength;
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

        const int DIGCF_PRESENT_DEVICEINTERFACE = 0x12;

        public static SafeFileHandle Open(string path, bool readWrite)
        {
            return CreateFile(path, readWrite ? 0xC0000000 : 0u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }

        public static List<HidDeviceInfo> Enumerate(int vid, int[] pids)
        {
            var list = new List<HidDeviceInfo>();
            Guid hid; HidD_GetHidGuid(out hid);
            IntPtr set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            try
            {
                string vidTag = string.Format("vid_{0:x4}", vid);
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d); i++)
                {
                    string path = InterfacePath(set, ref d);
                    if (path == null) continue;
                    string lower = path.ToLowerInvariant();
                    if (lower.IndexOf(vidTag) < 0) continue;
                    int pid = ParsePid(lower);
                    if (Array.IndexOf(pids, pid) < 0) continue;
                    var info = Describe(path, pid);
                    if (info != null) list.Add(info);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static string InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d)
        {
            int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr buf = Marshal.AllocHGlobal(req);
            try
            {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);   // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA
                if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(buf + 4);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static int ParsePid(string lowerPath)
        {
            int at = lowerPath.IndexOf("pid_");
            if (at < 0 || at + 8 > lowerPath.Length) return -1;
            try { return Convert.ToInt32(lowerPath.Substring(at + 4, 4), 16); }
            catch { return -1; }
        }

        static HidDeviceInfo Describe(string path, int pid)
        {
            using (var h = Open(path, false))
            {
                if (h.IsInvalid) return null;
                IntPtr pp;
                if (!HidD_GetPreparsedData(h, out pp)) return null;
                HIDP_CAPS caps;
                HidP_GetCaps(pp, out caps);
                HidD_FreePreparsedData(pp);
                return new HidDeviceInfo
                {
                    Path = path, Pid = pid,
                    UsagePage = caps.UsagePage, Usage = caps.Usage,
                    InputLength = caps.InputReportByteLength, FeatureLength = caps.FeatureReportByteLength
                };
            }
        }
    }
}
