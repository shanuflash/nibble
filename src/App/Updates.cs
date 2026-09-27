using System;
using System.Net;
using System.Text.RegularExpressions;
using Nibble.Platform;

namespace Nibble
{
    sealed class UpdateInfo
    {
        public Version Current, Latest;
        public string Tag, Url;
        public bool Failed, NoReleases;
        public bool Available { get { return Latest != null && Current != null && Latest > Current; } }
    }

    // Nibble's own updates, from the latest GitHub release. Checked on demand only.
    static class Updates
    {
        const string Api = "https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest";

        public static UpdateInfo Check()
        {
            var info = new UpdateInfo { Current = AppInfo.Version };
            try
            {
                string json = Http.Get(Api, "application/vnd.github+json");
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
