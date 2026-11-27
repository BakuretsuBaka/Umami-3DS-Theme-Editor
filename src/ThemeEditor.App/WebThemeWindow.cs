// --------------------------------------------------
// Umami 3DS Theme Editor - WebThemeWindow.cs
// --------------------------------------------------

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Asks which theme to open from the web: a link to any of the sites, a Themezer quick ID or a theme number, and
    ///     shows right away what it recognised. Bare numbers belong to the site picked in the list.
    /// </summary>
    internal sealed class WebThemeWindow : Window
    {
        private static readonly WebThemeSource[] NumberSources = { WebThemeSource.ThreeDsThemes, WebThemeSource.ThemePlaza };

        private readonly TextBox _input;
        private readonly ComboBox _numbers;
        private readonly TextBlock _recognised;
        private readonly Button _open;

        /// <summary>The theme to open, once the user pressed Open.</summary>
        public WebThemeId Result { get; private set; }

        public WebThemeWindow()
        {
            Title = MainResources.Web_Title;
            Width = 500;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _input = new TextBox { Watermark = "T1A20D  ·  https://themezer.net/3ds/themes/…  ·  10001" };
            _numbers = new ComboBox
            {
                ItemsSource = new[] { WebThemes.SiteName(NumberSources[0]), WebThemes.SiteName(NumberSources[1]) },
                SelectedIndex = Settings.Default.Web_NumberSource == nameof(WebThemeSource.ThemePlaza) ? 1 : 0,
                MinWidth = 140
            };
            _recognised = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 0) };
            _open = new Button { Content = MainResources.Web_Open, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = MainResources.Dialog_Cancel, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            _input.TextChanged += (s, e) => Recognise();
            _numbers.SelectionChanged += (s, e) => Recognise();
            _open.Click += (s, e) =>
            {
                Result = Current();
                if (Result == null)
                    return;
                Settings.Default.Web_NumberSource = NumberSources[_numbers.SelectedIndex].ToString();
                Close();
            };
            cancel.Click += (s, e) => Close();

            var numbersRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new TextBlock { Text = MainResources.Web_NumbersFrom, VerticalAlignment = VerticalAlignment.Center }, _numbers }
            };
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    // Which sites work, Themezer included
                    new TextBlock { Text = MainResources.Web_Hint, TextWrapping = TextWrapping.Wrap },
                    _input,
                    numbersRow,
                    _recognised,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _open, cancel } }
                }
            };
            Recognise();
            Opened += (s, e) => _input.Focus();
        }

        private WebThemeId Current() => WebThemes.Parse(_input.Text, NumberSources[System.Math.Max(0, _numbers.SelectedIndex)]);

        private void Recognise()
        {
            var theme = Current();
            _open.IsEnabled = theme != null;
            _recognised.Text = theme != null
                ? "→ " + string.Format(MainResources.Web_Recognised, WebThemes.SiteName(theme.Source), theme)
                : string.IsNullOrWhiteSpace(_input.Text) ? string.Empty : MainResources.Web_NotRecognised;
        }
    }
}
