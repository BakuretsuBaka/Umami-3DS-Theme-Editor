// --------------------------------------------------
// Umami 3DS Theme Editor - ItemRenderer.cs
// --------------------------------------------------

using System;
using SkiaSharp;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Draws the icons that populate the HOME menu grid (cartridge slot, file, folder, cursor) the way the
    ///     original editor's WPF styles did. All shapes are centered on the origin; callers translate first.
    /// </summary>
    internal static class ItemRenderer
    {
        private static readonly SKRect FileBounds = new SKRect(-24, -24, 24, 24);

        private static SKPath _fileGeometry;
        private static SKPath _fileLabelGeometry;
        private static SKPath _noCartFore;
        private static SKPath _cursor;
        private static SKPath _folderBackBase;
        private static SKPath _folderItem;
        private static SKPath _folderForeClosed;
        private static SKPath _folderForeOpen;
        private static SKPath _folderBackClosed;
        private static SKPath _folderBackOpen;

        public static SKPath FileGeometry => _fileGeometry ?? (_fileGeometry = SkiaHelpers.RoundRect(-24, -24, 48, 48, 8, 8));
        public static SKPath FileLabelGeometry => _fileLabelGeometry ?? (_fileLabelGeometry = SkiaHelpers.RoundRect(-19, -19, 38, 38, 3, 3));
        public static SKPath FolderItem => _folderItem ?? (_folderItem = SkiaHelpers.RoundRect(-15, -17, 36, 36, 5, 5));
        public static SKPath FolderForeClosed => _folderForeClosed ?? (_folderForeClosed = SkiaHelpers.RoundRect(-24, -14, 48, 36, 3, 3));

        public static SKPath FolderForeOpen
        {
            get
            {
                if (_folderForeOpen == null)
                {
                    _folderForeOpen = SkiaHelpers.Parse(
                        "M -24 -6 l 48 0 a 3,3 0 0 1 3,3 l -3 22 a 3,3 0 0 1 -3 3 l -42 0 a 3,3 0 0 1 -3 -3 l -3 -22 a 3,3 0 0 1 3 -3 z");
                }
                return _folderForeOpen;
            }
        }

        private static SKPath FolderBackBase
        {
            get
            {
                if (_folderBackBase == null)
                {
                    _folderBackBase = SkiaHelpers.Parse(
                        "M -24 0 L -24 -17 A 3,3 0 0 1 -21,-20 L -5 -20 L -3 -17 L 21 -17 A 3,3 0 0 1 24 -14 L 24 0 Z");
                }
                return _folderBackBase;
            }
        }

        public static SKPath FolderBackClosed => _folderBackClosed ?? (_folderBackClosed = SkiaHelpers.Combine(FolderForeClosed, FolderBackBase, SKPathOp.Union));
        public static SKPath FolderBackOpen => _folderBackOpen ?? (_folderBackOpen = SkiaHelpers.Combine(FolderForeOpen, FolderBackBase, SKPathOp.Union));

        private static SKPath NoCartFore
        {
            get
            {
                if (_noCartFore == null)
                {
                    var corners = new SKPath();
                    corners.AddRoundRect(new SKRect(-24, -24, 0, 0), 2, 2);
                    corners.AddRoundRect(new SKRect(0, -24, 24, 0), 2, 2);
                    corners.AddRoundRect(new SKRect(0, 0, 24, 24), 2, 2);
                    corners.AddRoundRect(new SKRect(-24, 0, 0, 24), 8, 8);
                    var cross = new SKPath { FillType = SKPathFillType.Winding };
                    cross.AddRect(new SKRect(-8, -24, 8, 24));
                    cross.AddRect(new SKRect(-24, -8, 24, 8));
                    var whole = SkiaHelpers.Combine(corners, cross, SKPathOp.Union);
                    var hole = SkiaHelpers.RoundRect(-16, -16, 32, 32, 2, 2);
                    // Geometry group with even-odd fill: the inner rectangle punches a hole
                    _noCartFore = SkiaHelpers.Combine(whole, hole, SKPathOp.Xor);
                }
                return _noCartFore;
            }
        }

        private static SKPath CursorGeometry
        {
            get
            {
                if (_cursor == null)
                {
                    var ring = new SKPath { FillType = SKPathFillType.EvenOdd };
                    ring.AddRoundRect(new SKRect(-27, -27, 27, 27), 6, 6);
                    ring.AddRoundRect(new SKRect(-23, -23, 23, 23), 4, 4);
                    var diamond = new SKPath();
                    diamond.AddRect(new SKRect(-27, -27, 27, 27));
                    diamond.Transform(SKMatrix.CreateRotationDegrees(45));
                    _cursor = SkiaHelpers.Combine(ring, diamond, SKPathOp.Difference);
                }
                return _cursor;
            }
        }

        // ---------------------------------------------------------------- Cartridge slot

        public static void DrawNoCart(SKCanvas c)
        {
            SkiaHelpers.WithDropShadow(c, SKColors.Black, 0.35, 5, 0, 0, () =>
            {
                SkiaHelpers.FillPath(c, FileGeometry, SkiaHelpers.Hex("#e0e0de"));
                SkiaHelpers.FillPath(c, NoCartFore, SkiaHelpers.Hex("#d3d3d1"));
                SkiaHelpers.StrokePath(c, NoCartFore, SkiaHelpers.Hex("#b9b9b7"), 1);
            });
        }

        // ---------------------------------------------------------------- File

        public static void DrawFile(SKCanvas c, ThemeViewModel t)
        {
            var colored = t != null && t.Flags.FileColor;
            var textured = t != null && t.Flags.FileTexture;
            var shadow = colored ? t.Colors.File.Shadow.ToSk() : SKColors.Black;

            // Back (shadow caster)
            SkiaHelpers.WithDropShadow(c, shadow, 0.35, 5, 0, 0, () =>
            {
                var tex = t?.Textures.FileLarge.Pixels;
                if (textured && tex != null)
                {
                    c.Save();
                    c.ClipPath(FileGeometry, SKClipOperation.Intersect, true);
                    // Right half: viewbox 1,1,34,69 of the texture into a 24x48 tile, left half mirrored.
                    // The halves overlap by half a pixel: two antialiased edges meeting exactly left a visible seam.
                    var view = new SKRect(1, 1, 35, 70);
                    SkiaHelpers.DrawImageUniform(c, tex.Sk, view, new SKRect(-0.25f, -24, 24, 24));
                    SkiaHelpers.DrawImageUniform(c, tex.Sk, view, new SKRect(-24, -24, 0.25f, 24), true);
                    c.Restore();
                }
                else
                {
                    SkiaHelpers.FillPath(c, FileGeometry,
                        colored ? t.Colors.File.Main.ToSk() : SkiaHelpers.Hex("#f6f6f6"));
                }
            });

            DrawSoftEdge(c, FileGeometry, FileBounds, colored, textured,
                colored ? t.Colors.File.Light.ToSk() : SkiaHelpers.Hex("#f8f8f8"),
                colored ? t.Colors.File.Dark.ToSk() : SkiaHelpers.Hex("#d0d0d0"));

            // Label (SMDH large icon)
            var icon = t?.Info.LargeIcon.Pixels;
            if (icon != null)
            {
                c.Save();
                c.ClipPath(FileLabelGeometry, SKClipOperation.Intersect, true);
                using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
                    c.DrawBitmap(icon.Sk, new SKRect(-19, -19, 19, 19), paint);
                c.Restore();
            }
        }

        /// <summary>The blurred 2px rim gradient that gives files and the folder item their glassy edge.</summary>
        private static void DrawSoftEdge(SKCanvas c, SKPath geometry, SKRect bounds, bool colored, bool transparent,
            SKColor top, SKColor bottom)
        {
            if (transparent)
                return;
            c.Save();
            c.ClipPath(geometry, SKClipOperation.Intersect, true);
            SkiaHelpers.WithBlur(c, 3, () =>
            {
                using (var shader = SkiaHelpers.VGradient(bounds, new Stop(0, top), new Stop(1, bottom)))
                    SkiaHelpers.StrokePath(c, geometry, shader, 2);
            });
            c.Restore();
        }

        // ---------------------------------------------------------------- Folder

        /// <summary>Draws a closed (main menu) or open (folder view) folder with the item peeking out of it.</summary>
        public static void DrawFolder(SKCanvas c, ThemeViewModel t, bool open)
        {
            var colored = t != null && t.Flags.FolderColor;
            var textured = t != null && t.Flags.FolderTexture;
            var back = open ? FolderBackOpen : FolderBackClosed;
            var fore = open ? FolderForeOpen : FolderForeClosed;

            // Back (shadow caster)
            var shadow = colored ? t.Colors.Folder.Shadow.ToSk() : SKColors.Black;
            SkiaHelpers.WithDropShadow(c, shadow, 0.35, 5, 0, 0, () => DrawFolderBack(c, t, back, open, colored, textured));

            // The item sticking out of the folder, clipped
            c.Save();
            c.ClipRect(open ? new SKRect(-20, -23, 20, -6) : new SKRect(-20, -23, 20, -14));
            DrawFolderItem(c, t);
            c.Restore();

            // Front
            if (!textured)
            {
                c.Save();
                c.ClipPath(fore, SKClipOperation.Intersect, true);
                var b = fore.Bounds;
                var main = colored ? t.Colors.Folder.Main.ToSk() : SkiaHelpers.Hex("#92e1ee");
                var light = colored ? t.Colors.Folder.Light.ToSk() : SKColors.White;
                var dark = colored ? t.Colors.Folder.Dark.ToSk() : SkiaHelpers.Hex("#65a8bb");
                using (var shader = SkiaHelpers.VGradient(b,
                           new Stop(0, light), new Stop(0.1f, main), new Stop(0.9f, main), new Stop(1, dark)))
                    SkiaHelpers.FillPath(c, fore, shader);
                SkiaHelpers.StrokePath(c, fore, dark, 2);
                c.Restore();
            }
        }

        private static void DrawFolderBack(SKCanvas c, ThemeViewModel t, SKPath back, bool open, bool colored, bool textured)
        {
            var bounds = back.Bounds;
            c.Save();
            c.ClipPath(back, SKClipOperation.Intersect, true);
            var tex = open ? t?.Textures.FolderOpen.Pixels : t?.Textures.FolderClosed.Pixels;
            if (textured && tex != null)
            {
                var view = open ? new SKRect(5, 1, 77, 63) : new SKRect(1, 1, 73, 63);
                SkiaHelpers.DrawImageUniform(c, tex.Sk, view, bounds);
            }
            else
            {
                var baseColor = colored ? t.Colors.Folder.Dark.ToSk() : SkiaHelpers.Hex("#65a8bb");
                var faded = ColorBlend(baseColor, SKColors.White, 0.4f);
                using (var shader = SkiaHelpers.VGradient(bounds, new Stop(0, faded), new Stop(0.1f, baseColor)))
                    SkiaHelpers.FillPath(c, back, shader);
                SkiaHelpers.StrokePath(c, back, baseColor, 2);
            }
            c.Restore();
        }

        private static void DrawFolderItem(SKCanvas c, ThemeViewModel t)
        {
            var colored = t != null && t.Flags.FileColor;
            c.Save();
            c.RotateDegrees(-24);
            SkiaHelpers.FillPath(c, FolderItem, colored ? t.Colors.File.Main.ToSk() : SkiaHelpers.Hex("#f6f6f6"));
            DrawSoftEdge(c, FolderItem, FolderItem.Bounds, colored, false,
                colored ? t.Colors.File.Light.ToSk() : SkiaHelpers.Hex("#f8f8f8"),
                colored ? t.Colors.File.Dark.ToSk() : SkiaHelpers.Hex("#d0d0d0"));
            c.Restore();
        }

        // ---------------------------------------------------------------- Cursor

        public static void DrawCursor(SKCanvas c, ThemeViewModel t, bool animate, double time)
        {
            var colored = t != null && t.Flags.CursorColor;
            var main = colored ? t.Colors.Cursor.Main.ToSk() : SkiaHelpers.Hex("#39eda6");
            var shading = colored ? t.Colors.Cursor.Shading.ToSk() : SkiaHelpers.Hex("#33e19a");
            var glow = colored ? t.Colors.Cursor.Glow.ToSk() : SkiaHelpers.Hex("#39eda6");

            var scale = 1f;
            var glowOpacity = 1.0;
            if (animate)
            {
                // 0.5s auto-reversing pulse
                var phase = (time % 1.0) / 0.5;
                var tri = phase <= 1 ? phase : 2 - phase;
                scale = 1 + 0.05f * (float) tri;
                glowOpacity = Math.Pow(tri, 0.33);
            }

            c.Save();
            c.Scale(scale, scale);
            SkiaHelpers.FillPath(c, CursorGeometry, main);
            SkiaHelpers.StrokePath(c, CursorGeometry, shading, 1);
            SkiaHelpers.WithBlur(c, 5, () =>
            {
                SkiaHelpers.FillPath(c, CursorGeometry, glow);
                SkiaHelpers.StrokePath(c, CursorGeometry, glow, 2);
            }, glowOpacity);
            c.Restore();
        }

        // ---------------------------------------------------------------- Utilities

        /// <summary>Alpha blend of <paramref name="fg" /> over <paramref name="bg" /> at the given factor.</summary>
        public static SKColor ColorBlend(SKColor bg, SKColor fg, float factor)
        {
            var a = factor * fg.Alpha / 255f;
            byte Mix(byte b, byte f)
            {
                return (byte) Math.Max(0, Math.Min(255, b * (1 - a) + f * a));
            }
            return new SKColor(Mix(bg.Red, fg.Red), Mix(bg.Green, fg.Green), Mix(bg.Blue, fg.Blue), bg.Alpha);
        }

        public static SKPoint GridPosition(int column, int row, bool folderMode)
        {
            float[] cols = { 54, 107, 160, 213, 266 };
            float[] mainRows = { 176, 123, 70 };
            float[] folderRows = { 164, 110 };
            var rows = folderMode ? folderRows : mainRows;
            return new SKPoint(cols[Math.Max(0, Math.Min(cols.Length - 1, column))],
                rows[Math.Max(0, Math.Min(rows.Length - 1, row))]);
        }
    }
}
