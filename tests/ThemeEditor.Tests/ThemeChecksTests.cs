using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ThemeEditor.App;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Library;
using ThemeEditor.App.Localization;
using ThemeEditor.Common.Audio;
using Xunit;

namespace ThemeEditor.Tests
{
    public class ThemeChecksTests
    {
        private static MainWindow NewTheme()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
            return win;
        }

        private static short[] Tone(int samples)
        {
            var s = new short[samples];
            for (var i = 0; i < s.Length; i++)
                s[i] = (short) (Math.Sin(i * 0.05) * 8000);
            return s;
        }

        private static byte[] Music(int channels, bool loop)
        {
            var pcm = Enumerable.Range(0, channels).Select(_ => Tone(8000)).ToArray();
            return BcstmConverter.Encode(BcstmConverter.FromPcm(pcm, 32000), new BcstmOptions { Loop = loop });
        }

        private static byte[] Sounds(int cursorChannels)
        {
            var block = new CwavBlock();
            var pcm = Enumerable.Range(0, cursorChannels).Select(_ => Tone(2000)).ToArray();
            block.Sounds[CwavKind.Cursor].CwavData = CwavConverter.Encode(BcstmConverter.FromPcm(pcm, 22050), CwavCodec.DspAdpcm);
            return block.Generate();
        }

        [AvaloniaFact]
        public void OnlyRealFaultsAreProblems()
        {
            var win = NewTheme();
            var vm = win.ViewModel;
            Assert.Empty(win.ThemeProblems());

            // Stereo, looping music and stereo sounds are fine (sound effects need no loop)
            new MainWindow.LibraryTarget(win).ApplyMusicData(Music(2, true), null);
            vm.CWavBytes = Sounds(2);
            vm.Flags.SoundEffect = true;
            Assert.Empty(win.ThemeProblems());

            // Music that doesn't loop, and a mono sound effect (Umami writes music as stereo itself, so mono music only comes in
            // with BCSTMs made elsewhere)
            new MainWindow.LibraryTarget(win).ApplyMusicData(Music(2, false), null);
            vm.CWavBytes = Sounds(1);
            var problems = win.ThemeProblems();
            Assert.Contains(MainResources.Check_BgmNotLooped, problems);
            Assert.DoesNotContain(MainResources.Check_BgmMono, problems);
            // Mono sound effects are fine: most themes have them
            Assert.Equal(new[] { MainResources.Check_BgmNotLooped }, problems);

            // Too large
            vm.CWavBytes = new byte[MainWindow.MaxSoundEffectBytes + 1];
            Assert.Contains(problems = win.ThemeProblems(), p => p == string.Format(MainResources.Check_SfxTooLarge, (MainWindow.MaxSoundEffectBytes + 1) / 1024, MainWindow.MaxSoundEffectBytes / 1024));
        }

        [AvaloniaFact]
        public async Task EachProblemIsToldOnce()
        {
            var win = NewTheme();
            var asked = new List<string>();
            var answer = 1;
            win.AskThemeProblems = message =>
            {
                asked.Add(message);
                return Task.FromResult(answer);
            };
            Assert.True(await win.ConfirmThemeProblems());
            Assert.Empty(asked);

            new MainWindow.LibraryTarget(win).ApplyMusicData(Music(2, false), null);
            // Cancelled: asked again next time
            Assert.False(await win.ConfirmThemeProblems());
            answer = 0;
            Assert.True(await win.ConfirmThemeProblems());
            Assert.Equal(2, asked.Count);
            Assert.Contains(MainResources.Check_BgmNotLooped, asked[1]);
            // Gone along with: not again
            Assert.True(await win.ConfirmThemeProblems());
            Assert.Equal(2, asked.Count);
        }

        [AvaloniaFact]
        public async Task ThemesOpenedFromTheWebAreCreditedAsTheBase()
        {
            var archived = await WebThemes.CreditOf(new WebThemeId { Source = WebThemeSource.ThreeDsThemes, Id = "1" }, null);
            Assert.Equal("Rawk Hawk", archived.ThemeName);
            Assert.Equal("Fayne_Aldan", archived.Creator);
            Assert.StartsWith(WebThemes.ThreeDsThemesArchive, archived.Url);
            var official = await WebThemes.CreditOf(new WebThemeId { Source = WebThemeSource.Official, Id = "EU-0006" }, null);
            Assert.Equal("Mighty Mushrooms", official.ThemeName);
            Assert.Equal("Nintendo", official.Creator);

            archived.Base = true;
            var music = new ThemeCredit { Part = LibraryKind.Music, ThemeName = "Song Theme", Creator = "Bob" };
            Assert.Equal("**Credits**\n- Based on [Rawk Hawk](" + archived.Url + ") by Fayne_Aldan\n- Music from \"Song Theme\" by Bob",
                ThemeCredit.Markdown(new[] { music, archived }, "Me"));
        }
    }
}
