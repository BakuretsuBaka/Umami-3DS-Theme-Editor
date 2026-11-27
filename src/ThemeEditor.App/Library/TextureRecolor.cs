// --------------------------------------------------
// Umami 3DS Theme Editor - TextureRecolor.cs
// --------------------------------------------------

using System;
using SkiaSharp;

namespace ThemeEditor.App.Library
{
    /// <summary>
    ///     How a library texture is recoloured: optionally one colour range moved to a target colour ("make the blue parts
    ///     red", the rest untouched), then a hue turn, saturation and brightness over the whole texture.
    /// </summary>
    public sealed class TextureColors
    {
        /// <summary>Degrees the whole texture's hue is turned by.</summary>
        public float Hue;
        /// <summary>Saturation factor; 1 keeps it.</summary>
        public float Saturation = 1f;
        /// <summary>In [-1, 1]: below 0 towards black, above 0 towards white.</summary>
        public float Brightness;
        /// <summary>The hue (degrees) of the colour range that changes, or null.</summary>
        public float? SourceHue;
        /// <summary>The hue that range becomes, or null.</summary>
        public float? TargetHue;

        public bool IsIdentity => Math.Abs(Hue) < 0.01f && Math.Abs(Saturation - 1) < 0.001f && Math.Abs(Brightness) < 0.001f &&
                                  (SourceHue == null || TargetHue == null);
    }

    /// <summary>
    ///     Colour variations of library textures, worked out per pixel in HSV: only what is asked for changes (a hue turn keeps
    ///     saturation and brightness exactly, grey stays grey), unlike a colour matrix, which also shifts brightness.
    /// </summary>
    public static class TextureRecolor
    {
        /// <summary>The target colours offered as one-click variations (hue in degrees).</summary>
        public static readonly (string Key, float Hue)[] TargetColors =
        {
            ("Red", 0), ("Orange", 30), ("Yellow", 55), ("Green", 120), ("Turquoise", 175), ("Blue", 220), ("Purple", 275), ("Pink", 320)
        };

        /// <summary>A colour range covers this many degrees either side of its hue fully, fading out to <see cref="RangeFade" />.</summary>
        private const float RangeFull = 22f, RangeFade = 45f;
        /// <summary>Pixels less saturated than this count as grey: they never belong to a colour range.</summary>
        private const float GreyBelow = 0.15f;

        public static bool IsIdentity(float hue, float saturation, float brightness)
        {
            return new TextureColors { Hue = hue, Saturation = saturation, Brightness = brightness }.IsIdentity;
        }

        /// <summary>
        ///     Returns a recoloured copy. <paramref name="hue" /> is in degrees, <paramref name="saturation" /> a factor
        ///     (1 = unchanged) and <paramref name="brightness" /> in [-1, 1].
        /// </summary>
        public static SKBitmap Apply(SKBitmap src, float hue, float saturation = 1f, float brightness = 0f)
        {
            return Apply(src, new TextureColors { Hue = hue, Saturation = saturation, Brightness = brightness });
        }

        /// <summary>Returns a recoloured copy of <paramref name="src" />.</summary>
        public static SKBitmap Apply(SKBitmap src, TextureColors colors)
        {
            // Always premultiplied BGRA, whatever the PNG decoded to, which is what the pixel loop below reads
            var bitmap = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(src, 0, 0);
            }
            if (colors == null || colors.IsIdentity)
                return bitmap;

            var px = bitmap.Bytes;
            var shift = colors.SourceHue != null && colors.TargetHue != null ? Delta(colors.SourceHue.Value, colors.TargetHue.Value) : 0f;
            var scale = colors.Brightness < 0 ? 1 + colors.Brightness : 1 - colors.Brightness;
            var lift = colors.Brightness > 0 ? colors.Brightness : 0f;
            for (var i = 0; i < px.Length; i += 4)
            {
                var a = px[i + 3];
                if (a == 0)
                    continue;
                // Premultiplied BGRA to straight RGB in 0..1
                float r = px[i + 2] / (float) a, g = px[i + 1] / (float) a, b = px[i] / (float) a;
                ToHsv(r, g, b, out var h, out var sat, out var v);
                if (shift != 0f && sat >= GreyBelow)
                {
                    // Inside the range the hue moves fully; at its edges gradually, so there is no hard seam
                    var d = Math.Abs(Delta(colors.SourceHue.Value, h));
                    var w = d <= RangeFull ? 1f : d >= RangeFade ? 0f : 1f - (d - RangeFull) / (RangeFade - RangeFull);
                    w *= Math.Min(1f, (sat - GreyBelow) / 0.1f + 0.0001f);
                    h += shift * w;
                }
                h += colors.Hue;
                sat = Math.Min(1f, sat * colors.Saturation);
                FromHsv(h, sat, v, out r, out g, out b);
                r = r * scale + lift;
                g = g * scale + lift;
                b = b * scale + lift;
                px[i + 2] = (byte) Math.Round(Math.Clamp(r, 0f, 1f) * a);
                px[i + 1] = (byte) Math.Round(Math.Clamp(g, 0f, 1f) * a);
                px[i] = (byte) Math.Round(Math.Clamp(b, 0f, 1f) * a);
            }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, bitmap.GetPixels(), px.Length);
            bitmap.NotifyPixelsChanged();
            return bitmap;
        }

        /// <summary>
        ///     The texture's main colour: the most common hue among its clearly coloured pixels (grey, white, black and
        ///     transparent ones do not count), or null when it has none worth naming.
        /// </summary>
        public static float? MainHue(SKBitmap src)
        {
            var bins = new double[36];
            var counted = 0;
            var opaque = 0;
            for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                if (c.Alpha < 128)
                    continue;
                opaque++;
                ToHsv(c.Red / 255f, c.Green / 255f, c.Blue / 255f, out var h, out var s, out var v);
                if (s < 0.25f || v < 0.15f)
                    continue;
                counted++;
                bins[(int) (h / 10f) % 36] += s * v;
            }
            // A few coloured specks on a grey texture are no main colour
            if (counted == 0 || counted < opaque / 20)
                return null;
            var best = 0;
            for (var i = 1; i < 36; i++)
                if (bins[i] + bins[(i + 35) % 36] + bins[(i + 1) % 36] > bins[best] + bins[(best + 35) % 36] + bins[(best + 1) % 36])
                    best = i;
            // Weighted mean of the peak and its neighbours, on the circle
            double sx = 0, sy = 0;
            for (var k = -1; k <= 1; k++)
            {
                var bin = (best + k + 36) % 36;
                var angle = (bin * 10 + 5) * Math.PI / 180;
                sx += Math.Cos(angle) * bins[bin];
                sy += Math.Sin(angle) * bins[bin];
            }
            var mean = (float) (Math.Atan2(sy, sx) * 180 / Math.PI);
            return mean < 0 ? mean + 360 : mean;
        }

        /// <summary>The hue of one pixel, or null when it is too grey to stand for a colour.</summary>
        public static float? HueAt(SKBitmap src, int x, int y)
        {
            if (x < 0 || y < 0 || x >= src.Width || y >= src.Height)
                return null;
            var c = src.GetPixel(x, y);
            ToHsv(c.Red / 255f, c.Green / 255f, c.Blue / 255f, out var h, out var s, out var v);
            return c.Alpha < 128 || s < 0.25f || v < 0.15f ? (float?) null : h;
        }

        /// <summary>The shortest turn from hue <paramref name="from" /> to <paramref name="to" />, in (-180, 180].</summary>
        public static float Delta(float from, float to)
        {
            var d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        public static void ToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0)
                h = 0;
            else if (max == r)
                h = 60f * (((g - b) / d) % 6f);
            else if (max == g)
                h = 60f * ((b - r) / d + 2f);
            else
                h = 60f * ((r - g) / d + 4f);
            if (h < 0) h += 360f;
        }

        public static void FromHsv(float h, float s, float v, out float r, out float g, out float b)
        {
            h = ((h % 360f) + 360f) % 360f;
            var c = v * s;
            var x = c * (1 - Math.Abs((h / 60f) % 2f - 1));
            var m = v - c;
            float r1, g1, b1;
            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }
            r = r1 + m;
            g = g1 + m;
            b = b1 + m;
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
