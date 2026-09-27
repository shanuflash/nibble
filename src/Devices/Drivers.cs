using Nibble.Devices.RoyalKludge;

namespace Nibble.Devices
{
    static class Drivers
    {
        // Every supported mouse. The first is used when none is plugged in.
        public static readonly IMouse[] All = { new M3Mouse() };

        public static IMouse Detect()
        {
            foreach (var m in All)
            {
                try { if (m.IsPresent()) return m; }
                catch { }
            }
            return All[0];
        }
    }
}
