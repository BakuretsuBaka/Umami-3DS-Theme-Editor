using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    public class WebThemesTests
    {
        [Theory]
        [InlineData("T1A20D", WebThemeSource.Themezer, "1A20D")]
        [InlineData("https://themezer.net/3ds/themes/yarn-yoshi-1A20D", WebThemeSource.Themezer, "1A20D")]
        // A bare number belongs to the site picked for numbers
        [InlineData("10001", WebThemeSource.ThreeDsThemes, "10001")]
        [InlineData("#00042", WebThemeSource.ThreeDsThemes, "42")]
        // Links say where they are from, whatever is picked
        [InlineData("https://archive.org/download/3dsthem.es-archive/Themes/Themes/%5B10001%5D%20Lati%20Sunset%20Soaring%20by%20snowehuskeh%20%28Rev.%202%29.zip", WebThemeSource.ThreeDsThemes, "10001")]
        [InlineData("https://archive.org/download/3dsthem.es-archive/Themes/Themes/[10002] Yakuza 0 by Makkusu (Rev. 3).zip", WebThemeSource.ThreeDsThemes, "10002")]
        [InlineData("https://3dsthem.es/theme.php?id=1234", WebThemeSource.ThreeDsThemes, "1234")]
        [InlineData("https://themeplaza.art/themes/60000", WebThemeSource.ThemePlaza, "60000")]
        [InlineData("themeplaza.eu/item/100", WebThemeSource.ThemePlaza, "100")]
        [InlineData("https://web.archive.org/web/2024/https://themeplaza.art/download/20000", WebThemeSource.ThemePlaza, "20000")]
        [InlineData("https://archive.org/download/3ds-theme-plaza-archive/Theme Plaza NSFW Archive/NSFW/Themes/Some Theme by Someone (79960).zip", WebThemeSource.ThemePlaza, "79960")]
        public void WhatIsTypedIsRecognised(string input, WebThemeSource source, string id)
        {
            var theme = WebThemes.Parse(input, WebThemeSource.ThreeDsThemes);
            Assert.Equal(source, theme.Source);
            Assert.Equal(id, theme.Id);
        }

        [Fact]
        public void ThemesAreFoundByNameNumberOrLink()
        {
            // The same official theme came out in every region; official themes come first
            var hits = WebThemes.Search("yoshi springtime");
            Assert.Equal(new[] { "EU-0117", "JP-0362", "US-0114" }, hits.Where(h => h.Source == WebThemeSource.Official).Select(h => h.Id));
            Assert.Equal(WebThemeSource.Official, hits[0].Source);
            Assert.Equal("Yoshi's Springtime Siesta (EU)", hits[0].ToString());
            Assert.Equal(WebThemes.OfficialArchive + "European%20Themes/0117%20-%20Yoshi_s%20Springtime%20Siesta.zip", WebThemes.SourcesOf(hits[0]).Single());
            Assert.Empty(WebThemes.Search("m"));
            Assert.Equal("Stripes (Black & White) (EU)", WebThemes.TitleOf(WebThemeSource.Official, "EU-0012"));

            // The other archives by name too, with their section
            var rawk = Assert.Single(WebThemes.Search("rawk hawk fayne"));
            Assert.Equal(WebThemeSource.ThreeDsThemes, rawk.Source);
            Assert.Equal("1", rawk.Id);
            Assert.Equal("Rawk Hawk by Fayne_Aldan", rawk.ToString());
            var plaza = WebThemes.Search("new thanos").Single(t => t.Source == WebThemeSource.ThemePlaza);
            Assert.Equal("38872", plaza.Id);
            Assert.Equal("Sketchy", plaza.Section);
            Assert.True(WebThemes.Search("mario").Count(t => t.Source == WebThemeSource.Official) <= 50);

            // Numbers: per region, or with the region in front
            Assert.Equal("JP-0362", WebThemes.Parse("362", WebThemeSource.Official, "JP").Id);
            Assert.Equal("EU-0006", WebThemes.Parse("eu-6", WebThemeSource.ThreeDsThemes).Id);
            Assert.Equal("US-0114", WebThemes.Parse("US 0114", WebThemeSource.ThemePlaza).Id);
            Assert.Null(WebThemes.Parse("99999", WebThemeSource.Official, "EU"));

            var linked = WebThemes.Parse("https://archive.org/download/3ds-official-themes-archive/European%20Themes/0006%20-%20Mighty%20Mushrooms.zip", WebThemeSource.ThreeDsThemes);
            Assert.Equal(WebThemeSource.Official, linked.Source);
            Assert.Equal("EU-0006", linked.Id);
            Assert.Equal("US-0001", WebThemes.Parse("https://archive.org/download/3ds-official-themes-archive/North American Themes/0001 - x.zip", WebThemeSource.ThemePlaza)?.Id);
        }

        [Fact]
        public void NumbersFollowThePickedSiteAndNonsenseIsNothing()
        {
            Assert.Equal(WebThemeSource.ThemePlaza, WebThemes.Parse("10001", WebThemeSource.ThemePlaza).Source);
            Assert.Null(WebThemes.Parse("yarn yoshi", WebThemeSource.ThreeDsThemes));
            Assert.Null(WebThemes.Parse("1A20D", WebThemeSource.ThreeDsThemes));
            Assert.Null(WebThemes.Parse("", WebThemeSource.ThemePlaza));
        }

        [AvaloniaFact]
        public void TheShippedIndexFindsArchivedThemes()
        {
            Assert.Equal("[10001] Lati Sunset Soaring by snowehuskeh (Rev. 2).zip", WebThemes.ArchivedFile(WebThemeSource.ThreeDsThemes, "10001"));
            Assert.EndsWith("(10).zip", WebThemes.ArchivedFile(WebThemeSource.ThemePlaza, "10") ?? "");
            // Badge sets are not themes, so they are not in it
            Assert.Null(WebThemes.ArchivedFile(WebThemeSource.ThemePlaza, "79960"));
            Assert.Null(WebThemes.ArchivedFile(WebThemeSource.ThreeDsThemes, "9999999"));

            var three = WebThemes.SourcesOf(new WebThemeId {Source = WebThemeSource.ThreeDsThemes, Id = "10001"});
            Assert.Equal("https://archive.org/download/3dsthem.es-archive/Themes/Themes/%5B10001%5D%20Lati%20Sunset%20Soaring%20by%20snowehuskeh%20%28Rev.%202%29.zip", three.Single());
            // Theme Plaza: the archive.org copy first, then the Wayback Machine's copies of the download
            var plaza = WebThemes.SourcesOf(new WebThemeId {Source = WebThemeSource.ThemePlaza, Id = "10"});
            Assert.StartsWith(WebThemes.ThemePlazaArchive, plaza[0]);
            Assert.Equal(WebThemes.Wayback + "https://themeplaza.art/download/10", plaza[1]);
            Assert.Equal(2, WebThemes.SourcesOf(new WebThemeId {Source = WebThemeSource.ThemePlaza, Id = "60000"}).Count);
        }

        /// <summary>Stands in for archive.org and the Wayback Machine.</summary>
        /// <summary>Themezer answering every search with one "Mighty Mushrooms" theme and one that only matches elsewhere.</summary>
        private sealed class NoSearch : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var nodes = new JArray(
                    new JObject {["hexId"] = "1A20D", ["name"] = "Mighty Mushrooms Remix", ["creator"] = new JObject {["username"] = "Someone"}},
                    new JObject {["hexId"] = "1B000", ["name"] = "Sunny Day", ["creator"] = new JObject {["username"] = "Other"}});
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JObject {["data"] = new JObject {["threeDs"] = new JObject {["theme"] = new JObject {["themes"] = new JObject {["nodes"] = nodes}}}}}.ToString())
                });
            }
        }

        [AvaloniaFact]
        public async Task TheSearchAlsoFindsThemezerThemes()
        {
            var window = new WebThemeWindow(new ThemezerClient(new NoSearch()));
            window.Show();
            var search = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "Search");
            search.Text = "mighty mushrooms";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await window.OnlineSearch;
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
            var found = list.Items.Cast<WebThemeId>().ToList();
            // Themezer's (only the name that matches) first, then the archives'
            Assert.Equal(WebThemeSource.Themezer, found[0].Source);
            Assert.Equal("Mighty Mushrooms Remix by Someone", found[0].ToString());
            Assert.DoesNotContain(found, t => t.Id == "1B000");
            Assert.Contains(found, t => t.Id == "EU-0006");
            window.Close();
        }

        private sealed class FakeArchives : HttpMessageHandler
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
            public readonly List<string> Asked = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var url = request.RequestUri.AbsoluteUri;
                Asked.Add(url);
                return Task.FromResult(Files.TryGetValue(url, out var data)
                    ? new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(data)}
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private static readonly byte[] Zip = {80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};

        [AvaloniaFact]
        public async Task DownloadsTryTheCopiesInTurn()
        {
            var folder = Path.Combine(Path.GetTempPath(), "umami-web-" + Guid.NewGuid().ToString("N"));
            try
            {
                var archives = new FakeArchives();
                var theme = new WebThemeId {Source = WebThemeSource.ThemePlaza, Id = "60000"};
                // Not in the archive.org copy; the Wayback Machine kept an error page under .art and the ZIP under .eu
                archives.Files[WebThemes.Wayback + "https://themeplaza.art/download/60000"] = System.Text.Encoding.ASCII.GetBytes("<html>error</html>");
                archives.Files[WebThemes.Wayback + "https://themeplaza.eu/download/60000"] = Zip;
                var path = await WebThemes.Download(theme, folder, CancellationToken.None, archives);
                Assert.Equal(Zip, File.ReadAllBytes(path));
                Assert.Equal("ThemePlaza-60000.zip", Path.GetFileName(path));

                // 3DSThem.es comes from archive.org under its own name
                var three = new WebThemeId {Source = WebThemeSource.ThreeDsThemes, Id = "10001"};
                archives.Files[WebThemes.SourcesOf(three).Single()] = Zip;
                Assert.Equal("[10001] Lati Sunset Soaring by snowehuskeh (Rev. 2).zip", Path.GetFileName(await WebThemes.Download(three, folder, CancellationToken.None, archives)));

                // Nowhere to be found
                var gone = await Assert.ThrowsAsync<ThemezerException>(() =>
                    WebThemes.Download(new WebThemeId {Source = WebThemeSource.ThemePlaza, Id = "5000"}, folder, CancellationToken.None, archives));
                Assert.Equal("NOT_FOUND", gone.Code);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [AvaloniaFact]
        public void TheDialogNamesThemezerAndShowsWhatItRecognised()
        {
            var window = new WebThemeWindow(new ThemezerClient(new NoSearch()));
            window.Show();
            var texts = window.GetLogicalDescendants().OfType<TextBlock>().ToList();
            Assert.Contains(texts, t => t.Text == MainResources.Web_Hint && t.Text.Contains("Themezer"));
            var input = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "Code");
            var open = window.GetLogicalDescendants().OfType<Button>().Single(b => (string) b.Content == MainResources.Web_Open);
            Assert.False(open.IsEnabled);

            input.Text = "T1A20D";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(open.IsEnabled);
            Assert.Contains(texts, t => t.Text == "→ " + string.Format(MainResources.Web_Recognised, "Themezer", "T1A20D"));
            input.Text = "qqzx";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(open.IsEnabled);
            Assert.Contains(texts, t => t.Text == MainResources.Web_NotRecognised);

            // The search box finds themes by name, the first one picked; the link box no longer counts then
            var search = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "Search");
            search.Text = "mighty mushrooms";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(open.IsEnabled);
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
            Assert.True(list.IsVisible);
            Assert.Equal("EU-0006", ((WebThemeId) list.SelectedItem).Id);
            // Typing in the link box again goes back to it
            input.Text = "qqzy";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(open.IsEnabled);
            window.Close();
        }
    }
}
