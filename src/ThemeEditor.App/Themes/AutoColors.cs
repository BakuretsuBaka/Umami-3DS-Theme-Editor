// --------------------------------------------------
// Umami 3DS Theme Editor - AutoColors.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using ThemeEditor.App.Localization.Enums;

namespace ThemeEditor.App.Themes
{
    /// <summary>
    ///     Derives a complete colour scheme from the theme's wallpapers: a base tone (what most of the bottom screen
    ///     looks like), an accent (the most colourful hue that stands out from the base) and readable text. Every colour
    ///     set is then built from those as lighter or darker shades, and text and icons are checked against what they sit
    ///     on, so the result is always legible. The work happens in OKLab, where equal steps look equally bright.
    /// </summary>
    public static class AutoColors
    {
        public enum Style
        {
            Light,
            Dark,
            Vivid,
            Soft
        }

        /// <summary>The handful of colours a scheme is built from; also what the style picker shows as swatches.</summary>
        public sealed class Scheme
        {
            public Style Style;
            public Lch Base;    // dominant wallpaper tone
            public Lch Surface; // buttons, bars, file icons
            public Lch Accent;  // folders, cursor, arrows
            public Lch Text;    // text on surfaces
            public Lch Contrast; // the opposite surface (Close button)

            public IEnumerable<Color> Swatches => new[] { Surface, Accent, Base, Text }.Select(c => c.ToColor());
        }

        // Stock HOME Menu look, used when the theme has no wallpaper to take colours from
        private static readonly Lch StockBase = Lch.FromColor(Color.FromRgb(0xe9, 0xe9, 0xea));
        private static readonly Lch StockAccent = Lch.FromColor(Color.FromRgb(0x5a, 0xc8, 0xe6));

        // ================================================================ Suggestions

        /// <summary>The four styles for the theme's current images.</summary>
        public static List<Scheme> Suggest(ThemeViewModel t)
        {
            var bottom = Clusters(t.Textures.Bottom, 320);
            var top = Clusters(t.Textures.Top, 412);
            var all = bottom.Concat(top).ToList();

            var baseTone = bottom.Count > 0 ? bottom[0].Color : top.Count > 0 ? top[0].Color : StockBase;
            var accents = PickAccents(all, baseTone);
            return Enum.GetValues(typeof(Style)).Cast<Style>().Select(s => Build(s, baseTone, accents.Item1, accents.Item2)).ToList();
        }

        private static Scheme Build(Style style, Lch baseTone, Lch accent, Lch accent2)
        {
            var s = new Scheme { Style = style, Base = baseTone };
            switch (style)
            {
                case Style.Dark:
                    s.Surface = new Lch(0.30, Math.Min(baseTone.C * 0.8, 0.05), baseTone.H);
                    s.Accent = Vibrant(accent, 0.72);
                    break;
                case Style.Vivid:
                    s.Surface = new Lch(0.90, Math.Min(accent.C * 0.35, 0.07), accent.H);
                    s.Accent = Vibrant(accent2, 0.66);
                    break;
                case Style.Soft:
                    s.Surface = new Lch(0.92, Math.Min(Math.Max(baseTone.C, accent.C) * 0.25, 0.04), accent.H);
                    s.Accent = new Lch(0.80, Math.Min(accent.C * 0.6, 0.10), accent.H);
                    break;
                default:
                    s.Surface = new Lch(0.94, Math.Min(baseTone.C, 0.03), baseTone.H);
                    s.Accent = Vibrant(accent, 0.70);
                    break;
            }
            var lightSurface = s.Surface.L > 0.6;
            s.Text = Readable(new Lch(lightSurface ? 0.28 : 0.95, Math.Min(s.Surface.C, 0.02), s.Surface.H), s.Surface, 4.5);
            s.Contrast = lightSurface
                ? new Lch(0.34, Math.Min(s.Surface.C + 0.01, 0.04), s.Surface.H)
                : new Lch(0.86, Math.Min(s.Surface.C, 0.03), s.Surface.H);
            return s;
        }

        /// <summary>An accent bright and colourful enough to read as one, keeping its hue.</summary>
        private static Lch Vibrant(Lch c, double lightness)
        {
            return new Lch(Clamp(c.L, lightness - 0.08, lightness + 0.08), Math.Max(c.C, 0.11), c.H);
        }

        /// <summary>
        ///     The main accent is the most colourful cluster that differs from the base (weighted by how much of the
        ///     image it covers); the second one also differs in hue from the first. Greyscale images get a stock accent.
        /// </summary>
        private static Tuple<Lch, Lch> PickAccents(List<Cluster> clusters, Lch baseTone)
        {
            Func<Cluster, double> score = k => k.Color.C * Math.Sqrt(k.Weight) * Math.Min(1, Lch.Distance(k.Color, baseTone) / 0.15);
            var colourful = clusters.Where(k => k.Color.C > 0.045).OrderByDescending(score).ToList();
            if (colourful.Count == 0)
            {
                // Pastel wallpaper: a stronger version of its own hue (Vibrant raises the chroma)
                var tinted = clusters.Where(k => k.Color.C > 0.015).OrderByDescending(k => k.Color.C * Math.Sqrt(k.Weight)).FirstOrDefault();
                if (tinted != null)
                {
                    var own = new Lch(Math.Min(tinted.Color.L, 0.8), Math.Max(tinted.Color.C * 2.5, 0.11), tinted.Color.H);
                    return Tuple.Create(own, new Lch(own.L, own.C, own.H + 150));
                }
                // Black-and-white wallpaper: one calm stock accent
                return Tuple.Create(StockAccent, new Lch(StockAccent.L, StockAccent.C, StockAccent.H + 140));
            }
            var first = colourful[0].Color;
            var second = colourful.Skip(1).FirstOrDefault(k => HueDistance(k.Color.H, first.H) > 45)?.Color
                         ?? new Lch(first.L, first.C, first.H + 150);
            return Tuple.Create(first, second);
        }

        // ================================================================ Applying

        /// <summary>Sets every colour of the theme from <paramref name="s" /> and switches the colour flags on.</summary>
        public static void Apply(ThemeViewModel t, Scheme s)
        {
            var rules = t.Rules;
            var wasPaused = rules?.Paused ?? false;
            if (rules != null)
                rules.Paused = true;
            try
            {
                foreach (var set in AllSets)
                    FillSet(t, s, set);

                var f = t.Flags;
                f.FolderColor = f.FileColor = f.CursorColor = f.OpenCloseColor = true;
                f.BottomCornerButtonColor = f.TopCornerButtonColor = true;
                f.ArrowButtonColor = f.ArrowColor = f.FolderArrowColor = f.FolderBackgroundColor = true;
                f.DemoTextColor = true;
                if (f.GameTextDrawType != GameTextDrawType.Hidden)
                    f.GameTextDrawType = GameTextDrawType.Colored;
                // The solid backgrounds only exist in "solid colour" mode; how the screens are drawn is never changed
                if (f.BottomDrawType == BottomDrawType.SolidColor)
                    f.BottomBackgroundInnerColor = f.BottomBackgroundOuterColor = true;
            }
            finally
            {
                if (rules != null)
                {
                    rules.Paused = wasPaused;
                    rules.Apply(t.Flags);
                    rules.Apply(t.Colors);
                }
            }
        }

        /// <summary>The sets <see cref="FillSet" /> knows, by the name of their <see cref="ColorsViewModel" /> property.</summary>
        public static readonly string[] AllSets =
        {
            nameof(ColorsViewModel.Folder), nameof(ColorsViewModel.File), nameof(ColorsViewModel.Cursor),
            nameof(ColorsViewModel.Open), nameof(ColorsViewModel.Close), nameof(ColorsViewModel.BottomCorner),
            nameof(ColorsViewModel.TopCorner), nameof(ColorsViewModel.ArrowButton), nameof(ColorsViewModel.Arrow),
            nameof(ColorsViewModel.FolderArrow), nameof(ColorsViewModel.FolderBackground),
            nameof(ColorsViewModel.BottomBackgroundInner), nameof(ColorsViewModel.BottomBackgroundOuter),
            nameof(ColorsViewModel.TopBackground), nameof(ColorsViewModel.GameText), nameof(ColorsViewModel.DemoText)
        };

        /// <summary>Fills one colour set from the scheme (debug-only fields are left alone).</summary>
        public static void FillSet(ThemeViewModel t, Scheme s, string set)
        {
            var c = t.Colors;
            var surface = s.Surface;
            var light = surface.L > 0.6;
            var dark = new Lch(0.16, Math.Min(s.Base.C, 0.03), s.Base.H);
            switch (set)
            {
                case nameof(ColorsViewModel.Folder):
                    c.Folder.Main = s.Accent.ToColor();
                    c.Folder.Light = s.Accent.Lighter(0.13).ToColor();
                    c.Folder.Dark = s.Accent.Darker(0.16).ToColor();
                    c.Folder.Shadow = dark.ToColor();
                    break;
                case nameof(ColorsViewModel.File):
                    c.File.Main = surface.ToColor();
                    c.File.Light = surface.Lighter(0.04).ToColor();
                    c.File.Dark = surface.Darker(0.13).ToColor();
                    c.File.Shadow = dark.ToColor();
                    break;
                case nameof(ColorsViewModel.Cursor):
                {
                    // The cursor frames icons on the wallpaper: it has to stand out from the base tone
                    var cursor = Readable(s.Accent.Lighter(0.06), s.Base, 1.8);
                    c.Cursor.Main = cursor.ToColor();
                    c.Cursor.Glow = cursor.Lighter(0.08).ToColor();
                    c.Cursor.Shading = cursor.Darker(0.05).ToColor();
                    break;
                }
                case nameof(ColorsViewModel.Open):
                    FillButton(c.Open, surface, s.Text, s.Accent);
                    break;
                case nameof(ColorsViewModel.Close):
                    // Like the console, the Close button is the opposite of Open (dark next to a light Open button)
                    FillButton(c.Close, s.Contrast, Readable(s.Contrast.L > 0.6 ? s.Text : new Lch(0.95, 0.01, s.Contrast.H), s.Contrast, 4.5), s.Accent);
                    break;
                case nameof(ColorsViewModel.BottomCorner):
                {
                    c.BottomCorner.Main = surface.ToColor();
                    c.BottomCorner.Highlight = surface.Lighter(0.04).ToColor();
                    c.BottomCorner.Border = surface.Darker(0.09).ToColor();
                    var icon = Readable(s.Accent, surface, 3);
                    c.BottomCorner.IconTop = icon.Lighter(0.04).ToColor();
                    c.BottomCorner.IconBottom = icon.Darker(0.10).ToColor();
                    break;
                }
                case nameof(ColorsViewModel.TopCorner):
                    c.TopCorner.Main = surface.ToColor();
                    c.TopCorner.Text = s.Text.ToColor();
                    break;
                case nameof(ColorsViewModel.ArrowButton):
                    c.ArrowButton.Main = surface.ToColor();
                    c.ArrowButton.Glow = surface.Lighter(0.06).ToColor();
                    c.ArrowButton.Shading = surface.Darker(0.11).ToColor();
                    break;
                case nameof(ColorsViewModel.Arrow):
                {
                    var arrow = Readable(s.Accent.Darker(0.04), surface, 3);
                    c.Arrow.Unpressed = arrow.ToColor();
                    c.Arrow.Pressed = arrow.Darker(0.14).ToColor();
                    c.Arrow.Border = arrow.Darker(0.24).ToColor();
                    break;
                }
                case nameof(ColorsViewModel.FolderArrow):
                {
                    c.FolderArrow.Highlight = surface.Lighter(0.04).ToColor();
                    c.FolderArrow.Main = surface.Darker(0.06).ToColor();
                    c.FolderArrow.Shading = surface.Darker(0.26).ToColor();
                    c.FolderArrow.Shadow = dark.ToColor();
                    var arrow = Readable(s.Accent.Darker(0.04), surface.Darker(0.03), 3);
                    c.FolderArrow.ArrowUnpressed = arrow.ToColor();
                    c.FolderArrow.ArrowPressed = arrow.Darker(0.14).ToColor();
                    c.FolderArrow.ArrowGlow = surface.Lighter(0.08).ToColor();
                    break;
                }
                case nameof(ColorsViewModel.FolderBackground):
                    FillPanel(c.FolderBackground, Mix(surface, s.Base, 0.35).Darker(light ? 0.05 : -0.04), dark);
                    break;
                case nameof(ColorsViewModel.BottomBackgroundInner):
                    FillPanel(c.BottomBackgroundInner, new Lch(light ? 0.86 : 0.30, Math.Min(s.Base.C, 0.06), s.Base.H), dark);
                    break;
                case nameof(ColorsViewModel.BottomBackgroundOuter):
                {
                    var outer = new Lch(light ? 0.80 : 0.24, Math.Min(s.Base.C, 0.06), s.Base.H);
                    c.BottomBackgroundOuter.Main = outer.ToColor();
                    c.BottomBackgroundOuter.Stripe = outer.Darker(0.05).ToColor();
                    c.BottomBackgroundOuter.Glow = outer.Lighter(0.02).ToColor();
                    break;
                }
                case nameof(ColorsViewModel.TopBackground):
                    c.TopBackground.Main = new Lch(light ? 0.84 : 0.26, Math.Min(s.Base.C, 0.07), s.Base.H).ToColor();
                    break;
                case nameof(ColorsViewModel.GameText):
                    c.GameText.Main = surface.ToColor();
                    c.GameText.Light = surface.Lighter(0.05).ToColor();
                    c.GameText.Text = s.Text.ToColor();
                    break;
                case nameof(ColorsViewModel.DemoText):
                {
                    var bg = s.Accent.Darker(0.22);
                    c.DemoText.Main = bg.ToColor();
                    c.DemoText.Text = Readable(new Lch(0.97, 0.01, bg.H), bg, 4.5).ToColor();
                    break;
                }
            }
        }

        private static void FillButton(ColorSets.OpenCloseSetViewModel b, Lch face, Lch text, Lch accent)
        {
            var pressed = Mix(face, accent, 0.35).Darker(face.L > 0.6 ? 0.14 : -0.10);
            b.Unpressed = face.ToColor();
            b.Light = face.Lighter(0.06).ToColor();
            b.Pressed = pressed.ToColor();
            b.TextUnpressed = Readable(text, face, 4.5).ToColor();
            b.TextPressed = Readable(text, pressed, 4.5).ToColor();
            b.TextShadow = face.Lighter(face.L > 0.6 ? 0.06 : -0.06).ToColor();
        }

        /// <summary>The rounded inner panels: "Border" is the light rim, "Highlight" a mid line and "Glow" the drop shadow.</summary>
        private static void FillPanel(ColorSets.BottomBackgroundInnerSetViewModel p, Lch main, Lch shadow)
        {
            p.Main = main.ToColor();
            p.Border = main.Lighter(0.05).ToColor();
            p.Highlight = main.Darker(0.30).ToColor();
            p.Glow = shadow.ToColor();
        }

        // ================================================================ "Switched on but still black"

        /// <summary>Which colour sets a colour flag of <see cref="FlagsViewModel" /> turns on.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> SetsOfFlag = new Dictionary<string, string[]>
        {
            { nameof(FlagsViewModel.FolderColor), new[] { nameof(ColorsViewModel.Folder) } },
            { nameof(FlagsViewModel.FileColor), new[] { nameof(ColorsViewModel.File) } },
            { nameof(FlagsViewModel.CursorColor), new[] { nameof(ColorsViewModel.Cursor) } },
            { nameof(FlagsViewModel.OpenCloseColor), new[] { nameof(ColorsViewModel.Open), nameof(ColorsViewModel.Close) } },
            { nameof(FlagsViewModel.BottomCornerButtonColor), new[] { nameof(ColorsViewModel.BottomCorner) } },
            { nameof(FlagsViewModel.TopCornerButtonColor), new[] { nameof(ColorsViewModel.TopCorner) } },
            { nameof(FlagsViewModel.ArrowButtonColor), new[] { nameof(ColorsViewModel.ArrowButton) } },
            { nameof(FlagsViewModel.ArrowColor), new[] { nameof(ColorsViewModel.Arrow) } },
            { nameof(FlagsViewModel.FolderArrowColor), new[] { nameof(ColorsViewModel.FolderArrow) } },
            { nameof(FlagsViewModel.FolderBackgroundColor), new[] { nameof(ColorsViewModel.FolderBackground) } },
            { nameof(FlagsViewModel.BottomBackgroundInnerColor), new[] { nameof(ColorsViewModel.BottomBackgroundInner) } },
            { nameof(FlagsViewModel.BottomBackgroundOuterColor), new[] { nameof(ColorsViewModel.BottomBackgroundOuter) } },
            { nameof(FlagsViewModel.DemoTextColor), new[] { nameof(ColorsViewModel.DemoText) } },
            { nameof(FlagsViewModel.GameTextDrawType), new[] { nameof(ColorsViewModel.GameText) } }
        };

        /// <summary>True when every colour of the set is pure black, i.e. it was never set.</summary>
        public static bool IsUnset(ThemeViewModel t, string set)
        {
            var vm = typeof(ColorsViewModel).GetProperty(set)?.GetValue(t.Colors);
            if (vm == null)
                return false;
            var colors = vm.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(Color) && p.CanRead && p.CanWrite)
                .Select(p => (Color) p.GetValue(vm))
                .ToList();
            return colors.Count > 0 && colors.All(col => col.R == 0 && col.G == 0 && col.B == 0);
        }

        // ================================================================ Wallpaper analysis

        private sealed class Cluster
        {
            public Lch Color;
            public double Weight;
        }

        /// <summary>Colour clusters of the visible part of a wallpaper, heaviest (most covered area) first.</summary>
        private static List<Cluster> Clusters(TextureViewModel tex, int screenWidth)
        {
            var px = tex?.Pixels;
            var result = new List<Cluster>();
            if (px == null || (px.Width == 64 && px.Height == 64))
                return result; // no wallpaper, or an A8 pattern mask rather than a picture
            // Only what is on screen: the screen width of a 512 wide texture, 1008 of a 1024 wide (scrolling) one
            var w = Math.Min(px.Width, px.Width >= 1024 ? 1008 : screenWidth);
            var h = Math.Min(px.Height, 240);

            // 4 bits per channel buckets, sampled every second pixel
            var counts = new Dictionary<int, double>();
            var sums = new Dictionary<int, double[]>();
            var bgra = px.Bgra;
            for (var y = 0; y < h; y += 2)
            for (var x = 0; x < w; x += 2)
            {
                var i = (y * px.Width + x) * 4;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                var key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                if (!sums.TryGetValue(key, out var sum))
                    sums[key] = sum = new double[3];
                sum[0] += r;
                sum[1] += g;
                sum[2] += b;
                counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + 1;
            }
            var total = counts.Values.Sum();
            if (total == 0)
                return result;

            // Merge buckets that look alike, heaviest first
            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            {
                var sum = sums[kv.Key];
                var col = Lch.FromColor(Avalonia.Media.Color.FromRgb((byte) (sum[0] / kv.Value), (byte) (sum[1] / kv.Value), (byte) (sum[2] / kv.Value)));
                var near = result.FirstOrDefault(k => Lch.Distance(k.Color, col) < 0.07);
                if (near != null)
                    near.Weight += kv.Value / total;
                else
                    result.Add(new Cluster { Color = col, Weight = kv.Value / total });
            }
            return result.Where(k => k.Weight >= 0.002).OrderByDescending(k => k.Weight).Take(24).ToList();
        }

        // ================================================================ Colour maths

        /// <summary>Moves lightness until <paramref name="fg" /> reaches the contrast ratio against <paramref name="bg" />.</summary>
        public static Lch Readable(Lch fg, Lch bg, double ratio)
        {
            if (Contrast(fg, bg) >= ratio)
                return fg;
            // Try both directions and take the smaller change that works
            Lch best = fg;
            for (var step = 0.02; step <= 1.0; step += 0.02)
            {
                foreach (var dir in new[] { bg.L > 0.55 ? -1 : 1, bg.L > 0.55 ? 1 : -1 })
                {
                    var c = new Lch(Clamp(fg.L + dir * step, 0, 1), fg.C, fg.H);
                    if (Contrast(c, bg) >= ratio)
                        return c;
                    best = c;
                }
            }
            return bg.L > 0.55 ? new Lch(0, 0, 0) : new Lch(1, 0, 0);
        }

        /// <summary>WCAG contrast ratio (1 to 21).</summary>
        public static double Contrast(Lch a, Lch b)
        {
            var la = Luminance(a.ToColor());
            var lb = Luminance(b.ToColor());
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public static double Contrast(Color a, Color b) => Contrast(Lch.FromColor(a), Lch.FromColor(b));

        private static double Luminance(Color c)
        {
            Func<byte, double> lin = v =>
            {
                var s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
        }

        private static Lch Mix(Lch a, Lch b, double t)
        {
            var x = a.ToLab();
            var y = b.ToLab();
            return Lch.FromLab(x.Item1 + (y.Item1 - x.Item1) * t, x.Item2 + (y.Item2 - x.Item2) * t, x.Item3 + (y.Item3 - x.Item3) * t);
        }

        private static double HueDistance(double a, double b)
        {
            var d = Math.Abs(((a - b) % 360 + 360) % 360);
            return Math.Min(d, 360 - d);
        }

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        /// <summary>OKLCh colour: L 0..1, chroma, hue in degrees.</summary>
        public struct Lch
        {
            public readonly double L, C, H;

            public Lch(double l, double c, double h)
            {
                L = Clamp(l, 0, 1);
                C = Math.Max(0, c);
                H = ((h % 360) + 360) % 360;
            }

            public Lch Lighter(double amount) => new Lch(L + amount, C, H);
            public Lch Darker(double amount) => new Lch(L - amount, C, H);

            public Tuple<double, double, double> ToLab()
            {
                var r = H * Math.PI / 180;
                return Tuple.Create(L, C * Math.Cos(r), C * Math.Sin(r));
            }

            public static Lch FromLab(double l, double a, double b)
            {
                return new Lch(l, Math.Sqrt(a * a + b * b), Math.Atan2(b, a) * 180 / Math.PI);
            }

            public static double Distance(Lch x, Lch y)
            {
                var p = x.ToLab();
                var q = y.ToLab();
                return Math.Sqrt(Math.Pow(p.Item1 - q.Item1, 2) + Math.Pow(p.Item2 - q.Item2, 2) + Math.Pow(p.Item3 - q.Item3, 2));
            }

            public static Lch FromColor(Color c)
            {
                Func<byte, double> lin = v =>
                {
                    var s = v / 255.0;
                    return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                };
                double r = lin(c.R), g = lin(c.G), b = lin(c.B);
                var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
                var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
                var s2 = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
                return FromLab(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s2,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s2,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s2);
            }

            /// <summary>Back to sRGB; chroma is reduced until the colour fits instead of clipping the channels.</summary>
            public Color ToColor()
            {
                var c = C;
                for (var i = 0; i < 24; i++)
                {
                    double r, g, b;
                    if (ToLinear(L, c, H, out r, out g, out b) || c < 1e-4)
                        return Color.FromRgb(Encode(r), Encode(g), Encode(b));
                    c *= 0.85;
                }
                double r0, g0, b0;
                ToLinear(L, 0, H, out r0, out g0, out b0);
                return Color.FromRgb(Encode(r0), Encode(g0), Encode(b0));
            }

            private static bool ToLinear(double lightness, double chroma, double hue, out double r, out double g, out double b)
            {
                var hr = hue * Math.PI / 180;
                var a = chroma * Math.Cos(hr);
                var bb = chroma * Math.Sin(hr);
                var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * bb, 3);
                var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * bb, 3);
                var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * bb, 3);
                r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
                g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
                b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
                const double eps = 1e-4;
                return r >= -eps && r <= 1 + eps && g >= -eps && g <= 1 + eps && b >= -eps && b <= 1 + eps;
            }

            private static byte Encode(double v)
            {
                v = Clamp(v, 0, 1);
                var s = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
                return (byte) Math.Round(Clamp(s, 0, 1) * 255);
            }
        }
    }
}
