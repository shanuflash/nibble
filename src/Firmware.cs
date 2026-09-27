using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nibble
{
    // Firmware status: versions on the hardware vs. RK's published manifest (the same feed the web driver
    // uses). Checked only on demand; Nibble never flashes firmware itself, it hands off to RK's updater.
    class FirmwareInfo
    {
        public string Receiver;          // on the hardware, null if unknown
        public string Mouse;             // on the hardware, only readable over the cable
        public string LatestReceiver, LatestMouse;
        public string ReceiverUrl, MouseUrl;
        public bool Checked, Failed;
        public DateTime CheckedAt;

        public bool ReceiverUpdate { get { return Newer(LatestReceiver, Receiver); } }
        public bool MouseUpdate { get { return Newer(LatestMouse, Mouse); } }

        // Versions are 4 hex digits ("0109"); compare numerically.
        static bool Newer(string latest, string current)
        {
            int a, b;
            return latest != null && current != null
                && int.TryParse(latest, System.Globalization.NumberStyles.HexNumber, null, out a)
                && int.TryParse(current, System.Globalization.NumberStyles.HexNumber, null, out b)
                && a > b;
        }
    }

    static class Firmware
    {
        const string Manifest = "https://drive.rkgaming.com/down/work/RKWEB/firmware/M3/M3(533)_firmware.json";

        public static FirmwareInfo Check()
        {
            var info = new FirmwareInfo();
            bool wired;
            try
            {
                string v = RkM3.ReadFirmwareVersion(out wired);
                if (wired) info.Mouse = v; else info.Receiver = v;
            }
            catch { }

            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET 4.x
                using (var wc = new TimeoutClient())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/json";
                    string json = wc.DownloadString(Manifest);
                    info.LatestMouse = Field(json, "mouse", "version");
                    info.MouseUrl = Field(json, "mouse", "url");
                    info.LatestReceiver = Field(json, "dongle", "version");
                    info.ReceiverUrl = Field(json, "dongle", "url");
                }
                info.Checked = info.LatestMouse != null || info.LatestReceiver != null;
                info.Failed = !info.Checked;
            }
            catch { info.Failed = true; }
            info.CheckedAt = DateTime.Now;
            return info;
        }

        // Tiny targeted reader for {"mouse":{"version":"0122","url":"..."},"dongle":{...}}
        static string Field(string json, string section, string key)
        {
            var m = Regex.Match(json, "\"" + section + "\"\\s*:\\s*\\{([^}]*)\\}");
            if (!m.Success) return null;
            var f = Regex.Match(m.Groups[1].Value, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return f.Success ? f.Groups[1].Value : null;
        }

        class TimeoutClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                r.Timeout = 10000;
                return r;
            }
        }
    }
}
