using System;
using System.Drawing;

namespace Nibble.Devices.RoyalKludge
{
    // The M3's 56-byte settings table, read in 10-byte chunks at offsets 0x40..0x45:
    //   [1] polling rate id (both nibbles)   [2] active stage << 4 | stage count
    //   [3] lift-off 1..3   [4] debounce ms   [5] tuning bits, sensor mode in bits 6..7   [8] sleep / 30 s
    //   [11 + 2i] DPI of stage i   [27 + 3i] stage LED colour   [54..55] A5 5A end marker
    static class M3Table
    {
        public const int Size = 56;

        static readonly int[] RateIds = { 3, 2, 1, 0, 6, 5, 4 };
        public static readonly int[] RateHz = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        const int BitAngleSnap = 1, BitGlass = 2, BitRipple = 16, BitMotionSync = 32, ModeShift = 6;
        public const int UnmodelledBits = 0x0C;

        public static bool Valid(byte[] raw) { return raw.Length >= Size && raw[54] == 0xA5 && raw[55] == 0x5A; }

        public static MouseSettings Decode(byte[] raw, MouseCaps caps)
        {
            var s = new MouseSettings(caps);
            int rate = Array.IndexOf(RateIds, raw[1] & 7);
            s.PollingHz = rate < 0 ? 1000 : RateHz[rate];
            s.SetStages(raw[2] >> 4, raw[2] & 15);
            s.LiftOff = (raw[3] & 15) - 1;
            s.Debounce = raw[4];
            s.SensorMode = raw[5] >> ModeShift;
            if ((raw[5] & BitAngleSnap) != 0) s.Tuning |= Tuning.AngleSnapping;
            if ((raw[5] & BitGlass) != 0) s.Tuning |= Tuning.GlassMode;
            if ((raw[5] & BitRipple) != 0) s.Tuning |= Tuning.RippleControl;
            if ((raw[5] & BitMotionSync) != 0) s.Tuning |= Tuning.MotionSync;
            s.SleepSeconds = raw[8] * 30;
            for (int i = 1; i <= caps.MaxStages; i++)
            {
                int o = 11 + (i - 1) * 2, c = 27 + (i - 1) * 3;
                s.SetDpi(i, DecodeDpi(raw[o] | (raw[o + 1] << 8)));
                s.SetStageColor(i, Color.FromArgb(255, raw[c], raw[c + 1], raw[c + 2]));
            }
            return s;
        }

        // Payload for the DPI command: writes the active stage and makes it current.
        public static byte[] ActiveStage(MouseSettings s)
        {
            int enc = EncodeDpi(s.CurrentDpi);
            var c = s.CurrentColor;
            return new[] { (byte)((s.Stage << 4) | s.StageCount), (byte)(enc & 255), (byte)(enc >> 8), c.R, c.G, c.B };
        }

        public static byte Rate(int hz)
        {
            int i = Array.IndexOf(RateHz, hz);
            int id = i < 0 ? 0 : RateIds[i];
            return (byte)(id | (id << 4));
        }

        public static byte TuningByte(MouseSettings s, int keep)
        {
            int b = keep & UnmodelledBits;
            if (s.Has(Tuning.AngleSnapping)) b |= BitAngleSnap;
            if (s.Has(Tuning.GlassMode)) b |= BitGlass;
            if (s.Has(Tuning.RippleControl)) b |= BitRipple;
            if (s.Has(Tuning.MotionSync)) b |= BitMotionSync;
            return (byte)(b | (s.SensorMode << ModeShift));
        }

        // Up to 30000 DPI the firmware stores dpi / 50 - 1; above that, the raw value.
        static int EncodeDpi(int dpi)
        {
            dpi = Math.Max(50, Math.Min(42000, dpi));
            return dpi <= 30000 ? dpi / 50 - 1 : dpi;
        }

        static int DecodeDpi(int raw)
        {
            if (raw > 42000) raw = 42000;
            return raw < 600 ? (raw + 1) * 50 : raw;
        }
    }
}
