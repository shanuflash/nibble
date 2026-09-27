using System;
using System.Drawing;
using Microsoft.Win32.SafeHandles;
using Nibble.Platform;

namespace Nibble.Devices.RoyalKludge
{
    sealed class M3Mouse : IMouse
    {
        const int Vid = 0x372E, PidReceiver = 0x1019, PidWired = 0x102A;
        const byte CmdParam = 0x35, CmdDpi = 0x3A, CmdReset = 0x06;
        const byte OffsetStatus = 0x81, OffsetTable = 0x40;
        const byte EvtLink = 2, EvtDpi = 3, EvtBattery = 5;
        const string Manifest = "https://drive.rkgaming.com/down/work/RKWEB/firmware/M3/M3(533)_firmware.json";

        static readonly MouseCaps caps = new MouseCaps
        {
            PollingRates = M3Table.RateHz,
            MaxStages = 6,
            DpiMin = 50, DpiMax = 26000, DpiStep = 50,
            LedColors = new[]
            {
                Color.FromArgb(255, 0, 0), Color.FromArgb(255, 128, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0),
                Color.FromArgb(0, 255, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 255, 255)
            },
            SleepOptions = new[] { 0, 30, 60, 120, 180, 300, 600, 900, 1800, 3600 },
            LiftOffNames = new[] { "0.7 mm", "1.0 mm", "2.0 mm" },
            SensorModes = new[] { "Office", "Performance", "Competitive" },
            SensorModeHints = new[] { "Low power: slower response, longest battery", "Balanced response and battery", "Fastest response, highest power draw" },
            DebounceMin = 1, DebounceMax = 20,
            Tunings = Tuning.MotionSync | Tuning.RippleControl | Tuning.AngleSnapping | Tuning.GlassMode,
            MotionSyncMaxHz = 4000
        };

        readonly RkLink link = new RkLink(Vid, PidWired, PidReceiver, PidWired);
        volatile int tuningKeep;   // table bits Nibble doesn't model, written back unchanged

        public string Name { get { return "RK M3"; } }
        public string Model { get { return "Royal Kludge M3"; } }
        public MouseCaps Caps { get { return caps; } }
        public string VendorSite { get { return "https://drive.rkgaming.com/"; } }

        public bool IsPresent() { return link.Present(); }

        public MouseStatus Query(bool withSettings)
        {
            bool found = link.Present();
            var status = link.Use(delegate (SafeFileHandle h, HidDeviceInfo d)
            {
                var q = link.Read(h, d, OffsetStatus);
                if (q == null) return null;
                int b = q[RkLink.ReplyData];
                if ((b & 0x7F) > 100) return null;
                return new MouseStatus
                {
                    Found = true, Online = true,
                    Percent = b & 0x7F, Charging = (b & 0x80) != 0, Wired = link.IsWired(d),
                    Settings = withSettings ? ReadTable(h, d) : null
                };
            });
            return status ?? new MouseStatus { Found = found };
        }

        public MouseSettings ReadSettings() { return link.Use<MouseSettings>(ReadTable); }

        MouseSettings ReadTable(SafeFileHandle h, HidDeviceInfo d)
        {
            var raw = new byte[60];
            for (int chunk = 0; chunk * 10 < M3Table.Size; chunk++)
            {
                var q = link.Read(h, d, (byte)(OffsetTable + chunk));
                if (q == null) return null;
                Buffer.BlockCopy(q, RkLink.ReplyData, raw, chunk * 10, 10);
            }
            if (!M3Table.Valid(raw)) return null;
            tuningKeep = raw[5];
            return M3Table.Decode(raw, caps);
        }

        public bool Write(MouseSettings s, SettingGroup group)
        {
            switch (group)
            {
                case SettingGroup.Dpi: return link.Send(0x50, CmdDpi, 0, M3Table.ActiveStage(s));
                case SettingGroup.PollingRate: return link.Send(0x31, CmdParam, 1, M3Table.Rate(s.PollingHz));
                case SettingGroup.Sleep: return link.Send(0x31, CmdParam, 8, (byte)(s.SleepSeconds / 30));
                case SettingGroup.Sensor: return link.Send(0x32, CmdParam, 3, (byte)(s.LiftOff + 1), (byte)s.Debounce, M3Table.TuningByte(s, tuningKeep));
                default: return false;
            }
        }

        public bool FactoryReset() { return link.Send(0, CmdReset, 0, 0xFF); }

        public FirmwareInfo CheckFirmware()
        {
            var info = new FirmwareInfo();
            try
            {
                bool fromMouse;
                string v = RkFirmware.ReadVersion(link, out fromMouse);
                if (fromMouse) info.Mouse = v; else info.Receiver = v;
            }
            catch { }
            RkFirmware.FetchLatest(info, Manifest);
            info.CheckedAt = DateTime.Now;
            return info;
        }

        public void Listen(Func<bool> stop, Action<DeviceEvent> onEvent)
        {
            link.Listen(stop, delegate (byte type, byte value)
            {
                switch (type)
                {
                    case EvtBattery:
                        if ((value & 0x7F) > 100) return;
                        onEvent(new DeviceEvent { Kind = DeviceEventKind.Battery, Percent = value & 0x7F, Charging = (value & 0x80) != 0 });
                        break;
                    case EvtDpi:
                        onEvent(new DeviceEvent { Kind = DeviceEventKind.DpiButton });
                        break;
                    case EvtLink:
                        onEvent(new DeviceEvent { Kind = DeviceEventKind.Link, Online = value == 1 });
                        break;
                }
            }, delegate { onEvent(new DeviceEvent { Kind = DeviceEventKind.Reattached }); });
        }
    }
}
