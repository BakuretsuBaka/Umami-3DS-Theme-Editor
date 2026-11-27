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
            var window = new WebThemeWindow();
            window.Show();
            var texts = window.GetLogicalDescendants().OfType<TextBlock>().ToList();
            Assert.Contains(texts, t => t.Text == MainResources.Web_Hint && t.Text.Contains("Themezer"));
            var input = window.GetLogicalDescendants().OfType<TextBox>().Single();
            var open = window.GetLogicalDescendants().OfType<Button>().Single(b => (string) b.Content == MainResources.Web_Open);
            Assert.False(open.IsEnabled);

            input.Text = "T1A20D";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(open.IsEnabled);
            Assert.Contains(texts, t => t.Text == "→ " + string.Format(MainResources.Web_Recognised, "Themezer", "T1A20D"));
            input.Text = "hello";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(open.IsEnabled);
            Assert.Contains(texts, t => t.Text == MainResources.Web_NotRecognised);
            window.Close();
        }
    }
}
