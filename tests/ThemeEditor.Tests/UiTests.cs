using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ThemeEditor.App;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Themes;
using ThemeEditor.App.Themes.ColorSets;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ThemeEditor.Tests.TestAppBuilder))]

namespace ThemeEditor.Tests
{
    public class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<UmamiApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    public class UiTests
    {
        private static string OutDir()
        {
            var d = Environment.GetEnvironmentVariable("UMAMI_SHOT_DIR");
            if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
            return d;
        }

        [AvaloniaFact]
        public void MainWindowRendersWithDefaultTheme()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            Assert.NotNull(win.ViewModel);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var dir = OutDir();
            var tabs = win.FindControl<Avalonia.Controls.TabControl>("EditorTabs");
            for (var tab = 0; tab < 3; tab++)
            {
                tabs.SelectedIndex = tab;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var frame = win.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (dir != null) frame.Save(Path.Combine(dir, "window" + tab + ".png"));
            }
        }

        [AvaloniaFact]
        public void PreviewsRender()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            var dir = OutDir();
            foreach (var kind in new[] { PreviewKind.Top, PreviewKind.Bottom, PreviewKind.Both })
            {
                var png = PreviewExporter.RenderPng(win.ViewModel, kind);
                Assert.True(png.Length > 1000);
                if (dir != null) File.WriteAllBytes(Path.Combine(dir, "preview_" + kind + ".png"), png);
            }
            ThemeEditor.App.Properties.Settings.Default.Preview_ModeToggle = true;
            var folder = PreviewExporter.RenderPng(win.ViewModel, PreviewKind.Both);
            if (dir != null) File.WriteAllBytes(Path.Combine(dir, "preview_folder.png"), folder);
            ThemeEditor.App.Properties.Settings.Default.Preview_ModeToggle = false;
        }
    }
}
namespace ThemeEditor.Tests
{
    public class LocalizationTests
    {
        public static System.Collections.Generic.IEnumerable<object[]> Cultures() =>
            ThemeEditor.App.MainWindow.Languages.Where(l => l.Culture != "en").Select(l => new object[] { l.Culture });

        [Theory]
        [MemberData(nameof(Cultures))]
        public void PlaceholdersMatchEnglish(string culture)
        {
            var ci = new System.Globalization.CultureInfo(culture);
            var rm = ThemeEditor.App.Localization.MainResources.ResourceManager;
            var bad = new System.Collections.Generic.List<string>();
            foreach (var prop in typeof(ThemeEditor.App.Localization.MainResources).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                         .Where(p => p.PropertyType == typeof(string)))
            {
                var en = rm.GetString(prop.Name, System.Globalization.CultureInfo.InvariantCulture);
                var tr = rm.GetString(prop.Name, ci);
                if (en == null || tr == null) { bad.Add(prop.Name + " missing"); continue; }
                var re = new System.Text.RegularExpressions.Regex(@"\{\d+(:[^}]*)?\}");
                var a = re.Matches(en).Select(m => m.Value).OrderBy(x => x);
                var b = re.Matches(tr).Select(m => m.Value).OrderBy(x => x);
                if (!a.SequenceEqual(b)) bad.Add(prop.Name);
            }
            Assert.True(bad.Count == 0, culture + ": " + string.Join(", ", bad));
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void ActuallyTranslated(string culture)
        {
            var ci = new System.Globalization.CultureInfo(culture);
            var rm = ThemeEditor.App.Localization.MainResources.ResourceManager;
            Assert.NotEqual(rm.GetString("Menu_Language", System.Globalization.CultureInfo.InvariantCulture), rm.GetString("Menu_Language", ci));
            var tm = ThemeEditor.App.Localization.ThemeResources.ResourceManager;
            Assert.NotEqual(tm.GetString("Theme_Info_Author", System.Globalization.CultureInfo.InvariantCulture), tm.GetString("Theme_Info_Author", ci));
        }
    }
}
namespace ThemeEditor.Tests
{
    public class ColorEditTests
    {
        [AvaloniaFact]
        public void EditingDisabledSetEnablesFlagAndChangesPreview()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(20); }
            var vm = win.ViewModel;
            Assert.False(vm.Flags.CursorColor);
            var before = ThemeEditor.App.Previews.PreviewExporter.RenderPng(vm, ThemeEditor.App.PreviewKind.Bottom);
            vm.Colors.Cursor.Main = Avalonia.Media.Color.FromRgb(200, 0, 120);
            Assert.True(vm.Flags.CursorColor);
            var after = ThemeEditor.App.Previews.PreviewExporter.RenderPng(vm, ThemeEditor.App.PreviewKind.Bottom);
            Assert.False(before.SequenceEqual(after));
        }
    }
}
namespace ThemeEditor.Tests
{
    public class AllSetsTests
    {
        public static System.Collections.Generic.IEnumerable<object[]> Sets() =>
            new[] { "TopBackground", "TopCorner", "BottomBackgroundInner", "BottomBackgroundOuter", "BottomCorner", "Open", "Close",
                    "FolderBackground", "Folder", "FolderArrow", "File", "Cursor", "ArrowButton" }.Select(x => new object[] { x });

        [AvaloniaTheory]
        [MemberData(nameof(Sets))]
        public void EditingFirstColorOfSetChangesPreview(string setName)
        {
            var win = new MainWindow(); win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(20); }
            var vm = win.ViewModel;
            var s = ThemeEditor.App.Properties.Settings.Default;
            System.Func<byte[]> snap = () =>
            {
                var all = new System.Collections.Generic.List<byte>();
                foreach (var folder in new[] { false, true })
                foreach (var pressed in new[] { false, true })
                {
                    s.Preview_ModeToggle = folder; s.Preview_PressedToggle = pressed;
                    all.AddRange(ThemeEditor.App.Previews.PreviewExporter.RenderPng(vm, ThemeEditor.App.PreviewKind.Both));
                }
                s.Preview_ModeToggle = false; s.Preview_PressedToggle = false;
                return all.ToArray();
            };
            var set = (ViewModelBase)typeof(ColorsViewModel).GetProperty(setName).GetValue(vm.Colors);
            var before = snap();
            foreach (var cp in set.GetType().GetProperties().Where(p => p.PropertyType == typeof(Avalonia.Media.Color) && p.CanWrite))
            {
                var orig = (Avalonia.Media.Color)cp.GetValue(set);
                cp.SetValue(set, Avalonia.Media.Color.FromRgb((byte)(orig.R ^ 0x80), (byte)(orig.G ^ 0x40), (byte)(orig.B ^ 0xC0)));
                break;
            }
            Assert.False(before.SequenceEqual(snap()), setName + " did not change the preview");
        }
    }
}
