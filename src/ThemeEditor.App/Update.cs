// --------------------------------------------------
// Umami 3DS Theme Editor - Update.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ThemeEditor.App
{
    /// <summary>How this copy of the editor was installed, which decides how it can update itself.</summary>
    public enum InstallKind
    {
        /// <summary>Portable zip, Linux archive or a development build: the user downloads the new release.</summary>
        Manual,

        /// <summary>Installed with the Windows setup.exe: the new setup runs silently over it.</summary>
        WindowsInstaller,

        /// <summary>The macOS app bundle: the new one from the .dmg replaces it.</summary>
        MacApp
    }

    /// <summary>A file of a GitHub release.</summary>
    public sealed class ReleaseAsset
    {
        public string Name;
        public string Url;
        public long Size;
        /// <summary>"sha256:..." as GitHub reports it, or null.</summary>
        public string Digest;
        /// <summary>The checksum file published next to it (name.sha256), used when <see cref="Digest" /> is unknown.</summary>
        public string ChecksumUrl;
    }

    /// <summary>The newest release on GitHub.</summary>
    public sealed class ReleaseInfo
    {
        public Version Version;
        public List<ReleaseAsset> Assets = new List<ReleaseAsset>();
    }

    /// <summary>
    ///     Looks for newer releases on GitHub and, where the way the editor was installed allows it, installs them: the
    ///     Windows setup runs silently over the installed copy, the macOS app bundle is swapped for the one in the new .dmg.
    ///     Portable copies and Linux keep the download page. Every download is checked against GitHub's SHA-256 checksum.
    /// </summary>
    public static class Update
    {
        public const string RepositoryUrl = "https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor";
        public const string ReleasesUrl = RepositoryUrl + "/releases/latest";
        private const string ApiUrl = "https://api.github.com/repos/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/latest";
        public const string MacAppName = "Umami 3DS Theme Editor.app";

        public static Version AppVersion
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(2, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static Version UpdateVersion { get; private set; } = AppVersion;

        /// <summary>Whether the last check could not reach GitHub (so nothing is known about updates).</summary>
        public static bool CheckFailed { get; private set; }

        /// <summary>The newest release, once <see cref="CheckUpdateAvailable" /> found one.</summary>
        public static ReleaseInfo Latest { get; private set; }

        public static async Task<bool> CheckUpdateAvailable()
        {
            var latest = await LatestRelease();
            CheckFailed = latest == null;
            if (latest != null)
            {
                Latest = latest;
                UpdateVersion = latest.Version;
            }
            return latest != null && latest.Version > AppVersion;
        }

        public static async Task<Version> CheckLatestVersion()
        {
            return (await LatestRelease())?.Version;
        }

        private static HttpClient Http(HttpMessageHandler handler, TimeSpan timeout)
        {
            var http = handler == null ? new HttpClient() : new HttpClient(handler, false);
            http.Timeout = timeout;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("UmamiThemeEditor/" + AppVersion);
            return http;
        }

        private const string LatestPage = RepositoryUrl + "/releases/latest";
        private const string DownloadBase = RepositoryUrl + "/releases/download/";

        /// <summary>The files a release has, by the end of their name, for when only the release page can be read.</summary>
        private static readonly string[] AssetSuffixes =
            { "win-x64-setup.exe", "win-arm64-setup.exe", "osx-x64.dmg", "osx-arm64.dmg" };

        /// <summary>
        ///     The newest release on GitHub, or null when it cannot be reached. GitHub's API (which knows every file and its
        ///     checksum) allows few requests without an account, so when it refuses, the release page tells the version and
        ///     the checksum files published next to each download stand in.
        /// </summary>
        public static async Task<ReleaseInfo> LatestRelease(HttpMessageHandler handler = null)
        {
            return await FromApi(handler) ?? await FromReleasePage(handler);
        }

        private static async Task<ReleaseInfo> FromReleasePage(HttpMessageHandler handler)
        {
            try
            {
                // github.com/.../releases/latest answers with a redirect to .../releases/tag/v1.2.3
                var noRedirect = handler ?? new HttpClientHandler { AllowAutoRedirect = false };
                using (var http = Http(noRedirect, TimeSpan.FromSeconds(8)))
                using (var response = await http.GetAsync(LatestPage, HttpCompletionOption.ResponseHeadersRead))
                {
                    var location = response.Headers.Location?.ToString() ?? string.Empty;
                    var tag = location.Substring(location.LastIndexOf('/') + 1);
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                        return null;
                    var release = new ReleaseInfo { Version = version };
                    foreach (var suffix in AssetSuffixes)
                    {
                        var name = "Umami-3DS-Theme-Editor-" + tag.TrimStart('v', 'V') + "-" + suffix;
                        var url = DownloadBase + tag + "/" + name;
                        release.Assets.Add(new ReleaseAsset { Name = name, Url = url, ChecksumUrl = url + ".sha256" });
                    }
                    return release;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<ReleaseInfo> FromApi(HttpMessageHandler handler)
        {
            try
            {
                using (var http = Http(handler, TimeSpan.FromSeconds(8)))
                {
                    var json = JObject.Parse(await http.GetStringAsync(ApiUrl));
                    var tag = ((string) json["tag_name"] ?? string.Empty).TrimStart('v', 'V');
                    if (!Version.TryParse(tag, out var version))
                        return null;
                    var release = new ReleaseInfo { Version = version };
                    foreach (var a in json["assets"] ?? new JArray())
                        release.Assets.Add(new ReleaseAsset
                        {
                            Name = (string) a["name"],
                            Url = (string) a["browser_download_url"],
                            Size = (long?) a["size"] ?? 0,
                            Digest = (string) a["digest"],
                            ChecksumUrl = (string) a["browser_download_url"] + ".sha256"
                        });
                    return release;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- How this copy was installed

        public static InstallKind Kind => KindOf(AppContext.BaseDirectory, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

        /// <summary>
        ///     The setup leaves its uninstaller next to the editor; a macOS app runs from inside its bundle. Anything else is
        ///     a portable or Linux copy (or a development build), which the user updates by hand.
        /// </summary>
        public static InstallKind KindOf(string baseDirectory, bool windows, bool mac)
        {
            if (windows && File.Exists(Path.Combine(baseDirectory, "unins000.exe")))
                return InstallKind.WindowsInstaller;
            if (mac && MacBundleOf(baseDirectory) != null)
                return InstallKind.MacApp;
            return InstallKind.Manual;
        }

        /// <summary>The .app bundle the editor runs from (…/Name.app/Contents/MacOS/), or null.</summary>
        public static string MacBundleOf(string baseDirectory)
        {
            var macOs = new DirectoryInfo(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var contents = macOs.Parent;
            var bundle = contents?.Parent;
            return macOs.Name == "MacOS" && contents?.Name == "Contents" && bundle != null &&
                   bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                ? bundle.FullName
                : null;
        }

        /// <summary>The release file that updates this kind of install on this processor, or null.</summary>
        public static ReleaseAsset AssetFor(ReleaseInfo release, InstallKind kind, Architecture arch)
        {
            var cpu = arch == Architecture.Arm64 ? "arm64" : "x64";
            var suffix = kind == InstallKind.WindowsInstaller ? "-win-" + cpu + "-setup.exe"
                : kind == InstallKind.MacApp ? "-osx-" + cpu + ".dmg"
                : null;
            if (release == null || suffix == null)
                return null;
            return release.Assets.Find(a => a.Name != null && a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------- Downloading and installing

        /// <summary>
        ///     Downloads <paramref name="asset" /> into <paramref name="folder" />, checks it against GitHub's checksum and
        ///     returns its path. <paramref name="progress" /> gets 0..1.
        /// </summary>
        public static async Task<string> Download(ReleaseAsset asset, string folder, IProgress<double> progress, CancellationToken cancel,
            HttpMessageHandler handler = null)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Path.GetFileName(asset.Name));
            try
            {
                var expected = asset.Digest ?? await PublishedChecksum(asset, handler, cancel);
                string digest;
                using (var http = Http(handler, Timeout.InfiniteTimeSpan))
                using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? asset.Size;
                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var file = File.Create(path))
                    using (var sha = SHA256.Create())
                    {
                        var buffer = new byte[1 << 16];
                        long done = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0)
                        {
                            await file.WriteAsync(buffer, 0, read, cancel);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                            if (total > 0)
                                progress?.Report(Math.Min(1.0, (double) done / total));
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                        digest = "sha256:" + Convert.ToHexString(sha.Hash).ToLowerInvariant();
                    }
                }
                if (expected == null || !string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update download is damaged (checksum mismatch). Please try again.");
                return path;
            }
            catch (Exception)
            {
                try { File.Delete(path); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>The checksum from the file published next to the download ("hex  name"), as "sha256:hex", or null.</summary>
        private static async Task<string> PublishedChecksum(ReleaseAsset asset, HttpMessageHandler handler, CancellationToken cancel)
        {
            if (asset.ChecksumUrl == null)
                return null;
            try
            {
                using (var http = Http(handler, TimeSpan.FromSeconds(15)))
                using (var response = await http.GetAsync(asset.ChecksumUrl, cancel))
                {
                    if (!response.IsSuccessStatusCode)
                        return null;
                    var hex = (await response.Content.ReadAsStringAsync()).Trim().Split(' ', '\t')[0];
                    return hex.Length == 64 ? "sha256:" + hex.ToLowerInvariant() : null;
                }
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>
        ///     The arguments for the new setup: silent, closing the editor if it still runs, for the same kind of install
        ///     (just this user, or everyone when it lives in Program Files).
        /// </summary>
        public static string SetupArguments(string baseDirectory)
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var forEveryone = !string.IsNullOrEmpty(programFiles) &&
                              baseDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase);
            return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS " + (forEveryone ? "/ALLUSERS" : "/CURRENTUSER");
        }

        /// <summary>
        ///     The shell script that swaps the macOS app: it waits for the editor (<paramref name="pid" />) to quit, copies the
        ///     app out of the .dmg next to the old one, replaces it and starts it again. If anything fails it opens the .dmg
        ///     instead, so the user can drag the app over by hand.
        /// </summary>
        /// <param name="open">False runs the swap without starting the app or opening the .dmg afterwards (for tests).</param>
        public static string MacUpdateScript(string dmg, string bundle, int pid, bool open = true)
        {
            string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
            return string.Join("\n",
                "#!/bin/bash",
                "open() { " + (open ? "/usr/bin/open \"$@\"" : ":") + "; }",
                "DMG=" + Q(dmg),
                "APP=" + Q(bundle),
                "PID=" + pid,
                "for i in $(seq 1 120); do kill -0 \"$PID\" 2>/dev/null || break; sleep 0.5; done",
                "MNT=$(mktemp -d)",
                "fail() { hdiutil detach \"$MNT\" -quiet >/dev/null 2>&1; open \"$DMG\"; exit 1; }",
                "hdiutil attach \"$DMG\" -nobrowse -readonly -noautoopen -mountpoint \"$MNT\" -quiet || fail",
                "rm -rf \"$APP.new\"",
                "ditto \"$MNT/" + MacAppName + "\" \"$APP.new\" || fail",
                "hdiutil detach \"$MNT\" -quiet",
                "rm -rf \"$APP.old\"",
                "mv \"$APP\" \"$APP.old\" && mv \"$APP.new\" \"$APP\" || { mv \"$APP.old\" \"$APP\" 2>/dev/null; open \"$DMG\"; exit 1; }",
                "rm -rf \"$APP.old\" \"$DMG\"",
                "open \"$APP\"",
                "");
        }

        /// <summary>Whether the folder holding the app bundle can be written (otherwise the .dmg is opened for the user).</summary>
        public static bool CanReplace(string bundle)
        {
            try
            {
                var parent = Path.GetDirectoryName(bundle.TrimEnd('/'));
                var probe = Path.Combine(parent ?? ".", ".umami-update-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Starts installing the downloaded update; the caller then quits the editor, which the installer waits for.
        ///     Returns false when it could only open the download for the user (the app folder is not writable).
        /// </summary>
        public static bool Install(string download, InstallKind kind)
        {
            if (kind == InstallKind.WindowsInstaller)
            {
                Process.Start(new ProcessStartInfo(download, SetupArguments(AppContext.BaseDirectory)) { UseShellExecute = true });
                return true;
            }
            var bundle = MacBundleOf(AppContext.BaseDirectory);
            if (bundle == null || !CanReplace(bundle))
            {
                OpenUrl(download);
                return false;
            }
            var script = Path.Combine(Path.GetDirectoryName(download) ?? Path.GetTempPath(), "update.sh");
            File.WriteAllText(script, MacUpdateScript(download, bundle, Environment.ProcessId));
            // Started detached, so it keeps running after the editor quits
            Process.Start(new ProcessStartInfo("/bin/bash") { ArgumentList = { "-c", "nohup /bin/bash \"$0\" >/dev/null 2>&1 &", script }, UseShellExecute = false });
            return true;
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
