using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Newtonsoft.Json.Linq;
using ThemeEditor.App;
using Xunit;

namespace ThemeEditor.Tests
{
    public class RemoteInstallTests
    {
        /// <summary>Themezer's remote install: start (where to put the file), the file storage, finish.</summary>
        private sealed class FakeInstall : HttpMessageHandler
        {
            public readonly List<JObject> Operations = new List<JObject>();
            public byte[] Stored;
            public string StoredMd5, StoredType, StoreAuth;
            public string Username;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.Method == HttpMethod.Put)
                {
                    Stored = await request.Content.ReadAsByteArrayAsync();
                    StoredMd5 = Convert.ToBase64String(request.Content.Headers.ContentMD5 ?? new byte[0]);
                    StoredType = request.Content.Headers.ContentType?.MediaType;
                    StoreAuth = request.Headers.Authorization?.ToString();
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                var op = JObject.Parse(await request.Content.ReadAsStringAsync());
                Operations.Add(op);
                var q = (string) op["query"];
                string answer;
                if (q.Contains("auth"))
                    answer = "{\"data\":{\"auth\":{\"me\":{\"username\":\"" + Username + "\",\"uuid\":\"u\"}}}}";
                else if (q.Contains("initializeRemoteInstallTheme"))
                    answer = "{\"data\":{\"threeDs\":{\"theme\":{\"initializeRemoteInstallTheme\":{\"uuid\":\"11111111-1111-1111-1111-111111111111\"," +
                             "\"uploadUrl\":\"https://storage.example/put\",\"uploadContentType\":\"application/zip\",\"uploadContentMd5\":\"" +
                             (string) op["variables"]["d"]["expectedContentMd5"] + "\"}}}}}";
                else
                    answer = "{\"data\":{\"threeDs\":{\"theme\":{\"completeRemoteInstallTheme\":{\"uuid\":\"11111111-1111-1111-1111-111111111111\"," +
                             "\"code\":\"F4OIA\",\"quickId\":\"RF4OIA\",\"downloadUrl\":\"https://api.themezer.net/3ds/remote-install-content/1111/download\"}}}}}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
            }
        }

        [Fact]
        public async Task TheThemeIsPutOnThemezerAndItsLinkComesBack()
        {
            var fake = new FakeInstall();
            var client = new ThemezerClient(fake);
            var zip = new byte[] { (byte) 'P', (byte) 'K', 3, 4, 9, 9 };
            var install = await client.RemoteInstall("Pink Clouds", zip);

            Assert.Equal("https://api.themezer.net/3ds/remote-install-content/1111/download", install.DownloadUrl);
            Assert.Equal("F4OIA", install.Code);
            var start = fake.Operations.Single(o => ((string) o["query"]).Contains("initialize"))["variables"]["d"];
            // Signed out, Themezer itself is the author
            Assert.Equal("Themezer", (string) start["declaredAuthor"]);
            Assert.Equal(zip.Length, (int) start["expectedContentLength"]);
            string md5;
            using (var hash = MD5.Create())
                md5 = Convert.ToBase64String(hash.ComputeHash(zip));
            Assert.Equal(md5, (string) start["expectedContentMd5"]);
            // The file went to the storage as it was announced, without the sign-in
            Assert.Equal(zip, fake.Stored);
            Assert.Equal(md5, fake.StoredMd5);
            Assert.Equal("application/zip", fake.StoredType);
            Assert.Null(fake.StoreAuth);
            Assert.Equal("11111111-1111-1111-1111-111111111111", (string) fake.Operations.Last()["variables"]["u"]);

            // Signed in, the user is the author
            var signedIn = new FakeInstall { Username = "Alteration" };
            var other = new ThemezerClient(signedIn);
            await other.Use("tz_oat_test");
            await other.RemoteInstall("Pink Clouds", zip);
            Assert.Equal("Alteration", (string) signedIn.Operations.Single(o => ((string) o["query"]).Contains("initialize"))["variables"]["d"]["declaredAuthor"]);
        }

        [AvaloniaFact]
        public async Task TheWindowShowsAQrCodeOfTheDownload()
        {
            var window = new RemoteInstallWindow(new ThemezerClient(new FakeInstall()), "Pink Clouds", () => Task.FromResult(new byte[] { 1, 2, 3 }));
            window.Show();
            await window.Running;
            var image = window.GetLogicalDescendants().OfType<Image>().Single();
            Assert.True(image.IsVisible);
            Assert.NotNull(image.Source);
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBox>(), t => t.Text == window.Install.DownloadUrl);
            window.Close();
        }
    }
}
