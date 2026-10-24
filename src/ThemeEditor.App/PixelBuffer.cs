// --------------------------------------------------
// Umami 3DS Theme Editor - PixelBuffer.cs
// --------------------------------------------------

using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ThemeEditor.App
{
    /// <summary>
    ///     CPU-side image (premultiplied BGRA) used for importing, resizing and encoding textures.
    ///     Built on SkiaSharp, so it behaves identically on Windows, Linux and macOS.
    /// </summary>
    public sealed class PixelBuffer
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>Premultiplied BGRA, 4 bytes per pixel, tightly packed.</summary>
        public byte[] Bgra { get; }

        public PixelBuffer(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public static PixelBuffer Load(string path)
        {
            using (var fs = File.OpenRead(path))
                return Load(fs);
        }

        public static PixelBuffer Load(Stream stream)
        {
            using (var data = SKData.Create(stream))
            using (var bmp = SKBitmap.Decode(data))
            {
                if (bmp == null)
                    throw new InvalidDataException("Unsupported image");
                return FromSkia(bmp);
            }
        }

        public static PixelBuffer FromBgr24(int width, int height, byte[] bgr)
        {
            var bgra = new byte[width * height * 4];
            for (int i = 0, j = 0; j < width * height * 3; i += 4, j += 3)
            {
                bgra[i] = bgr[j];
                bgra[i + 1] = bgr[j + 1];
                bgra[i + 2] = bgr[j + 2];
                bgra[i + 3] = 255;
            }
            return new PixelBuffer(width, height, bgra);
        }

        /// <summary>White with the given alpha plane, used for A8 (alpha mask) textures.</summary>
        public static PixelBuffer FromAlphaMask(int width, int height, byte[] mask)
        {
            var bgra = new byte[width * height * 4];
            for (int i = 0, j = 0; j < width * height; i += 4, j++)
            {
                // premultiplied white
                bgra[i] = bgra[i + 1] = bgra[i + 2] = bgra[i + 3] = mask[j];
            }
            return new PixelBuffer(width, height, bgra);
        }

        /// <summary>Color channels with the alpha dropped (composited over black), 3 bytes per pixel.</summary>
        public byte[] GetBgr24Data()
        {
            var bgr = new byte[Width * Height * 3];
            for (int i = 0, j = 0; i < Bgra.Length; i += 4, j += 3)
            {
                bgr[j] = Bgra[i];
                bgr[j + 1] = Bgra[i + 1];
                bgr[j + 2] = Bgra[i + 2];
            }
            return bgr;
        }

        public PixelBuffer Crop(int x, int y, int width, int height)
        {
            width = Math.Min(width, Width - x);
            height = Math.Min(height, Height - y);
            var dst = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(Bgra, ((y + row) * Width + x) * 4, dst, row * width * 4, width * 4);
            return new PixelBuffer(width, height, dst);
        }

        public PixelBuffer Resize(int width, int height, int margin = 0)
        {
            using (var src = ToSkia())
            using (var dst = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul)))
            using (var canvas = new SKCanvas(dst))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(src,
                    new SKRect(0, 0, src.Width, src.Height),
                    new SKRect(margin, margin, width - margin, height - margin),
                    paint);
                return FromSkia(dst);
            }
        }

        /// <summary>Copies unscaled into a (width x height) transparent canvas at (x, y).</summary>
        public PixelBuffer ResizeNoScale(int x, int y, int width, int height)
        {
            using (var src = ToSkia())
            using (var dst = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul)))
            using (var canvas = new SKCanvas(dst))
            {
                canvas.Clear(SKColors.Transparent);
                var w = Math.Min(src.Width, width);
                var h = Math.Min(src.Height, height);
                canvas.DrawBitmap(src, new SKRect(0, 0, w, h), new SKRect(x, y, x + w, y + h));
                return FromSkia(dst);
            }
        }

        public PixelBuffer ResizeNextPot()
        {
            var potW = (int) ((uint) Width).NextPowerOfTwo();
            var potH = (int) ((uint) Height).NextPowerOfTwo();
            if (potW == Width && potH == Height)
                return this;
            return ResizeNoScale(0, 0, potW, potH);
        }

        public byte[] EncodePng()
        {
            using (var bmp = ToSkia())
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                return data.ToArray();
        }

        public void SavePng(string path)
        {
            File.WriteAllBytes(path, EncodePng());
        }

        public WriteableBitmap ToBitmap()
        {
            var bmp = new WriteableBitmap(new PixelSize(Width, Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using (var fb = bmp.Lock())
            {
                for (var row = 0; row < Height; row++)
                    System.Runtime.InteropServices.Marshal.Copy(Bgra,
                        row * Width * 4,
                        fb.Address + row * fb.RowBytes,
                        Width * 4);
            }
            return bmp;
        }

        /// <summary>Snapshot of an Avalonia bitmap (e.g. from a RenderTargetBitmap).</summary>
        public static PixelBuffer FromBitmap(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms);
                ms.Position = 0;
                return Load(ms);
            }
        }

        private SKBitmap _sk;

        /// <summary>Cached SkiaSharp view of the pixels (never dispose it).</summary>
        public SKBitmap Sk => _sk ?? (_sk = ToSkia());

        private SKBitmap ToSkia()
        {
            var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(Bgra, 0, bmp.GetPixels(), Bgra.Length);
            return bmp;
        }

        private static PixelBuffer FromSkia(SKBitmap src)
        {
            using (var conv = src.ColorType == SKColorType.Bgra8888 && src.AlphaType == SKAlphaType.Premul
                       ? null
                       : src.Copy(SKColorType.Bgra8888))
            {
                var bmp = conv ?? src;
                if (bmp.AlphaType != SKAlphaType.Premul)
                {
                    using (var premul = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul)))
                    {
                        bmp.CopyTo(premul);
                        return Extract(premul);
                    }
                }
                return Extract(bmp);
            }
        }

        private static PixelBuffer Extract(SKBitmap bmp)
        {
            var bytes = new byte[bmp.Width * bmp.Height * 4];
            var rowBytes = bmp.RowBytes;
            var ptr = bmp.GetPixels();
            for (var y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(ptr + y * rowBytes, bytes, y * bmp.Width * 4, bmp.Width * 4);
            return new PixelBuffer(bmp.Width, bmp.Height, bytes);
        }
    }
}
