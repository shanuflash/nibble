using System;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Nibble
{
    // Nibble's own updates, from the latest GitHub release. Checked on demand only.
    class UpdateInfo
    {
        public Version Current, Latest;
        public string Tag, Url;
        public bool Failed, NoReleases;
        public bool Available { get { return Latest != null && Current != null && Latest > Current; } }
    }

    static class Updates
    {
        const string Api = "https://api.github.com/repos/shanuflash/nibble/releases/latest";
        public const string ReleasesPage = "https://github.com/shanuflash/nibble/releases";

        public static Version Current
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        public static UpdateInfo Check()
        {
            var info = new UpdateInfo { Current = Current };
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                var req = (HttpWebRequest)WebRequest.Create(Api);
                req.UserAgent = "Nibble/" + info.Current;          // GitHub's API requires a user agent
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000;
                string json;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new System.IO.StreamReader(resp.GetResponseStream()))
                    json = r.ReadToEnd();
                info.Tag = Str(json, "tag_name");
                info.Url = Str(json, "html_url");
                Version v;
                if (info.Tag != null && Version.TryParse(info.Tag.TrimStart('v', 'V'), out v))
                    info.Latest = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) info.NoReleases = true;
                else info.Failed = true;
            }
            catch { info.Failed = true; }
            return info;
        }

        static string Str(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
