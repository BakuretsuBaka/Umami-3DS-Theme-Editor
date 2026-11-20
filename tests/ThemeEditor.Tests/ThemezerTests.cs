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
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Answer(operation), Encoding.UTF8, "application/json")
                };
            }
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
            var window = new ThemezerUploadWindow(client, "Pink Clouds", "", true,
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
