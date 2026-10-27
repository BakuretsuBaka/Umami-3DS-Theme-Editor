// --------------------------------------------------
// Umami 3DS Theme Editor - TopRenderer.cs
// --------------------------------------------------

using System;
using SkiaSharp;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Localization.Enums;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Draws the 3DS top screen preview: warped dot wallpaper or scrolling texture, the system bar and the
    ///     camera/shoulder button bar, and optionally the rotating 3D folder.
    /// </summary>
    internal sealed class TopRenderer
    {
        public const int Width = 412;
        public const int Height = 240;

        private static readonly SKColor DefaultBackground = new SKColor(204, 204, 217);

        private readonly WarpBackground _warp = new WarpBackground();
        private readonly MeshRenderer _mesh = new MeshRenderer();

        public void Draw(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            c.Save();
            c.ClipRect(new SKRect(0, 0, Width, Height));

            DrawBackground(c, t, animate, time);
            DrawSystemBar(c);
            DrawBottomBar(c, t);
            // The screen-edge shading lies over everything, bars included
            DrawSideShade(c);

            if (Settings.Default.Preview_ModeToggle)
                DrawFolder(c, t, animate, time);

            c.Restore();
        }

        // ================================================================ Wallpaper

        private void DrawBackground(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            if (t == null)
            {
                SkiaHelpers.FillPath(c, SkiaHelpers.RoundRect(0, 0, Width, Height), DefaultBackground);
                return;
            }

            switch (t.Flags.TopDrawType)
            {
                case TopDrawType.SolidColor:
                case TopDrawType.SolidColorTexture:
                    DrawWarp(c, t, t.Colors.TopBackground.Main.ToSk(), animate, time);
                    break;
                case TopDrawType.Texture:
                    DrawTexture(c, t, animate, time);
                    break;
                default:
                    DrawWarp(c, t, DefaultBackground, animate, time);
                    break;
            }
        }

        private void DrawWarp(SKCanvas c, ThemeViewModel t, SKColor baseColor, bool animate, double time)
        {
            var bg = t.Colors.TopBackground;
            var textured = t.Flags.TopDrawType == TopDrawType.SolidColorTexture;

            _warp.GrayLevel = (float) bg.GradientColor;
            _warp.Gradient = (float) bg.Gradient;
            _warp.PatternOpacity = (float) bg.TextureOpacity;
            _warp.AlternateOpacity = (float) bg.AlternateOpacity;
            _warp.Offset = animate ? (float) (time / 10.0) : 0;

            var moving = textured ? t.Textures.Top.Pixels ?? WarpBackground.DefaultMask : WarpBackground.DefaultMask;
            var fixedMask = textured ? t.Textures.TopAlt.Pixels ?? WarpBackground.DefaultMask : WarpBackground.DefaultMask;

            _warp.Render(baseColor, moving, fixedMask);
            _warp.DrawTo(c);
        }

        private static void DrawTexture(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            var tex = t.Textures.Top.Pixels;
            if (tex != null)
            {
                var frame = t.Flags.TopFrameType;
                if (frame == TopFrameType.SlowScroll || frame == TopFrameType.FastScroll)
                {
                    const int scrOffset = 1008 - 412 / 2;
                    const int offset3D = 6;
                    var pos = animate ? (Math.Sin(time / 3) + 1) * scrOffset : 0;
                    pos -= offset3D;
                    if (pos <= offset3D)
                        c.DrawBitmap(tex.Sk, (float) (-pos - 1008), 0);
                    if (pos < 1008)
                        c.DrawBitmap(tex.Sk, (float) -pos, 0);
                    if (pos + Width > 1008)
                        c.DrawBitmap(tex.Sk, (float) (-pos + 1007), 0);
                }
                else
                {
                    c.DrawBitmap(tex.Sk, 0, 0);
                }
            }
        }

        private static void DrawSideShade(SKCanvas c)
        {
            using (var paint = SkiaHelpers.FillPaint(new SKColor(0, 0, 0, 100)))
            {
                c.DrawRect(new SKRect(0, 0, 6, Height), paint);
                c.DrawRect(new SKRect(406, 0, 412, Height), paint);
            }
        }

        // ================================================================ System bar

        private static void DrawSystemBar(SKCanvas c)
        {
            var white = SKColors.White;
            var barText = SkiaHelpers.Hex("#3d3d3d");
            var semi = PreviewFonts.SemiBold;

            // Wi-Fi
            c.Save();
            c.Translate(6, 2);
            var wifiColor = SkiaHelpers.Hex("#27b7eb");
            using (var wifi = new SKPath { FillType = SKPathFillType.EvenOdd })
            {
                wifi.AddOval(new SKRect(1.5f, 1.5f, 6.5f, 6.5f));
                wifi.AddRect(new SKRect(3, 7, 6, 17));
                wifi.AddRect(new SKRect(7, 12, 12, 17));
                wifi.AddRect(new SKRect(12, 7, 17, 17));
                wifi.AddRect(new SKRect(17, 2, 22, 17));
                SkiaHelpers.FillPath(c, wifi, wifiColor);
                using (var p = SkiaHelpers.StrokePaint(white, 1))
                {
                    p.IsAntialias = false;
                    c.DrawPath(wifi, p);
                }
            }
            c.Restore();

            // "Internet" button
            var button = new SKRect(31, 2, 141, 19);
            using (var rr = new SKPath())
            {
                rr.AddRoundRect(button, 2, 2);
                using (var shader = SkiaHelpers.Gradient(button, 0.5f, 0, 0.5f, 1,
                           new Stop(0, SkiaHelpers.Hex("#31aadf")), new Stop(0.5f, SkiaHelpers.Hex("#59c3f3")),
                           new Stop(0.5f, SkiaHelpers.Hex("#31aadf")), new Stop(1, SkiaHelpers.Hex("#59c3f3"))))
                    SkiaHelpers.FillPath(c, rr, shader);
                SkiaHelpers.StrokePath(c, rr, white, 0.75f);
            }
            {
                var face = PreviewFonts.Regular;
                const float size = 12;
                float w, h;
                using (PreviewText.BuildPath("Internet", face, size, out w, out h)) {}
                PreviewText.Draw(c, "Internet", button.Left + (button.Width - w) / 2,
                    button.Top + (button.Height - h) / 2 - 1, face, size, white, null, 0);
            }

            // Coins
            c.Save();
            c.Translate(150, 3);
            using (var fill = SkiaHelpers.FillPaint(SkiaHelpers.Hex("#D5C500")))
                c.DrawOval(new SKRect(0, 0, 16, 16), fill);
            using (var pen = SkiaHelpers.StrokePaint(white, 0.5f))
                c.DrawOval(new SKRect(0.25f, 0.25f, 15.75f, 15.75f), pen);
            using (var pen = SkiaHelpers.StrokePaint(white, 1))
            {
                c.DrawOval(new SKRect(2, 2, 14, 14), pen);
                c.DrawRect(new SKRect(6, 5, 10, 8), pen);
                c.DrawRect(new SKRect(6, 8, 10, 11), pen);
            }
            c.Restore();

            BarText(c, "300", 170, 0, semi, barText, white);

            // Battery
            using (var body = SkiaHelpers.Combine(SkiaHelpers.RoundRect(2, 0, 27, 17, 2, 2),
                       SkiaHelpers.RoundRect(0, 5, 2, 8), SKPathOp.Union))
            {
                c.Save();
                c.Translate(375.5f, 2);
                SkiaHelpers.FillPath(c, body, SKColors.Black);
                SkiaHelpers.StrokePath(c, body, white, 1);
                c.Restore();
            }
            {
                var r = new SKRect(380, 5, 401, 16);
                using (var shader = SkiaHelpers.Gradient(r, 0.5f, 0, 0.5f, 1,
                           new Stop(0, SKColors.Orange), new Stop(0.25f, SKColors.White),
                           new Stop(0.4f, SkiaHelpers.Hex("#FFCD8040")), new Stop(0.55f, SKColors.Orange)))
                using (var fill = new SKPaint { Shader = shader })
                    c.DrawRect(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), fill);
                using (var pen = SkiaHelpers.StrokePaint(white, 1))
                    c.DrawRect(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), pen);
            }

            // Clock and date
            var now = DateTime.Now;
            var hour = now.ToString(MainResources.Preview_HourFormat);
            var min = now.ToString(MainResources.Preview_MinFormat);
            var sep = now.Second % 2 == 0 ? " " : ":";
            var hourW = PreviewText.Measure(hour, semi, 15);
            var minW = PreviewText.Measure(min, semi, 15);
            var clockWidth = hourW + 7 + minW;
            var clockLeft = 412 - 43 - clockWidth;
            BarText(c, hour, clockLeft, 0, semi, barText, white);
            var sepW = PreviewText.Measure(sep, semi, 15);
            BarText(c, sep, clockLeft + hourW + (7 - sepW) / 2, -1, semi, barText, white);
            BarText(c, min, clockLeft + clockWidth - minW, 0, semi, barText, white);

            var date = now.ToString(MainResources.Preview_DateFormat);
            var dateW = PreviewText.Measure(date, semi, 15);
            BarText(c, date, 412 - 95 - dateW, 0, semi, barText, white);
        }

        private static void BarText(SKCanvas c, string text, float x, float y, SKTypeface face, SKColor fill, SKColor stroke)
        {
            PreviewText.Draw(c, text, x, y, face, 15, fill, stroke, 1, true);
        }

        // ================================================================ Bottom bar

        private static void DrawBottomBar(SKCanvas c, ThemeViewModel t)
        {
            var colored = t != null && t.Flags.TopCornerButtonColor;
            var fg = colored ? t.Colors.TopCorner.Text.ToSk() : SkiaHelpers.Hex("#3d3d3d");
            var bg = colored ? t.Colors.TopCorner.Main.ToSk() : SKColors.White;

            c.Save();
            c.Translate(0, 217);

            using (var bar = SkiaHelpers.Parse(
                       "M 0 23 L 0 0 L 76 0 Q 88 0 90 10 L 92 23 L 200 23 L 320 23 L 323 10 Q 326 0 334 0 L 412 0 L 412 23 L 0 23 Z"))
            {
                // Opacity .75 for the whole bar including its shadow
                c.SaveLayer(new SKPaint { Color = SKColors.White.WithOpacity(0.75) });
                SkiaHelpers.WithDropShadow(c, SKColors.Black, 0.5, 3, 0, 0, () => SkiaHelpers.FillPath(c, bar, bg));
                c.Restore();
            }

            // Left camera
            c.Save();
            c.Translate(22, 5);
            using (var a = SkiaHelpers.Parse("M 9 12 L 2 12 Q 0 12 0 10 L 0 2 Q 0 0 2 0 L 15 0 Q 17 0 17 2"))
                SkiaHelpers.StrokePath(c, a, fg, 2);
            using (var body = SkiaHelpers.Combine(SkiaHelpers.RoundRect(11, 6, 12, 8, 1.5f, 1.5f),
                       SkiaHelpers.RoundRect(14, 4, 6, 4, 3, 3), SKPathOp.Union))
            using (var lens = SkiaHelpers.Ellipse(17, 10, 2.5f, 2.5f))
            using (var group = new SKPath { FillType = SKPathFillType.EvenOdd })
            {
                group.AddPath(body);
                group.AddPath(lens);
                SkiaHelpers.FillPath(c, group, fg);
            }
            c.Restore();

            // Y button
            c.Save();
            c.Translate(52, 3);
            using (var ring = SkiaHelpers.Ellipse(8, 8, 7, 7))
                SkiaHelpers.StrokePath(c, ring, fg, 2);
            CornerText(c, "Y", 8, 8, fg);
            c.Restore();

            // Right camera
            c.Save();
            c.Translate(328, 5);   // camera, then L + R with even gaps
            using (var body = SkiaHelpers.Combine(SkiaHelpers.RoundRect(0, 2, 16, 12, 1.5f, 1.5f),
                       SkiaHelpers.RoundRect(4, 0, 8, 4, 3, 3), SKPathOp.Union))
            using (var lens = SkiaHelpers.Ellipse(8, 8, 3, 3))
            using (var group = new SKPath { FillType = SKPathFillType.EvenOdd })
            {
                group.AddPath(body);
                group.AddPath(lens);
                SkiaHelpers.FillPath(c, group, fg);
            }
            c.Restore();

            // L
            c.Save();
            c.Translate(349, 5);
            using (var shape = SkiaHelpers.Parse("M 0 13 Q 2 0 3 0 L 16 0 Q 15 2 13 13 Z"))
                SkiaHelpers.StrokePath(c, shape, fg, 1.5f);
            // Middle of the slanted plate (its corners are 0,13 3,0 16,0 13,13)
            CornerText(c, "L", 8, 6.5f, fg);
            c.Restore();

            // R
            c.Save();
            c.Translate(377, 5);
            using (var shape = SkiaHelpers.Parse("M 16 13 Q 14 0 13 0 L 0 0 Q 1 2 3 13 Z"))
                SkiaHelpers.StrokePath(c, shape, fg, 1.5f);
            CornerText(c, "R", 8, 6.5f, fg);
            c.Restore();

            // + between L (ends at 365) and R (starts at 377), level with the middle of the plates
            CornerText(c, "+", 371, 11.5f, fg);

            c.Restore();
        }

        /// <summary>Draws a button letter centred (by its actual outline) on (cx, cy).</summary>
        private static void CornerText(SKCanvas c, string text, float cx, float cy, SKColor color)
        {
            PreviewText.DrawCentered(c, text, cx, cy, PreviewFonts.Mono, 12, color, color, 1);
        }

        // ================================================================ 3D folder

        private static readonly MeshMaterial DefaultFolder = new MeshMaterial(
            SkiaHelpers.Hex("#92E1EE"), SkiaHelpers.Hex("#65A8BB"));

        private static readonly MeshMaterial DefaultFile = new MeshMaterial(SKColors.White, SKColors.White);

        private void DrawFolder(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            var rotation = animate ? time * -36 : 0;
            var bounce = animate ? (Math.Sin(time) + 1) / 2 : 0;

            var folder = DefaultFolder;
            var file = DefaultFile;
            if (t != null)
            {
                if (t.Flags.FolderColor)
                    folder = new MeshMaterial(t.Colors.Folder.Main.ToSk(), t.Colors.Folder.Dark.ToSk());
                if (t.Flags.FileColor)
                    file = new MeshMaterial(t.Colors.File.Main.ToSk(), t.Colors.File.Dark.ToSk());
            }
            _mesh.Draw(c, folder, file, rotation, bounce);
        }
    }
}
