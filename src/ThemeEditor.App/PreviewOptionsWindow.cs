// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewOptionsWindow.cs
// --------------------------------------------------

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    /// <summary>Where the cart, file, folder and cursor icons are placed in the bottom screen preview.</summary>
    internal sealed class PreviewOptionsWindow : Window
    {
        public PreviewOptionsWindow()
        {
            Title = MainResources.Preview_Options_Title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var main = new Grid {ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,*"), Margin = new Thickness(6)};
            var folder = new Grid {ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,*"), Margin = new Thickness(6)};

            var row = 0;
            AddRow(main, row++, MainResources.Preview_Options_Cart, "Preview_Main_CartEnable", "Preview_Main_CartY", "Preview_Main_CartX", 2);
            AddRow(main, row++, MainResources.Preview_Options_File, "Preview_Main_FileEnable", "Preview_Main_FileY", "Preview_Main_FileX", 2);
            AddRow(main, row++, MainResources.Preview_Options_Folder, "Preview_Main_FolderEnable", "Preview_Main_FolderY", "Preview_Main_FolderX", 2);
            AddRow(main, row++, MainResources.Preview_Options_Cursor, "Preview_Main_CursorEnable", "Preview_Main_CursorY", "Preview_Main_CursorX", 2);

            row = 0;
            AddRow(folder, row++, MainResources.Preview_Options_File, "Preview_Folder_FileEnable", "Preview_Folder_FileY", "Preview_Folder_FileX", 1);
            AddRow(folder, row++, MainResources.Preview_Options_Folder, "Preview_Folder_FolderEnable", "Preview_Folder_FolderY", "Preview_Folder_FolderX", 1);
            AddRow(folder, row++, MainResources.Preview_Options_Cursor, "Preview_Folder_CursorEnable", "Preview_Folder_CursorY", "Preview_Folder_CursorX", 1);

            var close = new Button {Content = MainResources.Dialog_Close, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true, IsCancel = true};
            close.Click += (s, e) => Close();

            Content = new StackPanel
            {
                Margin = new Thickness(8),
                Spacing = 6,
                Children =
                {
                    new Expander {Header = MainResources.Preview_Options_MainMode, IsExpanded = true, Content = main, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch},
                    new Expander {Header = MainResources.Preview_Options_FolderMode, IsExpanded = true, Content = folder, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch},
                    close
                }
            };
        }

        private static void AddRow(Grid grid, int row, string label, string enableProp, string rowProp, string colProp, int maxRow)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var settings = Settings.Default;

            var name = new TextBlock {Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3), MinWidth = 60};
            var toggle = new ToggleButton {Margin = new Thickness(3), MinWidth = 60, HorizontalContentAlignment = HorizontalAlignment.Center};
            toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(enableProp, BindingMode.TwoWay) {Source = settings});
            Action sync = () => toggle.Content = toggle.IsChecked == true ? MainResources.Control_Toggle_True : MainResources.Control_Toggle_False;
            toggle.IsCheckedChanged += (s, e) => sync();
            sync();

            var rowLabel = new TextBlock {Text = MainResources.Preview_Options_Row, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3)};
            var rowBox = new NumericUpDown {Minimum = 0, Maximum = maxRow, Margin = new Thickness(3), FormatString = "0", MinWidth = 90};
            rowBox.Bind(NumericUpDown.ValueProperty, new Binding(rowProp, BindingMode.TwoWay) {Source = settings, Converter = IntToDecimal});
            rowBox.Bind(IsEnabledProperty, new Binding(enableProp) {Source = settings});

            var colLabel = new TextBlock {Text = MainResources.Preview_Options_Column, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3)};
            var colBox = new NumericUpDown {Minimum = 0, Maximum = 4, Margin = new Thickness(3), FormatString = "0", MinWidth = 90};
            colBox.Bind(NumericUpDown.ValueProperty, new Binding(colProp, BindingMode.TwoWay) {Source = settings, Converter = IntToDecimal});
            colBox.Bind(IsEnabledProperty, new Binding(enableProp) {Source = settings});

            Place(grid, row, 0, name);
            Place(grid, row, 1, toggle);
            Place(grid, row, 2, rowLabel);
            Place(grid, row, 3, rowBox);
            Place(grid, row, 4, colLabel);
            Place(grid, row, 5, colBox);
        }

        private static void Place(Grid grid, int row, int col, Control c)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            grid.Children.Add(c);
        }

        private static readonly Avalonia.Data.Converters.IValueConverter IntToDecimal =
            new Avalonia.Data.Converters.FuncValueConverter<int, decimal?>(i => i);
    }
}
