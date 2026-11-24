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
using ThemeEditor.App.Localization;
using Xunit;

namespace ThemeEditor.Tests
{
    public class ThemezerTests
    {
        /// <summary>
        ///     Stands in for Themezer: the GraphQL API (records each operation and answers with canned JSON) and the OAuth
        ///     token and revoke endpoints.
        /// </summary>
        private sealed class FakeThemezer : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Requests = new List<HttpRequestMessage>();
            public readonly List<JObject> Operations = new List<JObject>();
            public readonly List<Dictionary<string, (string FileName, byte[] Data)>> Parts = new List<Dictionary<string, (string, byte[])>>();
            public readonly List<Dictionary<string, string>> Forms = new List<Dictionary<string, string>>();
            public Func<JObject, string> Answer = op => Me;
            public Func<Dictionary<string, string>, string> TokenAnswer = form => "{\"access_token\":\"tz_oat_test\",\"token_type\":\"Bearer\"}";
            public HttpStatusCode Status = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests.Add(request);
                var url = request.RequestUri.ToString();
                if (url == ThemezerClient.TokenUrl || url == ThemezerClient.RevokeUrl)
                {
                    var form = (await request.Content.ReadAsStringAsync()).Split('&').Select(p => p.Split('='))
                        .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
                    Forms.Add(form);
                    var tokenAnswer = url == ThemezerClient.TokenUrl ? TokenAnswer(form) : "{}";
                    var tokenStatus = tokenAnswer.Contains("\"error\"") ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
                    return new HttpResponseMessage(tokenStatus) {Content = new StringContent(tokenAnswer, Encoding.UTF8, "application/json")};
                }

                var parts = new Dictionary<string, (string, byte[])>();
                JObject operation;
                if (request.Content is MultipartFormDataContent multipart)
                {
                    foreach (var part in multipart)
                    {
                        var name = part.Headers.ContentDisposition.Name.Trim('"');
                        parts[name] = (part.Headers.ContentDisposition.FileName?.Trim('"'), await part.ReadAsByteArrayAsync());
                    }
                    operation = JObject.Parse(Encoding.UTF8.GetString(parts["operations"].Item2));
                }
                else
                    operation = JObject.Parse(await request.Content.ReadAsStringAsync());
                Operations.Add(operation);
                Parts.Add(parts);
                return new HttpResponseMessage(Status) {Content = new StringContent(Answer(operation), Encoding.UTF8, "application/json")};
            }
        }

        /// <summary>A password vault in memory.</summary>
        private sealed class MemoryVault : ICredentialStore
        {
            public readonly Dictionary<string, string> Secrets = new Dictionary<string, string>();
            public int Reads, Writes;
            public bool IsAvailable => true;
            public string Read(string account) { Reads++; return Secrets.TryGetValue(account, out var s) ? s : null; }
            public bool Write(string account, string secret) { Writes++; Secrets[account] = secret; return true; }
            public void Delete(string account) => Secrets.Remove(account);
        }

        private const string Me = "{\"data\":{\"auth\":{\"me\":{\"username\":\"Tester\"}}}}";
        private const string NotSignedIn = "{\"errors\":[{\"message\":\"Not authenticated\",\"extensions\":{\"code\":\"NOT_AUTHENTICATED\"}}]}";
        private const string Created = "{\"data\":{\"threeDs\":{\"theme\":{\"createTheme\":" +
                                       "{\"hexId\":\"1A2B3\",\"name\":\"Pink Clouds\",\"pageUrl\":\"https://themezer.net/3ds/themes/pink-clouds-1A2B3\"}}}}}";

        private static Dictionary<string, string> Query(string url) =>
            new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('='))
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        /// <summary>A browser that does what Themezer's consent page does once the user allows the app (or refuses).</summary>
        private static Action<string> Browser(Func<Dictionary<string, string>, string> answer, List<string> opened = null)
        {
            return url =>
            {
                opened?.Add(url);
                var q = Query(url);
                _ = Task.Run(async () =>
                {
                    using (var http = new HttpClient())
                    {
                        // Something else knocking first (a favicon) is not the answer
                        await http.GetAsync(q["redirect_uri"].Replace("/callback", "/favicon.ico"));
                        await http.GetAsync(q["redirect_uri"] + "?" + answer(q));
                    }
                });
            };
        }

        private static async Task<(ThemezerClient, FakeThemezer)> SignedIn()
        {
            var fake = new FakeThemezer();
            var client = new ThemezerClient(fake);
            await client.Use("tz_oat_test");
            return (client, fake);
        }

        [Fact]
        public async Task SigningInRunsTheOAuthFlowWithPkce()
        {
            var fake = new FakeThemezer();
            var client = new ThemezerClient(fake);
            var opened = new List<string>();
            var name = await client.SignIn(Browser(q => "code=the-code&state=" + q["state"], opened), CancellationToken.None);

            Assert.Equal("Tester", name);
            Assert.Equal("tz_oat_test", client.Token);
            var url = opened.Single();
            Assert.StartsWith(ThemezerClient.AuthorizeUrl + "?", url);
            var q = Query(url);
            Assert.Equal("code", q["response_type"]);
            Assert.Equal("umami-theme-editor", q["client_id"]);
            Assert.Equal("profile:read content:write", q["scope"]);
            Assert.Equal("S256", q["code_challenge_method"]);
            Assert.Matches(@"^http://127\.0\.0\.1:\d+/callback$", q["redirect_uri"]);

            // The exchange proves the verifier behind the challenge, from the same redirect
            var form = fake.Forms.Single();
            Assert.Equal("authorization_code", form["grant_type"]);
            Assert.Equal("the-code", form["code"]);
            Assert.Equal("umami-theme-editor", form["client_id"]);
            Assert.Equal(q["redirect_uri"], form["redirect_uri"]);
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(q["code_challenge"], challenge);

            // And the API is then called with the token
            Assert.Equal("Bearer", fake.Requests.Last().Headers.Authorization.Scheme);
            Assert.Equal("tz_oat_test", fake.Requests.Last().Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task RefusingInTheBrowserOrAnotherStateDoesNotSignIn()
        {
            var client = new ThemezerClient(new FakeThemezer());
            var refused = await Assert.ThrowsAsync<ThemezerException>(() =>
                client.SignIn(Browser(q => "error=access_denied&state=" + q["state"]), CancellationToken.None));
            Assert.Equal("access_denied", refused.Code);
            Assert.Null(client.Token);

            // An answer with someone else's state is ignored; then it is just waiting until cancelled
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    client.SignIn(Browser(q => "code=stolen&state=other"), cancel.Token));
            Assert.Null(client.Token);
        }

        [Fact]
        public async Task ACodeThemezerRefusesReportsItsError()
        {
            var fake = new FakeThemezer {TokenAnswer = form => "{\"error\":\"invalid_grant\",\"error_description\":\"The code is invalid, expired or already used.\"}"};
            var client = new ThemezerClient(fake);
            var ex = await Assert.ThrowsAsync<ThemezerException>(() =>
                client.SignIn(Browser(q => "code=late&state=" + q["state"]), CancellationToken.None));
            Assert.Equal("invalid_grant", ex.Code);
            Assert.Contains("expired", ex.Message);
            Assert.Null(client.Username);
        }

        [Fact]
        public async Task UploadSendsEveryFileWithTheTokenAndMarksTheEditor()
        {
            var (client, fake) = await SignedIn();
            fake.Answer = op => Created;
            var theme = await client.UploadTheme(Upload(true));

            Assert.Equal("1A2B3", theme.HexId);
            Assert.Equal("https://themezer.net/3ds/themes/pink-clouds-1A2B3", theme.PageUrl);

            var request = fake.Requests.Last();
            Assert.True(request.Headers.Contains("apollo-require-preflight"));
            Assert.Equal("tz_oat_test", request.Headers.Authorization.Parameter);
            var d = fake.Operations.Last()["variables"]["d"];
            Assert.Equal("Pink Clouds", (string) d["name"]);
            Assert.Equal(new[] {"pink", "clouds"}, d["tags"].Select(t => (string) t));
            Assert.True((bool) d["externalEditor"]);
            Assert.True((bool) d["isPrivate"]);
            Assert.False((bool) d["isNSFW"]);
            Assert.Equal("Some Artist - Some Song", (string) d["bgmInfo"]);
            Assert.Null(((JObject) d).Property("uploadGrant"));

            // The GraphQL multipart format: operations, then a map from part number to the variable it fills, then the files
            var parts = fake.Parts.Last();
            Assert.Equal(new[] {"operations", "map"}, parts.Keys.Take(2));
            var map = JObject.Parse(Encoding.UTF8.GetString(parts["map"].Data));
            var byVariable = map.Properties().ToDictionary(p => (string) p.Value[0], p => parts[p.Name]);
            Assert.Equal(new byte[] {1, 2, 3}, byVariable["variables.d.body"].Data);
            Assert.Equal("body_LZ.bin", byVariable["variables.d.body"].FileName);
            Assert.Equal(new byte[] {4, 5}, byVariable["variables.d.preview"].Data);
            Assert.Equal(new byte[] {6}, byVariable["variables.d.icon"].Data);
            Assert.Equal(new byte[] {7, 8, 9, 10}, byVariable["variables.d.bgm"].Data);
        }

        [Fact]
        public async Task AThemeWithoutMusicSendsNoMusic()
        {
            var (client, fake) = await SignedIn();
            fake.Answer = op => Created;
            await client.UploadTheme(Upload(false));
            var d = (JObject) fake.Operations.Last()["variables"]["d"];
            Assert.False(d.ContainsKey("bgm"));
            Assert.False(d.ContainsKey("bgmInfo"));
            Assert.Equal(3, JObject.Parse(Encoding.UTF8.GetString(fake.Parts.Last()["map"].Data)).Count);
        }

        [Fact]
        public async Task ARevokedTokenSignsOut()
        {
            var client = new ThemezerClient(new FakeThemezer());
            await Assert.ThrowsAsync<ThemezerException>(() => client.UploadTheme(Upload(false)));

            var (signedIn, fake) = await SignedIn();
            fake.Status = HttpStatusCode.Unauthorized;
            fake.Answer = op => NotSignedIn;
            var ex = await Assert.ThrowsAsync<ThemezerException>(() => signedIn.UploadTheme(Upload(false)));
            Assert.Equal("NOT_AUTHENTICATED", ex.Code);
            Assert.Null(signedIn.Username);
            Assert.Null(signedIn.Token);

            // A saved token Themezer no longer accepts is not used
            Assert.Null(await new ThemezerClient(fake).Use("tz_oat_revoked"));
        }

        [Fact]
        public async Task SigningOutRevokesTheToken()
        {
            var (client, fake) = await SignedIn();
            await client.SignOut();
            Assert.Null(client.Username);
            Assert.Null(client.Token);
            Assert.Equal(ThemezerClient.RevokeUrl, fake.Requests.Last().RequestUri.ToString());
            Assert.Equal("tz_oat_test", fake.Forms.Last()["token"]);
        }

        private static ThemezerUpload Upload(bool withMusic) => new ThemezerUpload
        {
            Name = "Pink Clouds",
            Description = "Soft pink sky",
            Tags = new List<string> {"pink", "clouds"},
            BgmInfo = withMusic ? "Some Artist - Some Song" : null,
            IsPrivate = true,
            Body = new byte[] {1, 2, 3},
            Preview = new byte[] {4, 5},
            Icon = new byte[] {6},
            Bgm = withMusic ? new byte[] {7, 8, 9, 10} : null
        };

        private static ThemezerUploadWindow Dialog(ThemezerClient client, ICredentialStore vault, Action<string> browser = null) =>
            new ThemezerUploadWindow(client, vault, "Pink Clouds", "", false, () => Task.FromResult(new MainWindow.ThemePackage()), browser);

        [AvaloniaFact]
        public async Task ASavedTokenSignsInAndIsReadOnce()
        {
            var vault = new MemoryVault {Secrets = {["themezer-token"] = "tz_oat_saved"}};
            var fake = new FakeThemezer();
            var client = new ThemezerClient(fake);
            Assert.True(await Dialog(client, vault).Resume());
            Assert.Equal("Tester", client.Username);
            Assert.Equal("tz_oat_saved", fake.Requests.Last().Headers.Authorization.Parameter);
            // The dialog opened again while the editor runs
            Assert.True(await Dialog(new ThemezerClient(fake), vault).Resume());
            Assert.Equal(1, vault.Reads);
            Assert.Equal(0, vault.Writes);
        }

        [AvaloniaFact]
        public async Task ASavedTokenThemezerNoLongerAcceptsIsForgotten()
        {
            var vault = new MemoryVault {Secrets = {["themezer-token"] = "tz_oat_revoked"}};
            var fake = new FakeThemezer {Status = HttpStatusCode.Unauthorized, Answer = op => NotSignedIn};
            var client = new ThemezerClient(fake);
            Assert.False(await Dialog(client, vault).Resume());
            Assert.Null(client.Username);
            Assert.Empty(vault.Secrets);
        }

        /// <summary>Writes, reads and deletes a test entry in the real password vault (opt in: UMAMI_TEST_VAULT=1).</summary>
        [Fact]
        public void TheSystemVaultKeepsAndForgetsASecret()
        {
            if (Environment.GetEnvironmentVariable("UMAMI_TEST_VAULT") != "1")
                return;
            var vault = CredentialStore.System;
            Assert.True(vault.IsAvailable);
            const string account = "umami-test@example.invalid";
            try
            {
                Assert.True(vault.Write(account, "first"));
                Assert.Equal("first", vault.Read(account));
                Assert.True(vault.Write(account, "second ünïcode"));
                Assert.Equal("second ünïcode", vault.Read(account));
            }
            finally
            {
                vault.Delete(account);
            }
            Assert.Null(vault.Read(account));
        }

        [Fact]
        public void TagsAreSplitAtCommasTrimmedAndDeduplicated()
        {
            Assert.Equal(new[] {"pokemon", "Pink Sky", "cute"}, ThemezerClient.ParseTags(" pokemon, Pink Sky ,,cute, POKEMON "));
            Assert.Empty(ThemezerClient.ParseTags("  , "));
            Assert.Empty(ThemezerClient.ParseTags(null));
        }

        [AvaloniaFact]
        public void TheDialogOffersSignInAndNeedsATag()
        {
            var window = Dialog(new ThemezerClient(new FakeThemezer()), new MemoryVault());
            window.Show();
            var buttons = window.GetLogicalDescendants().OfType<Button>().ToList();
            Assert.Contains(buttons, b => (string) b.Content == MainResources.Themezer_SignIn && b.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBox>(), t => t.PasswordChar != default);
            Assert.Equal(MainResources.Themezer_NeedsTags, window.Validate());
            window.Close();
        }

        [AvaloniaFact]
        public async Task ThemezersRefusalsAreExplained()
        {
            var (client, fake) = await SignedIn();
            fake.Answer = op => "{\"errors\":[{\"message\":\"Image flagged\",\"extensions\":{\"code\":\"INVALID_THEME_CONTENTS\",\"reason\":\"UNMARKED_NSFW\"}}]}";
            var flagged = await Assert.ThrowsAsync<ThemezerException>(() => client.UploadTheme(Upload(false)));
            Assert.Equal("UNMARKED_NSFW", flagged.Reason);

            var window = Dialog(client, new MemoryVault());
            Assert.Equal(MainResources.Themezer_UnmarkedNsfw, window.Explain(flagged));
            Assert.Equal(MainResources.Themezer_RateLimited, window.Explain(new ThemezerException("Too many", "RATE_LIMITED")));
            // Not permitted means the account's adult content setting only when the upload was marked as adult content
            var notPermitted = new ThemezerException("Not your item", "NOT_PERMITTED");
            Assert.Equal(string.Format(MainResources.Themezer_Error, "Not your item"), window.Explain(notPermitted));
            window.GetLogicalDescendants().OfType<CheckBox>().Single(c => (string) c.Content == MainResources.Themezer_Nsfw).IsChecked = true;
            Assert.Equal(MainResources.Themezer_NsfwNotAllowed, window.Explain(notPermitted));
        }

        [AvaloniaFact]
        public void TheFileMenuOffersTheUpload()
        {
            var win = new MainWindow();
            win.Show();
            var menu = win.FindControl<Menu>("MainMenu");
            var file = (MenuItem) menu.Items[0];
            Assert.Contains(file.Items.OfType<MenuItem>(), i => (string) i.Header == MainResources.Menu_File_UploadThemezer);
        }
    }
}
