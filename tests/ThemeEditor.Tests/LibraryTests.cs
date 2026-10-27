using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using ThemeEditor.App;
using ThemeEditor.App.Library;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Audio;
using ThemeEditor.Common.Compression;
using ThemeEditor.Common.Graphics;
using Xunit;

namespace ThemeEditor.Tests
{
    public class LibraryTests
    {
        private static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "umami-lib-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        private static PixelBuffer Solid(int w, int h, byte r, byte g, byte b)
        {
            using (var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                bmp.Erase(new SKColor(r, g, b));
                using (var img = SKImage.FromBitmap(bmp)) using (var d = img.Encode(SKEncodedImageFormat.Png, 100)) using (var ms = new MemoryStream(d.ToArray()))
                    return PixelBuffer.Load(ms);
            }
        }

        private static ThemeViewModel NewTheme()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(20); }
            return win.ViewModel;
        }

        private static byte[] ThemeZip(string name, byte tint, bool music)
        {
            var vm = NewTheme();
            vm.Textures.FolderClosed.EncodeTexture(Solid(128, 64, tint, 40, 90), RawTexture.DataFormat.Bgr888);
            vm.Textures.FolderOpen.EncodeTexture(Solid(128, 64, 40, tint, 90), RawTexture.DataFormat.Bgr888);
            vm.Textures.FileLarge.EncodeTexture(Solid(64, 128, tint, tint, 90), RawTexture.DataFormat.Bgr888);
            vm.Textures.FileSmall.EncodeTexture(Solid(32, 64, 90, tint, tint), RawTexture.DataFormat.Bgr888);
            vm.Flags.FolderTexture = true; vm.Flags.FileTexture = true; vm.Flags.SoundEffect = true;
            var cwav = new byte[0x400]; new Random(tint).NextBytes(cwav); vm.CWavBytes = cwav;
            byte[] body;
            using (var dec = new MemoryStream()) using (var com = new MemoryStream()) { vm.Save(dec); dec.Position = 0; LZ11.Compress(dec, dec.Length, com, true); body = com.ToArray(); }
            byte[] bgm = null;
            if (music)
            {
                var pcm = new short[2][]; for (var c = 0; c < 2; c++) { pcm[c] = new short[32000]; for (var i = 0; i < 32000; i++) pcm[c][i] = (short) (Math.Sin(i * 0.05 * (1 + tint / 255.0)) * 8000); }
                bgm = BcstmConverter.Encode(BcstmConverter.FromPcm(pcm, 32000));
            }
            return ThemeArchive.Create(name, body, new byte[0x36C0], bgm, new byte[] { 1 }, new byte[] { 2 });
        }

        [AvaloniaFact]
        public void ImportsTexturesSoundsAndMusicAndDeduplicates()
        {
            var src = TempDir(); var root = TempDir();
            try
            {
                File.WriteAllBytes(Path.Combine(src, "0001 - Alpha Theme.zip"), ThemeZip("Alpha Theme", 200, true));
                File.WriteAllBytes(Path.Combine(src, "0002 - Alpha Copy.zip"), ThemeZip("Alpha Theme", 200, true)); // identical content
                File.WriteAllBytes(Path.Combine(src, "0003 - Beta by Someone (123).zip"), ThemeZip("Beta", 90, false));
                File.WriteAllText(Path.Combine(src, "broken.zip"), "not a zip");

                var lib = new ResourceLibrary(root);
                var progress = new ImportProgress();
                var result = new ThemeImporter(lib, "Test").ImportFolder(src);

                Assert.Equal(2, lib.Count(LibraryKind.FolderTexture));
                Assert.Equal(2, lib.Count(LibraryKind.IconTexture));
                Assert.Equal(2, lib.Count(LibraryKind.SoundEffects));
                Assert.Equal(1, lib.Count(LibraryKind.Music));
                Assert.Equal(7, result.Added);

                // Self-contained: every referenced file lives inside the library folder
                foreach (var item in lib.Items)
                    foreach (var f in new[] { item.FileA, item.FileB, item.DataFile }.Where(x => x != null))
                        Assert.True(File.Exists(lib.PathOf(item, f)) && Path.GetFullPath(lib.PathOf(item, f)).StartsWith(root));

                // The index survives a reload
                var again = new ResourceLibrary(root); again.Load(false);
                Assert.Equal(lib.Items.Count, again.Items.Count);
                Assert.Contains(again.Items, i => i.Name == "Beta");
                Assert.Contains(again.Items, i => i.Name == "Alpha Theme" && i.Kind == LibraryKind.Music && i.Seconds > 0.5);

                // Importing the same folder again adds nothing
                Assert.Equal(0, new ThemeImporter(again, "Test").ImportFolder(src).Added);
            }
            finally { Directory.Delete(src, true); Directory.Delete(root, true); }
        }

        [Theory]
        [InlineData("0260 - Taiko no Tatsujin - Pink Version.zip", "Taiko no Tatsujin - Pink Version")]
        [InlineData("Wii Settings Theme by lekton14 (Themezer-T1B44E).zip", "Wii Settings Theme by lekton14")]
        [InlineData("Sleepy Sailor Moon by Alteration (83718).zip", "Sleepy Sailor Moon")]
        public void CleansThemeNames(string file, string expected)
        {
            Assert.Equal(expected, ThemeImporter.CleanName(file));
        }

        [Fact]
        public void RecolorRotatesHueAndKeepsIdentity()
        {
            using (var red = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                red.Erase(new SKColor(220, 30, 30));
                using (var same = TextureRecolor.Apply(red, 0))
                    Assert.Equal(red.GetPixel(2, 2), same.GetPixel(2, 2));
                using (var green = TextureRecolor.Apply(red, 120))
                {
                    var p = green.GetPixel(3, 3);
                    Assert.True(p.Green > p.Red + 60 && p.Green > p.Blue + 60, "red rotated by 120 degrees should look green");
                }
                using (var gray = TextureRecolor.Apply(red, 0, 0))
                {
                    var p = gray.GetPixel(1, 1);
                    Assert.True(Math.Abs(p.Red - p.Green) < 4 && Math.Abs(p.Green - p.Blue) < 4);
                }
                using (var bright = TextureRecolor.Apply(red, 0, 1, 0.3f))
                    Assert.True(bright.GetPixel(0, 0).Blue > 60);
            }
        }

        private sealed class NullTarget : ILibraryTarget
        {
            public bool HasTheme => true;
            public void ApplyTextures(LibraryItem item, float hue, float saturation, float brightness) {}
            public void ApplySoundEffects(LibraryItem item) {}
            public void ApplySound(LibraryItem item, ThemeEditor.App.Experimental.CWAV.CwavKind kind) {}
            public void ApplyMusic(LibraryItem item) {}
        }

        [AvaloniaFact]
        public void LibraryWindowRendersForScreenshots()
        {
            var dir = Environment.GetEnvironmentVariable("UMAMI_SHOT_DIR"); var root = Environment.GetEnvironmentVariable("UMAMI_LIB_ROOT");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(root)) return;
            Directory.CreateDirectory(dir);
            var lib = new ResourceLibrary(root); lib.Load(false);
            var win = new ResourceLibraryWindow(lib, new NullTarget()); win.Show();
            var list = win.GetType().GetField("_list", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(win) as Avalonia.Controls.ListBox;
            var tabs = (Avalonia.Controls.RadioButton[]) win.GetType().GetField("_tabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(win);
            for (var k = 0; k < 4; k++)
            {
                tabs[k].IsChecked = true; tabs[k].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (list.ItemCount > 3) list.SelectedIndex = 3;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var frame = win.CaptureRenderedFrame(); frame?.Save(Path.Combine(dir, "library_" + k + ".png"));
            }
            win.Close();
        }
    }
}
