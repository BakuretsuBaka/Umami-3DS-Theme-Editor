using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThemeEditor.App;
using ThemeEditor.App.Localization;
using Xunit;

namespace ThemeEditor.Tests
{
    public class NativeMenuTests
    {
        private static Menu WindowMenu(params MenuItem[] tops)
        {
            var menu = new Menu();
            foreach (var t in tops)
                menu.Items.Add(t);
            return menu;
        }

        [AvaloniaFact]
        public void TheMenuBarMirrorsTheWindowMenu()
        {
            var ran = 0;
            var open = new MenuItem {Header = "_Open", Command = new RelayCommand(() => ran++), InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Meta)};
            var hidden = new MenuItem {Header = "Hidden", IsVisible = false};
            var off = new MenuItem {Header = "Off", Command = new RelayCommand(() => { }, () => false)};
            var file = new MenuItem {Header = "_File", Items = {open, new Separator(), hidden, new Separator(), off, new Separator()}};
            var about = new MenuItem {Header = "About", Command = new RelayCommand(() => ran += 10)};

            var bar = NativeMenuMirror.Build(WindowMenu(file, about));
            var tops = bar.Items.Cast<NativeMenuItem>().ToList();
            Assert.Equal(new[] {"File", "About"}, tops.Select(t => t.Header));

            // Hidden entries and the separators they leave behind are dropped; disabled commands are greyed out
            var fileItems = tops[0].Menu.Items;
            Assert.Equal(3, fileItems.Count);
            var nativeOpen = (NativeMenuItem) fileItems[0];
            Assert.Equal("Open", nativeOpen.Header);
            Assert.Equal(open.InputGesture, nativeOpen.Gesture);
            Assert.True(nativeOpen.IsEnabled);
            Assert.IsType<NativeMenuItemSeparator>(fileItems[1]);
            Assert.False(((NativeMenuItem) fileItems[2]).IsEnabled);

            // A command on its own in the bar gets a menu holding it
            Assert.Equal("About", ((NativeMenuItem) tops[1].Menu.Items.Single()).Header);
        }

        [AvaloniaFact]
        public void ChoosingAnEntryRunsTheCommandOnceAndTicksRadioItems()
        {
            var ran = 0;
            var item = new MenuItem {Header = "Run", Command = new RelayCommand(() => ran++)};
            NativeMenuMirror.Click(item);
            Assert.Equal(1, ran);

            var clicked = 0;
            var a = new MenuItem {Header = "A", ToggleType = MenuItemToggleType.Radio, GroupName = "g", IsChecked = true};
            var b = new MenuItem {Header = "B", ToggleType = MenuItemToggleType.Radio, GroupName = "g"};
            b.Click += (s, e) => clicked++;
            var menu = WindowMenu(new MenuItem {Header = "Group", Items = {a, b}});
            var window = new Window {Content = menu};
            window.Show();
            NativeMenuMirror.Click(b);
            Assert.Equal(1, clicked);
            Assert.True(b.IsChecked);
            Assert.False(a.IsChecked);
            window.Close();
        }

        [AvaloniaFact]
        public void MenusAreRefilledWithTheCurrentEntries()
        {
            var recent = new MenuItem {Header = "Recent", Items = {new MenuItem {Header = "old/body_LZ.bin"}}};
            var bar = NativeMenuMirror.Build(WindowMenu(new MenuItem {Header = "File", Items = {recent}}));
            var recentNative = (NativeMenuItem) bar.Items.Cast<NativeMenuItem>().Single().Menu.Items.Single();
            Assert.Single(recentNative.Menu.Items);

            // What macOS triggers right before it shows the menu
            recent.Items.Clear();
            recent.Items.Add(new MenuItem {Header = "theme__one/body__LZ.bin"});
            recent.Items.Add(new MenuItem {Header = "theme2/body__LZ.bin"});
            NativeMenuMirror.Fill(recentNative.Menu, recent.Items.ToArray());
            Assert.Equal(new[] {"theme_one/body_LZ.bin", "theme2/body_LZ.bin"},
                recentNative.Menu.Items.Cast<NativeMenuItem>().Select(i => i.Header));
            Assert.Equal("Save as_", NativeMenuMirror.Header(new MenuItem {Header = "Save _as_"}));
        }

        [AvaloniaFact]
        public void OnMacTheMenuMovesToTheMenuBar()
        {
            var win = new MainWindow();
            win.Show();
            var menu = win.FindControl<Menu>("MainMenu");
            var bar = NativeMenu.GetMenu(win);
            if (OperatingSystem.IsMacOS())
            {
                Assert.False(menu.IsVisible);
                Assert.Contains(bar.Items.OfType<NativeMenuItem>(), i => i.Menu.Items.OfType<NativeMenuItem>()
                    .Any(e => e.Header == MainResources.Menu_File_UploadThemezer.Replace("_", "")));
            }
            else
            {
                Assert.True(menu.IsVisible);
                Assert.Null(bar);
            }
        }
    }
}
