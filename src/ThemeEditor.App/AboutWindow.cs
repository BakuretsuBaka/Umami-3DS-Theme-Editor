// --------------------------------------------------
// Umami 3DS Theme Editor - AboutWindow.cs
// --------------------------------------------------

using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    internal sealed class AboutWindow : Window
    {
        private readonly TextBlock _latest;
        private readonly TextBlock _status;

        public AboutWindow()
        {
            Title = MainResources.About_Title;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var asm = Assembly.GetEntryAssembly();
            var copyright = asm?.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

            var check = new CheckBox
            {
                Content = MainResources.About_CheckUpdates,
                IsChecked = Settings.Default.CheckUpdatesOnStartup
            };
            check.IsCheckedChanged += (s, e) => Settings.Default.CheckUpdatesOnStartup = check.IsChecked == true;

            _latest = new TextBlock {Text = MainResources.About_Checking};
            _status = new TextBlock();

            var link = new Button {Content = MainResources.About_OpenReleases, HorizontalAlignment = HorizontalAlignment.Left};
            link.Click += (s, e) => Update.OpenUrl(Update.ReleasesUrl);
            var repo = new Button {Content = Update.RepositoryUrl, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Foreground = Brushes.DodgerBlue, Padding = new Thickness(0)};
            repo.Click += (s, e) => Update.OpenUrl(Update.RepositoryUrl);

            var icon = new Image
            {
                Width = 96,
                Height = 96,
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Assets/umami.png")))
            };

            var text = new StackPanel
            {
                Spacing = 4,
                MaxWidth = 420,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock {Text = MainResources.App_Title, FontSize = 24, FontWeight = FontWeight.SemiBold},
                            new TextBlock {Text = "v" + Update.AppVersion, FontSize = 14, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3)}
                        }
                    },
                    new TextBlock {Text = copyright, FontSize = 12, Opacity = 0.8},
                    new TextBlock {Text = MainResources.About_Credits, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8},
                    repo,
                    new Separator {Margin = new Thickness(0, 6)},
                    check,
                    _latest,
                    _status,
                    link
                }
            };

            Content = new StackPanel {Orientation = Orientation.Horizontal, Margin = new Thickness(20), Children = {icon, text}};
            KeyDown += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape)
                    Close();
            };
            Opened += async (s, e) => await CheckLatest();
        }

        private async System.Threading.Tasks.Task CheckLatest()
        {
            var has = await Update.CheckUpdateAvailable();
            _latest.Text = string.Format(MainResources.About_Latest, Update.UpdateVersion);
            _status.Text = has ? MainResources.About_UpdateAvailable : MainResources.About_UpToDate;
        }
    }
}
