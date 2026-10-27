using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using ThemeEditor.App;
using ThemeEditor.App.Localization.Enums;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Graphics;
using Xunit;

namespace ThemeEditor.Tests
{
    public class AutoColorsTests
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

        /// <summary>A 512x256 texture painted by <paramref name="pixel" /> (x, y) -> (r, g, b) over the visible area.</summary>
        internal static PixelBuffer Image(int visibleWidth, Func<int, int, (byte, byte, byte)> pixel)
        {
            var bgra = new byte[512 * 256 * 4];
            for (var y = 0; y < 256; y++)
            for (var x = 0; x < 512; x++)
            {
                var (r, g, b) = pixel(Math.Min(x, visibleWidth - 1), Math.Min(y, 239));
                var i = (y * 512 + x) * 4;
                bgra[i] = b;
                bgra[i + 1] = g;
                bgra[i + 2] = r;
                bgra[i + 3] = 255;
            }
            return new PixelBuffer(512, 256, bgra);
        }

        internal static readonly Dictionary<string, Func<int, int, (byte, byte, byte)>> Wallpapers = new Dictionary<string, Func<int, int, (byte, byte, byte)>>
        {
            ["sunset"] = (x, y) => ((byte) (250 - y / 3), (byte) (120 + y / 4), (byte) (60 + y / 2)),
            ["forest"] = (x, y) => (y > 170 ? ((byte) 40, (byte) 90, (byte) 40) : ((byte) 150, (byte) 200, (byte) 120)),
            ["pastel"] = (x, y) => ((byte) 248, (byte) (200 + (x % 40)), (byte) 225),
            ["black"] = (x, y) => ((byte) 0, (byte) 0, (byte) 0),
            ["white"] = (x, y) => ((byte) 255, (byte) 255, (byte) 255),
            ["grey"] = (x, y) => ((byte) (90 + x / 4), (byte) (90 + x / 4), (byte) (90 + x / 4)),
            ["noise"] = (x, y) => ((byte) (x * 37 % 256), (byte) (y * 91 % 256), (byte) ((x + y) * 53 % 256)),
            ["neon"] = (x, y) => ((x / 20 + y / 20) % 2 == 0 ? ((byte) 10, (byte) 10, (byte) 30) : ((byte) 255, (byte) 30, (byte) 200))
        };

        internal static void Paint(ThemeViewModel t, string wallpaper)
        {
            var f = Wallpapers[wallpaper];
            t.Textures.Top.EncodeTexture(Image(412, f), RawTexture.DataFormat.Bgr565);
            t.Textures.Bottom.EncodeTexture(Image(320, f), RawTexture.DataFormat.Bgr565);
        }

        private static void AssertReadable(ThemeViewModel t, string what)
        {
            var c = t.Colors;
            void Pair(Color fg, Color bg, double min, string name)
            {
                var ratio = AutoColors.Contrast(fg, bg);
                Assert.True(ratio >= min - 0.05, $"{what}: {name} contrast {ratio:0.00} < {min}");
            }
            Pair(c.TopCorner.Text, c.TopCorner.Main, 4.5, "top bar text");
            Pair(c.GameText.Text, c.GameText.Main, 4.5, "description text");
            Pair(c.DemoText.Text, c.DemoText.Main, 4.5, "demo text");
            Pair(c.Open.TextUnpressed, c.Open.Unpressed, 4.5, "Open label");
            Pair(c.Open.TextPressed, c.Open.Pressed, 4.5, "Open label (pressed)");
            Pair(c.Close.TextUnpressed, c.Close.Unpressed, 4.5, "Close label");
            Pair(c.Close.TextPressed, c.Close.Pressed, 4.5, "Close label (pressed)");
            Pair(c.Arrow.Unpressed, c.ArrowButton.Main, 3, "page arrow");
            Pair(c.BottomCorner.IconTop, c.BottomCorner.Main, 2.5, "corner icons");
            Pair(c.FolderArrow.ArrowUnpressed, c.FolderArrow.Main, 2.5, "folder back arrow");
        }

        [AvaloniaFact]
        public void EveryStyleIsReadableOnVeryDifferentWallpapers()
        {
            var t = NewTheme();
            foreach (var wallpaper in Wallpapers.Keys)
            {
                Paint(t, wallpaper);
                var schemes = AutoColors.Suggest(t);
                Assert.Equal(4, schemes.Count);
                foreach (var scheme in schemes)
                {
                    AutoColors.Apply(t, scheme);
                    AssertReadable(t, $"{wallpaper}/{scheme.Style}");
                    // Nothing is left black, and the cursor stands out from the wallpaper's main tone
                    foreach (var set in AutoColors.AllSets)
                        Assert.False(AutoColors.IsUnset(t, set), $"{wallpaper}/{scheme.Style}: {set} is still black");
                    Assert.True(AutoColors.Contrast(t.Colors.Cursor.Main, scheme.Base.ToColor()) >= 1.75,
                        $"{wallpaper}/{scheme.Style}: cursor hardly visible on the wallpaper");
                }
            }
        }

        [AvaloniaFact]
        public void ButtonOffersFourStylesAndOneUndoRestoresTheColours()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            Paint(win.ViewModel, "forest");
            win.FlushPendingSnapshot();
            var before = win.ViewModel.Colors.Folder.Main;
            var cursorFlag = win.ViewModel.Flags.CursorColor;
            win.FindControl<Avalonia.Controls.TabControl>("EditorTabs").SelectedIndex = 1; // Colors
            Dispatcher.UIThread.RunJobs();

            var button = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<Avalonia.Controls.Button>()
                .First(b => b.Content is string text && text.StartsWith(ThemeEditor.App.Localization.MainResources.Control_AutoColors));
            var flyout = (Avalonia.Controls.MenuFlyout) button.Flyout;
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, flyout.Items.Count);

            ((Avalonia.Controls.MenuItem) flyout.Items[0]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, win.ViewModel.Colors.Folder.Main);
            Assert.True(win.ViewModel.Flags.CursorColor);

            win.UndoCommandWrapper.Command.Execute(null);
            Assert.Equal(before, win.ViewModel.Colors.Folder.Main);
            Assert.Equal(cursorFlag, win.ViewModel.Flags.CursorColor);
        }

        [AvaloniaFact]
        public void StylesTakeTheirColoursFromTheWallpaper()
        {
            var t = NewTheme();
            Paint(t, "forest");
            var light = AutoColors.Suggest(t)[0];
            // Forest is mostly light green with a dark green ground: the accent is green
            Assert.InRange(light.Accent.H, 100, 160);
            Paint(t, "sunset");
            var warm = AutoColors.Suggest(t)[0];
            Assert.True(warm.Accent.H < 80 || warm.Accent.H > 330, $"sunset accent hue {warm.Accent.H:0}");
            // Soft pastel pink keeps its own hue rather than a contrasting stock colour
            Paint(t, "pastel");
            var pastel = AutoColors.Suggest(t)[0];
            Assert.True(pastel.Accent.H < 30 || pastel.Accent.H > 300, $"pastel accent hue {pastel.Accent.H:0}");
            // Black-and-white wallpapers still get a real accent rather than grey on grey
            Paint(t, "grey");
            Assert.True(AutoColors.Suggest(t)[0].Accent.C > 0.08);
        }

        [AvaloniaFact]
        public void ApplySwitchesColourFlagsOnButNeverChangesHowScreensAreDrawn()
        {
            var t = NewTheme();
            Paint(t, "sunset");
            t.Flags.TopDrawType = TopDrawType.Texture;
            t.Flags.BottomDrawType = BottomDrawType.Texture;
            AutoColors.Apply(t, AutoColors.Suggest(t)[1]);
            Assert.True(t.Flags.FolderColor && t.Flags.FileColor && t.Flags.CursorColor && t.Flags.OpenCloseColor);
            Assert.True(t.Flags.ArrowColor && t.Flags.ArrowButtonColor && t.Flags.TopCornerButtonColor && t.Flags.BottomCornerButtonColor);
            Assert.Equal(GameTextDrawType.Colored, t.Flags.GameTextDrawType);
            Assert.Equal(TopDrawType.Texture, t.Flags.TopDrawType);
            Assert.Equal(BottomDrawType.Texture, t.Flags.BottomDrawType);
            // A theme that hides the description keeps hiding it
            t.Flags.GameTextDrawType = GameTextDrawType.Hidden;
            AutoColors.Apply(t, AutoColors.Suggest(t)[0]);
            Assert.Equal(GameTextDrawType.Hidden, t.Flags.GameTextDrawType);
        }

        [AvaloniaFact]
        public void SwitchingAFlagOnFillsColoursThatWereNeverSet()
        {
            var t = NewTheme();
            Paint(t, "forest");
            // A fresh theme stores black for every colour
            t.Flags.FolderColor = false;
            t.Colors.Folder.Main = t.Colors.Folder.Light = t.Colors.Folder.Dark = t.Colors.Folder.Shadow = Colors.Black;
            Assert.True(AutoColors.IsUnset(t, nameof(ColorsViewModel.Folder)));

            t.Flags.FolderColor = true;
            Assert.False(AutoColors.IsUnset(t, nameof(ColorsViewModel.Folder)));
            Assert.NotEqual(Colors.Black, t.Colors.Folder.Main);

            // Colours the user picked are left alone when the flag is switched off and on again, even dark ones
            var mine = Color.FromRgb(20, 10, 5);
            t.Colors.Folder.Main = mine;
            t.Flags.FolderColor = false;
            t.Flags.FolderColor = true;
            Assert.Equal(mine, t.Colors.Folder.Main);

            // Open and Close share one flag; both get filled
            t.Flags.OpenCloseColor = false;
            foreach (var set in new[] { t.Colors.Open, t.Colors.Close })
                set.Pressed = set.Unpressed = set.Light = set.TextPressed = set.TextUnpressed = set.TextShadow = Colors.Black;
            t.Flags.OpenCloseColor = true;
            Assert.False(AutoColors.IsUnset(t, nameof(ColorsViewModel.Open)));
            Assert.False(AutoColors.IsUnset(t, nameof(ColorsViewModel.Close)));
        }

        [AvaloniaFact]
        public void PreviewItemsStayOnScreenAndInOrderAtEveryZoom()
        {
            var s = Settings.Default;
            var saved = (s.Preview_ZoomRows, s.Preview_Main_CartEnable, s.Preview_Main_FileEnable, s.Preview_Main_FolderEnable,
                s.Preview_Main_CartX, s.Preview_Main_CartY, s.Preview_Main_FileX, s.Preview_Main_FileY, s.Preview_Main_FolderX, s.Preview_Main_FolderY);
            try
            {
                s.Preview_Main_CartEnable = s.Preview_Main_FileEnable = s.Preview_Main_FolderEnable = true;
                // The default layout: card top-left, folder below it, file at the top of the second column (slot 3)
                s.Preview_Main_CartX = 0; s.Preview_Main_CartY = 2;
                s.Preview_Main_FolderX = 0; s.Preview_Main_FolderY = 1;
                s.Preview_Main_FileX = 1; s.Preview_Main_FileY = 2;

                s.Preview_ZoomRows = 3;
                var file3 = ItemRenderer.GridPosition(1, 2, false);
                Assert.Equal(107, file3.X, 2); // the default zoom keeps the arranged layout
                Assert.Equal(70, file3.Y, 2);

                for (var rows = 1; rows <= 6; rows++)
                {
                    s.Preview_ZoomRows = rows;
                    Assert.True(PreviewGrid.IsVisible(1, 2), $"file icon off screen with {rows} rows");
                    Assert.True(PreviewGrid.IsVisible(0, 2) && PreviewGrid.IsVisible(0, 1), $"an item is off screen with {rows} rows");
                    var cells = new[] { (0, 2), (0, 1), (1, 2) }.Select(p => ItemRenderer.GridPosition(p.Item1, p.Item2, false)).ToList();
                    Assert.Equal(3, cells.Distinct().Count());
                }
            }
            finally
            {
                (s.Preview_ZoomRows, s.Preview_Main_CartEnable, s.Preview_Main_FileEnable, s.Preview_Main_FolderEnable,
                    s.Preview_Main_CartX, s.Preview_Main_CartY, s.Preview_Main_FileX, s.Preview_Main_FileY, s.Preview_Main_FolderX, s.Preview_Main_FolderY) = saved;
            }
        }
    }
}
