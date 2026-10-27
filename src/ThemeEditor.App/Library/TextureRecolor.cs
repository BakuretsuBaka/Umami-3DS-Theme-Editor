// --------------------------------------------------
// Umami 3DS Theme Editor - TextureRecolor.cs
// --------------------------------------------------

using System;
using SkiaSharp;

namespace ThemeEditor.App.Library
{
    /// <summary>Colour variations of library textures: hue rotation, saturation and brightness.</summary>
    public static class TextureRecolor
    {
        public static readonly int[] PresetHues = { 0, 45, 90, 135, 180, 225, 270, 315 };

        public static bool IsIdentity(float hue, float saturation, float brightness)
        {
            return Math.Abs(hue) < 0.01f && Math.Abs(saturation - 1) < 0.001f && Math.Abs(brightness) < 0.001f;
        }

        /// <summary>
        ///     Returns a recoloured copy. <paramref name="hue" /> is in degrees, <paramref name="saturation" /> a factor
        ///     (1 = unchanged) and <paramref name="brightness" /> an offset in [-1, 1].
        /// </summary>
        public static SKBitmap Apply(SKBitmap src, float hue, float saturation = 1f, float brightness = 0f)
        {
            var copy = src.Copy();
            if (IsIdentity(hue, saturation, brightness))
                return copy;

            var rad = hue * Math.PI / 180.0;
            var cos = (float) Math.Cos(rad);
            var sin = (float) Math.Sin(rad);
            // Hue rotation about the grey axis (luma weights), then saturation about luma, then an additive brightness
            const float lr = 0.213f, lg = 0.715f, lb = 0.072f;
            var h = new[]
            {
                lr + cos * (1 - lr) + sin * -lr,       lg + cos * -lg + sin * -lg,            lb + cos * -lb + sin * (1 - lb),
                lr + cos * -lr + sin * 0.143f,         lg + cos * (1 - lg) + sin * 0.140f,    lb + cos * -lb + sin * -0.283f,
                lr + cos * -lr + sin * -(1 - lr),      lg + cos * -lg + sin * lg,             lb + cos * (1 - lb) + sin * lb
            };
            var s = saturation;
            var sat = new[]
            {
                lr * (1 - s) + s, lg * (1 - s),     lb * (1 - s),
                lr * (1 - s),     lg * (1 - s) + s, lb * (1 - s),
                lr * (1 - s),     lg * (1 - s),     lb * (1 - s) + s
            };
            var m = new float[9];
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                m[r * 3 + c] = sat[r * 3] * h[c] + sat[r * 3 + 1] * h[3 + c] + sat[r * 3 + 2] * h[6 + c];

            var b = brightness * 255f;
            var matrix = new[]
            {
                m[0], m[1], m[2], 0, b,
                m[3], m[4], m[5], 0, b,
                m[6], m[7], m[8], 0, b,
                0, 0, 0, 1, 0
            };
            var result = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(result))
            using (var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(matrix) })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(src, 0, 0, paint);
            }
            copy.Dispose();
            return result;
        }

        /// <summary>
        ///     The part of a stored texture the console actually shows: folders are cropped to their 72x62 area, icon
        ///     textures hold the right half of the icon (the console mirrors it), so the full icon is rebuilt here.
        /// </summary>
        public static SKBitmap Visible(SKBitmap tex, LibraryKind kind, bool second)
        {
            if (kind == LibraryKind.FolderTexture)
            {
                var crop = new SKBitmap(72, 62, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var c = new SKCanvas(crop))
                    c.DrawBitmap(tex, new SKRect(second ? 5 : 1, 1, (second ? 5 : 1) + 72, 63), new SKRect(0, 0, 72, 62));
                return crop;
            }
            var w = second ? 25 : 34; var h = second ? 50 : 69; var x0 = second ? 0 : 1; var y0 = second ? 0 : 1;
            var full = new SKBitmap(w * 2, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(full))
            {
                var right = new SKRect(x0, y0, x0 + w, y0 + h);
                c.DrawBitmap(tex, right, new SKRect(w, 0, w * 2, h));
                c.Save(); c.Translate(w, 0); c.Scale(-1, 1);
                c.DrawBitmap(tex, right, new SKRect(0, 0, w, h));
                c.Restore();
            }
            return full;
        }
    }
}
