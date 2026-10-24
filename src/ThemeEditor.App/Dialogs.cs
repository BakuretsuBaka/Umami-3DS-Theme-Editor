// --------------------------------------------------
// Umami 3DS Theme Editor - Dialogs.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace ThemeEditor.App
{
    public enum MessageKind
    {
        Information,
        Warning,
        Error
    }

    /// <summary>File pickers and simple message boxes shared by every window.</summary>
    internal static class Dialogs
    {
        public static Window MainWindow
            => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        public static FilePickerFileType FileType(string name, params string[] extensions)
        {
            return new FilePickerFileType(name)
            {
                Patterns = extensions.Select(e => "*" + e).ToArray(),
                AppleUniformTypeIdentifiers = null
            };
        }

        public static async Task<string> OpenFile(Window owner, string title, params FilePickerFileType[] types)
        {
            owner = owner ?? MainWindow;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = types
            });
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }

        public static async Task<string> SaveFile(Window owner, string title, string suggestedName, params FilePickerFileType[] types)
        {
            owner = owner ?? MainWindow;
            var defaultExt = types.Length > 0 && types[0].Patterns?.Count > 0
                ? types[0].Patterns.First().TrimStart('*').TrimStart('.')
                : null;
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = defaultExt,
                FileTypeChoices = types,
                ShowOverwritePrompt = true
            });
            return file?.TryGetLocalPath();
        }

        public static Task Show(Window owner, string message, string title, MessageKind kind = MessageKind.Information)
        {
            return Ask(owner, message, title, kind, false);
        }

        /// <summary>Single line text prompt. Returns null when cancelled.</summary>
        public static async Task<string> Prompt(Window owner, string title, string label, string initial)
        {
            owner = owner ?? MainWindow;
            string result = null;
            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 340,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            var box = new TextBox { Text = initial ?? string.Empty, MinWidth = 280 };
            var ok = new Button { Content = Localization.MainResources.Dialog_OK, IsDefault = true, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = Localization.MainResources.Dialog_Cancel, IsCancel = true, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
            ok.Click += (s, e) => { result = box.Text; window.Close(); };
            cancel.Click += (s, e) => window.Close();
            window.Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = label },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } }
                }
            };
            window.Opened += (s, e) => box.Focus();
            await window.ShowDialog(owner);
            return result;
        }

        /// <summary>Yes/No question. Returns true for yes.</summary>
        public static Task<bool> Confirm(Window owner, string message, string title)
        {
            return Ask(owner, message, title, MessageKind.Information, true);
        }

        private static async Task<bool> Ask(Window owner, string message, string title, MessageKind kind, bool question)
        {
            owner = owner ?? MainWindow;
            var result = false;

            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 320,
                MaxWidth = 640,
                CanResize = false,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false
            };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                VerticalAlignment = VerticalAlignment.Center
            };

            var accent = kind == MessageKind.Error ? "!" : kind == MessageKind.Warning ? "!" : "i";
            var glyph = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = kind == MessageKind.Error ? Brushes.Firebrick
                    : kind == MessageKind.Warning ? Brushes.DarkOrange : Brushes.SteelBlue,
                Child = new TextBlock
                {
                    Text = accent,
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0)
            };

            Button Make(string label, bool value, bool isDefault)
            {
                var b = new Button { Content = label, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = isDefault };
                b.Click += (s, e) =>
                {
                    result = value;
                    window.Close();
                };
                return b;
            }

            if (question)
            {
                buttons.Children.Add(Make(Localization.MainResources.Dialog_Yes ?? "Yes", true, true));
                buttons.Children.Add(Make(Localization.MainResources.Dialog_No ?? "No", false, false));
            }
            else
            {
                buttons.Children.Add(Make(Localization.MainResources.Dialog_OK ?? "OK", true, true));
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, text } };
            window.Content = new StackPanel { Margin = new Thickness(18), Children = { row, buttons } };
            window.KeyDown += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape)
                    window.Close();
            };

            if (owner != null)
                await window.ShowDialog(owner);
            else
                window.Show();
            return result;
        }
    }
}
