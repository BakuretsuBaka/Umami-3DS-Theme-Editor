// --------------------------------------------------
// Umami 3DS Theme Editor - LibraryPack.cs
// --------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App.Library
{
    /// <summary>The resource pack as published on GitHub.</summary>
    public sealed class PackRelease
    {
        public string Url;
        public long Size;
        /// <summary>"sha256:..." as GitHub reports it, or null when it does not.</summary>
        public string Digest;
    }

    /// <summary>
    ///     The resource library's ready-made textures, sound effects and music: a separate download (the "library-pack"
    ///     release on GitHub) installed into the settings folder, so the editor itself stays small, updates keep the pack,
    ///     and it can be removed again, on macOS too.
    /// </summary>
    public sealed class LibraryPack
    {
        public const string ReleaseApi = "https://api.github.com/repos/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/tags/library-pack";
        public const string AssetName = "library-pack.zip";
        private const string InfoName = "pack.json";

        public static LibraryPack Default { get; } = new LibraryPack(Path.Combine(Settings.SettingsDirectory, "LibraryPack"));

        private readonly HttpMessageHandler _handler;

        /// <summary>Where the pack is installed.</summary>
        public string Root { get; }

        public LibraryPack(string root, HttpMessageHandler handler = null)
        {
            Root = root;
            _handler = handler;
        }

        public bool IsInstalled => File.Exists(Path.Combine(Root, "index.json"));

        /// <summary>The checksum of the installed pack (null when unknown or not installed).</summary>
        public string InstalledDigest
        {
            get
            {
                try
                {
                    var path = Path.Combine(Root, InfoName);
                    return File.Exists(path) ? (string) JObject.Parse(File.ReadAllText(path))["digest"] : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public bool IsUpdate(PackRelease latest)
        {
            return IsInstalled && latest?.Digest != null && !string.Equals(InstalledDigest, latest.Digest, StringComparison.OrdinalIgnoreCase);
        }

        private HttpClient Http()
        {
            var http = _handler == null ? new HttpClient() : new HttpClient(_handler, false);
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("UmamiThemeEditor/" + Update.AppVersion);
            return http;
        }

        /// <summary>The pack currently published, or null when GitHub cannot be reached.</summary>
        public async Task<PackRelease> Latest()
        {
            try
            {
                using (var http = Http())
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var json = JObject.Parse(await (await http.GetAsync(ReleaseApi, cts.Token)).EnsureSuccessStatusCode()
                        .Content.ReadAsStringAsync());
                    foreach (var asset in json["assets"] ?? new JArray())
                        if ((string) asset["name"] == AssetName)
                            return new PackRelease
                            {
                                Url = (string) asset["browser_download_url"],
                                Size = (long?) asset["size"] ?? 0,
                                Digest = (string) asset["digest"]
                            };
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        ///     Downloads <paramref name="release" />, checks it against its checksum and installs it in place of the current
        ///     pack. Nothing changes when it fails or is cancelled. <paramref name="progress" /> gets 0..1.
        /// </summary>
        public async Task Install(PackRelease release, IProgress<double> progress, CancellationToken token)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(Root)) ?? ".";
            Directory.CreateDirectory(parent);
            var download = Root + ".download";
            var staging = Root + ".new";
            try
            {
                string digest;
                using (var http = Http())
                using (var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? release.Size;
                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var file = File.Create(download))
                    using (var sha = SHA256.Create())
                    {
                        var buffer = new byte[1 << 16];
                        long done = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await file.WriteAsync(buffer, 0, read, token);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                            if (total > 0)
                                progress?.Report(Math.Min(1.0, (double) done / total));
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                        digest = "sha256:" + Convert.ToHexString(sha.Hash).ToLowerInvariant();
                    }
                }
                if (release.Digest != null && !string.Equals(digest, release.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The download is damaged (checksum mismatch). Please try again.");

                // Unpack next to the old pack first, so a broken download never replaces a working one
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
                await Task.Run(() => ZipFile.ExtractToDirectory(download, staging), token);
                if (!File.Exists(Path.Combine(staging, "index.json")))
                    throw new InvalidDataException("The download is not a resource pack.");
                File.WriteAllText(Path.Combine(staging, InfoName), JsonConvert.SerializeObject(new
                {
                    digest,
                    size = new FileInfo(download).Length,
                    installed = DateTime.UtcNow
                }));
                if (Directory.Exists(Root))
                    Directory.Delete(Root, true);
                Directory.Move(staging, Root);
            }
            finally
            {
                TryDelete(download);
                if (Directory.Exists(staging))
                    try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Deletes the installed pack.</summary>
        public void Remove()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
