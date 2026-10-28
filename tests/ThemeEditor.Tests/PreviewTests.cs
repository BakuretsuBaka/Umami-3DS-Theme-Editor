using System;
using System.Linq;
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
            var closeToggle = s.Preview_CloseToggle;
            var enabled = (s.Preview_Main_CartEnable, s.Preview_Main_FileEnable, s.Preview_Main_FolderEnable);
            try
            {
                s.Preview_ModeToggle = false;
                PreviewInteraction.Shared.Page = 0;
                body(s);
            }
            finally
            {
                s.Preview_ZoomRows = rows;
                s.Preview_Main_CursorX = cx;
                s.Preview_Main_CursorY = cy;
                s.Preview_ModeToggle = folder;
                s.Preview_CloseToggle = closeToggle;
                (s.Preview_Main_CartEnable, s.Preview_Main_FileEnable, s.Preview_Main_FolderEnable) = enabled;
                (s.Preview_Main_CartEnable, s.Preview_Main_CartX, s.Preview_Main_CartY) = cart;
            }
        }

        // Tests that change preview settings run on the UI thread: open previews redraw when a setting changes
        [AvaloniaFact]
        public void DefaultZoomKeepsTheOriginalLayout()
        {
            WithSettings(s =>
            {
                s.Preview_ZoomRows = 3;
                // Without items the slots are exactly the default layout
                var items = (s.Preview_Main_CartEnable, s.Preview_Main_FileEnable, s.Preview_Main_FolderEnable);
                s.Preview_Main_CartEnable = s.Preview_Main_FileEnable = s.Preview_Main_FolderEnable = false;
                try
                {
                float[] cols = { 54, 107, 160, 213, 266 };
                float[] rows = { 176, 123, 70 };
                for (var c = 0; c < 5; c++)
                for (var r = 0; r < 3; r++)
                {
                    var p = ItemRenderer.GridPosition(c, r, false);
                    Assert.Equal(cols[c], p.X, 3);
                    Assert.Equal(rows[r], p.Y, 3);
                }
                }
                finally
                {
                    (s.Preview_Main_CartEnable, s.Preview_Main_FileEnable, s.Preview_Main_FolderEnable) = items;
                }
            });
        }

        [AvaloniaTheory]
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
        public void ArrowScrollsTheIconsAndTheCursorAPage()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                var ia = PreviewInteraction.Shared;
                s.Preview_ZoomRows = 3;
                s.Preview_Main_FileEnable = true;
                PreviewActions.SetCursor(s, false, 2, 1); // middle of the default page
                var spot = ItemRenderer.GridPosition(2, 1, false, PreviewGrid.PageOf(ia));
                Assert.True(PreviewGrid.IsVisible(s.Preview_Main_FileX, s.Preview_Main_FileY, 0));

                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                Assert.Equal(1, PreviewGrid.PageOf(ia));
                // Same spot on the screen, but a different slot: the cursor went along with the page
                Assert.False(s.Preview_Main_CursorX == 2 && s.Preview_Main_CursorY == 1);
                var moved = ItemRenderer.GridPosition(s.Preview_Main_CursorX, s.Preview_Main_CursorY, false, PreviewGrid.PageOf(ia));
                Assert.Equal(spot.X, moved.X, 2);
                Assert.Equal(spot.Y, moved.Y, 2);
                // The first page's icons scrolled away
                Assert.False(PreviewGrid.IsVisible(s.Preview_Main_FileX, s.Preview_Main_FileY, PreviewGrid.PageOf(ia)));
                // Clicking the icon spot on this page selects this page's slot
                int col, row;
                Assert.Equal(PreviewTarget.Cell, PreviewHit.Hit(s, spot.X, spot.Y, out col, out row, PreviewGrid.PageOf(ia)));
                Assert.Equal((s.Preview_Main_CursorX, s.Preview_Main_CursorY), (col, row));

                // Three pages, and no wrapping: the forward arrow is gone on the last page, the back arrow returns
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                Assert.Equal(2, PreviewGrid.PageOf(ia));
                Assert.NotEqual(PreviewTarget.Arrow, PreviewHit.Hit(s, 310, 123, out col, out row, PreviewGrid.PageOf(ia)));
                Assert.Equal(PreviewTarget.ArrowBack, PreviewHit.Hit(s, 10, 123, out col, out row, PreviewGrid.PageOf(ia)));
                PreviewActions.Click(theme, s, ia, PreviewTarget.ArrowBack, -1, -1);
                PreviewActions.Click(theme, s, ia, PreviewTarget.ArrowBack, -1, -1);
                Assert.Equal(0, PreviewGrid.PageOf(ia));
                Assert.NotEqual(PreviewTarget.ArrowBack, PreviewHit.Hit(s, 10, 123, out col, out row, 0));
                Assert.Equal((2, 1), (s.Preview_Main_CursorX, s.Preview_Main_CursorY));

                // Exports always show the first page, whatever the live preview shows
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                Assert.Equal(0, PreviewGrid.PageOf(null));
                Assert.True(PreviewGrid.IsVisible(s.Preview_Main_FileX, s.Preview_Main_FileY, PreviewGrid.PageOf(null)));
            });
        }

        [AvaloniaFact]
        public void DraggingMovesTheIconsLiveAndSettlesOnAPage()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                var ia = new PreviewInteraction();
                s.Preview_ZoomRows = 3;
                s.Preview_Main_CartEnable = true;
                var before = ItemRenderer.GridPosition(s.Preview_Main_CartX, s.Preview_Main_CartY, false, PreviewGrid.ViewOf(ia));

                // Halfway through a drag to the left the icons have moved left with the pointer
                ia.DragPages = -0.3;
                var during = ItemRenderer.GridPosition(s.Preview_Main_CartX, s.Preview_Main_CartY, false, PreviewGrid.ViewOf(ia));
                Assert.True(during.X < before.X - 50, $"{during.X} vs {before.X}");

                // Let go far enough: the next page, reached by gliding from where the drag ended
                ia.SettleDrag();
                PreviewActions.NextPage(theme, s, ia);
                Assert.Equal(1, PreviewGrid.PageOf(ia));
                Assert.InRange(ia.GridView, 0.29, 0.31);
                for (var t = 0.0; t < 2; t += 0.05)
                    ia.Tick(10 + t, s, theme);
                Assert.Equal(1.0, ia.GridView, 3);
            });
        }

        [AvaloniaFact]
        public void BottomBarFollowsTheSelection()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                s.Preview_CloseToggle = true;
                s.Preview_Main_CartEnable = s.Preview_Main_FolderEnable = true;

                PreviewActions.SetCursor(s, false, s.Preview_Main_FolderX, s.Preview_Main_FolderY);
                Assert.Equal(PreviewItem.Folder, PreviewHit.Selected(s));
                Assert.True(PreviewHit.LeftButtonShown(s)); // Settings | Open
                using var folder = PreviewExporter.Render(theme, PreviewKind.Bottom);
                // The left button is "Settings" here: clicking it does not close anything
                PreviewActions.Click(theme, s, PreviewInteraction.Shared, PreviewTarget.Close, -1, -1);
                Assert.False(s.Preview_ModeToggle);

                PreviewActions.SetCursor(s, false, s.Preview_Main_CartX, s.Preview_Main_CartY);
                Assert.True(PreviewHit.LeftButtonShown(s)); // Close | Start
                using var title = PreviewExporter.Render(theme, PreviewKind.Bottom);

                // An empty slot: one wide bar, the left part is the same button as the right
                int col, row;
                PreviewGrid.FromZoomCell(4, 0, 3, out col, out row);
                PreviewActions.SetCursor(s, false, col, row);
                Assert.Equal(PreviewItem.None, PreviewHit.Selected(s));
                Assert.False(PreviewHit.LeftButtonShown(s));
                Assert.Equal(PreviewTarget.Open, PreviewHit.Hit(s, 40, 230, out col, out row));
                using var empty = PreviewExporter.Render(theme, PreviewKind.Bottom);

                // The three bars look different (the labels change)
                bool Differs(SKBitmap a, SKBitmap b)
                {
                    for (var y = 216; y < 240; y++)
                    for (var x = 0; x < 320; x++)
                        if (a.GetPixel(x, y) != b.GetPixel(x, y))
                            return true;
                    return false;
                }
                Assert.True(Differs(folder, title));
                Assert.True(Differs(title, empty));
            });
        }

        [AvaloniaFact]
        public void ZoomButtonsFadeAtTheirLimits()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                SKColor Button(int x)
                {
                    using var b = PreviewExporter.Render(theme, PreviewKind.Bottom);
                    return b.GetPixel(x, 15);
                }
                s.Preview_ZoomRows = 3;
                var bigMiddle = Button(281);
                var smallMiddle = Button(304);
                s.Preview_ZoomRows = PreviewGrid.MinRows; // biggest icons: the big-square button can't go further
                Assert.NotEqual(bigMiddle, Button(281));
                Assert.Equal(smallMiddle, Button(304));
                s.Preview_ZoomRows = PreviewGrid.MaxRows;
                Assert.Equal(bigMiddle, Button(281));
                Assert.NotEqual(smallMiddle, Button(304));
            });
        }

        [AvaloniaFact]
        public void ACursorSavedOffScreenIsPulledOntoThePageAfterAFrame()
        {
            var theme = NewTheme();
            WithSettings(s =>
            {
                s.Preview_ZoomRows = 1; // three slots per page
                int col, row;
                PreviewGrid.FromZoomCell(7, 0, 1, out col, out row); // far beyond the first page
                PreviewActions.SetCursor(s, false, col, row);
                var ia = new PreviewInteraction();
                ia.Tick(0, s, theme); // drawing a frame must not change settings mid-render...
                Assert.False(PreviewGrid.CursorIsOnPage(s, 0));
                Dispatcher.UIThread.RunJobs(); // ...it is fixed right after
                Assert.True(PreviewGrid.CursorIsOnPage(s, 0));
            });
        }

        [AvaloniaFact]
        public void FlagTogglesShowTheRealStateWhenTheRulesRefuse()
        {
            var theme = NewTheme();
            theme.Flags.BottomDrawType = ThemeEditor.App.Localization.Enums.BottomDrawType.Texture;
            using var editors = new ThemeEditor.App.Controls.PropertyEditors();
            var host = new Avalonia.Controls.Window { Content = editors.BuildFlags(theme), Width = 600, Height = 2000 };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var name = typeof(FlagsViewModel).GetProperty(nameof(FlagsViewModel.BottomBackgroundInnerColor))
                .GetCustomAttributes(typeof(ThemeEditor.App.Localization.DisplayNameAttribute), false)
                .Cast<ThemeEditor.App.Localization.DisplayNameAttribute>().First().DisplayName;
            var row = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(host).OfType<Avalonia.Controls.Grid>()
                .First(g => g.Children.OfType<Avalonia.Controls.TextBlock>().Any(tb => tb.Text == name));
            var toggle = row.Children.OfType<Avalonia.Controls.Primitives.ToggleButton>().Single();

            toggle.IsChecked = true; // what a click does
            Dispatcher.UIThread.RunJobs();
            // Only solid colour backgrounds have these colours: the rules switch the flag back off, and so must the label
            Assert.False(theme.Flags.BottomBackgroundInnerColor);
            Assert.False(toggle.IsChecked == true);
            Assert.Equal(ThemeEditor.App.Localization.MainResources.Control_Toggle_False, toggle.Content);

            theme.Flags.BottomDrawType = ThemeEditor.App.Localization.Enums.BottomDrawType.SolidColor;
            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(theme.Flags.BottomBackgroundInnerColor);
            Assert.Equal(ThemeEditor.App.Localization.MainResources.Control_Toggle_True, toggle.Content);
            host.Close();
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
