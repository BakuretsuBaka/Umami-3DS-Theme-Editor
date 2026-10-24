// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewExporter.cs
// --------------------------------------------------

using System;
using Avalonia.Platform;
using SkiaSharp;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Previews
{
    /// <summary>Renders the preview.png files exported with a theme (static, no animation).</summary>
    public static class PreviewExporter
    {
        private static readonly TopRenderer Top = new TopRenderer();
        private static PixelBuffer _noise;

        private static PixelBuffer Noise
        {
            get
            {
                if (_noise == null)
                    using (var s = AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/BLACK_NOISE.png")))
                        _noise = PixelBuffer.Load(s);
                return _noise;
            }
        }

        public static byte[] RenderPng(ThemeViewModel theme, PreviewKind kind)
        {
            using (var bmp = Render(theme, kind))
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                return data.ToArray();
        }

        public static SKBitmap Render(ThemeViewModel theme, PreviewKind kind)
        {
            switch (kind)
            {
                case PreviewKind.Top:
                    return Controls.PreviewSurface.RenderToBitmap(c => Top.Draw(c, theme, false, 0), TopRenderer.Width, TopRenderer.Height);
                case PreviewKind.Bottom:
                    return Controls.PreviewSurface.RenderToBitmap(c => BottomRenderer.Draw(c, theme, false, 0), BottomRenderer.Width, BottomRenderer.Height);
                default:
                    return Controls.PreviewSurface.RenderToBitmap(c =>
                    {
                        // Tiled noise background like the editor's preview panel
                        using (var shader = SKShader.CreateBitmap(Noise.Sk, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat))
                        using (var paint = new SKPaint {Shader = shader})
                            c.DrawRect(new SKRect(0, 0, 412, 480), paint);
                        Top.Draw(c, theme, false, 0);
                        c.Save();
                        c.Translate((412 - 320) / 2, 240);
                        BottomRenderer.Draw(c, theme, false, 0);
                        c.Restore();
                    }, 412, 480);
            }
        }
    }
}
