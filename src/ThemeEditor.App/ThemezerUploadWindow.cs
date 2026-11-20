// --------------------------------------------------
// Umami 3DS Theme Editor - ThemezerUploadWindow.cs
// --------------------------------------------------

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    /// <summary>Logs in to Themezer and uploads the open theme (name, description, tags, music info, visibility).</summary>
    internal sealed class ThemezerUploadWindow : Window
    {
        private readonly ThemezerClient _client;
        private readonly Func<Task<MainWindow.ThemePackage>> _buildPackage;

        private readonly StackPanel _loginPanel;
        private readonly TextBox _email;
        private readonly TextBox _password;
        private readonly Button _logIn;
        private readonly StackPanel _accountPanel;
        private readonly TextBlock _account;

        private readonly TextBox _name;
        private readonly TextBox _description;
        private readonly TextBox _tags;
        private readonly TextBox _bgmInfo;
        private readonly CheckBox _private;
        private readonly CheckBox _nsfw;
        private readonly StackPanel _details;

        private readonly TextBlock _status;
        private readonly Button _openPage;
        private readonly Button _upload;
        private string _pageUrl;
        private bool _busy;

        /// <param name="hasBgm">Whether the theme has music (then the music info field is shown).</param>
        /// <param name="buildPackage">Compresses the theme and renders its preview and icon when the upload starts.</param>
        public ThemezerUploadWindow(ThemezerClient client, string name, string description, bool hasBgm,
            Func<Task<MainWindow.ThemePackage>> buildPackage)
        {
            _client = client;
            _buildPackage = buildPackage;
            Title = MainResources.Themezer_Title;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // ---- Account
            _email = new TextBox {Text = Settings.Default.Themezer_Email, Watermark = MainResources.Themezer_Email};
            _password = new TextBox {PasswordChar = '•', Watermark = MainResources.Themezer_Password};
            _logIn = new Button {Content = MainResources.Themezer_LogIn, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            _logIn.Click += async (s, e) => await LogIn();
            _password.KeyDown += async (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    e.Handled = true;
                    await LogIn();
                }
            };
            _loginPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _email, _password,
                    new TextBlock {Text = MainResources.Themezer_PasswordNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12},
                    _logIn
                }
            };

            _account = new TextBlock {VerticalAlignment = VerticalAlignment.Center};
            var logOut = new Button {Content = MainResources.Themezer_LogOut, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            logOut.Click += async (s, e) => await LogOut();
            var accountRow = new DockPanel {LastChildFill = true};
            DockPanel.SetDock(logOut, Dock.Right);
            accountRow.Children.Add(logOut);
            accountRow.Children.Add(_account);
            _accountPanel = new StackPanel {Children = {accountRow}};

            // ---- Theme details
            _name = new TextBox {Text = name ?? string.Empty};
            _description = new TextBox {Text = description ?? string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80};
            _tags = new TextBox {Watermark = MainResources.Themezer_TagsExample};
            _bgmInfo = new TextBox();
            _private = new CheckBox {Content = MainResources.Themezer_Private};
            _nsfw = new CheckBox {Content = MainResources.Themezer_Nsfw};
            _details = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    Label(MainResources.Themezer_Name), _name,
                    Label(MainResources.Themezer_Description), _description,
                    Label(MainResources.Themezer_Tags), _tags
                }
            };
            if (hasBgm)
            {
                _details.Children.Add(Label(MainResources.Themezer_BgmInfo));
                _details.Children.Add(_bgmInfo);
            }
            _details.Children.Add(_private);
            _details.Children.Add(_nsfw);

            // ---- Result and buttons
            _status = new TextBlock {TextWrapping = TextWrapping.Wrap, IsVisible = false};
            _openPage = new Button {Content = MainResources.Themezer_OpenPage, IsVisible = false};
            _openPage.Click += (s, e) => Update.OpenUrl(_pageUrl);
            _upload = new Button {Content = MainResources.Themezer_Upload, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            _upload.Click += async (s, e) => await UploadTheme();
            var close = new Button {Content = MainResources.Dialog_Close, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            close.Click += (s, e) => Close();

            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock {Text = "Themezer", FontSize = 18, FontWeight = FontWeight.SemiBold},
                    _loginPanel,
                    _accountPanel,
                    new Separator(),
                    _details,
                    _status,
                    new StackPanel {Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = {_openPage, _upload, close}}
                }
            };
            Refresh();
            Opened += (s, e) =>
            {
                if (_client.Username != null)
                    _tags.Focus();
                else if (string.IsNullOrEmpty(_email.Text))
                    _email.Focus();
                else
                    _password.Focus();
            };
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock {Text = text, Margin = new Thickness(0, 4, 0, 0)};
        }

        private void Refresh()
        {
            var loggedIn = _client.Username != null;
            _loginPanel.IsVisible = !loggedIn;
            _accountPanel.IsVisible = loggedIn;
            _account.Text = loggedIn ? string.Format(MainResources.Themezer_LoggedInAs, _client.Username) : string.Empty;
            _logIn.IsDefault = !loggedIn;
            _upload.IsDefault = loggedIn;
            _logIn.IsEnabled = !_busy;
            _upload.IsEnabled = loggedIn && !_busy;
            _details.IsEnabled = !_busy;
            _openPage.IsVisible = _pageUrl != null;
        }

        private void ShowStatus(string text, bool error)
        {
            _status.Text = text;
            _status.Foreground = error ? Brushes.Firebrick : null;
            _status.IsVisible = !string.IsNullOrEmpty(text);
        }

        private async Task Run(string busyText, Func<Task> work)
        {
            _busy = true;
            ShowStatus(busyText, false);
            Refresh();
            try
            {
                await work();
            }
            catch (ThemezerException ex)
            {
                ShowStatus(string.Format(MainResources.Themezer_Error, ex.Message), true);
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message, true);
            }
            finally
            {
                _busy = false;
                Refresh();
            }
        }

        private async Task LogIn()
        {
            if (_busy || _client.Username != null)
                return;
            var email = (_email.Text ?? string.Empty).Trim();
            if (email.Length == 0 || string.IsNullOrEmpty(_password.Text))
                return;
            await Run(MainResources.Themezer_LoggingIn, async () =>
            {
                await _client.LogIn(email, _password.Text);
                Settings.Default.Themezer_Email = email;
                Settings.Default.Save();
                ShowStatus(null, false);
            });
            _password.Text = string.Empty;
            if (_client.Username != null)
                _tags.Focus();
        }

        private async Task LogOut()
        {
            if (_busy)
                return;
            await Run(null, () => _client.LogOut());
            ShowStatus(null, false);
        }

        /// <summary>Checks the fields; returns what to tell the user, or null when the theme can be uploaded.</summary>
        internal string Validate()
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
                return MainResources.Themezer_NeedsName;
            if (ThemezerClient.ParseTags(_tags.Text).Count == 0)
                return MainResources.Themezer_NeedsTags;
            return null;
        }

        private async Task UploadTheme()
        {
            if (_busy || _client.Username == null)
                return;
            var problem = Validate();
            if (problem != null)
            {
                ShowStatus(problem, true);
                return;
            }
            _pageUrl = null;
            await Run(MainResources.Themezer_Uploading, async () =>
            {
                var package = await _buildPackage();
                var theme = await _client.UploadTheme(new ThemezerUpload
                {
                    Name = _name.Text.Trim(),
                    Description = _description.Text,
                    Tags = ThemezerClient.ParseTags(_tags.Text),
                    BgmInfo = string.IsNullOrWhiteSpace(_bgmInfo.Text) ? null : _bgmInfo.Text.Trim(),
                    IsPrivate = _private.IsChecked == true,
                    IsNsfw = _nsfw.IsChecked == true,
                    Body = package.Body,
                    Bgm = package.Bgm,
                    Preview = package.Preview,
                    Icon = package.Icon
                });
                _pageUrl = string.IsNullOrEmpty(theme.PageUrl) ? ThemezerClient.SiteUrl : theme.PageUrl;
                ShowStatus(MainResources.Themezer_Uploaded, false);
            });
        }
    }
}
