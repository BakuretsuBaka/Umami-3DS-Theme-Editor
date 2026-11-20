using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ThemeEditor.App.Library;
using Xunit;

namespace ThemeEditor.Tests
{
    public class LibraryPackTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "umami-pack-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        /// <summary>Stands in for GitHub: the release API and the pack download.</summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public byte[] Zip;
            public string Digest;
            public int Downloads;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.RequestUri.ToString() == LibraryPack.ReleaseApi)
                {
                    var json = "{\"assets\":[{\"name\":\"library-pack.zip\",\"size\":" + Zip.Length + ",\"digest\":" +
                               (Digest == null ? "null" : "\"" + Digest + "\"") + ",\"browser_download_url\":\"https://example.invalid/pack.zip\"}]}";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(json)});
                }
                Downloads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(Zip)});
            }
        }

        private static byte[] PackZip(string marker = "a")
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    using (var w = new StreamWriter(zip.CreateEntry("index.json").Open()))
                        w.Write("{\"Items\":[],\"Sources\":[\"" + marker + "\"]}");
                    using (var w = new StreamWriter(zip.CreateEntry("textures/x.png").Open()))
                        w.Write(marker);
                }
                return ms.ToArray();
            }
        }

        private static string Sha(byte[] data) => "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        [Fact]
        public async Task ThePackIsDownloadedCheckedAndInstalled()
        {
            var zip = PackZip();
            var github = new FakeGitHub {Zip = zip, Digest = Sha(zip)};
            var pack = new LibraryPack(Path.Combine(_dir, "LibraryPack"), github);
            Assert.False(pack.IsInstalled);

            var latest = await pack.Latest();
            Assert.Equal(zip.Length, latest.Size);
            var reported = new List<double>();
            await pack.Install(latest, new Progress<double>(reported.Add), CancellationToken.None);

            Assert.True(pack.IsInstalled);
            Assert.True(File.Exists(Path.Combine(pack.Root, "textures", "x.png")));
            Assert.Equal(Sha(zip), pack.InstalledDigest);
            Assert.False(pack.IsUpdate(latest));
            // No leftovers next to the pack
            Assert.Single(Directory.GetFileSystemEntries(_dir));
        }

        [Fact]
        public async Task ADamagedDownloadChangesNothing()
        {
            var good = PackZip("old");
            var github = new FakeGitHub {Zip = good, Digest = Sha(good)};
            var pack = new LibraryPack(Path.Combine(_dir, "LibraryPack"), github);
            await pack.Install(await pack.Latest(), null, CancellationToken.None);

            // A newer pack is announced, but the bytes that arrive do not match its checksum
            github.Digest = Sha(PackZip("new"));
            github.Zip = PackZip("tampered");
            var latest = await pack.Latest();
            Assert.True(pack.IsUpdate(latest));
            await Assert.ThrowsAsync<InvalidDataException>(() => pack.Install(latest, null, CancellationToken.None));

            Assert.Equal("old", File.ReadAllText(Path.Combine(pack.Root, "textures", "x.png")));
            Assert.Equal(Sha(good), pack.InstalledDigest);
            Assert.Single(Directory.GetFileSystemEntries(_dir));
        }

        [Fact]
        public async Task AZipThatIsNoPackIsRefusedAndRemoveDeletesThePack()
        {
            byte[] notAPack;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                using (var w = new StreamWriter(zip.CreateEntry("readme.txt").Open()))
                    w.Write("hello");
                notAPack = ms.ToArray();
            }
            var github = new FakeGitHub {Zip = notAPack, Digest = Sha(notAPack)};
            var pack = new LibraryPack(Path.Combine(_dir, "LibraryPack"), github);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await pack.Install(await pack.Latest(), null, CancellationToken.None));
            Assert.False(pack.IsInstalled);

            github.Zip = PackZip();
            github.Digest = Sha(github.Zip);
            await pack.Install(await pack.Latest(), null, CancellationToken.None);
            Assert.True(pack.IsInstalled);
            pack.Remove();
            Assert.False(pack.IsInstalled);
            Assert.False(Directory.Exists(pack.Root));
        }

        [Fact]
        public async Task WithoutGitHubThereIsNoUpdateOffer()
        {
            var pack = new LibraryPack(Path.Combine(_dir, "LibraryPack"), new FailingHandler());
            Assert.Null(await pack.Latest());
            Assert.False(pack.IsUpdate(null));
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                throw new HttpRequestException("offline");
        }

        [Fact]
        public void BrightnessKeepsTheTextureReadable()
        {
            using (var src = new SKBitmap(3, 1, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                src.SetPixel(0, 0, new SKColor(0, 0, 0));
                src.SetPixel(1, 0, new SKColor(128, 128, 128));
                src.SetPixel(2, 0, new SKColor(255, 255, 255));
                using (var light = TextureRecolor.Apply(src, 0, 1, 0.4f))
                using (var dark = TextureRecolor.Apply(src, 0, 1, -0.4f))
                {
                    // Lighter blends towards white: dark parts lift, light ones do not clip, the order stays
                    Assert.InRange(light.GetPixel(0, 0).Red, 95, 110);
                    Assert.InRange(light.GetPixel(1, 0).Red, 170, 185);
                    Assert.Equal(255, light.GetPixel(2, 0).Red);
                    // Darker scales towards black: black stays black, mid grey does not vanish
                    Assert.Equal(0, dark.GetPixel(0, 0).Red);
                    Assert.InRange(dark.GetPixel(1, 0).Red, 70, 85);
                    Assert.InRange(dark.GetPixel(2, 0).Red, 145, 160);
                }
            }
        }
    }
}
