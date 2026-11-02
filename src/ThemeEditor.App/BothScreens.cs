// --------------------------------------------------
// Umami 3DS Theme Editor - BothScreens.cs
// --------------------------------------------------

using System;
using System.Linq;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Cuts one picture into the top and bottom wallpapers so it runs on across both screens, the way they sit on
    ///     the console: the bottom screen is centred under the top one and the hinge between them hides a strip of the
    ///     picture. How tall that strip is depends on the model.
    /// </summary>
    public static class BothScreens
    {
        /// <summary>A console model and how many rows of the picture its hinge hides.</summary>
        public sealed class Model
        {
            public string Name { get; }
            public int Gap { get; }

            public Model(string name, int gap)
            {
                Name = name;
                Gap = gap;
            }

            public int Height => ScreenHeight * 2 + Gap;
        }

        /// <summary>
        ///     Gaps between the lit areas of the screens, in screen pixels, measured on straight-on product photos
        ///     (about ±2 px; Nintendo does not publish them). The original 3DS XL and the 2DS are not measured yet.
        /// </summary>
        public static readonly Model[] Models =
        {
            new Model("3DS", 92),
            new Model("New 3DS", 88),
            new Model("New 3DS XL", 73),
            new Model("New 2DS XL", 85)
        };

        public static Model DefaultModel => Models[2];

        /// <summary>The model with this name, or the default one.</summary>
        public static Model Find(string name) =>
            Models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? DefaultModel;

        // The top texture shows 412 px (400 on screen plus 6 on each side for the 3D offset)
        public const int TopWidth = 412;
        public const int ScreenHeight = 240;
        public const int BottomWidth = 320;

        /// <summary>Where the bottom screen starts, measured from the left of the top texture.</summary>
        public const int BottomLeft = (TopWidth - BottomWidth) / 2;

        /// <summary>Width of the whole picture; its height is <see cref="Model.Height" />.</summary>
        public const int Width = TopWidth;

        private const int TextureWidth = 512;
        private const int TextureHeight = 256;

        /// <summary>
        ///     Scales <paramref name="image" /> to <see cref="Width" /> x <see cref="Model.Height" /> (cropping the
        ///     overhang evenly with <paramref name="crop" />, otherwise stretching) and cuts out the two 512x256 wallpapers.
        /// </summary>
        public static void Split(PixelBuffer image, bool crop, Model model, out PixelBuffer top, out PixelBuffer bottom)
        {
            var height = model.Height;
            var whole = image.Width == Width && image.Height == height
                ? image
                : image.Fit(Width, height, Width, height, crop);
            top = whole.Crop(0, 0, TopWidth, ScreenHeight).ResizeNoScale(0, 0, TextureWidth, TextureHeight);
            bottom = whole.Crop(BottomLeft, ScreenHeight + model.Gap, BottomWidth, ScreenHeight)
                .ResizeNoScale(0, 0, TextureWidth, TextureHeight);
        }

        /// <summary>
        ///     The opposite of <see cref="Split" />: both wallpapers placed as they sit on the console, with the hinge
        ///     and the sides of the bottom screen left transparent. Either may be null.
        /// </summary>
        public static PixelBuffer Join(PixelBuffer top, PixelBuffer bottom, Model model)
        {
            var dst = new byte[Width * model.Height * 4];
            Copy(top, 0, 0, TopWidth, dst);
            Copy(bottom, BottomLeft, ScreenHeight + model.Gap, BottomWidth, dst);
            return new PixelBuffer(Width, model.Height, dst);
        }

        private static void Copy(PixelBuffer src, int x, int y, int width, byte[] dst)
        {
            if (src == null)
                return;
            width = Math.Min(width, src.Width);
            var height = Math.Min(ScreenHeight, src.Height);
            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(src.Bgra, row * src.Width * 4, dst, ((y + row) * Width + x) * 4, width * 4);
        }
    }
}
