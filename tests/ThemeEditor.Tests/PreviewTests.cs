using System;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkiaSharp;
using ThemeEditor.App;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;
using Xunit;

namespace ThemeEditor.Tests
{
    public class PreviewTests
    {
        private static ThemeViewModel NewTheme()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            return win.ViewModel;
        }

        /// <summary>Runs <paramref name="body" /> and restores the preview settings it may change.</summary>
        private static void WithSettings(Action<Settings> body)
        {
            var s = Settings.Default;
            var rows = s.Preview_ZoomRows;
            var cx = s.Preview_Main_CursorX;
            var cy = s.Preview_Main_CursorY;
            var folder = s.Preview_ModeToggle;
            var cart = (s.Preview_Main_CartEnable, s.Preview_Main_CartX, s.Preview_Main_CartY);
            try
            {
                s.Preview_ModeToggle = false;
                body(s);
            }
            finally
            {
                s.Preview_ZoomRows = rows;
                s.Preview_Main_CursorX = cx;
                s.Preview_Main_CursorY = cy;
                s.Preview_ModeToggle = folder;
                (s.Preview_Main_CartEnable, s.Preview_Main_CartX, s.Preview_Main_CartY) = cart;
            }
        }

        [Fact]
        public void DefaultZoomKeepsTheOriginalLayout()
        {
            WithSettings(s =>
            {
                s.Preview_ZoomRows = 3;
                float[] cols = { 54, 107, 160, 213, 266 };
                float[] rows = { 176, 123, 70 };
                for (var c = 0; c < 5; c++)
                for (var r = 0; r < 3; r++)
                {
                    var p = ItemRenderer.GridPosition(c, r, false);
                    Assert.Equal(cols[c], p.X, 3);
                    Assert.Equal(rows[r], p.Y, 3);
                }
            });
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(2, 3)]
        [InlineData(4, 7)]
        [InlineData(6, 10)]
        public void EveryZoomedCellHitsBackToItself(int rows, int columns)
        {
            WithSettings(s =>
            {
                s.Preview_ZoomRows = rows;
                Assert.Equal(columns, PreviewGrid.Columns(rows));
                for (var zc = 0; zc < columns; zc++)
                for (var zr = 0; zr < rows; zr++)
                {
                    var centre = PreviewGrid.Centre(zc, zr, rows);
                    int col, row;
                    Assert.Equal(PreviewTarget.Cell, PreviewHit.Hit(s, centre.X, centre.Y, out col, out row));
                    // The stored position is drawn exactly where it was hit
                    var drawn = ItemRenderer.GridPosition(col, row, false);
                    Assert.Equal(centre.X, drawn.X, 3);
                    Assert.Equal(centre.Y, drawn.Y, 3);
                }
            });
        }

        [AvaloniaFact]
        public void ZoomButtonsChangeTheRowsAndKeepTheCursorOnScreen()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                s.Preview_ZoomRows = 6;
                // Last cell of the 6-row page (column 9, bottom row): far beyond the default 5x3 layout
                int col, row;
                PreviewGrid.FromZoomCell(9, 5, 6, out col, out row);
                PreviewActions.SetCursor(s, false, col, row);
                Assert.True(PreviewGrid.IsVisible(col, row));

                // The big-square button shows fewer, larger icons
                PreviewActions.Click(theme, s, PreviewInteraction.Shared, PreviewTarget.CornerA, -1, -1);
                Assert.Equal(5, s.Preview_ZoomRows);
                Assert.True(PreviewGrid.IsVisible(s.Preview_Main_CursorX, s.Preview_Main_CursorY));

                for (var i = 0; i < 10; i++)
                    PreviewActions.Zoom(theme, s, -1);
                Assert.Equal(1, s.Preview_ZoomRows);
                Assert.True(PreviewGrid.IsVisible(s.Preview_Main_CursorX, s.Preview_Main_CursorY));

                // The four-squares button goes back up, and stops at 6 rows
                for (var i = 0; i < 10; i++)
                    PreviewActions.Click(theme, s, PreviewInteraction.Shared, PreviewTarget.CornerB, -1, -1);
                Assert.Equal(6, s.Preview_ZoomRows);
            });
        }

        [AvaloniaFact]
        public void ArrowKeysMoveOnTheZoomedGrid()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                s.Preview_ZoomRows = 6;
                PreviewActions.SetCursor(s, false, 0, 2); // top-left
                for (var i = 0; i < 20; i++)
                    PreviewActions.MoveCursor(theme, s, 0, -1); // down to the bottom row
                int zc, zr;
                PreviewGrid.ToZoomCell(s.Preview_Main_CursorX, s.Preview_Main_CursorY, 6, out zc, out zr);
                Assert.Equal(0, zc);
                Assert.Equal(5, zr);
                for (var i = 0; i < 20; i++)
                    PreviewActions.MoveCursor(theme, s, 1, 0); // and right to the last column of the page
                PreviewGrid.ToZoomCell(s.Preview_Main_CursorX, s.Preview_Main_CursorY, 6, out zc, out zr);
                Assert.Equal(9, zc);
            });
        }

        [AvaloniaFact]
        public void OneRowLayoutShowsTheDescriptionBubble()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                // Only the first column (slots 0-2) is on screen with one row: select an item in slot 0
                s.Preview_Main_CartEnable = true;
                s.Preview_Main_CartX = 0;
                s.Preview_Main_CartY = 2;
                PreviewActions.SetCursor(s, false, 0, 2);
                s.Preview_ZoomRows = 1;
                Assert.True(PreviewGrid.IsVisible(0, 2));
                using var withBubble = PreviewExporter.Render(theme, PreviewKind.Bottom);
                theme.Flags.GameTextDrawType = ThemeEditor.App.Localization.Enums.GameTextDrawType.Hidden;
                using var hidden = PreviewExporter.Render(theme, PreviewKind.Bottom);
                // The bubble sits between the top bar and the icon row
                var differs = 0;
                for (var y = 45; y < 95; y += 2)
                for (var x = 40; x < 260; x += 2)
                    if (withBubble.GetPixel(x, y) != hidden.GetPixel(x, y))
                        differs++;
                Assert.True(differs > 500, $"only {differs} pixels changed");
            });
        }

        [AvaloniaFact]
        public void ExportedPreviewIsTransparentBesideTheBottomScreen()
        {
            var theme = NewTheme();
            using var both = PreviewExporter.Render(theme, PreviewKind.Both);
            Assert.Equal(412, both.Width);
            Assert.Equal(480, both.Height);
            Assert.Equal(0, both.GetPixel(10, 360).Alpha);
            Assert.Equal(0, both.GetPixel(405, 360).Alpha);
            Assert.Equal(255, both.GetPixel(206, 120).Alpha); // top screen
            Assert.Equal(255, both.GetPixel(206, 360).Alpha); // bottom screen
        }

        [Fact]
        public void CentredTextIsCentredByItsInk()
        {
            foreach (var letter in new[] { "L", "R", "Y", "+" })
            {
                using var bmp = new SKBitmap(60, 60);
                using (var c = new SKCanvas(bmp))
                {
                    c.Clear(SKColors.Transparent);
                    PreviewText.DrawCentered(c, letter, 30, 30, PreviewFonts.Mono, 24, SKColors.Black, null, 0);
                }
                int minX = 60, minY = 60, maxX = -1, maxY = -1;
                for (var y = 0; y < 60; y++)
                for (var x = 0; x < 60; x++)
                    if (bmp.GetPixel(x, y).Alpha > 128)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                Assert.InRange((minX + maxX + 1) / 2.0, 29, 31);
                Assert.InRange((minY + maxY + 1) / 2.0, 29, 31);
            }
        }
    }
}
