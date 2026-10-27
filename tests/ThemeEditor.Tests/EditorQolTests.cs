using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using ThemeEditor.App;
using ThemeEditor.Common.Compression;
using Xunit;

namespace ThemeEditor.Tests
{
    public class LzMatchFinderTests
    {
        /// <summary>The hash-chain matcher must pick exactly what the original brute force search picks.</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public unsafe void MatchesBruteForceSearch(int seed)
        {
            var rnd = new Random(seed);
            var data = new byte[60000];
            for (var i = 0; i < data.Length; i++)
                data[i] = seed switch
                {
                    1 => (byte) rnd.Next(0, 4), // many short matches
                    2 => (byte) (i % 997 < 400 ? 0 : rnd.Next(0, 256)), // long runs and noise
                    _ => (byte) (Math.Sin(i / 50.0) * 40 + 100 + rnd.Next(0, 3)) // photo-like
                };

            var finder = new LZMatchFinder(data, data.Length);
            fixed (byte* start = &data[0])
            {
                for (var pos = 0; pos < data.Length;)
                {
                    var window = Math.Min(pos, 0x1000);
                    var max = Math.Min(data.Length - pos, 0x10110);
                    var expected = LZUtil.GetOccurrenceLength(start + pos, max, start + pos - window, window, out var expectedDisp);
                    var actual = finder.Find(pos, max, window, out var actualDisp);
                    if (expected >= 3)
                    {
                        Assert.Equal(expected, actual);
                        Assert.Equal(expectedDisp, actualDisp);
                        pos += expected;
                    }
                    else
                    {
                        Assert.True(actual < 3, $"pos {pos}: found {actual}, brute force {expected}");
                        pos++;
                    }
                }
            }
        }

        [Fact]
        public void CompressedThemeRoundTrips()
        {
            var rnd = new Random(7);
            var data = new byte[200000];
            rnd.NextBytes(data.AsSpan(0, 50000));
            using (var src = new MemoryStream(data))
            using (var dst = new MemoryStream())
            using (var back = new MemoryStream())
            {
                LZ11.Compress(src, data.Length, dst, true);
                dst.Position = 0;
                LZ11.Decompress(dst, dst.Length, back);
                Assert.Equal(data, back.ToArray());
            }
        }
    }

    public class EditorQolTests
    {
        private static MainWindow NewWindow()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            Dispatcher.UIThread.RunJobs();
            return win;
        }

        [AvaloniaFact]
        public void NewThemeStartsClean()
        {
            var win = NewWindow();
            Assert.False(win.HasUnsavedChanges);
            Assert.DoesNotContain("*", win.Title);
        }

        [AvaloniaFact]
        public void EditsCanBeUndoneAndRedone()
        {
            var win = NewWindow();
            var original = win.ViewModel.Colors.Cursor.Main;
            var edited = Color.FromRgb(0x12, 0x34, 0x56);

            win.ViewModel.Colors.Cursor.Main = edited;
            Assert.True(win.HasUnsavedChanges);
            Assert.StartsWith("*", win.Title);

            CommandManager.RequeryNow();
            Assert.True(win.UndoCommandWrapper.Command.CanExecute(null));
            win.UndoCommandWrapper.Command.Execute(null);
            Assert.Equal(original, win.ViewModel.Colors.Cursor.Main);
            Assert.False(win.HasUnsavedChanges);

            CommandManager.RequeryNow();
            Assert.True(win.RedoCommandWrapper.Command.CanExecute(null));
            win.RedoCommandWrapper.Command.Execute(null);
            Assert.Equal(edited, win.ViewModel.Colors.Cursor.Main);
            Assert.True(win.HasUnsavedChanges);
        }

        [AvaloniaFact]
        public void EditingBackToTheSavedStateIsClean()
        {
            var win = NewWindow();
            var original = win.ViewModel.Colors.Cursor.Main;
            win.ViewModel.Colors.Cursor.Main = Color.FromRgb(1, 2, 3);
            win.UndoCommandWrapper.Command.Execute(null); // flushes the pending edit and steps back
            Assert.False(win.HasUnsavedChanges);
            // (a cursor color edit would also switch on the custom cursor color flag, so use the name here)
            var name = win.ViewModel.Info.ShortDescription;
            win.ViewModel.Info.ShortDescription = "Temporary";
            win.ViewModel.Info.ShortDescription = name;
            win.FlushPendingSnapshot(); // what the debounce timer does after a short pause
            Assert.False(win.HasUnsavedChanges);
            Assert.DoesNotContain("*", win.Title);
        }

        /// <summary>Every top-level menu has to be fully visible at the smallest window size, in every language.</summary>
        [AvaloniaFact]
        public void MenuBarFitsAtMinimumWidthInAllLanguages()
        {
            var previous = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (culture, _) in MainWindow.Languages)
                {
                    System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
                    var win = new MainWindow();
                    win.Width = win.MinWidth;
                    win.Show();
                    Dispatcher.UIThread.RunJobs();
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var menu = win.FindControl<Avalonia.Controls.Menu>("MainMenu");
                    // Items that run past the menu bar end up hidden under the preview panel or the window edge
                    var menuRight = menu.TranslatePoint(new Avalonia.Point(menu.Bounds.Width, 0), win).Value.X;
                    Assert.True(menuRight <= win.Bounds.Width + 0.5);
                    Assert.True(menu.Items.Count >= 9);
                    foreach (var item in menu.Items)
                    {
                        var control = (Avalonia.Controls.Control) item;
                        Assert.True(control.Bounds.Width > 0, $"{culture}: menu not laid out");
                        var right = control.TranslatePoint(new Avalonia.Point(control.Bounds.Width, 0), win);
                        Assert.True(right.HasValue && right.Value.X <= menuRight + 0.5,
                            $"{culture}: menu '{(item as Avalonia.Controls.MenuItem)?.Header}' ends at {right?.X}, the menu bar at {menuRight}");
                    }
                    win.Close();
                }
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = previous;
            }
        }

        /// <summary>The library window's toolbar (category tabs, Import, Export) has to fit at its minimum width in every language.</summary>
        [AvaloniaFact]
        public void LibraryToolbarFitsAtMinimumWidthInAllLanguages()
        {
            var previous = System.Globalization.CultureInfo.CurrentUICulture;
            var dir = Path.Combine(Path.GetTempPath(), "umami-toolbar-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var (culture, _) in MainWindow.Languages)
                {
                    System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
                    var lib = new ThemeEditor.App.Library.ResourceLibrary(dir);
                    lib.Load(false);
                    var win = new ThemeEditor.App.Library.ResourceLibraryWindow(lib, null);
                    win.Width = win.MinWidth;
                    win.Show();
                    Dispatcher.UIThread.RunJobs();
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var controls = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<Control>()
                        .Where(c => c is Button || c is RadioButton || c is ComboBox || c is TextBox).Where(c => c.IsEffectivelyVisible).ToList();
                    Assert.True(controls.Count >= 8, $"{culture}: toolbar not laid out");
                    foreach (var c in controls)
                    {
                        var right = c.TranslatePoint(new Point(c.Bounds.Width, 0), win);
                        Assert.True(right.HasValue && right.Value.X <= win.Bounds.Width + 0.5,
                            $"{culture}: {c.GetType().Name} '{(c as ContentControl)?.Content}' ends at {right?.X} of {win.Bounds.Width}");
                    }
                    win.Close();
                }
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = previous;
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void FitCropsToTheVisibleArea()
        {
            // 400x100 red/blue halves, fitted into 320x240 of a 512x256 canvas
            var bgra = new byte[400 * 100 * 4];
            for (var y = 0; y < 100; y++)
            for (var x = 0; x < 400; x++)
            {
                var i = (y * 400 + x) * 4;
                bgra[i + (x < 200 ? 2 : 0)] = 255;
                bgra[i + 3] = 255;
            }
            var fitted = new PixelBuffer(400, 100, bgra).Fit(320, 240, 512, 256, true);
            Assert.Equal(512, fitted.Width);
            Assert.Equal(256, fitted.Height);
            // Inside the visible area everything is opaque, outside it stays transparent
            Assert.Equal(255, fitted.Bgra[(120 * 512 + 10) * 4 + 3]);
            Assert.Equal(0, fitted.Bgra[(250 * 512 + 400) * 4 + 3]);
            // Cropping keeps the middle: left is red, right is blue
            Assert.True(fitted.Bgra[(120 * 512 + 40) * 4 + 2] > 200);
            Assert.True(fitted.Bgra[(120 * 512 + 280) * 4 + 0] > 200);
        }
    }
}
