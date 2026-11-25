using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ThemeEditor.App;
using Xunit;

namespace ThemeEditor.Tests
{
    public class UpdateTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "umami-update-" + Guid.NewGuid().ToString("N"));

        public UpdateTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private static string Sha(byte[] data) => "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        /// <summary>Stands in for GitHub: the latest release and its files.</summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public byte[] File = Encoding.ASCII.GetBytes("new setup");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/releases/latest"))
                {
                    var assets = string.Join(",", new[] {"win-x64-setup.exe", "win-arm64-setup.exe", "win-x64-portable.zip", "osx-arm64.dmg", "osx-x64.dmg", "linux-x64.tar.gz"}
                        .Select(n => "{\"name\":\"Umami-3DS-Theme-Editor-9.1.0-" + n + "\",\"size\":9,\"digest\":\"" + Sha(File) +
                                     "\",\"browser_download_url\":\"https://example.invalid/" + n + "\"}"));
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {Content = new StringContent("{\"tag_name\":\"v9.1.0\",\"assets\":[" + assets + "]}")});
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(File)});
            }
        }

        [Fact]
        public void TheInstallKindFollowsWhereTheEditorRuns()
        {
            var installed = Path.Combine(_dir, "Programs", "Umami 3DS Theme Editor");
            Directory.CreateDirectory(installed);
            Assert.Equal(InstallKind.Manual, Update.KindOf(installed, true, false));
            File.WriteAllText(Path.Combine(installed, "unins000.exe"), "");
            Assert.Equal(InstallKind.WindowsInstaller, Update.KindOf(installed, true, false));

            var macOs = Path.Combine(_dir, "Applications", "Umami 3DS Theme Editor.app", "Contents", "MacOS") + "/";
            Assert.Equal(InstallKind.MacApp, Update.KindOf(macOs, false, true));
            Assert.Equal(Path.Combine(_dir, "Applications", "Umami 3DS Theme Editor.app"), Update.MacBundleOf(macOs));
            Assert.Equal(InstallKind.Manual, Update.KindOf(Path.Combine(_dir, "bin", "Debug"), false, true));
            Assert.Equal(InstallKind.Manual, Update.KindOf(installed, false, false));
        }

        [Fact]
        public async Task TheRightFileIsPickedForThisInstallAndProcessor()
        {
            var release = await Update.LatestRelease(new FakeGitHub());
            Assert.Equal(new Version(9, 1, 0), release.Version);
            Assert.EndsWith("-win-x64-setup.exe", Update.AssetFor(release, InstallKind.WindowsInstaller, Architecture.X64).Name);
            Assert.EndsWith("-win-arm64-setup.exe", Update.AssetFor(release, InstallKind.WindowsInstaller, Architecture.Arm64).Name);
            Assert.EndsWith("-osx-arm64.dmg", Update.AssetFor(release, InstallKind.MacApp, Architecture.Arm64).Name);
            Assert.EndsWith("-osx-x64.dmg", Update.AssetFor(release, InstallKind.MacApp, Architecture.X64).Name);
            Assert.Null(Update.AssetFor(release, InstallKind.Manual, Architecture.X64));
        }

        [Fact]
        public async Task OnlyADownloadMatchingItsChecksumIsKept()
        {
            var github = new FakeGitHub();
            var release = await Update.LatestRelease(github);
            var asset = Update.AssetFor(release, InstallKind.WindowsInstaller, Architecture.X64);
            var path = await Update.Download(asset, _dir, null, CancellationToken.None, github);
            Assert.Equal(github.File, File.ReadAllBytes(path));

            github.File = Encoding.ASCII.GetBytes("tampered");
            await Assert.ThrowsAsync<InvalidDataException>(() => Update.Download(asset, Path.Combine(_dir, "2"), null, CancellationToken.None, github));
            Assert.False(File.Exists(Path.Combine(_dir, "2", asset.Name)));

            // Without a checksum to compare with, nothing is installed either
            asset.Digest = null;
            await Assert.ThrowsAsync<InvalidDataException>(() => Update.Download(asset, Path.Combine(_dir, "3"), null, CancellationToken.None, github));
        }

        /// <summary>GitHub with its API out of requests: only the release page and the files themselves answer.</summary>
        private sealed class RateLimitedGitHub : HttpMessageHandler
        {
            public byte[] File = Encoding.ASCII.GetBytes("new dmg");
            public bool PublishChecksums = true;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var url = request.RequestUri.ToString();
                if (url.StartsWith("https://api.github.com/"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) {Content = new StringContent("{\"message\":\"API rate limit exceeded\"}")});
                if (url.EndsWith("/releases/latest"))
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/tag/v9.2.0");
                    return Task.FromResult(redirect);
                }
                if (url.EndsWith(".sha256"))
                    return Task.FromResult(PublishChecksums
                        ? new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(Sha(File).Substring(7) + "  " + url.Split('/').Last().Replace(".sha256", "") + "\n")}
                        : new HttpResponseMessage(HttpStatusCode.NotFound));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(File)});
            }
        }

        [Fact]
        public async Task WithoutTheApiTheReleasePageAndPublishedChecksumsAreUsed()
        {
            var github = new RateLimitedGitHub();
            var release = await Update.LatestRelease(github);
            Assert.Equal(new Version(9, 2, 0), release.Version);
            var asset = Update.AssetFor(release, InstallKind.MacApp, Architecture.X64);
            Assert.Equal("https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/download/v9.2.0/Umami-3DS-Theme-Editor-9.2.0-osx-x64.dmg", asset.Url);

            var path = await Update.Download(asset, _dir, null, CancellationToken.None, github);
            Assert.Equal(github.File, File.ReadAllBytes(path));

            // A release without checksum files cannot be verified, so it is not installed
            github.PublishChecksums = false;
            await Assert.ThrowsAsync<InvalidDataException>(() => Update.Download(asset, Path.Combine(_dir, "2"), null, CancellationToken.None, github));
        }

        [Fact]
        public void TheSetupRunsSilentlyForTheSameKindOfInstall()
        {
            var args = Update.SetupArguments(Path.Combine(_dir, "AppData", "Local", "Programs", "Umami 3DS Theme Editor"));
            Assert.Contains("/VERYSILENT", args);
            Assert.Contains("/CLOSEAPPLICATIONS", args);
            Assert.Contains("/CURRENTUSER", args);
        }

        /// <summary>Swaps a real app bundle for the one in a real .dmg, as the update does on macOS.</summary>
        [Fact]
        public void OnMacTheAppIsSwappedForTheOneInTheDmg()
        {
            if (!OperatingSystem.IsMacOS())
                return;
            // The installed app, and the new one packed into a .dmg as the release builds it
            var apps = Path.Combine(_dir, "Applications 'n' more");
            var bundle = Path.Combine(apps, Update.MacAppName);
            Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
            File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "version.txt"), "old");
            File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "only-in-old.txt"), "gone after the update");
            var stage = Path.Combine(_dir, "stage");
            Directory.CreateDirectory(Path.Combine(stage, Update.MacAppName, "Contents", "MacOS"));
            File.WriteAllText(Path.Combine(stage, Update.MacAppName, "Contents", "MacOS", "version.txt"), "new");
            var dmg = Path.Combine(_dir, "update.dmg");
            Run("hdiutil", "create", "-quiet", "-volname", "Umami 3DS Theme Editor", "-srcfolder", stage, "-format", "UDZO", dmg);

            // The script waits for the editor to quit: here a process that has already ended
            var gone = Process.Start(new ProcessStartInfo("/usr/bin/true"));
            gone.WaitForExit();
            var script = Path.Combine(_dir, "update.sh");
            File.WriteAllText(script, Update.MacUpdateScript(dmg, bundle, gone.Id, false));
            Run("/bin/bash", script);

            Assert.Equal("new", File.ReadAllText(Path.Combine(bundle, "Contents", "MacOS", "version.txt")));
            Assert.False(File.Exists(Path.Combine(bundle, "Contents", "MacOS", "only-in-old.txt")));
            Assert.False(Directory.Exists(bundle + ".old"));
            Assert.False(Directory.Exists(bundle + ".new"));
            Assert.False(File.Exists(dmg));
        }

        private static void Run(string file, params string[] args)
        {
            var info = new ProcessStartInfo(file) {RedirectStandardOutput = true, RedirectStandardError = true};
            foreach (var a in args)
                info.ArgumentList.Add(a);
            using (var p = Process.Start(info))
            {
                var err = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                Assert.True(p.WaitForExit(120000), file + " did not finish");
                Assert.True(p.ExitCode == 0, file + " failed: " + err.Result);
            }
        }
    }
}
