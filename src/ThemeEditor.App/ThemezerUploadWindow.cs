// --------------------------------------------------
// Umami 3DS Theme Editor - ThemezerUploadWindow.cs
// --------------------------------------------------

using System;
using System.IO;
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
        private readonly ICredentialStore _vault;
        private readonly string _sessionFolder;
        private readonly Func<Task<MainWindow.ThemePackage>> _buildPackage;

        private readonly StackPanel _loginPanel;
        private readonly TextBox _email;
        private readonly TextBox _password;
        private readonly Button _logIn;
        private readonly CheckBox _stay;
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
        /// <param name="vault">Where "Stay logged in" keeps the password and session (the system's password vault).</param>
        /// <param name="sessionFolder">Where "Stay logged in" keeps the session file (the settings folder when null).</param>
        public ThemezerUploadWindow(ThemezerClient client, ICredentialStore vault, string name, string description, bool hasBgm,
            Func<Task<MainWindow.ThemePackage>> buildPackage, string sessionFolder = null)
        {
            _sessionFolder = sessionFolder;
            _client = client;
            _vault = vault;
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
            _logIn = new Button {Content = MainResources.Themezer_LogIn, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            _logIn.Click += async (s, e) => await LogIn();
            _password.KeyDown += async (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    e.Handled = true;
                    await LogIn();
                }
            };
            _stay = new CheckBox {Content = MainResources.Themezer_StayLoggedIn, IsChecked = Settings.Default.Themezer_StayLoggedIn, IsVisible = vault.IsAvailable};
            var logInRow = new DockPanel();
            DockPanel.SetDock(_logIn, Dock.Right);
            logInRow.Children.Add(_logIn);
            logInRow.Children.Add(_stay);
            _loginPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _email, _password,
                    new TextBlock {Text = vault.IsAvailable ? MainResources.Themezer_PasswordNoteVault : MainResources.Themezer_PasswordNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12},
                    logInRow
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
            Opened += async (s, e) =>
            {
                if (_client.Username == null && Saved)
                    await Run(MainResources.Themezer_LoggingIn, async () =>
                    {
                        await Resume();
                        ShowStatus(null, false);
                    });
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

        private static string Email => Settings.Default.Themezer_Email;

        /// <summary>Whether "Stay logged in" keeps the login in the password vault.</summary>
        private bool Saved => _vault.IsAvailable && Settings.Default.Themezer_StayLoggedIn && !string.IsNullOrEmpty(Email);

        /// <summary>Logs in from the vault: the saved session if Themezer still knows it, otherwise the saved password.</summary>
        internal async Task<bool> Resume()
        {
            if (!Saved)
                return false;
            var saved = SavedLogin.For(_vault, Email, _sessionFolder);
            if (saved.Session != null && await _client.Resume(saved.Session) != null)
                return true;
            if (saved.Password == null)
                return false;
            try
            {
                await _client.LogIn(Email, saved.Password);
            }
            catch (ThemezerException ex) when (ex.Code == "INVALID_CREDENTIALS")
            {
                // The password was changed on Themezer: forget it, so the user types the new one
                Forget();
                throw;
            }
            KeepSession();
            return true;
        }

        private void KeepSession()
        {
            if (Saved && _client.Session != null)
                SavedLogin.For(_vault, Email, _sessionFolder).Keep(null, _client.Session);
        }

        private void Forget()
        {
            if (_vault.IsAvailable && !string.IsNullOrEmpty(Email))
                SavedLogin.For(_vault, Email, _sessionFolder).Forget();
        }

        private async Task LogIn()
        {
            if (_busy || _client.Username != null)
                return;
            var email = (_email.Text ?? string.Empty).Trim();
            if (email.Length == 0 || string.IsNullOrEmpty(_password.Text))
                return;
            var password = _password.Text;
            await Run(MainResources.Themezer_LoggingIn, async () =>
            {
                await _client.LogIn(email, password);
                Forget();
                Settings.Default.Themezer_Email = email;
                Settings.Default.Themezer_StayLoggedIn = _stay.IsChecked == true;
                Settings.Default.Save();
                if (Saved)
                    SavedLogin.For(_vault, email, _sessionFolder).Keep(password, _client.Session);
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
            Forget();
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
                var upload = new ThemezerUpload
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
                };
                ThemezerTheme theme;
                try
                {
                    theme = await _client.UploadTheme(upload);
                }
                catch (ThemezerException ex) when (ex.Code == "UNAUTHENTICATED" && Saved)
                {
                    // The session ran out while the dialog was open: log in again from the vault and try once more
                    if (!await Resume())
                        throw;
                    theme = await _client.UploadTheme(upload);
                }
                KeepSession();
                _pageUrl = string.IsNullOrEmpty(theme.PageUrl) ? ThemezerClient.SiteUrl : theme.PageUrl;
                ShowStatus(MainResources.Themezer_Uploaded, false);
            });
        }
    }

    /// <summary>
    ///     The "Stay logged in" login of one account. The session sits in a file in the settings folder that only the user
    ///     can read (as browsers keep cookies), so resuming it never involves the Keychain; the password is in the system's
    ///     password vault and only read when Themezer no longer accepts the session, at most once while the editor runs.
    /// </summary>
    internal sealed class SavedLogin
    {
        private static readonly System.Collections.Generic.Dictionary<string, SavedLogin> Cache = new System.Collections.Generic.Dictionary<string, SavedLogin>();

        private readonly ICredentialStore _vault;
        private readonly string _email;
        private readonly string _sessionFile;
        private bool _passwordLoaded, _sessionLoaded;
        private string _password;
        private string _session;

        private SavedLogin(ICredentialStore vault, string email, string folder)
        {
            _vault = vault;
            _email = email;
            _sessionFile = Path.Combine(folder, "themezer-session.json");
        }

        /// <param name="folder">Where the session file goes (the settings folder when null).</param>
        public static SavedLogin For(ICredentialStore vault, string email, string folder = null)
        {
            folder = folder ?? Settings.SettingsDirectory;
            var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vault) + "|" + folder + "|" + email;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var login) || login._vault != vault)
                    Cache[key] = login = new SavedLogin(vault, email, folder);
                return login;
            }
        }

        private string Account => _email + "#login";

        public string Session
        {
            get
            {
                if (!_sessionLoaded)
                {
                    _sessionLoaded = true;
                    try
                    {
                        var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(_sessionFile));
                        _session = (string) o["email"] == _email ? (string) o["session"] : null;
                    }
                    catch (Exception)
                    {
                        _session = null;
                    }
                }
                return _session;
            }
        }

        public string Password
        {
            get
            {
                if (!_passwordLoaded)
                {
                    _passwordLoaded = true;
                    try
                    {
                        var json = _vault.Read(Account);
                        _password = json == null ? null : (string) Newtonsoft.Json.Linq.JObject.Parse(json)["password"];
                    }
                    catch (Exception)
                    {
                        // A damaged entry counts as none: the user simply logs in again
                        _password = null;
                    }
                }
                return _password;
            }
        }

        /// <summary>Stores what changed: the password (null keeps the saved one) in the vault, the session in its file.</summary>
        public void Keep(string password, string session)
        {
            if (password != null && (!_passwordLoaded || password != _password))
            {
                _vault.Write(Account, new Newtonsoft.Json.Linq.JObject {["password"] = password}.ToString(Newtonsoft.Json.Formatting.None));
                _password = password;
                _passwordLoaded = true;
            }
            if (session != Session)
            {
                _session = session;
                WriteSession(session);
            }
        }

        private void WriteSession(string session)
        {
            try
            {
                if (session == null)
                {
                    File.Delete(_sessionFile);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_sessionFile) ?? ".");
                var json = new Newtonsoft.Json.Linq.JObject {["email"] = _email, ["session"] = session}.ToString(Newtonsoft.Json.Formatting.None);
                var options = new FileStreamOptions {Mode = FileMode.Create, Access = FileAccess.Write};
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(_sessionFile, options))
                using (var writer = new StreamWriter(stream))
                    writer.Write(json);
                // An older file keeps its mode when overwritten
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(_sessionFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception)
            {
                // Without the file the next start falls back to the password
            }
        }

        public void Forget()
        {
            _passwordLoaded = _sessionLoaded = true;
            _password = _session = null;
            WriteSession(null);
            _vault.Delete(Account);
            // Entries from v2.3.14, which kept the password and the session apart in the vault
            _vault.Delete(_email);
            _vault.Delete(_email + "#session");
        }
    }
}
