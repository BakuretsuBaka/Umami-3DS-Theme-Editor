// --------------------------------------------------
// Umami 3DS Theme Editor - WebThemeWindow.cs
// --------------------------------------------------

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Asks which theme to open from the web, in one of two ways: a link to any of the sites, a Themezer quick ID or a theme
    ///     number (bare numbers belong to the site picked in the list), shown right away as recognised; or a search by name
    ///     through the archives' indexes and Themezer.
    /// </summary>
    internal sealed class WebThemeWindow : Window
    {
        /// <summary>What a bare number can belong to: two sites and the official themes of each region.</summary>
        private static readonly (WebThemeSource Source, string Region)[] NumberSources =
        {
            (WebThemeSource.ThreeDsThemes, null), (WebThemeSource.ThemePlaza, null),
            (WebThemeSource.Official, "EU"), (WebThemeSource.Official, "JP"), (WebThemeSource.Official, "US")
        };

        private readonly ThemezerClient _themezer;
        private readonly TextBox _input;
        private readonly ComboBox _numbers;
        private readonly TextBlock _recognised;
        private readonly TextBox _search;
        private readonly ListBox _results;
        private readonly TextBlock _searchStatus;
        private readonly ObservableCollection<WebThemeId> _found = new ObservableCollection<WebThemeId>();
        private readonly Button _open;
        private CancellationTokenSource _online;
        /// <summary>Whether the search (not the link box) was used last: Open takes what it points at.</summary>
        private bool _searching;

        /// <summary>The theme to open, once the user pressed Open.</summary>
        public WebThemeId Result { get; private set; }

        /// <summary>The Themezer search running in the background, for tests to wait for.</summary>
        internal Task OnlineSearch { get; private set; } = Task.CompletedTask;

        /// <param name="themezer">Where to search Themezer (the shared client when null).</param>
        public WebThemeWindow(ThemezerClient themezer = null)
        {
            _themezer = themezer ?? ThemezerClient.Shared;
            Title = MainResources.Web_Title;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _input = new TextBox { Name = "Code", Watermark = "T1A20D  ·  https://themezer.net/3ds/themes/…  ·  10001  ·  EU-0006" };
            _numbers = new ComboBox { ItemsSource = Array.ConvertAll(NumberSources, n => NumberName(n.Source, n.Region)), MinWidth = 140 };
            _numbers.SelectedIndex = Math.Max(0, Array.FindIndex(NumberSources, n => Key(n.Source, n.Region) == Settings.Default.Web_NumberSource));
            _recognised = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 0) };

            _search = new TextBox { Name = "Search", Watermark = "Mighty Mushrooms  ·  Zelda  ·  Pokemon" };
            _searchStatus = new TextBlock { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
            _results = new ListBox
            {
                ItemsSource = _found,
                Height = 220,
                IsVisible = false,
                ItemTemplate = new FuncDataTemplate<WebThemeId>((theme, _) => theme == null ? null : new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = theme.ToString(), TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = SourceName(theme), FontSize = 11, Opacity = 0.65 }
                    }
                })
            };

            _open = new Button { Content = MainResources.Web_Open, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = MainResources.Dialog_Cancel, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            _input.TextChanged += (s, e) =>
            {
                _searching = false;
                Recognise();
            };
            _numbers.SelectionChanged += (s, e) => Recognise();
            _search.TextChanged += (s, e) =>
            {
                _searching = true;
                Search();
            };
            _results.SelectionChanged += (s, e) =>
            {
                if (_results.SelectedItem != null)
                    _searching = true;
                Recognise();
            };
            _results.DoubleTapped += (s, e) =>
            {
                if (_results.SelectedItem is WebThemeId)
                    Open();
            };
            _open.Click += (s, e) => Open();
            cancel.Click += (s, e) => Close();
            Closed += (s, e) => _online?.Cancel();

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
                    new Separator { Margin = new Thickness(0, 4) },
                    new TextBlock { Text = MainResources.Web_SearchLabel },
                    _search,
                    _results,
                    _searchStatus,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _open, cancel } }
                }
            };
            Recognise();
            Opened += (s, e) => _input.Focus();
        }

        private static string Key(WebThemeSource source, string region) => region == null ? source.ToString() : source + "-" + region;

        private static string NumberName(WebThemeSource source, string region) =>
            region == null ? WebThemes.SiteName(source) : WebThemes.SiteName(source) + " (" + region + ")";

        /// <summary>The site a search result is from, with its section ("Theme Plaza · NSFW").</summary>
        private static string SourceName(WebThemeId theme) =>
            theme.Section == null ? WebThemes.SiteName(theme.Source) : WebThemes.SiteName(theme.Source) + " · " + theme.Section;

        private WebThemeId Typed()
        {
            var n = NumberSources[Math.Max(0, _numbers.SelectedIndex)];
            return WebThemes.Parse(_input.Text, n.Source, n.Region ?? "EU");
        }

        private WebThemeId Current() => _searching ? _results.SelectedItem as WebThemeId : Typed();

        private void Open()
        {
            Result = Current();
            if (Result == null)
                return;
            var n = NumberSources[Math.Max(0, _numbers.SelectedIndex)];
            Settings.Default.Web_NumberSource = Key(n.Source, n.Region);
            Close();
        }

        private void Recognise()
        {
            var typed = Typed();
            _recognised.Text = typed != null
                ? "→ " + string.Format(MainResources.Web_Recognised, WebThemes.SiteName(typed.Source), typed)
                : string.IsNullOrWhiteSpace(_input.Text) ? string.Empty : MainResources.Web_NotRecognised;
            _open.IsEnabled = Current() != null;
        }

        /// <summary>Searches the archives' indexes right away and Themezer after a short pause in typing.</summary>
        private void Search()
        {
            _online?.Cancel();
            _online = null;
            var query = _search.Text ?? string.Empty;
            _found.Clear();
            foreach (var theme in WebThemes.Search(query))
                _found.Add(theme);
            _results.SelectedIndex = _found.Count > 0 ? 0 : -1;
            _results.IsVisible = _found.Count > 0;
            _searchStatus.Text = string.Empty;
            Recognise();
            if (query.Trim().Length < 2)
                return;
            var cts = _online = new CancellationTokenSource();
            OnlineSearch = SearchThemezer(query, cts.Token);
        }

        private async Task SearchThemezer(string query, CancellationToken cancel)
        {
            try
            {
                await Task.Delay(500, cancel);
                _searchStatus.Text = MainResources.Web_SearchingThemezer;
                var hits = await _themezer.SearchThemes(query, cancel);
                if (cancel.IsCancellationRequested)
                    return;
                // Themezer's first: it is the site still running
                for (var i = 0; i < hits.Count; i++)
                    _found.Insert(i, hits[i]);
                _results.IsVisible = _found.Count > 0;
                if (_found.Count > 0)
                    _results.SelectedIndex = 0;
                _searchStatus.Text = _found.Count == 0 ? MainResources.Web_NothingFound : string.Empty;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _searchStatus.Text = string.Format(MainResources.Web_ThemezerSearchFailed, ex.Message);
            }
        }
    }
}
