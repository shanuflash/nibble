using System;
using System.Globalization;

namespace Nibble.Devices
{
    // Installed firmware vs. the vendor's latest. Nibble never flashes firmware; it links to the vendor's updater.
    sealed class FirmwareInfo
    {
        public string Receiver;          // installed, null if unknown
        public string Mouse;             // installed, null if unknown
        public string LatestReceiver, LatestMouse;
        public string ReceiverUrl, MouseUrl;
        public bool Checked, Failed;
        public DateTime CheckedAt;

        public bool ReceiverUpdate { get { return Newer(LatestReceiver, Receiver); } }
        public bool MouseUpdate { get { return Newer(LatestMouse, Mouse); } }

        // Versions are hex strings like "0109".
        static bool Newer(string latest, string current)
        {
            int a, b;
            return latest != null && current != null
                && int.TryParse(latest, NumberStyles.HexNumber, null, out a)
                && int.TryParse(current, NumberStyles.HexNumber, null, out b)
                && a > b;
        }
    }
}
