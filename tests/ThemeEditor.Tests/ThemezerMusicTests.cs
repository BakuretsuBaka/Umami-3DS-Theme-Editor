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
            public string BgmInfo;
            public bool HasTheme => true;
            public void ApplyTextures(ThemeEditor.App.Library.LibraryItem item, ThemeEditor.App.Library.TextureColors colors) {}
            public void ApplySoundEffects(LibraryItem item) {}
            public void ApplySound(LibraryItem item, ThemeEditor.App.Experimental.CWAV.CwavKind kind) {}
            public void ApplyMusic(LibraryItem item) {}
            public void ApplyMusicData(byte[] bcstm, string bgmInfo, ThemeCredit credit = null) { Music = bcstm; BgmInfo = bgmInfo; Credit = credit; }
            public ThemeCredit Credit;
        }

        /// <summary>Themezer's search for themes with custom parts; one hit matches by its description only.</summary>
        private sealed class FakePartsSearch : HttpMessageHandler
        {
            public string Query;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Query = (string) JObject.Parse(await request.Content.ReadAsStringAsync())["query"];
                var nodes = new JArray(
                    new JObject {["hexId"] = "2029A", ["name"] = "Pokémon", ["pageUrl"] = "p", ["folderClosedUrl"] = "https://cdn/f.png", ["iconBorderLargeUrl"] = "https://cdn/i.png", ["creator"] = new JObject {["username"] = "Someone"}},
                    new JObject {["hexId"] = "1A000", ["name"] = "Sunny Day", ["pageUrl"] = "p", ["creator"] = new JObject {["username"] = "Other"}},
                    // Marked by Themezer, but without the textures themselves
                    new JObject {["hexId"] = "1B000", ["name"] = "Pokemon Wonder", ["pageUrl"] = "p", ["creator"] = new JObject {["username"] = "Third"}});
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JObject {["data"] = new JObject {["threeDs"] = new JObject {["theme"] = new JObject {["themes"] = new JObject {["nodes"] = nodes}}}}}.ToString())
                };
            }
        }

        [Fact]
        public async Task FoldersIconsAndSoundsAreFoundByThemeNameWithThemezersMarker()
        {
            var fake = new FakePartsSearch();
            var client = new ThemezerClient(fake);
            var folders = await client.SearchParts("pokemon", "hasCustomFolderModel");
            Assert.Contains("hasCustomFolderModel:true", fake.Query);
            var hit = Assert.Single(folders);
            Assert.Equal("Pokémon", hit.ThemeName);
            Assert.Equal("https://cdn/f.png", hit.PreviewUrl);
            Assert.Equal("https://cdn/i.png", Assert.Single(await client.SearchParts("pokemon", "hasCustomGamecardIcon")).PreviewUrl);
            // Sound effects have no picture, so the marker alone counts there
            Assert.Equal(2, (await client.SearchParts("pokemon", "hasCustomSfx")).Count);
            Assert.Contains("hasCustomSfx:true", fake.Query);
        }

        [Fact]
        public void APartFromThemezerIsReadFromItsOwnFolderAndCanBeKept()
        {
            var dir = Path.Combine(Path.GetTempPath(), "umami-keep-" + Guid.NewGuid().ToString("N"));
            try
            {
                var unpacked = Path.Combine(dir, "unpacked");
                Directory.CreateDirectory(Path.Combine(unpacked, "sfx"));
                File.WriteAllBytes(Path.Combine(unpacked, "sfx", "s-1.bin"), new byte[] {1, 2, 3});
                var item = new LibraryItem {Id = "s-1", Kind = LibraryKind.SoundEffects, Name = "Pokémon", DataFile = "sfx/s-1.bin", Bytes = 3, Root = unpacked};
                var lib = new ResourceLibrary(Path.Combine(dir, "mine"));
                lib.Load(false);
                Assert.Equal(new byte[] {1, 2, 3}, lib.ReadAllBytes(item, item.DataFile));

                var kept = lib.AddCopy(item);
                Assert.Null(kept.Root);
                Assert.Equal(new byte[] {1, 2, 3}, lib.ReadAllBytes(kept, kept.DataFile));
                Assert.Same(kept, lib.AddCopy(item));
                var reloaded = new ResourceLibrary(Path.Combine(dir, "mine"));
                reloaded.Load(false);
                Assert.Contains(reloaded.Of(LibraryKind.SoundEffects), i => i.Name == "Pokémon");
                // Where an unpacked item lived is not saved
                Assert.DoesNotContain("unpacked", File.ReadAllText(Path.Combine(dir, "mine", "index.json")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [AvaloniaFact]
        public async Task ThemesLookedAtOnThemezerAreDeletedWhenTheLibraryCloses()
        {
            var dir = Path.Combine(Path.GetTempPath(), "umami-preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                var lib = new ResourceLibrary(dir);
                lib.Load(false);
                var win = new ResourceLibraryWindow(lib, new Target(), null, new ThemezerClient(new FakeThemezer {Theme = ThemeWith(Music())}));
                win.Show();
                await win.PartsOf(new ThemezerPart {HexId = "2029A", ThemeName = "Pokémon"});
                await win.PartsOf(new ThemezerPart {HexId = "1A2D4", ThemeName = "Pokemon Battle Revolution"});
                var folders = win.PreviewFolders.ToList();
                Assert.Equal(2, folders.Count);
                Assert.All(folders, f => Assert.True(Directory.Exists(f)));

                win.Close();
                Assert.All(folders, f => Assert.False(Directory.Exists(f)));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [AvaloniaFact]
        public void MusicFromTheLibraryBringsItsBgmInfoToTheUpload()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
            var target = new MainWindow.LibraryTarget(win);
            target.ApplyMusicData(Music(), "Gwyn, Lord of Cinder - Dark Souls OST");
            Assert.Equal("Gwyn, Lord of Cinder - Dark Souls OST", win.BgmInfo);

            // The upload dialog fills it in
            var dialog = new ThemezerUploadWindow(new ThemezerClient(new FakeThemezer()), new NoVault(), "Theme", "", true,
                () => Task.FromResult(new MainWindow.ThemePackage()), bgmInfo: win.BgmInfo);
            dialog.Show();
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBox>(), t => t.Text == "Gwyn, Lord of Cinder - Dark Souls OST");
            dialog.Close();

            // Other music, or none, and the info goes
            win.RemoveBgmCommand.Execute(null);
            Assert.Null(win.BgmInfo);
            target.ApplyMusicData(Music(), null);
            Assert.Null(win.BgmInfo);
        }

        [Fact]
        public void CreditsListEveryoneElseOnce()
        {
            var credits = new[]
            {
                new ThemeCredit { Part = LibraryKind.SoundEffects, ThemeName = "Clicky", Creator = "Bob" },
                new ThemeCredit { Part = LibraryKind.Music, ThemeName = "Eternal Solaris", Creator = "Alteration", Url = "https://themezer.net/3ds/themes/T1" },
                new ThemeCredit { Part = LibraryKind.FolderTexture, ThemeName = "Mine", Creator = "me" },
                new ThemeCredit { Part = LibraryKind.SoundEffects, ThemeName = "Clicky", Creator = "Bob" }
            };
            Assert.Equal("**Credits**\n- Music from [Eternal Solaris](https://themezer.net/3ds/themes/T1) by Alteration\n- Sound effects from \"Clicky\" by Bob",
                ThemeCredit.Markdown(credits, "Me"));
            Assert.Equal(string.Empty, ThemeCredit.Markdown(new[] { credits[2] }, "me"));
        }

        [AvaloniaFact]
        public void ACreditLastsWhileThePartIsUnchanged()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
            var target = new MainWindow.LibraryTarget(win);
            target.ApplyMusicData(Music(), "Song", new ThemeCredit { ThemeName = "Eternal Solaris", Creator = "Alteration" });
            var credit = Assert.Single(win.CurrentCredits());
            Assert.Equal(LibraryKind.Music, credit.Part);

            // Other music: the credit goes
            win.RemoveBgmCommand.Execute(null);
            Assert.Empty(win.CurrentCredits());

            // Sound effects edited afterwards no longer count as taken
            win.ViewModel.CWavBytes = new byte[] { 1, 2, 3 };
            win.Credit(LibraryKind.SoundEffects, new ThemeCredit { ThemeName = "Clicky", Creator = "Bob" });
            Assert.Single(win.CurrentCredits());
            win.ViewModel.CWavBytes = new byte[] { 4, 5, 6 };
            Assert.Empty(win.CurrentCredits());
        }

        private sealed class NoVault : ICredentialStore
        {
            public bool IsAvailable => false;
            public string Read(string account) => null;
            public bool Write(string account, string secret) => false;
            public void Delete(string account) {}
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
                // Every tab offers it
                Assert.True(online.IsVisible);
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
                Assert.Equal(hit.BgmInfo, target.BgmInfo);

                await win.TakeHit(hit, true, new Button(), new Button());
                var kept = lib.Of(LibraryKind.Music).Single(i => i.Name == hit.BgmInfo);
                Assert.Equal(hit.BgmInfo, kept.BgmInfo);
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
