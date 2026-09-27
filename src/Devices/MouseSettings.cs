using System;
using System.Drawing;

namespace Nibble.Devices
{
    [Flags]
    enum Tuning
    {
        None = 0,
        AngleSnapping = 1,
        GlassMode = 2,
        RippleControl = 4,
        MotionSync = 8
    }

    // What a mouse supports. The settings UI is built from this.
    sealed class MouseCaps
    {
        public int[] PollingRates;           // Hz
        public int MaxStages;
        public int DpiMin, DpiMax, DpiStep;
        public Color[] LedColors;            // colours the stage LED can show; null if it has none
        public int[] SleepOptions;           // seconds, 0 = never
        public string[] LiftOffNames;
        public string[] SensorModes, SensorModeHints;
        public int DebounceMin, DebounceMax; // ms
        public Tuning Tunings;
        public int MotionSyncMaxHz;          // 0 = no limit
    }

    // A mouse's settings in device-neutral units. Drivers translate to and from their own format.
    sealed class MouseSettings
    {
        public readonly MouseCaps Caps;
        public int PollingHz;
        public int SleepSeconds;
        public Tuning Tuning;

        int stage = 1, stageCount = 1, liftOff, debounce, sensorMode;
        readonly int[] dpi;
        readonly Color[] colors;

        public MouseSettings(MouseCaps caps)
        {
            Caps = caps;
            dpi = new int[caps.MaxStages];
            colors = new Color[caps.MaxStages];
            debounce = caps.DebounceMin;
        }

        public MouseSettings Clone()
        {
            var c = new MouseSettings(Caps)
            {
                PollingHz = PollingHz, SleepSeconds = SleepSeconds, Tuning = Tuning,
                stage = stage, stageCount = stageCount, liftOff = liftOff, debounce = debounce, sensorMode = sensorMode
            };
            Array.Copy(dpi, c.dpi, dpi.Length);
            Array.Copy(colors, c.colors, colors.Length);
            return c;
        }

        public int Stage { get { return stage; } }             // 1-based
        public int StageCount { get { return stageCount; } }

        public void SetStages(int active, int count)
        {
            stageCount = Clamp(count, 1, Caps.MaxStages);
            stage = Clamp(active, 1, stageCount);
        }

        public int Dpi(int s) { return dpi[s - 1]; }
        public void SetDpi(int s, int value) { dpi[s - 1] = Clamp(value, Caps.DpiMin, Caps.DpiMax); }
        public int CurrentDpi { get { return Dpi(stage); } }

        public Color StageColor(int s) { return colors[s - 1]; }
        public void SetStageColor(int s, Color c) { colors[s - 1] = c; }
        public Color CurrentColor { get { return StageColor(stage); } }

        public int LiftOff { get { return liftOff; } set { liftOff = Clamp(value, 0, Caps.LiftOffNames.Length - 1); } }
        public int Debounce { get { return debounce; } set { debounce = Clamp(value, Caps.DebounceMin, Caps.DebounceMax); } }
        public int SensorMode { get { return sensorMode; } set { sensorMode = Clamp(value, 0, Caps.SensorModes.Length - 1); } }

        public bool Has(Tuning t) { return (Tuning & t) != 0; }
        public void Set(Tuning t, bool on) { Tuning = on ? Tuning | t : Tuning & ~t; }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
