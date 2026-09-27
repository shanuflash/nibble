using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Nibble.Platform;

namespace Nibble.Devices.RoyalKludge
{
    // Royal Kludge's vendor protocol, as used by drive.rkgaming.com. Commands are 64-byte feature reports
    // (ID 3) on the 0xFF00 collection:
    //   [3][crc][0x50][len][sn][cmd][offset][payload...]     crc = sum(bytes 2..62) & 0xFF
    // Replies echo sn and offset:
    //   [3][0x50][status][len][sn][offset & 0x3F][data...]
    // A reply can only be read once, so all traffic goes through one lock.
    // Events arrive as input reports on the 0xFF06 collection: [9][0xFA][type][value].
    sealed class RkLink
    {
        public const byte ReportId = 3;
        public const int ReplyData = 6;
        const byte Command = 0x50;
        const byte CmdRead = 0x4F;
        const ushort ConfigPage = 0xFF00, EventPage = 0xFF06;
        const byte EventReportId = 9, EventMarker = 0xFA;

        readonly int vid, wiredPid;
        readonly int[] pids;
        readonly object io = new object();
        byte readSn = 1;

        public RkLink(int vid, int wiredPid, params int[] pids)
        {
            this.vid = vid;
            this.wiredPid = wiredPid;
            this.pids = pids;
        }

        public bool IsWired(HidDeviceInfo d) { return d.Pid == wiredPid; }

        public bool Present()
        {
            return Hid.Enumerate(vid, pids).Exists(d => d.UsagePage == ConfigPage);
        }

        // Runs fn on each config collection until one returns non-null. The cable is tried first
        // because it reports charge state directly.
        public T Use<T>(Func<SafeFileHandle, HidDeviceInfo, T> fn) where T : class
        {
            lock (io)
            {
                var devs = Hid.Enumerate(vid, pids);
                devs.Sort((a, b) => (IsWired(b) ? 1 : 0) - (IsWired(a) ? 1 : 0));
                foreach (var d in devs)
                {
                    if (d.UsagePage != ConfigPage || d.FeatureLength < 16) continue;
                    using (var h = Hid.Open(d.Path, true))
                    {
                        if (h.IsInvalid) continue;
                        T r = fn(h, d);
                        if (r != null) return r;
                    }
                }
                return null;
            }
        }

        // Builds a report from its body (bytes 2 onwards) and fills in the checksum.
        public static byte[] Frame(int len, params byte[] body)
        {
            var p = new byte[len];
            p[0] = ReportId;
            Buffer.BlockCopy(body, 0, p, 2, body.Length);
            int sum = 0;
            for (int k = 2; k < Math.Min(63, len); k++) sum += p[k];
            p[1] = (byte)sum;
            return p;
        }

        static byte[] CommandFrame(int len, byte sn, byte cmd, byte offset, byte[] payload)
        {
            var body = new byte[5 + payload.Length];
            body[0] = Command;
            body[1] = (byte)payload.Length;
            body[2] = sn;
            body[3] = cmd;
            body[4] = offset;
            Buffer.BlockCopy(payload, 0, body, 5, payload.Length);
            return Frame(len, body);
        }

        public static byte[] Blank(int len)
        {
            var q = new byte[len];
            q[0] = ReportId;
            return q;
        }

        // Sends a frame and polls for the reply that satisfies match.
        public static byte[] Exchange(SafeFileHandle h, byte[] frame, int firstWait, int tries, int interval, Func<byte[], bool> match)
        {
            if (!Hid.HidD_SetFeature(h, frame, frame.Length)) return null;
            Thread.Sleep(firstWait);
            for (int i = 0; i < tries; i++)
            {
                var q = Blank(frame.Length);
                if (Hid.HidD_GetFeature(h, q, q.Length) && match(q)) return q;
                Thread.Sleep(interval);
            }
            return null;
        }

        // Reads 10 bytes at offset. Returns the whole reply (data at ReplyData), or null.
        public byte[] Read(SafeFileHandle h, HidDeviceInfo d, byte offset)
        {
            // The mouse echoes sn but otherwise ignores it on reads; rotating it keeps stale replies from matching.
            byte sn = readSn = (byte)(readSn % 200 + 1);
            var frame = CommandFrame(d.FeatureLength, sn, CmdRead, offset, new byte[0]);
            var q = Exchange(h, frame, 25, 12, 15, r => r[1] == Command && r[4] == sn && r[5] == (offset & 0x3F));
            return q != null && q[2] == 0 ? q : null;
        }

        public bool Send(byte sn, byte cmd, byte offset, params byte[] payload)
        {
            return Use(delegate (SafeFileHandle h, HidDeviceInfo d)
            {
                var frame = CommandFrame(d.FeatureLength, sn, cmd, offset, payload);
                if (!Hid.HidD_SetFeature(h, frame, frame.Length)) return null;
                // The web driver fires and forgets; give the firmware a moment before the next command.
                Thread.Sleep(40);
                Hid.HidD_GetFeature(h, Blank(d.FeatureLength), d.FeatureLength);
                return d;
            }) != null;
        }

        // Blocks reading the event collection, reopening it when the receiver is replugged.
        public void Listen(Func<bool> stop, Action<byte, byte> onEvent, Action onReattached)
        {
            bool first = true;
            while (!stop())
            {
                HidDeviceInfo d = null;
                try { d = Hid.Enumerate(vid, pids).Find(x => x.UsagePage == EventPage && x.InputLength >= 4); }
                catch { }
                if (d == null) { first = false; Thread.Sleep(4000); continue; }
                using (var h = Hid.Open(d.Path, true))
                {
                    if (!h.IsInvalid)
                    {
                        if (!first) onReattached();
                        first = false;
                        var buf = new byte[d.InputLength];
                        int n;
                        while (!stop() && Hid.ReadFile(h, buf, buf.Length, out n, IntPtr.Zero))
                            if (n >= 4 && buf[0] == EventReportId && buf[1] == EventMarker) onEvent(buf[2], buf[3]);
                    }
                }
                Thread.Sleep(2000);
            }
        }
    }
}
