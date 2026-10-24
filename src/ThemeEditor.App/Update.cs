// --------------------------------------------------
// Umami 3DS Theme Editor - Update.cs
// --------------------------------------------------

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Looks for newer releases on GitHub. Updating is left to the user (download the new release),
    ///     which works the same way on every platform and for portable and installed copies alike.
    /// </summary>
    public static class Update
    {
        public const string RepositoryUrl = "https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor";
        public const string ReleasesUrl = RepositoryUrl + "/releases/latest";
        private const string ApiUrl = "https://api.github.com/repos/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/latest";

        public static Version AppVersion
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(2, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static Version UpdateVersion { get; private set; } = AppVersion;

        public static async Task<bool> CheckUpdateAvailable()
        {
            var latest = await CheckLatestVersion();
            if (latest != null)
                UpdateVersion = latest;
            return latest != null && latest > AppVersion;
        }

        public static async Task<Version> CheckLatestVersion()
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("UmamiThemeEditor/" + AppVersion);
                    var json = await http.GetStringAsync(ApiUrl);
                    var tag = JObject.Parse(json)["tag_name"]?.Value<string>();
                    if (string.IsNullOrEmpty(tag))
                        return null;
                    tag = tag.TrimStart('v', 'V');
                    Version version;
                    return Version.TryParse(tag, out version) ? version : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No browser available
            }
        }
    }
}
