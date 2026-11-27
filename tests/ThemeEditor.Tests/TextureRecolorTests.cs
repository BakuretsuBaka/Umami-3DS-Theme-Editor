using SkiaSharp;
using ThemeEditor.App.Library;
using Xunit;

namespace ThemeEditor.Tests
{
    public class TextureRecolorTests
    {
        /// <summary>A "folder": mostly blue in a dark and a light shade, with a yellow star, a white highlight and a grey outline.</summary>
        private static SKBitmap Folder()
        {
            var b = new SKBitmap(10, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (var y = 0; y < 10; y++)
            for (var x = 0; x < 10; x++)
                b.SetPixel(x, y, x < 5 ? new SKColor(30, 60, 200) : new SKColor(110, 150, 240));
            b.SetPixel(0, 0, new SKColor(240, 210, 30));
            b.SetPixel(1, 0, SKColors.White);
            b.SetPixel(2, 0, new SKColor(90, 90, 90));
            return b;
        }

        private static (float H, float S, float V) Hsv(SKColor c)
        {
            TextureRecolor.ToHsv(c.Red / 255f, c.Green / 255f, c.Blue / 255f, out var h, out var s, out var v);
            return (h, s, v);
        }

        [Fact]
        public void TheMainColourIsFound()
        {
            using (var folder = Folder())
                Assert.InRange(TextureRecolor.MainHue(folder).Value, 215, 235);
            using (var grey = new SKBitmap(10, 10))
            {
                grey.Erase(new SKColor(128, 128, 128));
                Assert.Null(TextureRecolor.MainHue(grey));
            }
        }

        [Fact]
        public void ATargetColourChangesOnlyThatColourAndKeepsItsShading()
        {
            using (var folder = Folder())
            {
                var main = TextureRecolor.MainHue(folder);
                using (var red = TextureRecolor.Apply(folder, new TextureColors {SourceHue = main, TargetHue = 0}))
                {
                    var dark = Hsv(red.GetPixel(2, 5));
                    var light = Hsv(red.GetPixel(7, 5));
                    // Both blues are red now, each keeping its own saturation and brightness
                    Assert.True(dark.H < 12 || dark.H > 348, "dark hue " + dark.H);
                    Assert.True(light.H < 12 || light.H > 348, "light hue " + light.H);
                    Assert.Equal(Hsv(folder.GetPixel(2, 5)).V, dark.V, 2);
                    Assert.Equal(Hsv(folder.GetPixel(7, 5)).S, light.S, 1);
                    // The star, the highlight and the outline are untouched
                    Assert.Equal(folder.GetPixel(0, 0), red.GetPixel(0, 0));
                    Assert.Equal(folder.GetPixel(1, 0), red.GetPixel(1, 0));
                    Assert.Equal(folder.GetPixel(2, 0), red.GetPixel(2, 0));
                }
            }
        }

        [Fact]
        public void TheHueSliderKeepsSaturationAndBrightnessAndGrey()
        {
            using (var folder = Folder())
            using (var turned = TextureRecolor.Apply(folder, 120f))
            {
                var before = Hsv(folder.GetPixel(2, 5));
                var after = Hsv(turned.GetPixel(2, 5));
                Assert.Equal(before.S, after.S, 1);
                Assert.Equal(before.V, after.V, 1);
                Assert.Equal((before.H + 120) % 360, after.H, 0);
                Assert.Equal(folder.GetPixel(2, 0), turned.GetPixel(2, 0));
            }
        }
    }
}
