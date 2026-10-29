// --------------------------------------------------
// Umami 3DS Theme Editor - BothScreens.cs
// --------------------------------------------------

namespace ThemeEditor.App
{
    /// <summary>
    ///     Cuts one picture into the top and bottom wallpapers so it runs on across both screens, the way they sit on
    ///     the console: the bottom screen is centred under the top one and the hinge between them hides a strip of the
    ///     picture.
    /// </summary>
    public static class BothScreens
    {
        // The top texture shows 412 px (400 on screen plus 6 on each side for the 3D offset)
        public const int TopWidth = 412;
        public const int ScreenHeight = 240;
        public const int BottomWidth = 320;

        /// <summary>Rows of the picture hidden behind the hinge.</summary>
        public const int Gap = 36;

        /// <summary>Where the bottom screen starts, measured from the left of the top texture.</summary>
        public const int BottomLeft = (TopWidth - BottomWidth) / 2;

        /// <summary>Size of the whole picture: both screens and the gap.</summary>
        public const int Width = TopWidth;
        public const int Height = ScreenHeight * 2 + Gap;

        private const int TextureWidth = 512;
        private const int TextureHeight = 256;

        /// <summary>
        ///     Scales <paramref name="image" /> to <see cref="Width" />x<see cref="Height" /> (cropping the overhang evenly
        ///     with <paramref name="crop" />, otherwise stretching) and cuts out the two 512x256 wallpapers.
        /// </summary>
        public static void Split(PixelBuffer image, bool crop, out PixelBuffer top, out PixelBuffer bottom)
        {
            var whole = image.Width == Width && image.Height == Height
                ? image
                : image.Fit(Width, Height, Width, Height, crop);
            top = whole.Crop(0, 0, TopWidth, ScreenHeight).ResizeNoScale(0, 0, TextureWidth, TextureHeight);
            bottom = whole.Crop(BottomLeft, ScreenHeight + Gap, BottomWidth, ScreenHeight)
                .ResizeNoScale(0, 0, TextureWidth, TextureHeight);
        }

        /// <summary>
        ///     The opposite of <see cref="Split" />: both wallpapers placed as they sit on the console, with the hinge
        ///     and the sides of the bottom screen left transparent. Either may be null.
        /// </summary>
        public static PixelBuffer Join(PixelBuffer top, PixelBuffer bottom)
        {
            var dst = new byte[Width * Height * 4];
            Copy(top, 0, 0, 0, TopWidth, dst);
            Copy(bottom, BottomLeft, ScreenHeight + Gap, 0, BottomWidth, dst);
            return new PixelBuffer(Width, Height, dst);
        }

        private static void Copy(PixelBuffer src, int x, int y, int srcX, int width, byte[] dst)
        {
            if (src == null)
                return;
            width = System.Math.Min(width, src.Width - srcX);
            var height = System.Math.Min(ScreenHeight, src.Height);
            for (var row = 0; row < height; row++)
                System.Buffer.BlockCopy(src.Bgra, (row * src.Width + srcX) * 4, dst, ((y + row) * Width + x) * 4, width * 4);
        }
    }
}
