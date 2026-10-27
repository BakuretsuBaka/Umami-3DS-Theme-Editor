// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewExporter.cs
// --------------------------------------------------

using SkiaSharp;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Previews
{
    /// <summary>Renders the preview.png files exported with a theme (static, no animation).</summary>
    public static class PreviewExporter
    {
        private static readonly TopRenderer Top = new TopRenderer();

        public static byte[] RenderPng(ThemeViewModel theme, PreviewKind kind, PreviewInteraction state = null)
        {
            using (var bmp = Render(theme, kind, state))
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                return data.ToArray();
        }

        public static SKBitmap Render(ThemeViewModel theme, PreviewKind kind, PreviewInteraction state = null)
        {
            switch (kind)
            {
                case PreviewKind.Top:
                    return Controls.PreviewSurface.RenderToBitmap(c => Top.Draw(c, theme, false, 0), TopRenderer.Width, TopRenderer.Height);
                case PreviewKind.Bottom:
                    return Controls.PreviewSurface.RenderToBitmap(c => BottomRenderer.Draw(c, theme, false, 0, state), BottomRenderer.Width, BottomRenderer.Height);
                default:
                    return Controls.PreviewSurface.RenderToBitmap(c =>
                    {
                        // The strips beside the narrower bottom screen stay transparent
                        Top.Draw(c, theme, false, 0);
                        c.Save();
                        c.Translate((412 - 320) / 2, 240);
                        BottomRenderer.Draw(c, theme, false, 0, state);
                        c.Restore();
                    }, 412, 480);
            }
        }
    }
}
