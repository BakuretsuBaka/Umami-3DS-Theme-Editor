// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewFonts.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using Avalonia.Platform;
using SkiaSharp;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Fonts for the previews. Inter is bundled so the previews look the same on every platform;
    ///     characters Inter cannot show (CJK dates, etc.) fall back to whatever the system provides.
    /// </summary>
    internal static class PreviewFonts
    {
        private static readonly Dictionary<string, SKTypeface> Cache = new Dictionary<string, SKTypeface>();

        public static SKTypeface Regular => Load("Inter-Regular");
        public static SKTypeface SemiBold => Load("Inter-SemiBold");
        public static SKTypeface Bold => Load("Inter-Bold");
        public static SKTypeface Mono => SKTypeface.FromFamilyName("Menlo") ?? SKTypeface.FromFamilyName("Consolas")
                                         ?? SKTypeface.FromFamilyName("DejaVu Sans Mono") ?? Regular;

        private static SKTypeface Load(string name)
        {
            lock (Cache)
            {
                SKTypeface tf;
                if (Cache.TryGetValue(name, out tf))
                    return tf;
                try
                {
                    using (var s = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{name}.ttf")))
                        tf = SKTypeface.FromStream(s);
                }
                catch (Exception)
                {
                    tf = null;
                }
                tf = tf ?? SKTypeface.Default;
                Cache[name] = tf;
                return tf;
            }
        }
    }

    internal static class PreviewText
    {
        /// <summary>Builds outline geometry for a string. Baseline is at y = ascent so that (0,0) is the top-left.</summary>
        public static SKPath BuildPath(string text, SKTypeface face, float size, out float width, out float height)
        {
            var path = new SKPath();
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(text))
                return path;

            using (var paint = new SKPaint { Typeface = face, TextSize = size, IsAntialias = true, SubpixelText = true })
            {
                var m = paint.FontMetrics;
                var baseline = -m.Ascent;
                height = m.Descent - m.Ascent + m.Leading;
                float x = 0;
                foreach (var cluster in Clusters(text))
                {
                    SKPaint use = paint;
                    SKPaint fallback = null;
                    var glyphs = face.GetGlyphs(cluster);
                    if (glyphs.Length == 0 || glyphs[0] == 0)
                    {
                        var alt = SKFontManager.Default.MatchCharacter(char.ConvertToUtf32(cluster, 0));
                        if (alt != null)
                        {
                            fallback = new SKPaint { Typeface = alt, TextSize = size, IsAntialias = true, SubpixelText = true };
                            use = fallback;
                        }
                    }
                    using (var glyphPath = use.GetTextPath(cluster, x, baseline))
                        path.AddPath(glyphPath);
                    x += use.MeasureText(cluster);
                    fallback?.Dispose();
                }
                width = x;
            }
            return path;
        }

        public static float Measure(string text, SKTypeface face, float size)
        {
            float w, h;
            using (BuildPath(text, face, size, out w, out h)) {}
            return w;
        }

        public static float LineHeight(SKTypeface face, float size)
        {
            using (var paint = new SKPaint { Typeface = face, TextSize = size })
            {
                var m = paint.FontMetrics;
                return m.Descent - m.Ascent + m.Leading;
            }
        }

        /// <summary>
        ///     Draws outlined text like the original TextPath control: optional "faux bold" (the glyphs unioned with
        ///     their widened outline), filled and stroked.
        /// </summary>
        public static void Draw(SKCanvas c, string text, float x, float y, SKTypeface face, float size,
            SKColor? fill, SKColor? stroke, float strokeWidth, bool fauxBold = false)
        {
            float w, h;
            using (var path = BuildPath(text, face, size, out w, out h))
            {
                c.Save();
                c.Translate(x, y);
                SKPath outline = path;
                SKPath widened = null;
                SKPath combined = null;
                if (fauxBold && stroke.HasValue)
                {
                    using (var p = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth })
                        widened = p.GetFillPath(path);
                    combined = SkiaHelpers.Combine(path, widened, SKPathOp.Union);
                    outline = combined;
                }
                if (fill.HasValue)
                    SkiaHelpers.FillPath(c, outline, fill.Value);
                if (stroke.HasValue && strokeWidth > 0)
                    SkiaHelpers.StrokePath(c, outline, stroke.Value, strokeWidth);
                widened?.Dispose();
                combined?.Dispose();
                c.Restore();
            }
        }

        private static IEnumerable<string> Clusters(string text)
        {
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
                yield return (string) e.Current;
        }
    }
}
