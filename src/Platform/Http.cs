using System.IO;
using System.Net;

namespace Nibble.Platform
{
    static class Http
    {
        public static string Get(string url, string accept)
        {
            // .NET 4.x may default to TLS 1.0, which GitHub and RK's CDN refuse.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
            req.Accept = accept;
            req.Timeout = 10000;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                return r.ReadToEnd();
        }
    }
}
