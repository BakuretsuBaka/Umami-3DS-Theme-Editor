// --------------------------------------------------
// Umami 3DS Theme Editor - WarpBackground.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform;
using SkiaSharp;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     CPU port of the original WarpEffect pixel shader: the perspective-warped, scrolling dot pattern drawn on
    ///     the 3DS HOME menu's top screen when a solid color (with optional texture) background is used.
    /// </summary>
    internal sealed class WarpBackground
    {
        public const int Width = 412;
        public const int Height = 240;

        private static PixelBuffer _defaultMask;

        private readonly byte[] _pixels = new byte[Width * Height * 4];

        public float GrayLevel { get; set; }
        public float Times { get; set; } = 3.5f;
        public float Pinch { get; set; } = 0.15f;
        public float Offset { get; set; } = 0.5f;
        public float Aspect { get; set; } = 2f;
        public float Gradient { get; set; } = 0.6f;
        public float PatternOpacity { get; set; } = 0.25f;
        public float AlternateOpacity { get; set; } = 1f;

        public static PixelBuffer DefaultMask
        {
            get
            {
                if (_defaultMask == null)
                    using (var s = AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/TOPALT_DEFMASK.png")))
                        _defaultMask = PixelBuffer.Load(s);
                return _defaultMask;
            }
        }

        public byte[] Pixels => _pixels;

        /// <summary>
        ///     Renders into <see cref="Pixels" />. <paramref name="baseColor" /> is the flat background color,
        ///     <paramref name="moving" />/<paramref name="fixedMask" /> the pattern textures.
        /// </summary>
        public void Render(SKColor baseColor, PixelBuffer moving, PixelBuffer fixedMask)
        {
            var bR = baseColor.Red / 255f;
            var bG = baseColor.Green / 255f;
            var bB = baseColor.Blue / 255f;
            var gray = GrayLevel;
            var times = Times;
            var pinch = Pinch;
            var aspect = Aspect;
            var offset = Offset;
            var gradient = Gradient;
            var opacity = PatternOpacity;
            var fixedOpacity = AlternateOpacity;

            Parallel.For(0, Height, y =>
            {
                var v = (y + 0.5f) / Height;
                var rate = 1 - v;
                for (var x = 0; x < Width; x++)
                {
                    var u = (x + 0.5f) / Width;

                    // 3D side strips baked into the base image (OnRender_3DCorners)
                    var br = bR;
                    var bg = bG;
                    var bb = bB;
                    if (x < 6 || x >= 406)
                    {
                        const float keep = 1 - 100f / 255f;
                        br *= keep;
                        bg *= keep;
                        bb *= keep;
                    }

                    var halfX = 0.5f - u;
                    var halfY = v - 0.5f;
                    var yMod = halfY / (1 - (float) Math.Sin(u * 3.14f) * pinch);
                    var xMod = 1 - Math.Abs(halfX) * pinch;

                    var wx = (100 + xMod * halfX * aspect) * times;
                    var wy = (100 + (0.5f + yMod)) * times;
                    var fx = wx + 0.5f;
                    var fy = wy - pinch;
                    var mx = fx + offset;
                    var my = fy - offset;

                    var overlayMoving = Sample(moving, Frac(mx), Frac(my));
                    var overlayFixed = Sample(fixedMask, Frac(fx), Frac(fy));
                    var overlay = Math.Max(0, Math.Min(1, overlayMoving + overlayFixed * fixedOpacity)) * opacity;

                    var backT = rate * gradient;
                    var foreT = 0.5f + rate * gradient;

                    var o = (y * Width + x) * 4;
                    _pixels[o] = Out(Lerp(Lerp(bb, gray, backT), Lerp(bb, 1.2f * gray, foreT), overlay));
                    _pixels[o + 1] = Out(Lerp(Lerp(bg, gray, backT), Lerp(bg, 1.2f * gray, foreT), overlay));
                    _pixels[o + 2] = Out(Lerp(Lerp(br, gray, backT), Lerp(br, 1.2f * gray, foreT), overlay));
                    _pixels[o + 3] = 255;
                }
            });
        }

        public void DrawTo(SKCanvas canvas)
        {
            var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var bmp = new SKBitmap())
            {
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(_pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    bmp.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
                    canvas.DrawBitmap(bmp, 0, 0);
                }
                finally
                {
                    handle.Free();
                }
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static byte Out(float v)
        {
            return (byte) (Math.Max(0, Math.Min(1, v)) * 255 + 0.5f);
        }

        private static float Frac(float v)
        {
            return v - (float) Math.Floor(v);
        }

        /// <summary>Bilinear wrap-around sample of the premultiplied red channel (0..1).</summary>
        private static float Sample(PixelBuffer tex, float u, float v)
        {
            var w = tex.Width;
            var h = tex.Height;
            var fx = u * w - 0.5f;
            var fy = v * h - 0.5f;
            var x0 = (int) Math.Floor(fx);
            var y0 = (int) Math.Floor(fy);
            var tx = fx - x0;
            var ty = fy - y0;
            var x1 = (x0 + 1) % w;
            var y1 = (y0 + 1) % h;
            x0 = (x0 % w + w) % w;
            y0 = (y0 % h + h) % h;
            var d = tex.Bgra;
            float p00 = d[(y0 * w + x0) * 4 + 2];
            float p10 = d[(y0 * w + x1) * 4 + 2];
            float p01 = d[(y1 * w + x0) * 4 + 2];
            float p11 = d[(y1 * w + x1) * 4 + 2];
            var top = p00 + (p10 - p00) * tx;
            var bot = p01 + (p11 - p01) * tx;
            return (top + (bot - top) * ty) / 255f;
        }
    }
}
