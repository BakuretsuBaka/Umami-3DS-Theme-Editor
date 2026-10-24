// --------------------------------------------------
// Umami 3DS Theme Editor - BottomRenderer.cs
// --------------------------------------------------

using System;
using System.IO;
using Avalonia.Platform;
using SkiaSharp;
using ThemeEditor.App.Localization.Enums;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Draws the 3DS bottom (touch) screen preview: wallpaper, grid items, folder view and button overlays.
    /// </summary>
    internal static class BottomRenderer
    {
        public const int Width = 320;
        public const int Height = 240;

        private const double Gap = Width / 6.0;

        private static PixelBuffer _iconStrip;

        private static PixelBuffer IconStrip
        {
            get
            {
                if (_iconStrip == null)
                    using (var s = AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/BOT_ICON_ALL.png")))
                        _iconStrip = PixelBuffer.Load(s);
                return _iconStrip;
            }
        }

        public static void Draw(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            var s = Settings.Default;
            c.Save();
            c.ClipRect(new SKRect(0, 0, Width, Height));

            DrawWallpaper(c, t, animate, time);
            DrawMainItems(c, t, s, animate, time);
            if (s.Preview_ModeToggle)
                DrawFolderView(c, t, s, animate, time);
            DrawOverlays(c, t, s);

            c.Restore();
        }

        // ================================================================ Wallpaper

        private static void DrawWallpaper(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            if (t == null)
            {
                DrawSolidWallpaper(c, null, false, false);
                return;
            }

            switch (t.Flags.BottomDrawType)
            {
                case BottomDrawType.SolidColor:
                    DrawSolidWallpaper(c, t, t.Flags.BottomBackgroundInnerColor, t.Flags.BottomBackgroundOuterColor);
                    break;
                case BottomDrawType.Texture:
                    DrawTextureWallpaper(c, t, animate, time);
                    break;
                default:
                    DrawSolidWallpaper(c, t, false, false);
                    break;
            }
        }

        private static void DrawSolidWallpaper(SKCanvas c, ThemeViewModel t, bool inner, bool outer)
        {
            var outerMain = new SKColor(226, 226, 234);
            var outerStripes = new SKColor(208, 208, 216);
            var outerGlow = new SKColor(227, 228, 233);
            var innerMain = new SKColor(223, 220, 215);
            var innerEdge = new SKColor(234, 234, 234);
            var innerGlow = new SKColor(127, 127, 127);
            var innerShadow = new SKColor(63, 63, 63);

            if (inner)
            {
                innerMain = t.Colors.BottomBackgroundInner.Main.ToSk();
                innerEdge = t.Colors.BottomBackgroundInner.Border.ToSk();
                innerGlow = t.Colors.BottomBackgroundInner.Highlight.ToSk();
                innerShadow = t.Colors.BottomBackgroundInner.Glow.ToSk();
            }
            if (outer)
            {
                outerMain = t.Colors.BottomBackgroundOuter.Main.ToSk();
                outerStripes = t.Colors.BottomBackgroundOuter.Stripe.ToSk();
                outerGlow = t.Colors.BottomBackgroundOuter.Glow.ToSk();
            }

            // Outer area
            SkiaHelpers.FillPath(c, SkiaHelpers.RoundRect(0, 0, Width, Height), outerMain);
            var left = new SKRect(0, 0, 24, Height);
            var right = new SKRect(Width - 24, 0, Width, Height);
            using (var paint = new SKPaint())
            {
                paint.Shader = SkiaHelpers.Gradient(left, 0, 0, 1, 0, new Stop(0, outerGlow.WithOpacity(0.3)),
                    new Stop(1, outerMain.WithOpacity(0.3)));
                c.DrawRect(left, paint);
                paint.Shader?.Dispose();
                paint.Shader = SkiaHelpers.Gradient(right, 0, 0, 1, 0, new Stop(0, outerMain.WithOpacity(0.3)),
                    new Stop(1, outerGlow.WithOpacity(0.3)));
                c.DrawRect(right, paint);
                paint.Shader?.Dispose();
            }
            for (var i = 0; i < 7; i++)
            {
                var opacity = (1 - i / 7f) / 2;
                using (var pen = SkiaHelpers.StrokePaint(outerStripes.WithOpacity(opacity), 0.5f))
                    c.DrawLine(0, Height - 4 * i - 2.5f, Width, Height - 4 * i - 2.5f, pen);
            }

            // Inner area
            DrawInnerPanel(c, new SKRect(12, 39, 12 + Width, 39 + 170), 3, innerMain, innerEdge, innerGlow, innerShadow);
            DrawSlots(c, 31, 3, innerEdge, innerGlow, 170);
        }

        /// <summary>Rounded panel with the layered soft shadow and glow line used for the folder and main areas.</summary>
        private static void DrawInnerPanel(SKCanvas c, SKRect area, int rows, SKColor main, SKColor edge, SKColor glow,
            SKColor shadow)
        {
            const float radius = 10;
            using (var shadowPaint = SkiaHelpers.FillPaint(shadow.WithOpacity(1 / 25.0)))
            {
                for (var pX = -2; pX <= 2; pX++)
                for (var pY = -2; pY <= 2; pY++)
                {
                    var off = area;
                    off.Offset(pX, pY + 2);
                    c.DrawRoundRect(off, radius, radius, shadowPaint);
                }
            }
            using (var fill = SkiaHelpers.FillPaint(main))
                c.DrawRoundRect(area, radius, radius, fill);
            using (var stroke = SkiaHelpers.StrokePaint(edge, 1))
                c.DrawRoundRect(area, radius, radius, stroke);
            using (var line = SkiaHelpers.StrokePaint(glow, 0.5f))
                c.DrawLine(area.Left + radius, area.Bottom + 1, area.Right - 2 * radius, area.Bottom + 1, line);
        }

        private static void DrawSlots(SKCanvas c, float top, int rows, SKColor edge, SKColor glow, float panelHeight)
        {
            var offset = (float) (top + (panelHeight - 2 * Gap) / 2);
            using (var edgePen = SkiaHelpers.StrokePaint(edge, 1))
            using (var glowPen = SkiaHelpers.StrokePaint(glow, 0.5f))
            {
                for (var px = 1; px < 7; px++)
                for (var py = 0; py < rows; py++)
                {
                    var area = new SKRect((float) (Gap * px - 8), (float) Math.Floor(Gap * py + offset), 0, 0);
                    area.Right = area.Left + 16;
                    area.Bottom = area.Top + 16;
                    c.DrawRoundRect(area, 2, 2, edgePen);
                    c.DrawLine(area.Left + 2, area.Top, area.Right - 2, area.Top, glowPen);
                }
            }
        }

        private static void DrawTextureWallpaper(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            var tex = t.Textures.Bottom.Pixels;
            if (tex != null)
            {
                var frame = t.Flags.BottomFrameType;
                var w = tex.Width;
                var h = tex.Height;
                if (frame == BottomFrameType.SlowScroll || frame == BottomFrameType.FastScroll)
                {
                    // 802 = ...? Eyeballed it until it synced
                    var pos = animate ? (Math.Sin(time / 3) + 1) * 802 : 0;
                    if (pos < 1008)
                        c.DrawBitmap(tex.Sk, (float) -pos, 0);
                    if (pos + Width > 1008)
                        c.DrawBitmap(tex.Sk, (float) (-pos + 1007), 0);
                }
                else if (frame == BottomFrameType.PageScroll)
                {
                    var pos = animate ? (int) Math.Floor(time % 3) : 0;
                    c.DrawBitmap(tex.Sk, -pos * Width, 0);
                }
                else if (frame == BottomFrameType.BounceScroll)
                {
                    var pos = animate ? (int) Math.Floor(time % 4) : 0;
                    pos = pos == 3 ? 1 : pos;
                    c.DrawBitmap(tex.Sk, -pos * Width, 0);
                }
                else
                {
                    c.DrawBitmap(tex.Sk, 0, 0);
                }
            }

            // Slots
            var offset = 31 + (170 - 2 * Gap) / 2;
            using (var slot = SkiaHelpers.FillPaint(SKColors.White.WithOpacity(0.2)))
            {
                for (var px = 1; px < 7; px++)
                for (var py = 0; py < 3; py++)
                    c.DrawRoundRect(new SKRect((float) (Gap * px - 8), (float) (Gap * py + offset),
                        (float) (Gap * px + 8), (float) (Gap * py + offset + 16)), 2, 2, slot);
            }
        }

        // ================================================================ Items (main view)

        private static void DrawMainItems(SKCanvas c, ThemeViewModel t, Settings s, bool animate, double time)
        {
            if (s.Preview_Main_CartEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Main_CartX, s.Preview_Main_CartY, false), ItemRenderer.DrawNoCart);
            if (s.Preview_Main_FileEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Main_FileX, s.Preview_Main_FileY, false), cv => ItemRenderer.DrawFile(cv, t));
            if (s.Preview_Main_FolderEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Main_FolderX, s.Preview_Main_FolderY, false), cv => ItemRenderer.DrawFolder(cv, t, false));
            // Cursor is hidden while the folder preview is displayed
            if (!s.Preview_ModeToggle && s.Preview_Main_CursorEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Main_CursorX, s.Preview_Main_CursorY, false), cv => ItemRenderer.DrawCursor(cv, t, animate, time));
        }

        private static void At(SKCanvas c, SKPoint p, Action<SKCanvas> draw)
        {
            c.Save();
            c.Translate(p.X, p.Y);
            draw(c);
            c.Restore();
        }

        // ================================================================ Folder view

        private static void DrawFolderView(SKCanvas c, ThemeViewModel t, Settings s, bool animate, double time)
        {
            DrawBackButton(c, t, s);

            var custom = t != null && t.Flags.FolderBackgroundColor;
            var innerMain = new SKColor(223, 220, 215);
            var innerEdge = new SKColor(234, 234, 234);
            var innerGlow = new SKColor(127, 127, 127);
            var innerShadow = new SKColor(63, 63, 63);
            if (custom)
            {
                innerMain = t.Colors.FolderBackground.Main.ToSk();
                innerEdge = t.Colors.FolderBackground.Border.ToSk();
                innerGlow = t.Colors.FolderBackground.Highlight.ToSk();
                innerShadow = t.Colors.FolderBackground.Glow.ToSk();
            }
            DrawInnerPanel(c, new SKRect(12, 69, 12 + Width, 69 + 140), 2, innerMain, innerEdge, innerGlow, innerShadow);
            DrawSlots(c, 86, 2, innerEdge, innerGlow, 140);

            if (s.Preview_Folder_FileEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Folder_FileX, s.Preview_Folder_FileY, true), cv => ItemRenderer.DrawFile(cv, t));
            if (s.Preview_Folder_FolderEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Folder_FolderX, s.Preview_Folder_FolderY, true), cv => ItemRenderer.DrawFolder(cv, t, true));
            if (s.Preview_Folder_CursorEnable)
                At(c, ItemRenderer.GridPosition(s.Preview_Folder_CursorX, s.Preview_Folder_CursorY, true), cv => ItemRenderer.DrawCursor(cv, t, animate, time));
        }

        private static void DrawBackButton(SKCanvas c, ThemeViewModel t, Settings s)
        {
            var pressed = s.Preview_PressedToggle;
            var arrowFlag = t != null && t.Flags.FolderArrowColor;
            var both = t != null && t.Flags.OpenCloseColor && pressed;

            var bounds = new SKRect(27, 47, 93, 77);
            SKColor fill0 = SkiaHelpers.Hex("#8c8781"), fill1 = SkiaHelpers.Hex("#4e4b44");
            var stroke = SkiaHelpers.Hex("#2b2d2a");
            var shadowColor = SKColors.Black;
            if (pressed)
            {
                fill0 = SkiaHelpers.Hex("#4e4b44");
                fill1 = SkiaHelpers.Hex("#2b2d2a");
                stroke = SkiaHelpers.Hex("#8c8781");
            }
            if (arrowFlag)
            {
                var fa = t.Colors.FolderArrow;
                fill0 = fa.Highlight.ToSk();
                fill1 = fa.Main.ToSk();
                stroke = fa.Shading.ToSk();
                shadowColor = fa.Shadow.ToSk();
            }
            if (both)
            {
                var fa = t.Colors.FolderArrow;
                fill0 = fa.ArrowGlow.ToSk();
                fill1 = fa.Shading.ToSk();
                stroke = fa.Highlight.ToSk();
            }

            using (var geom = SkiaHelpers.RoundRect(27, 47, 66, 30, 8, 8))
            {
                SkiaHelpers.WithDropShadow(c, shadowColor, 0.5, 8, 2, -90, () =>
                {
                    using (var shader = SkiaHelpers.VGradient(bounds, new Stop(0, fill0), new Stop(0.3f, fill1)))
                        SkiaHelpers.FillPath(c, geom, shader);
                    SkiaHelpers.StrokePath(c, geom, stroke, 1);
                });
            }

            // Arrow glyph
            using (var arrow = SkiaHelpers.Parse(
                       "M 7 4 L 10 4 A 6,6 0 1 1 10,16 L 7,16 7,13 10,13 A 3,3 0 1 0 10,7 L 7,7 7,10 0,5.5 7,0 Z"))
            {
                c.Save();
                c.Translate(51, 50 + (pressed ? 1 : 0));
                SKColor arrowFill = SKColors.White;
                var shadowOn = true;
                SKColor arrowShadow = SkiaHelpers.Hex("#2b2d2a");
                double shadowOpacity = 1;
                float shadowDepth = 1;
                float shadowDir = -90;
                if (pressed)
                {
                    arrowFill = SkiaHelpers.Hex("#bbbbbb");
                    shadowOn = false;
                }
                if (arrowFlag)
                {
                    var fa = t.Colors.FolderArrow;
                    arrowFill = fa.ArrowUnpressed.ToSk();
                    shadowOn = true;
                    arrowShadow = fa.ArrowGlow.ToSk();
                    shadowOpacity = 1;
                    shadowDepth = (float) Math.Abs(fa.ShadowPosition);
                    shadowDir = fa.ShadowPosition < 0 ? -90 : 90;
                }
                if (both)
                {
                    arrowFill = t.Colors.FolderArrow.ArrowPressed.ToSk();
                    shadowOn = false;
                }
                if (shadowOn)
                    SkiaHelpers.WithDropShadow(c, arrowShadow, shadowOpacity, 2, shadowDepth, shadowDir,
                        () => SkiaHelpers.FillPath(c, arrow, arrowFill));
                else
                    SkiaHelpers.FillPath(c, arrow, arrowFill);
                c.Restore();
            }
        }

        // ================================================================ Overlays

        private static void DrawOverlays(SKCanvas c, ThemeViewModel t, Settings s)
        {
            DrawOpenClose(c, t, s);
            DrawCorners(c, t, s);
            DrawArrowButton(c, t, s);
        }

        private static SKPath OpenGeometry(bool closeEnabled)
        {
            var main = closeEnabled
                ? SkiaHelpers.RoundRect(0, -25, 214, 50, 10, 10)
                : SkiaHelpers.RoundRect(-106, -25, 320, 50, 10, 10);
            var tab = SkiaHelpers.RoundRect(0, -25, 20, 50);
            return SkiaHelpers.Combine(main, tab, SKPathOp.Union);
        }

        private static SKPath CloseGeometry(bool closeEnabled)
        {
            if (!closeEnabled)
                return new SKPath();
            var main = SkiaHelpers.RoundRect(0, -25, 106, 50, 10, 10);
            var tab = SkiaHelpers.RoundRect(86, -25, 20, 50);
            return SkiaHelpers.Combine(main, tab, SKPathOp.Union);
        }

        private static void DrawOpenClose(SKCanvas c, ThemeViewModel t, Settings s)
        {
            var closeEnabled = s.Preview_CloseToggle;
            var pressed = s.Preview_PressedToggle;
            var flag = t != null && t.Flags.OpenCloseColor;
            var both = flag && pressed;

            using (var open = OpenGeometry(closeEnabled))
            {
                c.Save();
                c.Translate(106, 240);
                DrawButton(c, open, t, pressed, flag, both, false);
                c.Restore();
            }
            using (var close = CloseGeometry(closeEnabled))
            {
                c.Save();
                c.Translate(0, 240);
                DrawButton(c, close, t, pressed, flag, both, true);
                c.Restore();
            }

            // Labels
            c.Save();
            c.Translate(0, 240);
            const float labelSize = 14;
            var face = PreviewFonts.Bold;
            var lineHeight = PreviewText.LineHeight(face, labelSize);
            if (closeEnabled)
            {
                // Close button: "Umami"
                DrawLabel(c, t, "Umami", 50, 60, face, labelSize, pressed, flag, both, true);
                DrawLabel(c, t, "3DS Theme Editor", 111, -1, face, labelSize, pressed, flag, both, false);
            }
            else
            {
                DrawLabel(c, t, "Umami 3DS Theme Editor", 0, Width, face, labelSize, pressed, flag, both, false);
            }
            c.Restore();
        }

        private static void DrawButton(SKCanvas c, SKPath geometry, ThemeViewModel t, bool pressed, bool flag, bool both, bool close)
        {
            if (geometry.IsEmpty)
                return;
            var b = geometry.Bounds;
            var open = !close;
            var set = close ? t?.Colors.Close : t?.Colors.Open;

            // Fill
            SKShader fill;
            if (both)
                fill = SkiaHelpers.Gradient(b, 0, 0.1f, 0, 0.5f, new Stop(-0.5f, set.Pressed), new Stop(0.5f, set.Pressed),
                    new Stop(1.5f, set.Unpressed));
            else if (flag)
                fill = SkiaHelpers.Gradient(b, 0, 0.1f, 0, 0.5f, new Stop(-0.5f, set.Light), new Stop(0.4f, set.Unpressed),
                    new Stop(0.5f, set.Unpressed), new Stop(1.5f, set.Pressed));
            else
            {
                var a = SkiaHelpers.Hex(open ? "#e6e3df" : "#58554E");
                var z = SkiaHelpers.Hex(open ? "#bdb9b5" : "#4B4841");
                fill = pressed
                    ? SkiaHelpers.Gradient(b, 0, 0.25f, 0, 1, new Stop(0, z), new Stop(1, a))
                    : SkiaHelpers.Gradient(b, 0, 0.25f, 0, 1, new Stop(0, a), new Stop(1, z));
            }
            SkiaHelpers.FillPath(c, geometry, fill);
            fill.Dispose();

            // Glows (hidden while pressed)
            if (!pressed)
            {
                var baseColor = flag ? set.Unpressed.ToSk() : SkiaHelpers.Hex(open ? "#bdb9b5" : "#4B4841");
                var glowColor = flag ? set.Light.ToSk() : SkiaHelpers.Hex(open ? "#f0eeea" : "#86837C");

                c.Save();
                c.ClipPath(geometry, SKClipOperation.Intersect, true);
                SkiaHelpers.WithBlur(c, 6, () => SkiaHelpers.StrokePath(c, geometry, baseColor, 6));
                SkiaHelpers.WithOpacityMask(c, b,
                    mb => SkiaHelpers.Gradient(mb, 0, 0, 0, 0.5f,
                        new Stop(0.4f, new SKColor(0, 0, 0, 255)), new Stop(0.7f, new SKColor(0, 0, 0, 0))),
                    () => SkiaHelpers.WithBlur(c, 6, () => SkiaHelpers.StrokePath(c, geometry, glowColor, 4)));
                c.Restore();
            }

            // Stroke
            SKColor stroke;
            if (both)
                stroke = set.Unpressed.ToSk();
            else if (flag)
                stroke = set.Pressed.ToSk();
            else if (pressed)
                stroke = SkiaHelpers.Hex(open ? "#e6e3df" : "#58554E");
            else
                stroke = SkiaHelpers.Hex(open ? "#bdb9b5" : "#4B4841");
            SkiaHelpers.StrokePath(c, geometry, stroke, 1);
        }

        /// <summary>Draws a button label. <paramref name="width" /> &lt; 0 means "size to the text".</summary>
        private static void DrawLabel(SKCanvas c, ThemeViewModel t, string text, float left, float width,
            SKTypeface face, float size, bool pressed, bool flag, bool both, bool close)
        {
            var set = close ? t?.Colors.Close : t?.Colors.Open;
            float textW, textH;
            using (PreviewText.BuildPath(text, face, size, out textW, out textH)) {}
            var slotWidth = width < 0 ? textW : width;
            var x = left + (slotWidth - textW) / 2;

            // Layout: the text box is 18 high, centered in a 26 high strip that starts at y = -26
            var mainTop = -26f + 4;
            var shadowTop = mainTop + 1;

            if (!close && !pressed)
            {
                var shadowColor = SkiaHelpers.Hex("#efede9");
                if (flag)
                {
                    shadowColor = set.TextShadow.ToSk();
                    shadowTop = mainTop - (float) (set.ShadowPosition * 1.2) / 2f;
                }
                PreviewText.Draw(c, text, x, shadowTop, face, size, shadowColor, null, 0);
            }

            SKColor fill;
            if (close)
            {
                fill = SkiaHelpers.Hex("#efede9");
                if (pressed) fill = SkiaHelpers.Hex("#A8A8A8");
                if (flag) fill = set.TextUnpressed.ToSk();
                if (both) fill = set.TextPressed.ToSk();
            }
            else
            {
                fill = SkiaHelpers.Hex("#3d3d3d");
                if (pressed) fill = SkiaHelpers.Hex("#efede9");
                if (flag) fill = set.TextUnpressed.ToSk();
                if (both) fill = set.TextPressed.ToSk();
            }
            var top = pressed ? mainTop + 1 : mainTop;
            PreviewText.Draw(c, text, x, top, face, size, fill, null, 0);
        }

        // ---------------------------------------------------------------- Corner buttons

        private static void DrawCorners(SKCanvas c, ThemeViewModel t, Settings s)
        {
            var flag = t != null && t.Flags.BottomCornerButtonColor;
            var pressed = s.Preview_PressedToggle;
            var cb = t?.Colors.BottomCorner;

            using (var shape = new SKPath { FillType = SKPathFillType.EvenOdd })
            {
                shape.AddRoundRect(new SKRect(-8, -8, 51, 30), 8, 8);
                shape.AddRoundRect(new SKRect(268, -8, 327, 30), 8, 8);
                var bounds = shape.Bounds;

                // Border (with outer shadow)
                var border = flag ? cb.Border.ToSk() : SkiaHelpers.Hex("#dcdcdc");
                SkiaHelpers.WithDropShadow(c, SKColors.Black, 0.5, 5, 0, 0, () => SkiaHelpers.StrokePath(c, shape, border, 2));

                // Body
                SkiaHelpers.FillPath(c, shape, flag ? cb.Main.ToSk() : SkiaHelpers.Hex("#e9e9ea"));
                SkiaHelpers.StrokePath(c, shape, flag ? cb.Highlight.ToSk() : SkiaHelpers.Hex("#f3f3f3"), 1);

                // Highlight gradient
                var hi = flag ? cb.Highlight.ToSk() : SkiaHelpers.Hex("#f3f3f3");
                SkiaHelpers.WithOpacityMask(c, SKRect.Inflate(bounds, 2, 2),
                    mb => SkiaHelpers.VGradient(bounds, new Stop(0, new SKColor(0, 0, 0, 0x7f)), new Stop(1, new SKColor(0, 0, 0, 0))),
                    () =>
                    {
                        SkiaHelpers.FillPath(c, shape, hi);
                        SkiaHelpers.StrokePath(c, shape, hi, 1);
                    });
            }

            // Separator lines
            DrawSeparator(c, 293, flag ? cb.Border.ToSk() : SkiaHelpers.Hex("#dcdcdc"));
            DrawSeparator(c, 294, flag ? cb.Highlight.ToSk() : SkiaHelpers.Hex("#f3f3f3"));

            // Icons
            using (var rightGlow = SkiaHelpers.RoundRect(0, 0, 20, 23, 3, 3))
            using (var leftGlow = SkiaHelpers.RoundRect(0, 0, 45, 25, 3, 3))
            using (var larger = SkiaHelpers.RoundRect(0, 0, 12, 12, 3, 3))
            using (var smaller = new SKPath())
            using (var home = new SKPath { FillType = SKPathFillType.EvenOdd })
            {
                smaller.AddRoundRect(new SKRect(0, 0, 5, 5), 1, 1);
                smaller.AddRoundRect(new SKRect(7, 0, 12, 5), 1, 1);
                smaller.AddRoundRect(new SKRect(7, 7, 12, 12), 1, 1);
                smaller.AddRoundRect(new SKRect(0, 7, 5, 12), 1, 1);
                using (var hp = SkiaHelpers.Parse(
                           "M 1 9 L 10 0 L 18 8 L 16 10 C 15 11 15 16 14 17 L 13 18 5 18 Q 3 18 3 16 L 3 9 Z " +
                           "M 7.5 10.5 L 12.5 10.5 L 12.5 14.5 L 7.5 14.5 " +
                           "M 15 19 L 17 17 C 18 15 18 12 19 11 L 21 9 Q 23 9 23 11 C 20 13 22 16 25 13 L 25 13 " +
                           "S 27 13 27 15 L 25 17 C 24 18 20 18 19 19 L 17 21 Q 15 21 15 19 Z"))
                    home.AddPath(hp);

                DrawIcon(c, rightGlow, 271, 4, larger, 275, 10, t, flag, pressed);
                DrawIcon(c, rightGlow, 297, 4, smaller, 301, 10, t, flag, pressed);
                DrawIcon(c, leftGlow, 3, 3, home, 13, 5, t, flag, pressed);
            }

            var strip = IconStrip;
            c.DrawBitmap(strip.Sk, 60, 0);
        }

        private static void DrawSeparator(SKCanvas c, float x, SKColor color)
        {
            var b = new SKRect(x - 1, 4, x + 2, 27);
            SkiaHelpers.WithOpacityMask(c, b,
                mb => SkiaHelpers.Gradient(new SKRect(x, 4, x + 1, 27), 0, 0, 0, 1,
                    new Stop(0, new SKColor(0, 0, 0, 0)), new Stop(0.5f, new SKColor(0, 0, 0, 255)),
                    new Stop(1, new SKColor(0, 0, 0, 0))),
                () => { using (var pen = SkiaHelpers.StrokePaint(color, 1)) c.DrawLine(x + 0.5f, 4, x + 0.5f, 27, pen); });
        }

        private static void DrawIcon(SKCanvas c, SKPath glow, float gx, float gy, SKPath icon, float ix, float iy,
            ThemeViewModel t, bool flag, bool pressed)
        {
            var both = flag && pressed;
            var cb = t?.Colors.BottomCorner;

            // Glow
            c.Save();
            c.Translate(gx, gy);
            SKColor g0 = SkiaHelpers.Hex("#00ffffff"), g1 = SkiaHelpers.Hex("#13ffffff");
            if (pressed)
            {
                g0 = SkiaHelpers.Hex("#829cc4");
                g1 = SkiaHelpers.Hex("#95b0d3");
            }
            if (both)
            {
                g0 = cb.IconBottom.ToSk();
                g1 = cb.IconTop.ToSk();
            }
            var gb = glow.Bounds;
            SkiaHelpers.WithBlur(c, 5, () =>
            {
                using (var shader = SkiaHelpers.Gradient(gb, 0, -1, 0, 1, new Stop(0, g0), new Stop(1, g1)))
                    SkiaHelpers.FillPath(c, glow, shader);
            }, 0.9);
            c.Restore();

            // Glyph
            c.Save();
            c.Translate(ix, iy);
            var ib = icon.Bounds;
            if (both)
            {
                SkiaHelpers.FillPath(c, icon, cb.Main.ToSk());
            }
            else if (flag)
            {
                using (var shader = SkiaHelpers.Gradient(ib, 0, -0.5f, 0, 1,
                           new Stop(0, cb.IconTop), new Stop(1, cb.IconBottom)))
                    SkiaHelpers.FillPath(c, icon, shader);
            }
            else if (pressed)
            {
                SkiaHelpers.FillPath(c, icon, SkiaHelpers.Hex("#e9e9ea"));
            }
            else
            {
                using (var shader = SkiaHelpers.Gradient(ib, 0, -0.5f, 0, 1,
                           new Stop(0, SkiaHelpers.Hex("#95b0d3")), new Stop(1, SkiaHelpers.Hex("#829cc4"))))
                    SkiaHelpers.FillPath(c, icon, shader);
            }
            c.Restore();
        }

        // ---------------------------------------------------------------- Right arrow button

        private static void DrawArrowButton(SKCanvas c, ThemeViewModel t, Settings s)
        {
            var flag = t != null && t.Flags.ArrowButtonColor;
            var arrowFlag = t != null && t.Flags.ArrowColor;
            var pressed = s.Preview_PressedToggle;
            var bothOpen = t != null && t.Flags.OpenCloseColor && pressed;

            c.Save();
            c.Translate(pressed ? 322 : 320, 123);

            using (var circle = SkiaHelpers.Ellipse(18, 0, 38, 39))
            {
                var b = circle.Bounds;
                c.Save();
                c.ClipRect(new SKRect(-60, -60, 100, 60));

                // Body
                using (var shader = flag
                           ? SkiaHelpers.VGradient(b, new Stop(0, t.Colors.ArrowButton.Main), new Stop(1, t.Colors.ArrowButton.Shading))
                           : SkiaHelpers.VGradient(b, new Stop(0, SkiaHelpers.Hex("#f0f0f0")), new Stop(1, SkiaHelpers.Hex("#f0f0f0"))))
                {
                    using (var paint = new SKPaint { Shader = shader, IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White.WithOpacity(0.9) })
                    {
                        c.SaveLayer(new SKPaint { Color = SKColors.White.WithOpacity(0.9) });
                        c.DrawPath(circle, new SKPaint { Shader = shader, IsAntialias = true });
                        c.Restore();
                    }
                }

                // Inner glow
                c.Save();
                c.ClipPath(circle, SKClipOperation.Intersect, true);
                SkiaHelpers.WithBlur(c, 8, () =>
                    SkiaHelpers.StrokePath(c, circle, flag ? t.Colors.ArrowButton.Glow.ToSk() : SKColors.White, 4));
                c.Restore();

                // Outer halo (shadow of the ellipse, clipped to everything except the ellipse)
                c.Save();
                using (var outside = SkiaHelpers.Combine(SkiaHelpers.RoundRect(-40, -40, 80, 80), circle, SKPathOp.Difference))
                    c.ClipPath(outside, SKClipOperation.Intersect, true);
                SkiaHelpers.WithDropShadow(c, SKColors.Black, 0.5, 5, 0, 0,
                    () => SkiaHelpers.FillPath(c, circle, flag ? t.Colors.ArrowButton.Glow.ToSk() : SKColors.White));
                c.Restore();

                c.Restore();
            }

            // Arrow glyph
            using (var glyph = SkiaHelpers.Parse("M 1 2 L 7 2 L 7 7 L 14 0 L 7 -7 L 7 -2 L 1 -2 Q 0 -2 0 0 Q 0 2 1 2 Z"))
            {
                var stroke = SkiaHelpers.Hex("#97b5b0");
                var fill = SkiaHelpers.Hex("#a1c0ba");
                if (pressed) fill = SkiaHelpers.Hex("#B1FFF0");
                if (arrowFlag)
                {
                    stroke = t.Colors.Arrow.Border.ToSk();
                    fill = t.Colors.Arrow.Unpressed.ToSk();
                }
                if (bothOpen)
                    fill = t.Colors.Arrow.Pressed.ToSk();

                c.Save();
                c.Translate(-17.25f, 0);
                SkiaHelpers.FillPath(c, glyph, fill);
                SkiaHelpers.StrokePath(c, glyph, stroke, 0.5f);
                c.Restore();
            }
            c.Restore();
        }
    }
}
