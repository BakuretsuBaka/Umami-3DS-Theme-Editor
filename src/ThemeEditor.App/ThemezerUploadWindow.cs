// --------------------------------------------------
// Umami 3DS Theme Editor - ThemezerUploadWindow.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ThemeEditor.App.Library;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    /// <summary>Signs in to Themezer and uploads the open theme (name, description, tags, music info, visibility).</summary>
    internal sealed class ThemezerUploadWindow : Window
    {
        private readonly ThemezerClient _client;
        private readonly ICredentialStore _vault;
        private readonly Func<Task<MainWindow.ThemePackage>> _buildPackage;
        private readonly Action<string> _openBrowser;

        private readonly StackPanel _signInPanel;
        private readonly Button _signIn;
        private readonly StackPanel _accountPanel;
        private readonly TextBlock _account;
        private CancellationTokenSource _signInCts;

        private readonly TextBox _name;
        private readonly TextBox _description;
        private readonly IReadOnlyList<ThemeCredit> _credits;
        private bool _credited;
        private readonly TextBox _tags;
        private readonly TextBox _bgmInfo;
        private readonly CheckBox _private;
        private readonly CheckBox _nsfw;
        private readonly CheckBox _aiImages;
        private readonly StackPanel _details;

        private readonly TextBlock _status;
        private readonly Button _openPage;
        private readonly Button _upload;
        private string _pageUrl;
        private bool _busy;

        /// <param name="vault">Where the sign-in is kept between starts (the system's password vault).</param>
        /// <param name="hasBgm">Whether the theme has music (then the music info field is shown).</param>
        /// <param name="buildPackage">Compresses the theme and renders its preview and icon when the upload starts.</param>
        /// <param name="openBrowser">Opens Themezer's sign-in page (the default browser when null).</param>
        /// <param name="bgmInfo">What is known about the music (from the resource library or Themezer), to fill in.</param>
        public ThemezerUploadWindow(ThemezerClient client, ICredentialStore vault, string name, string description, bool hasBgm,
            Func<Task<MainWindow.ThemePackage>> buildPackage, Action<string> openBrowser = null, string bgmInfo = null,
            IReadOnlyList<ThemeCredit> credits = null)
        {
            _credits = credits;
            AskReplace = AskReplaceDialog;
            _client = client;
            _vault = vault;
            _buildPackage = buildPackage;
            _openBrowser = openBrowser ?? Update.OpenUrl;
            Title = MainResources.Themezer_Title;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // ---- Account: signing in happens on Themezer's own page, in the browser
            _signIn = new Button {Content = MainResources.Themezer_SignIn, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 6)};
            _signIn.Click += async (s, e) => await SignIn();
            _signInPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock {Text = MainResources.Themezer_SignInNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12},
                    _signIn
                }
            };

            _account = new TextBlock {VerticalAlignment = VerticalAlignment.Center};
            var signOut = new Button {Content = MainResources.Themezer_LogOut, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            signOut.Click += async (s, e) => await SignOut();
            var accountRow = new DockPanel {LastChildFill = true};
            DockPanel.SetDock(signOut, Dock.Right);
            accountRow.Children.Add(signOut);
            accountRow.Children.Add(_account);
            _accountPanel = new StackPanel {Children = {accountRow}};

            // ---- Theme details
            // Themezer takes names of up to 32 characters; the theme's own name may be longer (the 3DS allows 64)
            var shortName = (name ?? string.Empty).Trim();
            if (shortName.Length > ThemezerClient.MaxNameLength)
                shortName = shortName.Substring(0, ThemezerClient.MaxNameLength).TrimEnd();
            _name = new TextBox {Text = shortName, MaxLength = ThemezerClient.MaxNameLength};
            _description = new TextBox {Text = description ?? string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80};
            _tags = new TextBox {Watermark = MainResources.Themezer_TagsExample};
            _bgmInfo = new TextBox { Text = bgmInfo ?? string.Empty };
            _private = new CheckBox {Content = MainResources.Themezer_Private};
            _nsfw = new CheckBox {Content = MainResources.Themezer_Nsfw};
            _aiImages = new CheckBox {Content = MainResources.Themezer_AiImages};
            ToolTip.SetTip(_aiImages, MainResources.Themezer_AiImagesHint);
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
            _details.Children.Add(_aiImages);

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
                    _signInPanel,
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
                SavedToken.CleanUpPasswordLogin(_vault);
                if (_client.Username == null && SavedToken.Read(_vault) != null)
                    await Run(MainResources.Themezer_LoggingIn, async () =>
                    {
                        await Resume();
                        ShowStatus(null, false);
                    });
                if (_client.Username != null)
                    _tags.Focus();
                else
                    _signIn.Focus();
            };
            Closed += (s, e) => _signInCts?.Cancel();
        }

        /// <summary>
        ///     Ends the description with a credit to the creators of the parts taken from their themes (once, and not for the
        ///     uploader's own themes, so it waits for the sign-in when there is one).
        /// </summary>
        internal void AddCredits()
        {
            if (_credited || _credits == null || _client.Username == null)
                return;
            var credits = ThemeCredit.Markdown(_credits, _client.Username);
            if (credits.Length == 0)
                return;
            _credited = true;
            var text = (_description.Text ?? string.Empty).TrimEnd();
            if (text.Contains(credits))
                return;
            _description.Text = text.Length == 0 ? credits : text + "\n\n" + credits;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock {Text = text, Margin = new Thickness(0, 4, 0, 0)};
        }

        private void Refresh()
        {
            var signedIn = _client.Username != null;
            AddCredits();
            _signInPanel.IsVisible = !signedIn;
            _accountPanel.IsVisible = signedIn;
            _account.Text = signedIn ? string.Format(MainResources.Themezer_LoggedInAs, _client.Username) : string.Empty;
            _signIn.Content = _signInCts != null ? MainResources.Dialog_Cancel : MainResources.Themezer_SignIn;
            _signIn.IsDefault = !signedIn;
            // Once uploaded, the theme page takes the upload button's place, so the theme is not sent twice by accident
            _upload.IsVisible = _pageUrl == null;
            _upload.IsDefault = signedIn && _pageUrl == null;
            _openPage.IsDefault = _pageUrl != null;
            _signIn.IsEnabled = !_busy || _signInCts != null;
            _upload.IsEnabled = signedIn && !_busy;
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
            catch (OperationCanceledException)
            {
                ShowStatus(null, false);
            }
            catch (ThemezerException ex)
            {
                if (ex.Code == "NOT_AUTHENTICATED")
                {
                    // The token was revoked or the app disabled: back to signing in
                    SavedToken.Delete(_vault);
                    ShowStatus(MainResources.Themezer_SignInEnded, true);
                }
                else
                    ShowStatus(Explain(ex), true);
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

        /// <summary>What to tell the user about an error Themezer reported; its own message for anything else.</summary>
        internal string Explain(ThemezerException ex)
        {
            switch (ex.Code)
            {
                case "RATE_LIMITED":
                    return MainResources.Themezer_RateLimited;
                case "INVALID_THEME_CONTENTS" when ex.Reason == "UNMARKED_NSFW":
                    return MainResources.Themezer_UnmarkedNsfw;
                case "INVALID_THEME_CONTENTS" when ex.Reason == "NSFW_CHECK_UNAVAILABLE":
                    return MainResources.Themezer_NsfwCheckDown;
                case "NOT_PERMITTED" when _nsfw.IsChecked == true:
                    return MainResources.Themezer_NsfwNotAllowed;
                case "USER_TEMPORARILY_BLOCKED":
                    return MainResources.Themezer_Blocked;
                default:
                    // What failed, and where, so an unexpected refusal can be told apart (and reported)
                    var detail = string.Join(" · ", new[] { ex.Code, ex.Path }.Where(d => !string.IsNullOrEmpty(d)));
                    return string.Format(MainResources.Themezer_Error, detail.Length > 0 ? ex.Message + " (" + detail + ")" : ex.Message);
            }
        }

        /// <summary>Signs in with the token kept from an earlier start; forgets it when Themezer no longer accepts it.</summary>
        internal async Task<bool> Resume()
        {
            var token = SavedToken.Read(_vault);
            if (token == null)
                return false;
            if (await _client.Use(token) != null)
                return true;
            SavedToken.Delete(_vault);
            return false;
        }

        private async Task SignIn()
        {
            if (_signInCts != null)
            {
                _signInCts.Cancel();
                return;
            }
            if (_busy || _client.Username != null)
                return;
            // Five minutes to sign in; closing the browser or refusing leaves the dialog as it was
            _signInCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await Run(MainResources.Themezer_WaitingForBrowser, async () =>
                {
                    await _client.SignIn(_openBrowser, _signInCts.Token);
                    SavedToken.Write(_vault, _client.Token);
                    ShowStatus(null, false);
                });
            }
            finally
            {
                _signInCts.Dispose();
                _signInCts = null;
                Refresh();
            }
            if (_client.Username != null)
                _tags.Focus();
        }

        private async Task SignOut()
        {
            if (_busy)
                return;
            SavedToken.Delete(_vault);
            await Run(null, () => _client.SignOut());
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

        internal async Task UploadTheme()
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
            // Uploaded under this name before: replace that theme, or keep both
            List<ThemezerTheme> earlier = null;
            await Run(MainResources.Themezer_Checking, async () => earlier = await _client.FindOwnThemes(_name.Text));
            if (earlier == null)
                return;
            ThemezerTheme replace = null;
            if (earlier.Count > 0)
            {
                var choice = await AskReplace(earlier);
                if (choice == ReplaceChoice.Cancel)
                {
                    ShowStatus(null, false);
                    return;
                }
                if (choice == ReplaceChoice.Replace)
                    replace = earlier[0];
            }
            await Run(replace != null ? MainResources.Themezer_Replacing : MainResources.Themezer_Uploading, async () =>
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
                    HasAiImages = _aiImages.IsChecked == true,
                    Body = package.Body,
                    Bgm = package.Bgm,
                    Preview = package.Preview,
                    Icon = package.Icon
                }, replace?.HexId);
                _pageUrl = !string.IsNullOrEmpty(theme.PageUrl) ? theme.PageUrl : replace?.PageUrl ?? ThemezerClient.SiteUrl;
                ShowStatus(replace != null ? MainResources.Themezer_Replaced : MainResources.Themezer_Uploaded, false);
            });
        }

        internal enum ReplaceChoice
        {
            Replace,
            KeepBoth,
            Cancel
        }

        /// <summary>
        ///     Asks what to do about the user's themes already named like this one (the most recently changed first); tests
        ///     answer it themselves.
        /// </summary>
        internal Func<List<ThemezerTheme>, Task<ReplaceChoice>> AskReplace;

        private async Task<ReplaceChoice> AskReplaceDialog(List<ThemezerTheme> earlier)
        {
            var name = earlier[0].Name;
            var message = earlier.Count == 1
                ? string.Format(MainResources.Themezer_AlreadyUploaded, name)
                : string.Format(MainResources.Themezer_AlreadyUploadedMany, name, earlier.Count);
            var choice = await Dialogs.Choose(this, message, MainResources.Themezer_Title, MessageKind.Information,
                MainResources.Themezer_Replace, MainResources.Themezer_KeepBoth, MainResources.Dialog_Cancel);
            return choice == 0 ? ReplaceChoice.Replace : choice == 1 ? ReplaceChoice.KeepBoth : ReplaceChoice.Cancel;
        }
    }

    /// <summary>
    ///     The Themezer access token, kept in the system's password vault so the next start is signed in already. It is
    ///     read at most once while the editor runs (on macOS each read of a Keychain entry can ask for permission).
    /// </summary>
    internal static class SavedToken
    {
        private const string Account = "themezer-token";
        private static ICredentialStore _readFrom;
        private static string _token;

        public static string Read(ICredentialStore vault)
        {
            if (!vault.IsAvailable)
                return null;
            if (_readFrom != vault)
            {
                _readFrom = vault;
                _token = vault.Read(Account);
            }
            return _token;
        }

        private static Task _resumed;
        private static ThemezerClient _resumedClient;

        /// <summary>
        ///     Signs <paramref name="client" /> in with the token kept from an earlier start, once per run, so searches show
        ///     what the account may see (Themezer only returns NSFW themes to accounts that allow them). Never asks anything:
        ///     without a saved sign-in, or when it fails, the client stays signed out.
        /// </summary>
        public static Task ResumeQuietly(ThemezerClient client, ICredentialStore vault)
        {
            if (client.Username != null || vault == null)
                return Task.CompletedTask;
            if (_resumedClient != client)
            {
                _resumedClient = client;
                _resumed = ResumeNow(client, vault);
            }
            return _resumed;
        }

        private static async Task ResumeNow(ThemezerClient client, ICredentialStore vault)
        {
            try
            {
                var token = Read(vault);
                if (token != null && await client.Use(token) == null)
                    Delete(vault);
            }
            catch (Exception)
            {
                // Offline or Themezer down: the search says so itself
            }
        }

        public static void Write(ICredentialStore vault, string token)
        {
            if (!vault.IsAvailable || token == null)
                return;
            if (_readFrom == vault && _token == token)
                return;
            vault.Write(Account, token);
            _readFrom = vault;
            _token = token;
        }

        public static void Delete(ICredentialStore vault)
        {
            if (!vault.IsAvailable)
                return;
            if (_readFrom == vault && _token == null)
                return;
            vault.Delete(Account);
            _readFrom = vault;
            _token = null;
        }

        /// <summary>
        ///     Removes what the email and password sign-in of earlier versions kept (the password in the vault, the session in a
        ///     file), once: Themezer asks apps to sign in through OAuth instead.
        /// </summary>
        public static void CleanUpPasswordLogin(ICredentialStore vault)
        {
            var settings = Settings.Default;
            var email = settings.Themezer_Email;
            if (string.IsNullOrEmpty(email))
                return;
            try
            {
                var session = Path.Combine(Settings.SettingsDirectory, "themezer-session.json");
                if (File.Exists(session))
                    File.Delete(session);
                // On macOS deleting Keychain entries an earlier build made can ask for permission each time, so they stay
                // there unused (Keychain Access lists them under "Umami 3DS Theme Editor")
                if (vault.IsAvailable && !OperatingSystem.IsMacOS())
                    foreach (var account in new[] {email, email + "#session", email + "#login"})
                        vault.Delete(account);
            }
            catch (Exception)
            {
                // Best effort: what is left is never read again
            }
            settings.Themezer_Email = string.Empty;
            settings.Save();
        }
    }
}
