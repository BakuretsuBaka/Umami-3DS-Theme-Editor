using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        /// <summary>Stands in for api.themezer.net: records what the editor sends and answers with canned JSON.</summary>
        private sealed class FakeThemezer : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Requests = new List<HttpRequestMessage>();
            public readonly List<JObject> Operations = new List<JObject>();
            public readonly List<Dictionary<string, (string FileName, byte[] Data)>> Parts = new List<Dictionary<string, (string, byte[])>>();
            public Func<JObject, string> Answer = op => "{\"data\":{}}";
            /// <summary>When set, a successful login hands out this session cookie, as the real server does.</summary>
            public CookieContainer Cookies;
            public string NewSession = "fresh-session";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests.Add(request);
                var parts = new Dictionary<string, (string, byte[])>();
                JObject operation;
                if (request.Content is MultipartFormDataContent form)
                {
                    foreach (var part in form)
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
                var answer = Answer(operation);
                if (Cookies != null && answer.Contains("\"login\":{"))
                    Cookies.Add(new Uri(ThemezerClient.ApiUrl), new Cookie("connect.sid", NewSession, "/"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(answer, Encoding.UTF8, "application/json")
                };
            }
        }

        /// <summary>A password vault in memory.</summary>
        private sealed class MemoryVault : ICredentialStore
        {
            public readonly Dictionary<string, string> Secrets = new Dictionary<string, string>();
            public bool IsAvailable => true;
            public string Read(string account) => Secrets.TryGetValue(account, out var s) ? s : null;
            public bool Write(string account, string secret) { Secrets[account] = secret; return true; }
            public void Delete(string account) => Secrets.Remove(account);
        }

        private const string Me = "{\"data\":{\"auth\":{\"me\":{\"username\":\"Tester\"}}}}";
        private const string NoOne = "{\"data\":{\"auth\":{\"me\":null}}}";
        private const string LoginOk = "{\"data\":{\"auth\":{\"login\":{\"username\":\"Tester\"}}}}";
        private const string LoginWrong = "{\"errors\":[{\"message\":\"Invalid email or password\",\"extensions\":{\"code\":\"INVALID_CREDENTIALS\"}}]}";

        /// <summary>Runs <paramref name="test" /> with a remembered Themezer login for me@example.com, then restores the settings.</summary>
        private static async Task WithSavedLogin(Func<Task> test)
        {
            var s = ThemeEditor.App.Properties.Settings.Default;
            var (email, stay) = (s.Themezer_Email, s.Themezer_StayLoggedIn);
            s.Themezer_Email = "me@example.com";
            s.Themezer_StayLoggedIn = true;
            try
            {
                await test();
            }
            finally
            {
                s.Themezer_Email = email;
                s.Themezer_StayLoggedIn = stay;
            }
        }

        private static ThemezerUploadWindow Dialog(ThemezerClient client, ICredentialStore vault) =>
            new ThemezerUploadWindow(client, vault, "Pink Clouds", "", false, () => Task.FromResult(new MainWindow.ThemePackage()));

        [AvaloniaFact]
        public Task AStillValidSessionLogsInWithoutThePassword() => WithSavedLogin(async () =>
        {
            var fake = new FakeThemezer {Answer = op => ((string) op["query"]).Contains("me {") ? Me : LoginOk};
            var vault = new MemoryVault {Secrets = {["me@example.com"] = "secret", ["me@example.com#session"] = "old-session"}};
            var client = new ThemezerClient(fake);
            Assert.True(await Dialog(client, vault).Resume());
            Assert.Equal("Tester", client.Username);
            Assert.DoesNotContain(fake.Operations, op => ((string) op["query"]).Contains("login"));
        });

        [AvaloniaFact]
        public Task AnExpiredSessionLogsInWithTheSavedPasswordAndKeepsTheNewSession() => WithSavedLogin(async () =>
        {
            var cookies = new CookieContainer();
            var fake = new FakeThemezer {Cookies = cookies, Answer = op => ((string) op["query"]).Contains("me {") ? NoOne : LoginOk};
            var vault = new MemoryVault {Secrets = {["me@example.com"] = "secret", ["me@example.com#session"] = "old-session"}};
            var client = new ThemezerClient(fake, cookies);
            Assert.True(await Dialog(client, vault).Resume());
            Assert.Equal("Tester", client.Username);
            Assert.Equal("secret", (string) fake.Operations.Last()["variables"]["d"]["password"]);
            Assert.Equal("fresh-session", vault.Read("me@example.com#session"));
        });

        [AvaloniaFact]
        public Task APasswordChangedOnThemezerIsForgotten() => WithSavedLogin(async () =>
        {
            var fake = new FakeThemezer {Answer = op => ((string) op["query"]).Contains("me {") ? NoOne : LoginWrong};
            var vault = new MemoryVault {Secrets = {["me@example.com"] = "old-password", ["me@example.com#session"] = "old-session"}};
            var client = new ThemezerClient(fake);
            await Assert.ThrowsAsync<ThemezerException>(() => Dialog(client, vault).Resume());
            Assert.Null(client.Username);
            Assert.Empty(vault.Secrets);
        });

        [AvaloniaFact]
        public Task WithoutStayLoggedInNothingIsResumed() => WithSavedLogin(async () =>
        {
            ThemeEditor.App.Properties.Settings.Default.Themezer_StayLoggedIn = false;
            var fake = new FakeThemezer();
            var vault = new MemoryVault {Secrets = {["me@example.com"] = "secret"}};
            Assert.False(await Dialog(new ThemezerClient(fake), vault).Resume());
            Assert.Empty(fake.Operations);
        });

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

        private static async Task<(ThemezerClient, FakeThemezer)> LoggedIn()
        {
            var fake = new FakeThemezer {Answer = op => "{\"data\":{\"auth\":{\"login\":{\"username\":\"Tester\"}}}}"};
            var client = new ThemezerClient(fake);
            await client.LogIn("me@example.com", "secret");
            return (client, fake);
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

        private const string Created = "{\"data\":{\"threeDs\":{\"theme\":{\"createTheme\":" +
                                       "{\"hexId\":\"1A2B3\",\"name\":\"Pink Clouds\",\"pageUrl\":\"https://themezer.net/3ds/themes/pink-clouds-1A2B3\"}}}}}";

        [Fact]
        public async Task LoggingInSendsEmailAndPasswordAndRemembersWho()
        {
            var (client, fake) = await LoggedIn();
            Assert.Equal("Tester", client.Username);
            var op = fake.Operations.Single();
            Assert.Contains("login(data:$d)", (string) op["query"]);
            Assert.Equal("me@example.com", (string) op["variables"]["d"]["email"]);
            Assert.Equal("secret", (string) op["variables"]["d"]["password"]);
            Assert.Equal(ThemezerClient.ApiUrl, fake.Requests[0].RequestUri.ToString());
        }

        [Fact]
        public async Task AWrongPasswordReportsThemezersMessage()
        {
            var fake = new FakeThemezer
            {
                Answer = op => "{\"errors\":[{\"message\":\"Invalid email or password\",\"extensions\":{\"code\":\"INVALID_CREDENTIALS\"}}],\"data\":null}"
            };
            var client = new ThemezerClient(fake);
            var ex = await Assert.ThrowsAsync<ThemezerException>(() => client.LogIn("me@example.com", "wrong"));
            Assert.Equal("INVALID_CREDENTIALS", ex.Code);
            Assert.Equal("Invalid email or password", ex.Message);
            Assert.Null(client.Username);
        }

        [Fact]
        public async Task UploadSendsEveryFileAndMarksTheEditor()
        {
            var (client, fake) = await LoggedIn();
            fake.Answer = op => Created;
            var theme = await client.UploadTheme(Upload(true));

            Assert.Equal("1A2B3", theme.HexId);
            Assert.Equal("https://themezer.net/3ds/themes/pink-clouds-1A2B3", theme.PageUrl);

            var request = fake.Requests.Last();
            Assert.True(request.Headers.Contains("apollo-require-preflight"));
            var d = fake.Operations.Last()["variables"]["d"];
            Assert.Equal("Pink Clouds", (string) d["name"]);
            Assert.Equal(new[] {"pink", "clouds"}, d["tags"].Select(t => (string) t));
            Assert.True((bool) d["externalEditor"]);
            Assert.True((bool) d["isPrivate"]);
            Assert.False((bool) d["isNSFW"]);
            Assert.Equal("Some Artist - Some Song", (string) d["bgmInfo"]);

            // The GraphQL multipart format: a map from part number to the variable it fills, then the files
            var parts = fake.Parts.Last();
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
            var (client, fake) = await LoggedIn();
            fake.Answer = op => Created;
            await client.UploadTheme(Upload(false));
            var d = (JObject) fake.Operations.Last()["variables"]["d"];
            Assert.False(d.ContainsKey("bgm"));
            Assert.False(d.ContainsKey("bgmInfo"));
            Assert.Equal(3, JObject.Parse(Encoding.UTF8.GetString(fake.Parts.Last()["map"].Data)).Count);
        }

        [Fact]
        public async Task UploadingNeedsALoginAndAnExpiredOneIsForgotten()
        {
            var client = new ThemezerClient(new FakeThemezer());
            await Assert.ThrowsAsync<ThemezerException>(() => client.UploadTheme(Upload(false)));

            var (loggedIn, fake) = await LoggedIn();
            fake.Answer = op => "{\"errors\":[{\"message\":\"Not logged in\",\"extensions\":{\"code\":\"UNAUTHENTICATED\"}}]}";
            await Assert.ThrowsAsync<ThemezerException>(() => loggedIn.UploadTheme(Upload(false)));
            Assert.Null(loggedIn.Username);
        }

        [Fact]
        public async Task LoggingOutForgetsWhoWasLoggedIn()
        {
            var (client, fake) = await LoggedIn();
            fake.Answer = op => "{\"data\":{\"auth\":{\"logout\":true}}}";
            await client.LogOut();
            Assert.Null(client.Username);
            Assert.Contains("logout", (string) fake.Operations.Last()["query"]);
        }

        [Fact]
        public void TagsAreSplitAtCommasTrimmedAndDeduplicated()
        {
            Assert.Equal(new[] {"pokemon", "Pink Sky", "cute"}, ThemezerClient.ParseTags(" pokemon, Pink Sky ,,cute, POKEMON "));
            Assert.Empty(ThemezerClient.ParseTags("  , "));
            Assert.Empty(ThemezerClient.ParseTags(null));
        }

        [AvaloniaFact]
        public void TheDialogAsksToLogInAndNeedsATag()
        {
            var client = new ThemezerClient(new FakeThemezer());
            var window = new ThemezerUploadWindow(client, new MemoryVault(), "Pink Clouds", "", true,
                () => Task.FromResult(new MainWindow.ThemePackage()));
            window.Show();
            var texts = window.GetLogicalDescendants().OfType<TextBox>().ToList();
            Assert.Contains(texts, t => t.PasswordChar == '•' && t.IsEffectivelyVisible);
            Assert.Equal(MainResources.Themezer_NeedsTags, window.Validate());
            window.Close();
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
