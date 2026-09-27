using System;
using System.Drawing;

namespace Nibble
{
    // The M3's 56-byte settings table, as stored on the mouse (read at 0x40..0x45).
    //  [1] polling rate id (both nibbles)   [2] active stage << 4 | stage count
    //  [3] LOD   [4] debounce ms   [5] perf bits   [8] sleep / 30 s
    //  [11 + 2i] DPI of stage i (u16, dpi/50-1 up to 30000)   [27 + 3i] stage LED colour RGB
    class MouseConfig
    {
        public const int Size = 56;
        public const int MaxStages = 6;

        // Polling rate ids used by the firmware.
        public static readonly int[] RateIds = { 3, 2, 1, 0, 6, 5, 4 };
        public static readonly int[] RateHz = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        public static readonly int[] SleepOptions = { 0, 30, 60, 120, 180, 300, 600, 900, 1800, 3600 };
        public static readonly string[] LodNames = { "0.7 mm", "1.0 mm", "2.0 mm" };   // values 1..3
        public static readonly string[] SensorModes = { "Office", "Performance", "Competitive" };

        public const int BitAngleSnap = 1, BitGlass = 2, BitRipple = 16, BitMotionSync = 32;

        public readonly byte[] Raw;

        public MouseConfig(byte[] raw) { Raw = (byte[])raw.Clone(); }
        public MouseConfig Clone() { return new MouseConfig(Raw); }

        public int RateId { get { return Raw[1] & 7; } set { Raw[1] = (byte)((value & 7) | ((value & 7) << 4)); } }
        public int RateHzValue
        {
            get { int i = Array.IndexOf(RateIds, RateId); return i < 0 ? 1000 : RateHz[i]; }
        }

        public int Stages { get { return Clamp(Raw[2] & 15, 1, MaxStages); } }
        public int Stage { get { return Clamp(Raw[2] >> 4, 1, Stages); } }
        public void SetStages(int stage, int count)
        {
            count = Clamp(count, 1, MaxStages);
            stage = Clamp(stage, 1, count);
            Raw[2] = (byte)((stage << 4) | count);
        }

        public int Lod { get { return Clamp(Raw[3] & 15, 1, 3); } set { Raw[3] = (byte)Clamp(value, 1, 3); } }
        public int Debounce { get { return Clamp(Raw[4], 1, 20); } set { Raw[4] = (byte)Clamp(value, 1, 20); } }

        public int PerfBits { get { return Raw[5]; } }
        public bool Flag(int bit) { return (Raw[5] & bit) != 0; }
        public void SetFlag(int bit, bool on) { Raw[5] = (byte)(on ? Raw[5] | bit : Raw[5] & ~bit); }
        public int SensorMode { get { return Clamp(Raw[5] >> 6, 0, 2); } set { Raw[5] = (byte)((Raw[5] & 63) | (Clamp(value, 0, 2) << 6)); } }

        public int SleepSeconds { get { return Raw[8] * 30; } set { Raw[8] = (byte)(Math.Max(0, value) / 30); } }

        public int Dpi(int stage)
        {
            int o = 11 + (stage - 1) * 2;
            return DecodeDpi(Raw[o] | (Raw[o + 1] << 8));
        }

        public void SetDpi(int stage, int dpi)
        {
            int o = 11 + (stage - 1) * 2, e = EncodeDpi(dpi);
            Raw[o] = (byte)(e & 255); Raw[o + 1] = (byte)(e >> 8);
        }

        public Color StageColor(int stage)
        {
            int o = 27 + (stage - 1) * 3;
            return Color.FromArgb(255, Raw[o], Raw[o + 1], Raw[o + 2]);
        }

        public void SetStageColor(int stage, Color c)
        {
            int o = 27 + (stage - 1) * 3;
            Raw[o] = c.R; Raw[o + 1] = c.G; Raw[o + 2] = c.B;
        }

        public int CurrentDpi { get { return Dpi(Stage); } }

        public static int EncodeDpi(int dpi)
        {
            dpi = Clamp(dpi, 50, 42000);
            return dpi <= 30000 ? dpi / 50 - 1 : dpi;
        }

        public static int DecodeDpi(int raw)
        {
            if (raw > 42000) raw = 42000;
            return raw < 600 ? (raw + 1) * 50 : raw;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
