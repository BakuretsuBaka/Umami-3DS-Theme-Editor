using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Newtonsoft.Json.Linq;
using ThemeEditor.App;
using ThemeEditor.App.Library;
using ThemeEditor.App.Localization;
using ThemeEditor.Common.Audio;
using Xunit;

namespace ThemeEditor.Tests
{
    public class ThemezerMusicTests
    {
        [Theory]
        [InlineData("Gwyn, Lord of Cinder - Dark Souls OST, Motoi Sakuraba", "Gwyn, Lord of Cinder", true)]
        [InlineData("Gwyn, Lord of Cinder - Dark Souls OST, Motoi Sakuraba", "gwyn cinder", true)]
        [InlineData("Gwyn, Lord of Cinder - Dark Souls OST, Motoi Sakuraba", "Gwy", true)]
        [InlineData("Pokémon Center (Day)", "pokemon center", true)]
        [InlineData("Megalovania - Toby Fox", "Megalovania", true)]
        // Every word has to be there, and as the start of a word
        [InlineData("Gwyn, Lord of Cinder - Dark Souls OST", "gwyn zelda", false)]
        [InlineData("Suzuki503x", "megalovania", false)]
        [InlineData("Megalovania", "alovania", false)]
        [InlineData(null, "anything", false)]
        public void OnlyMusicWhoseInfoMatchesCounts(string bgmInfo, string query, bool match)
        {
            Assert.Equal(match, ThemezerClient.MusicMatches(bgmInfo, query));
        }

        /// <summary>Themezer: its theme search (which also matches names, so it returns a theme with other music) and downloads.</summary>
        private sealed class FakeThemezer : HttpMessageHandler
        {
            public byte[] Theme;
            public readonly List<string> Queries = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(Theme)};
                var op = JObject.Parse(await request.Content.ReadAsStringAsync());
                Queries.Add((string) op["variables"]["q"]);
                var nodes = new JArray(
                    Node("21368", "Eternal Solaris", "Gwyn, Lord of Cinder - Dark Souls OST, Motoi Sakuraba"),
                    // Found by its name only: its music is something else
                    Node("1A20F", "Gwyn Tribute", "Firelink Shrine - Dark Souls OST"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JObject {["data"] = new JObject {["threeDs"] = new JObject {["theme"] = new JObject {["themes"] = new JObject {["nodes"] = nodes}}}}}.ToString())
                };
            }

            private static JObject Node(string id, string name, string bgm) => new JObject
            {
                ["hexId"] = id, ["name"] = name, ["bgmInfo"] = bgm, ["pageUrl"] = "https://themezer.net/3ds/themes/x-" + id,
                ["bgmPreviewUrl"] = "https://cdn.themezer.net/" + id + "/bgm.ogg", ["creator"] = new JObject {["username"] = "Alteration"}
            };
        }

        private static byte[] Music(double pitch = 0.05)
        {
            var samples = new short[8000];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (short) (Math.Sin(i * pitch) * 8000);
            return BcstmConverter.Encode(BcstmConverter.FromPcm(new[] {samples, samples}, 32000));
        }

        private static byte[] ThemeWith(byte[] bgm) => ThemeArchive.Create("Eternal Solaris", new byte[] {1, 2, 3, 4}, null, bgm, null, null);

        [Fact]
        public async Task TheSearchKeepsOnlyHitsWhoseMusicMatches()
        {
            var fake = new FakeThemezer();
            var hits = await new ThemezerClient(fake).SearchMusic("Gwyn");
            var hit = Assert.Single(hits);
            Assert.Equal("21368", hit.HexId);
            Assert.Equal("Eternal Solaris", hit.ThemeName);
            Assert.Equal("Alteration", hit.Creator);
            Assert.EndsWith("bgm.ogg", hit.PreviewUrl);
            Assert.Equal("Gwyn", fake.Queries.Single());
        }

        [Fact]
        public async Task OnlyTheMusicIsTakenFromTheTheme()
        {
            var music = Music();
            var fake = new FakeThemezer {Theme = ThemeWith(music)};
            Assert.Equal(music, await new ThemezerClient(fake).DownloadMusic("21368"));
            fake.Theme = ThemeWith(null);
            Assert.Null(await new ThemezerClient(fake).DownloadMusic("21368"));
        }

        private sealed class Target : ILibraryTarget
        {
            public byte[] Music;
            public bool HasTheme => true;
            public void ApplyTextures(LibraryItem item, float hue, float saturation, float brightness) {}
            public void ApplySoundEffects(LibraryItem item) {}
            public void ApplySound(LibraryItem item, ThemeEditor.App.Experimental.CWAV.CwavKind kind) {}
            public void ApplyMusic(LibraryItem item) {}
            public void ApplyMusicData(byte[] bcstm) => Music = bcstm;
        }

        [AvaloniaFact]
        public async Task TheLibraryShowsThemezerMusicAfterItsOwnAndCanUseOrKeepIt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "umami-music-" + Guid.NewGuid().ToString("N"));
            var settings = ThemeEditor.App.Properties.Settings.Default;
            var saved = settings.Library_SearchThemezer;
            try
            {
                var lib = new ResourceLibrary(dir);
                lib.Load(false);
                var music = Music();
                // The user's own library already has some other Gwyn music
                lib.AddMusic(Music(0.02), "Gwyn, Lord of Cinder (mine)");
                var fake = new FakeThemezer {Theme = ThemeWith(music)};
                var target = new Target();
                settings.Library_SearchThemezer = false;
                var win = new ResourceLibraryWindow(lib, target, null, new ThemezerClient(fake));
                win.Show();
                var online = win.GetLogicalDescendants().OfType<CheckBox>().Single(c => (string) c.Content == MainResources.Library_SearchThemezer);
                // Only the Music tab offers it
                Assert.False(online.IsVisible);
                win.GetLogicalDescendants().OfType<RadioButton>().ElementAt(3).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(online.IsVisible);

                var search = win.GetLogicalDescendants().OfType<TextBox>().First();
                search.Text = "gwyn";
                await win.RunOnlineSearch();
                // Off: nothing is sent to Themezer
                Assert.Empty(fake.Queries);

                online.IsChecked = true;
                await win.RunOnlineSearch();
                var list = win.GetLogicalDescendants().OfType<ListBox>().Single();
                var shown = list.ItemsSource.Cast<object>().ToList();
                Assert.IsType<LibraryItem>(shown[0]);
                var hit = Assert.IsType<ThemezerMusic>(shown[1]);
                Assert.Equal(2, shown.Count);
                Assert.True(settings.Library_SearchThemezer);

                await win.TakeHit(hit, false, new Button(), new Button());
                Assert.Equal(music, target.Music);

                await win.TakeHit(hit, true, new Button(), new Button());
                var kept = lib.Of(LibraryKind.Music).Single(i => i.Name == hit.BgmInfo);
                Assert.Equal(BcstmCompat.Normalize(music), lib.ReadAllBytes(kept, kept.DataFile));
                Assert.Equal(2, lib.Count(LibraryKind.Music));
                // Kept for good: a fresh look at the library folder finds it
                var reloaded = new ResourceLibrary(dir);
                reloaded.Load(false);
                Assert.Contains(reloaded.Of(LibraryKind.Music), i => i.Name == hit.BgmInfo);
                // Adding the same music twice keeps one copy
                await win.TakeHit(hit, true, new Button(), new Button());
                Assert.Equal(2, lib.Count(LibraryKind.Music));
                win.Close();
            }
            finally
            {
                settings.Library_SearchThemezer = saved;
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
