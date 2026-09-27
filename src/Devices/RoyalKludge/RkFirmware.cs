using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Nibble.Platform;

namespace Nibble.Devices.RoyalKludge
{
    static class RkFirmware
    {
        // Get-version uses the OTA frame family: [3][crc][0x65][58][sn][device][0x92][rfSn][len][0x92 0x00].
        // Through the receiver only the receiver answers; the mouse answers only over its cable.
        public static string ReadVersion(RkLink link, out bool fromMouse)
        {
            bool wired = false;
            string v = link.Use(delegate (SafeFileHandle h, HidDeviceInfo d)
            {
                var frame = RkLink.Frame(d.FeatureLength, 0x65, 58, 7, 0, 0x92, 1, 2, 0x92, 0);
                // reply: [3][0x65][status][len][sn][otaRsp][result=0x0E][payloadLen][payload...]
                var q = RkLink.Exchange(h, frame, 20, 15, 20, r => r[1] == 0x65 && r[4] == 7 && r[6] == 0x0E && r.Length > 26);
                if (q == null) return null;
                wired = link.IsWired(d);
                return string.Format("{0:x2}{1:x2}", q[8 + 18], q[8 + 17]);
            });
            fromMouse = wired;
            return v;
        }

        // Fills the latest versions from RK's manifest, the same feed the web driver uses:
        // {"mouse":{"version":"0122","url":"..."},"dongle":{...}}
        public static void FetchLatest(FirmwareInfo info, string manifestUrl)
        {
            try
            {
                string json = Http.Get(manifestUrl, "application/json");
                info.LatestMouse = Field(json, "mouse", "version");
                info.MouseUrl = Field(json, "mouse", "url");
                info.LatestReceiver = Field(json, "dongle", "version");
                info.ReceiverUrl = Field(json, "dongle", "url");
                info.Checked = info.LatestMouse != null || info.LatestReceiver != null;
                info.Failed = !info.Checked;
            }
            catch { info.Failed = true; }
        }

        static string Field(string json, string section, string key)
        {
            var m = Regex.Match(json, "\"" + section + "\"\\s*:\\s*\\{([^}]*)\\}");
            if (!m.Success) return null;
            var f = Regex.Match(m.Groups[1].Value, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return f.Success ? f.Groups[1].Value : null;
        }
    }
}
