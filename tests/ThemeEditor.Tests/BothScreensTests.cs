using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ThemeEditor.App;
using ThemeEditor.App.Localization.Enums;
using Xunit;

namespace ThemeEditor.Tests
{
    public class BothScreensTests
    {
        private static readonly BothScreens.Model NewXL = BothScreens.Find("New 3DS XL");

        // Every pixel says where it came from: blue = x / 2, green = y / 3 (both fit in a byte), red = 255
        private static PixelBuffer Picture(BothScreens.Model model = null)
        {
            var height = (model ?? NewXL).Height;
            var bgra = new byte[BothScreens.Width * height * 4];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < BothScreens.Width; x++)
            {
                var i = (y * BothScreens.Width + x) * 4;
                bgra[i] = (byte) (x / 2);
                bgra[i + 1] = (byte) (y / 3);
                bgra[i + 2] = 255;
                bgra[i + 3] = 255;
            }
            return new PixelBuffer(BothScreens.Width, height, bgra);
        }

        private static (int x, int y) Source(PixelBuffer p, int x, int y)
        {
            var i = (y * p.Width + x) * 4;
            return (p.Bgra[i] * 2, p.Bgra[i + 1] * 3);
        }

        [Fact]
        public void TheLayoutMatchesTheConsole()
        {
            Assert.Equal(412, BothScreens.Width);
            Assert.Equal(46, BothScreens.BottomLeft);
            // Hinge gaps measured on product photos, in screen pixels
            Assert.Equal(92, BothScreens.Find("3DS").Gap);
            Assert.Equal(88, BothScreens.Find("New 3DS").Gap);
            Assert.Equal(73, BothScreens.Find("New 3DS XL").Gap);
            Assert.Equal(85, BothScreens.Find("New 2DS XL").Gap);
            Assert.Equal(240 + 73 + 240, NewXL.Height);
            Assert.Same(BothScreens.DefaultModel, BothScreens.Find("Game Boy"));
        }

        [Theory]
        [InlineData("3DS")]
        [InlineData("New 3DS")]
        [InlineData("New 3DS XL")]
        [InlineData("New 2DS XL")]
        public void EachModelHidesItsOwnGap(string name)
        {
            var model = BothScreens.Find(name);
            BothScreens.Split(Picture(model), true, model, out var top, out var bottom);
            Assert.Equal((0, 0), Source(top, 0, 0));
            var (x, y) = Source(bottom, 0, 0);
            Assert.Equal(46, x);
            Assert.InRange(y, 240 + model.Gap - 2, 240 + model.Gap);
        }

        [Fact]
        public void SplitCutsTheTopAndTheCentredBottomAroundTheHinge()
        {
            BothScreens.Split(Picture(), true, NewXL, out var top, out var bottom);
            Assert.Equal((512, 256), (top.Width, top.Height));
            Assert.Equal((512, 256), (bottom.Width, bottom.Height));

            Assert.Equal((0, 0), Source(top, 0, 0));
            Assert.Equal((410, 237), Source(top, 411, 239));
            // The bottom screen starts below the hinge (row 313 on the New 3DS XL), 46 px in
            Assert.Equal((46, 312), Source(bottom, 0, 0));
            Assert.Equal((364, 552), Source(bottom, 319, 239));
        }

        [Fact]
        public void JoinIsTheOppositeOfSplit()
        {
            var picture = Picture();
            BothScreens.Split(picture, true, NewXL, out var top, out var bottom);
            var joined = BothScreens.Join(top, bottom, NewXL);
            Assert.Equal((picture.Width, picture.Height), (joined.Width, joined.Height));
            Assert.Equal((100, 99), Source(joined, 100, 100));
            Assert.Equal((200, 399), Source(joined, 200, 400));
            // The hinge and the sides of the bottom screen are left transparent
            Assert.Equal(0, joined.Bgra[((300 * joined.Width) + 200) * 4 + 3]);
            Assert.Equal(0, joined.Bgra[((400 * joined.Width) + 10) * 4 + 3]);
        }

        [Fact]
        public void OtherSizesAreScaledToFit()
        {
            var wide = new PixelBuffer(1600, 900, new byte[1600 * 900 * 4]);
            BothScreens.Split(wide, true, NewXL, out var top, out var bottom);
            Assert.Equal((512, 256), (top.Width, top.Height));
            Assert.Equal((512, 256), (bottom.Width, bottom.Height));
        }

        [AvaloniaFact]
        public void LoadingOnePictureFillsBothWallpapersAndSwitchesThemOn()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }

            var t = win.ViewModel;
            BothScreens.Split(Picture(), true, NewXL, out var top, out var bottom);
            win.SetBothScreens(top, bottom);

            Assert.Equal(512, t.Textures.Top.Width);
            Assert.Equal(512, t.Textures.Bottom.Width);
            Assert.Equal(TopDrawType.Texture, t.Flags.TopDrawType);
            Assert.Equal(BottomDrawType.Texture, t.Flags.BottomDrawType);
            // Bottom texture's first pixel comes from 46 px in, below the hinge (row 313) (BGR565 keeps 5 bits of blue, 6 of green)
            var (x, y) = Source(t.Textures.Bottom.Pixels, 0, 0);
            Assert.InRange(x, 30, 62);
            Assert.InRange(y, 306, 318);
        }

        [Fact]
        public void TheChosenModelIsRememberedAndUnknownNamesFallBack()
        {
            var s = ThemeEditor.App.Properties.Settings.Default;
            var saved = s.BothScreens_Model;
            try
            {
                s.BothScreens_Model = "new 3ds";
                Assert.Equal("New 3DS", s.BothScreens_Model);
                s.BothScreens_Model = "Virtual Boy";
                Assert.Equal(BothScreens.DefaultModel.Name, s.BothScreens_Model);
            }
            finally
            {
                s.BothScreens_Model = saved;
            }
        }
    }
}
