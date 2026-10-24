// --------------------------------------------------
// Umami 3DS Theme Editor - CrashWindow.cs
// --------------------------------------------------

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ThemeEditor.App.Localization;

namespace ThemeEditor.App
{
    internal sealed class CrashWindow : Window
    {
        public CrashWindow(Exception ex)
        {
            Title = MainResources.Crash_Title;
            Width = 560;
            Height = 380;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var box = new TextBox {Text = ex.ToString(), IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, AcceptsReturn = true};
            var ok = new Button {Content = MainResources.Dialog_OK, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true};
            ok.Click += (s, e) => Close();
            var grid = new Grid {Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,*,Auto")};
            grid.Children.Add(new TextBlock {Text = MainResources.Crash_Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)});
            Grid.SetRow(box, 1);
            Grid.SetRow(ok, 2);
            ok.Margin = new Thickness(0, 8, 0, 0);
            grid.Children.Add(box);
            grid.Children.Add(ok);
            Content = grid;
        }
    }
}
