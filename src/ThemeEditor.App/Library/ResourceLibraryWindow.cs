// --------------------------------------------------
// Umami 3DS Theme Editor - ResourceLibraryWindow.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Localization;
using ThemeEditor.Common.Audio;

namespace ThemeEditor.App.Library
{
    /// <summary>What the library window needs from the editor: applying things to the open theme.</summary>
    internal interface ILibraryTarget
    {
        bool HasTheme { get; }
        void ApplyTextures(LibraryItem item, TextureColors colors);
        void ApplySoundEffects(LibraryItem item);
        void ApplySound(LibraryItem item, CwavKind kind);
        void ApplyMusic(LibraryItem item);
        /// <summary>Music that is not in the library (e.g. found on Themezer), as a BCSTM, with what is known about it.</summary>
        void ApplyMusicData(byte[] bcstm, string bgmInfo);
    }

    /// <summary>Browse ready-made folder/icon textures, sound effects and music and apply them to the open theme.</summary>
    internal sealed class ResourceLibraryWindow : Window
    {
        private static readonly string[] SoundNames = { "Cursor", "Launch", "Folder", "Close", "Frame0", "Frame1", "Frame2", "OpenLid" };

        private readonly ResourceLibrary _lib;
        private readonly ILibraryTarget _target;
        private readonly AudioPlayer _player = new AudioPlayer { Loop = false };
        private LibraryItem _previewItem;
        private System.ComponentModel.PropertyChangedEventHandler _previewStateChanged;

        /// <summary>The music item whose audio is loaded in the preview player (null when none).</summary>
        internal LibraryItem PreviewTrack => _previewItem;
        private readonly RadioButton[] _tabs = new RadioButton[4];
        private readonly TextBox _search = new TextBox { Watermark = MainResources.Library_Search };
        private readonly ComboBox _show = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private static readonly string[] ShowModes = { "All", "Presets", "Mine" };
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ProgressBar _progress = new ProgressBar { Minimum = 0, Maximum = 1, Width = 160, IsVisible = false };
        private readonly Button _import = new Button { Content = MainResources.Library_Import + "  ▾" };
        private readonly Button _export = new Button { Content = MainResources.Library_Export + "  ▾" };
        private readonly MenuItem _exportItemPack = new MenuItem { Header = MainResources.Library_ExportPack };
        private readonly MenuItem _exportItemFiles = new MenuItem { Header = MainResources.Library_ExportFiles };
        private readonly MenuItem _exportShown = new MenuItem { Header = MainResources.Library_ExportShown };
        private readonly StackPanel _detail = new StackPanel { Spacing = 8, Margin = new Thickness(14) };

        private LibraryKind _kind = LibraryKind.FolderTexture;
        private LibraryItem _selected;
        private Slider _hue, _sat, _bright;
        // The colour range a target colour replaces (found in the texture, or clicked in its preview), and that colour
        private float? _sourceHue, _targetHue;
        private WrapPanel _presets;
        private Border _swatch;
        private TextBlock _swatchText;
        private TextBlock _hueMode;
        private Image _imgA, _imgB;
        private SKBitmap _srcA, _srcB;
        private bool _updatingSliders;
        private CancellationTokenSource _importCts;
        private readonly LibraryPack _pack;
        private readonly TextBlock _packText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        private readonly Button _packButton = new Button { MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _packBar = new Border { Padding = new Thickness(12, 8), Margin = new Thickness(10, 0, 10, 6), CornerRadius = new CornerRadius(4), IsVisible = false };
        private PackRelease _latest;
        private CancellationTokenSource _packCts;

        // Music found on Themezer (only in the Music tab, only with "Also search Themezer")
        private readonly ThemezerClient _themezer;
        private readonly CheckBox _online = new CheckBox { Content = MainResources.Library_SearchThemezer, Margin = new Thickness(0, 6, 0, 0) };
        private readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private CancellationTokenSource _searchCts;
        private List<object> _hits = new List<object>();
        // Themes from Themezer unpacked for a look at their folders, icons or sounds, by theme
        private readonly Dictionary<string, List<LibraryItem>> _parts = new Dictionary<string, List<LibraryItem>>();
        private readonly Dictionary<string, Bitmap> _thumbnails = new Dictionary<string, Bitmap>();
        private string _hitsFor;
        private bool _searching, _offline;
        private ThemezerMusic _previewHit;

        /// <param name="themezer">Searches music on Themezer (the editor's shared client when null).</param>
        public ResourceLibraryWindow(ResourceLibrary lib, ILibraryTarget target, LibraryPack pack = null, ThemezerClient themezer = null)
        {
            _pack = pack ?? LibraryPack.Default;
            _themezer = themezer ?? ThemezerClient.Shared;
            _lib = lib;
            _target = target;
            Title = MainResources.Library_Title;
            Width = 1120;
            Height = 640;
            MinWidth = 960;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var names = new[] { MainResources.Library_Folders, MainResources.Library_Icons, MainResources.Library_Sounds, MainResources.Library_Music };
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            for (var i = 0; i < 4; i++)
            {
                var kind = (LibraryKind) i;
                var rb = new RadioButton { GroupName = "lib-kind", IsChecked = i == 0, Content = names[i] + "  (" + lib.Count(kind) + ")", Margin = new Thickness(0, 0, 6, 0) };
                rb.Click += (_, __) => { _kind = kind; Refresh(); SearchOnline(); };
                _tabs[i] = rb;
                tabs.Children.Add(rb);
            }

            var importPack = new MenuItem { Header = MainResources.Library_ImportPack };
            importPack.Click += async (_, __) => await ImportPackFile();
            var importFiles = new MenuItem { Header = MainResources.Library_ImportFiles };
            importFiles.Click += async (_, __) => await ImportLooseFiles();
            var importThemes = new MenuItem { Header = MainResources.Library_AddFolder };
            importThemes.Click += async (_, __) => await ImportFolder();
            var purge = new MenuItem { Header = MainResources.Library_PurgeMenu };
            purge.Click += async (_, __) => await PurgeLibrary();
            var removePack = new MenuItem { Header = MainResources.Library_Pack_RemoveMenu };
            removePack.Click += async (_, __) => await RemovePack();
            var importMenu = new MenuFlyout { Items = { importPack, importFiles, new Separator(), importThemes, new Separator(), removePack, purge } };
            importMenu.Opening += (_, __) => removePack.IsVisible = _pack.IsInstalled;
            _import.Flyout = importMenu;

            // Presets only / my own imports only (remembered)
            _show.ItemsSource = new[] { MainResources.Library_Show_All, MainResources.Library_Show_Presets, MainResources.Library_Show_Mine };
            _show.SelectedIndex = Math.Max(0, Array.IndexOf(ShowModes, Properties.Settings.Default.Library_Show));
            ToolTip.SetTip(_show, MainResources.Library_Show_Tip);
            _show.SelectionChanged += (_, __) =>
            {
                Properties.Settings.Default.Library_Show = ShowModes[Math.Max(0, _show.SelectedIndex)];
                // Asking for the presets again brings back presets that were hidden by "delete everything" on macOS
                if (_show.SelectedIndex != 2 && Properties.Settings.Default.Library_HideBundled)
                {
                    Properties.Settings.Default.Library_HideBundled = false;
                    _lib.Load(true);
                }
                Refresh();
            };
            _exportItemPack.Click += async (_, __) => await ExportSelected(true);
            _exportItemFiles.Click += async (_, __) => await ExportSelected(false);
            _exportShown.Click += async (_, __) => await ExportShownPack();
            _export.Flyout = new MenuFlyout { Items = { _exportItemPack, _exportItemFiles, new Separator(), _exportShown } };
            _search.TextChanged += (_, __) => { Refresh(); SearchOnline(); };
            _online.IsChecked = Properties.Settings.Default.Library_SearchThemezer;
            _online.IsCheckedChanged += (_, __) =>
            {
                Properties.Settings.Default.Library_SearchThemezer = _online.IsChecked == true;
                SearchOnline();
                Refresh();
            };
            _searchDelay.Tick += async (_, __) =>
            {
                _searchDelay.Stop();
                await RunOnlineSearch();
            };
            _list.SelectionChanged += (_, __) =>
            {
                if (_list.SelectedItem is ThemezerMusic hit)
                    SelectHit(hit);
                else if (_list.SelectedItem is ThemezerPart part)
                    _ = SelectPart(part);
                else
                    Select(_list.SelectedItem as LibraryItem);
            };
            _list.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                item is ThemezerMusic hit ? BuildHitRow(hit) : item is ThemezerPart part ? BuildPartRow(part) : BuildRow(item as LibraryItem));

            // Top: what kind of resource, and import/export. The filters sit right above the list they filter.
            var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(10, 8) };
            Grid.SetColumn(_import, 2);
            Grid.SetColumn(_export, 3);
            _import.Margin = new Thickness(8, 0, 6, 0);
            toolbar.Children.Add(tabs);
            toolbar.Children.Add(_import);
            toolbar.Children.Add(_export);

            var showLabel = new TextBlock { Text = MainResources.Library_Show_Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(6, 0, 6, 6) };
            Grid.SetColumn(_show, 1);
            Grid.SetRow(_search, 1);
            Grid.SetColumnSpan(_search, 2);
            _search.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(_online, 2);
            Grid.SetColumnSpan(_online, 2);
            filters.Children.Add(showLabel);
            filters.Children.Add(_show);
            filters.Children.Add(_search);
            filters.Children.Add(_online);
            var listPane = new DockPanel();
            DockPanel.SetDock(filters, Dock.Top);
            listPane.Children.Add(filters);
            listPane.Children.Add(_list);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("340,Auto,*") };
            Grid.SetColumn(listPane, 0);
            var splitter = new GridSplitter { Width = 4, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            var scroll = new ScrollViewer { Content = _detail };
            Grid.SetColumn(scroll, 2);
            body.Children.Add(listPane);
            body.Children.Add(splitter);
            body.Children.Add(scroll);

            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 6) };
            Grid.SetColumn(_progress, 1);
            bottom.Children.Add(_status);
            bottom.Children.Add(_progress);

            // The ready-made presets are a separate download: offered here until installed, and when there is a newer one
            var packRow = new DockPanel();
            DockPanel.SetDock(_packButton, Dock.Right);
            _packButton.Margin = new Thickness(12, 0, 0, 0);
            packRow.Children.Add(_packButton);
            packRow.Children.Add(_packText);
            _packBar.Child = packRow;
            _packBar.Background = new SolidColorBrush(Color.FromArgb(40, 70, 130, 180));
            _packButton.Click += async (_, __) => await DownloadPack();

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(_packBar, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(toolbar);
            root.Children.Add(_packBar);
            root.Children.Add(bottom);
            root.Children.Add(body);
            Content = root;

            Opened += async (_, __) => await CheckPack();
            Closed += (_, __) =>
            {
                _searchDelay.Stop();
                _searchCts?.Cancel();
                _packCts?.Cancel();
                _importCts?.Cancel();
                _player.Dispose();
            };
            Refresh();
        }

        // ---------------------------------------------------------------- List

        private static Bitmap LoadBitmap(string path)
        {
            try
            {
                return path != null && File.Exists(path) ? new Bitmap(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private Control BuildRow(LibraryItem item)
        {
            if (item == null)
                return new TextBlock();
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            text.Children.Add(new TextBlock { Text = item.Name ?? string.Empty, TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = item.Kind == LibraryKind.Music
                ? string.Format(MainResources.Library_Duration, item.Seconds, item.Bytes / 1024)
                : item.Kind == LibraryKind.SoundEffects ? (item.Bytes / 1024) + " KB" : item.Collection;
            text.Children.Add(new TextBlock { Text = sub, FontSize = 11, Opacity = 0.6 });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2) };
            if (item.Kind == LibraryKind.FolderTexture || item.Kind == LibraryKind.IconTexture)
            {
                var icon = item.Kind == LibraryKind.IconTexture;
                Bitmap bmp = null;
                using (var raw = SKBitmap.Decode(_lib.PathOf(item, item.FileA)))
                    if (raw != null)
                        using (var vis = TextureRecolor.Visible(raw, item.Kind, false))
                            bmp = ToBitmap(vis);
                row.Children.Add(new Image { Source = bmp, Width = icon ? 48 : 72, Height = icon ? 48 : 62, Stretch = Stretch.Uniform });
            }
            row.Children.Add(text);
            return row;
        }

        private void Refresh()
        {
            var q = (_search.Text ?? string.Empty).Trim();
            var show = ShowModes[Math.Max(0, _show.SelectedIndex)];
            var items = _lib.Of(_kind)
                .Where(i => show == "All" || _lib.IsRemovable(i) == (show == "Mine"))
                .Where(i => q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(i => i.Collection == "Umami" ? 0 : 1).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            // Music found on Themezer comes after the user's own library
            var online = OnlineActive && _hitsFor == OnlineQuery;
            _list.ItemsSource = online ? items.Cast<object>().Concat(_hits).ToList() : items.Cast<object>().ToList();
            _status.Text = items.Count == 0 && !online ? MainResources.Library_Empty : string.Format(MainResources.Library_Count, items.Count);
            if (OnlineActive)
                _status.Text = _searching ? MainResources.Library_ThemezerSearching
                    : _offline ? MainResources.Library_ThemezerOffline
                    : online ? string.Format(MainResources.Library_ThemezerCount, items.Count, _hits.Count) : _status.Text;
            for (var i = 0; i < 4; i++)
            {
                var kind = (LibraryKind) i;
                var count = show == "All" ? _lib.Count(kind) : _lib.Of(kind).Count(x => _lib.IsRemovable(x) == (show == "Mine"));
                _tabs[i].Content = new[] { MainResources.Library_Folders, MainResources.Library_Icons, MainResources.Library_Sounds, MainResources.Library_Music }[i] + "  (" + count + ")";
            }
            Select(null);
        }

        // ---------------------------------------------------------------- Music on Themezer

        private string OnlineQuery => (_search.Text ?? string.Empty).Trim();

        /// <summary>Whether a Themezer search belongs to what is shown: the box ticked, three letters typed.</summary>
        private bool OnlineActive => _online.IsChecked == true && OnlineQuery.Length >= 3;

        /// <summary>The marker Themezer sets on themes that have the parts of a tab.</summary>
        private static string FilterOf(LibraryKind kind) =>
            kind == LibraryKind.FolderTexture ? "hasCustomFolderModel" : kind == LibraryKind.IconTexture ? "hasCustomGamecardIcon" : "hasCustomSfx";

        /// <summary>Searches Themezer half a second after the last change, so typing does not send a search per key.</summary>
        private void SearchOnline()
        {
            _searchDelay.Stop();
            _searchCts?.Cancel();
            _searching = _offline = false;
            if (!OnlineActive)
            {
                _hits = new List<object>();
                _hitsFor = null;
                return;
            }
            _searching = true;
            _searchDelay.Start();
        }

        internal async Task RunOnlineSearch()
        {
            var query = OnlineQuery;
            var kind = _kind;
            if (!OnlineActive)
                return;
            var cts = _searchCts = new CancellationTokenSource();
            _searching = true;
            Refresh();
            try
            {
                // Music is found by what the uploader wrote about it, folders, icons and sounds by the theme's name
                var hits = kind == LibraryKind.Music
                    ? (await _themezer.SearchMusic(query, cts.Token)).Cast<object>().ToList()
                    : (await _themezer.SearchParts(query, FilterOf(kind), cts.Token)).Cast<object>().ToList();
                if (cts.IsCancellationRequested || query != OnlineQuery || kind != _kind)
                    return;
                _hits = hits;
                _hitsFor = query;
                _offline = false;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                if (cts.IsCancellationRequested)
                    return;
                _hits = new List<object>();
                _hitsFor = null;
                _offline = true;
            }
            _searching = false;
            Refresh();
        }

        private static Bitmap _themezerLogo;

        private static Bitmap ThemezerLogo => _themezerLogo ??
            (_themezerLogo = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/themezer.png"))));

        private Control BuildHitRow(ThemezerMusic hit)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            text.Children.Add(new TextBlock { Text = hit.BgmInfo, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = string.Format(MainResources.Library_FromTheme, hit.ThemeName, hit.Creator), FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
            // Themezer's logo marks music found there
            var logo = new Image { Source = ThemezerLogo, Width = 26, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(logo, "Themezer");
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2), Children = { logo, text } };
        }

        private Control BuildPartRow(ThemezerPart part)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            text.Children.Add(new TextBlock { Text = part.ThemeName, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = string.Format(MainResources.Library_ByCreator, part.Creator), FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
            var logo = new Image { Source = ThemezerLogo, Width = 26, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(logo, "Themezer");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2), Children = { logo } };
            if (part.PreviewUrl != null)
            {
                // Themezer's own picture of the folder or icon, loaded in the background
                var icon = _kind == LibraryKind.IconTexture;
                var thumb = new Image { Width = icon ? 48 : 72, Height = icon ? 48 : 62, Stretch = Stretch.Uniform };
                row.Children.Add(thumb);
                _ = LoadThumbnail(part.PreviewUrl, thumb);
            }
            row.Children.Add(text);
            return row;
        }

        private async Task LoadThumbnail(string url, Image target)
        {
            if (!_thumbnails.TryGetValue(url, out var bmp))
            {
                try
                {
                    var png = await _themezer.DownloadBytes(url);
                    bmp = new Bitmap(new MemoryStream(png));
                    _thumbnails[url] = bmp;
                }
                catch (Exception)
                {
                    return;
                }
            }
            target.Source = bmp;
        }

        /// <summary>The parts of a theme on Themezer (folders, icons, sound effects), downloaded and unpacked once.</summary>
        internal async Task<List<LibraryItem>> PartsOf(ThemezerPart part)
        {
            if (_parts.TryGetValue(part.HexId, out var cached))
                return cached;
            var folder = Path.Combine(MainWindow.TemporaryRoot, "themezer-parts-" + part.HexId + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var zip = await _themezer.DownloadTheme(part.HexId, folder);
            var items = await Task.Run(() =>
            {
                var unpacked = new ResourceLibrary(Path.Combine(folder, "library"));
                var importer = new ThemeImporter(unpacked, "Themezer", false);
                foreach (var theme in ThemeImporter.ReadZip(zip))
                {
                    theme.Name = part.ThemeName;
                    importer.ImportTheme(theme);
                }
                foreach (var item in unpacked.Items)
                    item.Root = unpacked.WritableRoot;
                return unpacked.Items.ToList();
            });
            _parts[part.HexId] = items;
            return items;
        }

        /// <summary>Shows a theme's folder, icon or sound effects like the library's own, to recolour, listen, use or keep.</summary>
        private async Task SelectPart(ThemezerPart part)
        {
            Select(null);
            var kind = _kind;
            _detail.Children.Add(new TextBlock { Text = part.ThemeName, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _detail.Children.Add(new TextBlock { Text = MainResources.Library_ThemezerDownloading, Opacity = 0.7 });
            LibraryItem item;
            try
            {
                item = (await PartsOf(part)).FirstOrDefault(i => i.Kind == kind);
            }
            catch (Exception ex)
            {
                if (_list.SelectedItem == part)
                {
                    _detail.Children.RemoveAt(1);
                    _detail.Children.Add(new TextBlock { Text = string.Format(MainResources.Themezer_Error, ex.Message), Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
                }
                return;
            }
            // Another entry may have been picked while this one downloaded
            if (_list.SelectedItem != part || kind != _kind)
                return;
            if (item == null)
            {
                _detail.Children.RemoveAt(1);
                _detail.Children.Add(new TextBlock { Text = MainResources.Library_ThemezerNoPart, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
                return;
            }
            Select(item, part);
        }

        private void SelectHit(ThemezerMusic hit)
        {
            Select(null);
            _detail.Children.Add(new TextBlock { Text = hit.BgmInfo, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _detail.Children.Add(new TextBlock { Text = string.Format(MainResources.Library_FromTheme, hit.ThemeName, hit.Creator), Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            // The preview is Themezer's OGG of the music, streamed without downloading the theme
            var play = new Button { MinWidth = 100, IsEnabled = !string.IsNullOrEmpty(hit.PreviewUrl) };
            Action showState = () => play.Content = _player.IsPlaying && _previewHit == hit
                ? "■ " + MainResources.Control_AudioPlayer_Stop
                : "▶ " + MainResources.Control_AudioPlayer_Play;
            _previewStateChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(AudioPlayer.IsPlaying))
                    Dispatcher.UIThread.Post(showState);
            };
            _player.PropertyChanged += _previewStateChanged;
            showState();
            play.Click += async (_, __) =>
            {
                if (_player.IsPlaying && _previewHit == hit)
                {
                    _player.StopCommand.Execute(null);
                    return;
                }
                if (_player.HasData && _previewHit == hit)
                {
                    _player.PlayCommand.Execute(null);
                    return;
                }
                play.IsEnabled = false;
                try
                {
                    var ogg = await _themezer.DownloadBytes(hit.PreviewUrl);
                    var audio = await Task.Run(() => BcstmConverter.Load(new MemoryStream(ogg), ".ogg"));
                    _player.SetAudio(audio);
                    _previewItem = null;
                    _previewHit = hit;
                    _player.PlayCommand.Execute(null);
                }
                catch (Exception ex)
                {
                    _status.Text = string.Format(MainResources.Themezer_Error, ex.Message);
                }
                play.IsEnabled = true;
                showState();
            };
            var vol = new Slider { Minimum = 0, Maximum = 1, Value = _player.Volume, Width = 140 };
            vol.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) _player.Volume = (float) vol.Value; };
            _detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { play, vol } });

            var use = new Button { Content = MainResources.Library_UseInTheme, IsEnabled = _target.HasTheme, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            var keep = new Button { Content = MainResources.Library_AddToLibrary, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            var page = new Button { Content = MainResources.Library_OpenOnThemezer, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            use.Click += async (_, __) => await TakeHit(hit, false, use, keep);
            keep.Click += async (_, __) => await TakeHit(hit, true, use, keep);
            page.Click += (_, __) => Update.OpenUrl(hit.PageUrl);
            _detail.Children.Add(use);
            _detail.Children.Add(keep);
            _detail.Children.Add(page);
            if (!_target.HasTheme)
                _detail.Children.Add(new TextBlock { Text = MainResources.Library_NoTheme, Opacity = 0.7 });
        }

        /// <summary>Downloads the theme of a hit and uses its music in the open theme, or keeps it in the user's library.</summary>
        internal async Task TakeHit(ThemezerMusic hit, bool keep, params Button[] buttons)
        {
            foreach (var b in buttons)
                b.IsEnabled = false;
            _status.Text = MainResources.Library_ThemezerDownloading;
            try
            {
                var bcstm = await _themezer.DownloadMusic(hit.HexId);
                if (bcstm == null)
                    throw new InvalidOperationException(MainResources.Library_ThemezerNoMusic);
                if (keep)
                {
                    var item = await Task.Run(() => _lib.AddMusic(bcstm, hit.BgmInfo, hit.BgmInfo));
                    Refresh();
                    _list.SelectedItem = item;
                    _status.Text = MainResources.Library_AddedToLibrary;
                }
                else
                {
                    _target.ApplyMusicData(bcstm, hit.BgmInfo);
                    _status.Text = MainResources.Library_Applied;
                }
            }
            catch (Exception ex)
            {
                _status.Text = ex is ThemezerException ? string.Format(MainResources.Themezer_Error, ex.Message) : ex.Message;
            }
            finally
            {
                buttons[0].IsEnabled = _target.HasTheme;
                for (var i = 1; i < buttons.Length; i++)
                    buttons[i].IsEnabled = true;
            }
        }

        // ---------------------------------------------------------------- Detail

        /// <param name="from">The theme on Themezer the item was unpacked from (it is not in the library then), or null.</param>
        private void Select(LibraryItem item, ThemezerPart from = null)
        {
            _selected = item;
            _exportItemPack.IsEnabled = _exportItemFiles.IsEnabled = item != null;
            _player.StopCommand.Execute(null);
            if (_previewStateChanged != null)
            {
                _player.PropertyChanged -= _previewStateChanged;
                _previewStateChanged = null;
            }
            _detail.Children.Clear();
            _srcA?.Dispose();
            _srcB?.Dispose();
            _srcA = _srcB = null;
            _sourceHue = _targetHue = null;
            if (item == null)
                return;

            _detail.Children.Add(new TextBlock { Text = item.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (item.Kind == LibraryKind.FolderTexture || item.Kind == LibraryKind.IconTexture)
                BuildTextureDetail(item);
            else if (item.Kind == LibraryKind.SoundEffects)
                BuildSoundDetail(item);
            else
                BuildMusicDetail(item);

            var apply = new Button { Content = item.Kind == LibraryKind.SoundEffects ? MainResources.Library_ApplyWholeSet : MainResources.Library_Apply, IsEnabled = _target.HasTheme, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 6), Margin = new Thickness(0, 10, 0, 0) };
            apply.Click += (_, __) => Apply(item);
            _detail.Children.Add(apply);
            if (from != null)
            {
                // From Themezer: keep it in the library, or look at the theme, instead of removing it
                var keep = new Button { Content = MainResources.Library_AddToLibrary, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                keep.Click += async (_, __) =>
                {
                    try
                    {
                        var kept = await Task.Run(() => _lib.AddCopy(item));
                        Refresh();
                        _list.SelectedItem = kept;
                        _status.Text = MainResources.Library_AddedToLibrary;
                    }
                    catch (Exception ex)
                    {
                        _status.Text = ex.Message;
                    }
                };
                var page = new Button { Content = MainResources.Library_OpenOnThemezer, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                page.Click += (_, __) => Update.OpenUrl(from.PageUrl);
                _detail.Children.Add(keep);
                _detail.Children.Add(page);
                if (!_target.HasTheme)
                    _detail.Children.Add(new TextBlock { Text = MainResources.Library_NoTheme, Opacity = 0.7 });
                return;
            }
            var removable = _lib.IsRemovable(item);
            var remove = new Button { Content = MainResources.Library_Remove, IsEnabled = removable, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            if (!removable)
                ToolTip.SetTip(remove, MainResources.Library_RemoveBundled);
            remove.Click += async (_, __) => await RemoveSelected(item);
            _detail.Children.Add(remove);
            if (!_target.HasTheme)
                _detail.Children.Add(new TextBlock { Text = MainResources.Library_NoTheme, Opacity = 0.7 });
        }

        private void Apply(LibraryItem item)
        {
            try
            {
                switch (item.Kind)
                {
                    case LibraryKind.FolderTexture:
                    case LibraryKind.IconTexture:
                        _target.ApplyTextures(item, CurrentColors());
                        break;
                    case LibraryKind.SoundEffects:
                        _target.ApplySoundEffects(item);
                        break;
                    default:
                        _target.ApplyMusic(item);
                        break;
                }
                _status.Text = MainResources.Library_Applied;
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        }

        private static Bitmap ToBitmap(SKBitmap b)
        {
            using (var img = SKImage.FromBitmap(b))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var ms = new MemoryStream(data.ToArray()))
                return new Bitmap(ms);
        }

        private void BuildTextureDetail(LibraryItem item)
        {
            _srcA = SKBitmap.Decode(_lib.PathOf(item, item.FileA));
            _srcB = SKBitmap.Decode(_lib.PathOf(item, item.FileB));
            if (_srcA == null || _srcB == null)
                return;
            var icon = item.Kind == LibraryKind.IconTexture;
            var scale = icon ? 3 : 4;
            using (var va = TextureRecolor.Visible(_srcA, item.Kind, false)) _imgA = new Image { Width = va.Width * scale, Height = va.Height * scale, Stretch = Stretch.Fill };
            using (var vb = TextureRecolor.Visible(_srcB, item.Kind, true)) _imgB = new Image { Width = vb.Width * scale, Height = vb.Height * scale, Stretch = Stretch.Fill };
            var captionA = icon ? MainResources.Menu_Image_FileLarge : MainResources.Menu_Image_FolderClosed;
            var captionB = icon ? MainResources.Menu_Image_FileSmall : MainResources.Menu_Image_FolderOpen;
            var previews = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (img, cap) in new[] { (_imgA, captionA), (_imgB, captionB) })
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 8), Spacing = 2 };
                cell.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), Child = img, HorizontalAlignment = HorizontalAlignment.Left });
                cell.Children.Add(new TextBlock { Text = cap.Replace("_", ""), FontSize = 11, Opacity = 0.6 });
                previews.Children.Add(cell);
            }
            _detail.Children.Add(previews);

            // Clicking a colour in a preview picks the colour range the target colours replace
            foreach (var (img, second) in new[] { (_imgA, false), (_imgB, true) })
            {
                var view = img;
                var isSecond = second;
                view.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
                view.PointerPressed += (_, e) =>
                {
                    var at = e.GetPosition(view);
                    using (var vis = TextureRecolor.Visible(isSecond ? _srcB : _srcA, item.Kind, isSecond))
                    {
                        var hue = TextureRecolor.HueAt(vis, (int) (at.X * vis.Width / view.Bounds.Width), (int) (at.Y * vis.Height / view.Bounds.Height));
                        if (hue != null)
                            PickSource(hue, item);
                    }
                };
            }

            // One click turns the texture's main colour into a real colour; the sliders fine-tune on top
            _detail.Children.Add(new TextBlock { Text = MainResources.Library_Variants, FontWeight = FontWeight.SemiBold });
            _swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            _swatchText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            _detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _swatch, _swatchText } });
            _presets = new WrapPanel();
            _detail.Children.Add(_presets);
            using (var vis = TextureRecolor.Visible(_srcA, item.Kind, false))
                PickSource(TextureRecolor.MainHue(vis), item);

            _hue = new Slider { Minimum = -180, Maximum = 180, Value = 0, Width = 280 };
            _sat = new Slider { Minimum = 0, Maximum = 2, Value = 1, Width = 280 };
            _bright = new Slider { Minimum = -0.4, Maximum = 0.4, Value = 0, Width = 280 };
            foreach (var s in new[] { _hue, _sat, _bright })
                s.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_updatingSliders) UpdateTexturePreview(); };
            Control Row(string label, Slider s)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), HorizontalAlignment = HorizontalAlignment.Left };
                g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(s, 1);
                g.Children.Add(s);
                return g;
            }
            _detail.Children.Add(Row(MainResources.Library_Hue, _hue));
            _hueMode = new TextBlock { FontSize = 11, Opacity = 0.6, Margin = new Thickness(110, -2, 0, 4) };
            _detail.Children.Add(_hueMode);
            _detail.Children.Add(Row(MainResources.Library_Saturation, _sat));
            _detail.Children.Add(Row(MainResources.Library_Brightness, _bright));
            var reset = new Button { Content = MainResources.Library_Reset, HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += (_, __) =>
            {
                _updatingSliders = true; _hue.Value = 0; _sat.Value = 1; _bright.Value = 0; _updatingSliders = false;
                SetTarget(null);
            };
            _detail.Children.Add(reset);
            SetTarget(null);
        }

        /// <summary>
        ///     Everything the sliders and the chosen target colour do to the texture. With a target colour the hue slider
        ///     fine-tunes that colour only; without one it turns all colours.
        /// </summary>
        internal TextureColors CurrentColors()
        {
            var hue = (float) (_hue?.Value ?? 0);
            return new TextureColors
            {
                Hue = _targetHue == null ? hue : 0,
                Saturation = (float) (_sat?.Value ?? 1),
                Brightness = (float) (_bright?.Value ?? 0),
                SourceHue = _sourceHue,
                TargetHue = _targetHue == null ? (float?) null : _targetHue.Value + hue
            };
        }

        /// <summary>Picks a target colour (null for none) and switches what the hue slider does accordingly.</summary>
        internal void SetTarget(float? target)
        {
            _targetHue = target;
            if (_hue != null)
            {
                _updatingSliders = true;
                _hue.Value = 0;
                _hue.Minimum = target == null ? -180 : -40;
                _hue.Maximum = target == null ? 180 : 40;
                _updatingSliders = false;
                _hueMode.Text = target == null ? MainResources.Library_HueAll : MainResources.Library_HueTarget;
            }
            UpdateTexturePreview();
        }

        /// <summary>Sets the colour range the target colours replace, and shows it with a preview of each target colour.</summary>
        internal void PickSource(float? hue, LibraryItem item)
        {
            _sourceHue = hue;
            if (hue != null)
            {
                TextureRecolor.FromHsv(hue.Value, 0.8f, 0.9f, out var r, out var g, out var b);
                _swatch.Background = new SolidColorBrush(Color.FromRgb((byte) (r * 255), (byte) (g * 255), (byte) (b * 255)));
                _swatch.IsVisible = true;
                _swatchText.Text = MainResources.Library_ChangesHint;
            }
            else
            {
                _swatch.IsVisible = false;
                _swatchText.Text = MainResources.Library_NoMainColor;
            }

            _presets.Children.Clear();
            var icon = item.Kind == LibraryKind.IconTexture;
            foreach (var (key, target) in TextureRecolor.TargetColors)
            {
                var to = target;
                var b = new Button { Padding = new Thickness(2), Margin = new Thickness(0, 0, 4, 4), IsEnabled = hue != null };
                ToolTip.SetTip(b, MainResources.ResourceManager.GetString("Library_Color_" + key) ?? key);
                using (var variant = TextureRecolor.Apply(_srcA, new TextureColors { SourceHue = hue, TargetHue = to }))
                using (var vis = TextureRecolor.Visible(variant, item.Kind, false))
                    b.Content = new Image { Source = ToBitmap(vis), Width = icon ? 44 : 58, Height = icon ? 44 : 50, Stretch = Stretch.Uniform };
                b.Click += (_, __) => SetTarget(to);
                _presets.Children.Add(b);
            }
            if (_hue != null)
                UpdateTexturePreview();
        }

        private void UpdateTexturePreview()
        {
            if (_srcA == null || _srcB == null || _hue == null)
                return;
            var colors = CurrentColors();
            var kind = _selected.Kind;
            using (var a = TextureRecolor.Apply(_srcA, colors))
            using (var c = TextureRecolor.Apply(_srcB, colors))
            using (var va = TextureRecolor.Visible(a, kind, false))
            using (var vb = TextureRecolor.Visible(c, kind, true))
            {
                _imgA.Source = ToBitmap(va);
                _imgB.Source = ToBitmap(vb);
            }
        }

        private void BuildSoundDetail(LibraryItem item)
        {
            byte[] data;
            var block = new CwavBlock();
            try
            {
                data = _lib.ReadAllBytes(item, item.DataFile);
                block.TryImport(data);
            }
            catch (Exception)
            {
                return;
            }
            var names = new[] { MainResources.Library_Snd_Cursor, MainResources.Library_Snd_Launch, MainResources.Library_Snd_Folder, MainResources.Library_Snd_Close, MainResources.Library_Snd_Page + " 1", MainResources.Library_Snd_Page + " 2", MainResources.Library_Snd_Page + " 3", MainResources.Library_Snd_Lid };
            _detail.Children.Add(new TextBlock { Text = MainResources.Library_Snd_Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });

            var rows = new StackPanel { Spacing = 4 };
            for (var i = 0; i < 8; i++)
            {
                var kind = (CwavKind) i;
                var file = block.Sounds[kind];
                if (file.CwavData == null || file.CwavData.Length <= 0x10)
                    continue;
                var f = file;
                var play = new Button { Content = "▶", MinWidth = 36, HorizontalContentAlignment = HorizontalAlignment.Center };
                play.Click += (_, __) =>
                {
                    try
                    {
                        var wav = f.WavData;
                        if (wav != null && wav.Length > 0)
                            AudioPlayer.PlayOneShot(BcstmConverter.Load(new MemoryStream(wav), ".wav"), 0.9f);
                    }
                    catch (Exception) { }
                };
                var use = new Button { Content = MainResources.Library_UseSound, IsEnabled = _target.HasTheme, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
                ToolTip.SetTip(use, MainResources.Library_UseSound_Tip);
                use.Click += (_, __) => UseSound(item, kind);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,Auto,Auto"), HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(play, 1);
                Grid.SetColumn(use, 2);
                play.Margin = new Thickness(0, 0, 6, 0);
                row.Children.Add(play);
                row.Children.Add(use);
                rows.Children.Add(row);
            }
            _detail.Children.Add(rows);
            _detail.Children.Add(new TextBlock { Text = string.Format(MainResources.Library_SfxSize, data.Length / 1024), Opacity = 0.7 });
        }

        private void UseSound(LibraryItem item, CwavKind kind)
        {
            try
            {
                _target.ApplySound(item, kind);
                _status.Text = MainResources.Library_Applied;
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        }

        private void BuildMusicDetail(LibraryItem item)
        {
            _detail.Children.Add(new TextBlock { Text = string.Format(MainResources.Library_Duration, item.Seconds, item.Bytes / 1024), Opacity = 0.7 });
            // One button: Play while idle, Stop while this item's music is playing (it flips back when the track ends)
            var play = new Button { MinWidth = 100 };
            Action showState = () => play.Content = _player.IsPlaying && _previewItem == item
                ? "■ " + MainResources.Control_AudioPlayer_Stop
                : "▶ " + MainResources.Control_AudioPlayer_Play;
            _previewStateChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(AudioPlayer.IsPlaying))
                    Dispatcher.UIThread.Post(showState);
            };
            _player.PropertyChanged += _previewStateChanged;
            showState();
            play.Click += async (_, __) =>
            {
                if (_player.IsPlaying && _previewItem == item)
                {
                    _player.StopCommand.Execute(null);
                    showState();
                    return;
                }
                // Only reuse what is loaded when it is this item's music: checking the selection here was always true,
                // so after the first track every Play button replayed that first track
                if (_player.HasData && _previewItem == item)
                {
                    _player.PlayCommand.Execute(null);
                    showState();
                    return;
                }
                var path = _lib.PathOf(item, item.DataFile);
                var audio = await Task.Run(() =>
                {
                    try { return BcstmConverter.ReadBcstm(File.ReadAllBytes(path)); } catch (Exception) { return null; }
                });
                if (audio != null && _selected == item)
                {
                    _player.SetAudio(audio);
                    _previewItem = item;
                    _previewHit = null;
                    _player.PlayCommand.Execute(null);
                    showState();
                }
            };
            var vol = new Slider { Minimum = 0, Maximum = 1, Value = _player.Volume, Width = 140 };
            vol.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) _player.Volume = (float) vol.Value; };
            _detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { play, vol } });
        }

        // ---------------------------------------------------------------- Import

        private void Busy(bool on)
        {
            _import.IsEnabled = _export.IsEnabled = !on;
            _progress.IsVisible = on;
            if (on)
                _progress.Value = 0;
        }

        private IProgress<ImportProgress> Progress(string format)
        {
            return new Progress<ImportProgress>(p =>
            {
                _progress.Value = p.Total == 0 ? 0 : (double) p.Done / p.Total;
                _status.Text = string.Format(format, p.Done, p.Total, p.Added);
            });
        }

        private void Report(TransferResult r)
        {
            _status.Text = string.Format(MainResources.Library_Transfer_Done, r.Added, r.Existing, r.Invalid)
                           + (r.FirstError != null ? "  –  " + string.Format(MainResources.Library_Transfer_Error, r.FirstError) : string.Empty);
        }

        private async Task<List<string>> PickFiles(string title, FilePickerFileType type)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = true, FileTypeFilter = new[] { type } });
            return files.Select(f => f.TryGetLocalPath()).Where(x => x != null).ToList();
        }

        private async Task ImportPackFile()
        {
            var files = await PickFiles(MainResources.Library_ImportPack, Dialogs.FileType(MainResources.Library_Filter_Pack, LibraryTransfer.PackExtension, ".zip"));
            if (files.Count == 0)
                return;
            Busy(true);
            var total = new TransferResult();
            foreach (var f in files)
            {
                var r = await Task.Run(() => LibraryTransfer.ImportPack(_lib, f, Progress(MainResources.Library_Importing)));
                total.Added += r.Added;
                total.Existing += r.Existing;
                total.Invalid += r.Invalid;
                total.FirstError = total.FirstError ?? r.FirstError;
            }
            Busy(false);
            Refresh();
            Report(total);
        }

        private async Task ImportLooseFiles()
        {
            var files = await PickFiles(MainResources.Library_ImportFiles, Dialogs.FileType(MainResources.Library_Filter_Files, ".bcstm", ".bin", ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"));
            if (files.Count == 0)
                return;
            Busy(true);
            var r = await Task.Run(() => LibraryTransfer.ImportFiles(_lib, files));
            Busy(false);
            Refresh();
            Report(r);
        }

        private async Task ExportSelected(bool asPack)
        {
            var item = _selected;
            if (item == null)
                return;
            var invalid = Path.GetInvalidFileNameChars();
            var name = new string(item.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            if (name.Length == 0)
                name = "resource";
            try
            {
                if (asPack)
                {
                    var path = await Dialogs.SaveFile(this, MainResources.Library_ExportPack, name + LibraryTransfer.PackExtension, Dialogs.FileType(MainResources.Library_Filter_Pack, LibraryTransfer.PackExtension));
                    if (path == null)
                        return;
                    await Task.Run(() => LibraryTransfer.ExportPack(_lib, new[] { item }, path));
                    _status.Text = string.Format(MainResources.Library_Exported, 1);
                }
                else
                {
                    var ext = item.Kind == LibraryKind.Music ? ".bcstm" : item.Kind == LibraryKind.SoundEffects ? ".bin" : ".png";
                    var path = await Dialogs.SaveFile(this, MainResources.Library_ExportFiles, name + (ext == ".png" ? string.Empty : ext), Dialogs.FileType("*" + ext, ext));
                    if (path == null)
                        return;
                    var basePath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                    var written = await Task.Run(() => LibraryTransfer.ExportFiles(_lib, item, basePath));
                    _status.Text = string.Format(MainResources.Library_Exported, written.Count);
                }
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        }

        private async Task ExportShownPack()
        {
            var items = (_list.ItemsSource as IEnumerable<LibraryItem>)?.ToList() ?? new List<LibraryItem>();
            if (items.Count == 0)
                return;
            var path = await Dialogs.SaveFile(this, MainResources.Library_ExportShown, "resources" + LibraryTransfer.PackExtension, Dialogs.FileType(MainResources.Library_Filter_Pack, LibraryTransfer.PackExtension));
            if (path == null)
                return;
            Busy(true);
            try
            {
                await Task.Run(() => LibraryTransfer.ExportPack(_lib, items, path, Progress(MainResources.Library_Exporting)));
                _status.Text = string.Format(MainResources.Library_Exported, items.Count);
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
            Busy(false);
        }

        private async Task RemoveSelected(LibraryItem item)
        {
            if (!await Dialogs.Confirm(this, string.Format(MainResources.Library_RemoveConfirm, item.Name), Title))
                return;
            _lib.Remove(item);
            Refresh();
        }

        // ---------------------------------------------------------------- Resource pack

        private static string Megabytes(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0");

        /// <summary>Shows the download offer when the pack is missing, and the update offer when GitHub has a newer one.</summary>
        internal async Task CheckPack()
        {
            ShowPackState();
            _latest = await _pack.Latest();
            ShowPackState();
        }

        private void ShowPackState()
        {
            if (_packCts != null)
                return;
            var update = _pack.IsUpdate(_latest);
            _packBar.IsVisible = !_pack.IsInstalled || update;
            var size = _latest != null && _latest.Size > 0 ? Megabytes(_latest.Size) : "~175";
            _packText.Text = update ? string.Format(MainResources.Library_Pack_Update, size) : string.Format(MainResources.Library_Pack_Offer, size);
            _packButton.Content = update ? MainResources.Library_Pack_UpdateButton : MainResources.Library_Pack_Download;
        }

        private async Task DownloadPack()
        {
            if (_packCts != null)
            {
                _packCts.Cancel();
                return;
            }
            _packCts = new CancellationTokenSource();
            _packButton.Content = MainResources.Dialog_Cancel;
            _progress.Value = 0;
            _progress.IsVisible = true;
            var progress = new Progress<double>(p =>
            {
                _progress.Value = p;
                _packText.Text = string.Format(MainResources.Library_Pack_Downloading, (int) (p * 100));
            });
            string message = null;
            try
            {
                var release = _latest ?? await _pack.Latest() ?? new PackRelease { Url = LibraryPackFallbackUrl };
                await _pack.Install(release, progress, _packCts.Token);
                _lib.Load(!Properties.Settings.Default.Library_HideBundled);
                message = MainResources.Library_Pack_Installed;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                message = string.Format(MainResources.Library_Pack_Failed, ex.Message);
            }
            _packCts = null;
            _progress.IsVisible = false;
            ShowPackState();
            Refresh();
            if (message != null)
                _status.Text = message;
        }

        private const string LibraryPackFallbackUrl = "https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/download/library-pack/" + LibraryPack.AssetName;

        private async Task RemovePack()
        {
            if (!_pack.IsInstalled || !await Dialogs.Confirm(this, MainResources.Library_Pack_RemoveConfirm, Title))
                return;
            try
            {
                await Task.Run(() => _pack.Remove());
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                return;
            }
            _lib.Load(!Properties.Settings.Default.Library_HideBundled);
            ShowPackState();
            Refresh();
            _status.Text = MainResources.Library_Pack_Removed;
        }

        /// <summary>Frees disk space: deletes what the user imported, or everything including the presets that ship with the editor.</summary>
        private async Task PurgeLibrary()
        {
            var mine = _lib.Mine();
            var bundled = _lib.Bundled();
            if (mine.Count == 0 && bundled.Count == 0)
            {
                await Dialogs.Show(this, MainResources.Library_PurgeNothing, Title);
                return;
            }
            const double MB = 1024.0 * 1024.0;
            var mineMb = _lib.BytesOf(mine) / MB;
            var bundledMb = _lib.BytesOf(bundled) / MB;
            var choice = await Dialogs.Choose(this, string.Format(MainResources.Library_PurgeChoice, mine.Count, mineMb, bundled.Count, bundledMb),
                Title, MessageKind.Warning, MainResources.Library_PurgeMineButton, MainResources.Library_PurgeAllButton, MainResources.Dialog_Cancel);
            if (choice != 0 && choice != 1)
                return;

            var removed = await Task.Run(() => _lib.PurgeMine());
            var freed = mineMb;
            var hidden = false;
            if (choice == 1 && bundled.Count > 0)
            {
                if (_pack.IsInstalled && ResourceLibrary.BundledRoot == _pack.Root)
                {
                    // The downloaded pack goes as a whole, so the download is offered again
                    await Task.Run(() => _pack.Remove());
                    _lib.Load(!Properties.Settings.Default.Library_HideBundled);
                    removed += bundled.Count;
                    freed += bundledMb;
                    ShowPackState();
                }
                else if (ResourceLibrary.CanDeleteBundled())
                {
                    removed += await Task.Run(() => _lib.PurgeBundled());
                    freed += bundledMb;
                }
                else
                {
                    // Inside the macOS app the files cannot be deleted without breaking the app: stop loading them instead
                    Properties.Settings.Default.Library_HideBundled = true;
                    Properties.Settings.Default.Save();
                    _lib.Load(false);
                    removed += bundled.Count;
                    hidden = true;
                }
            }
            Refresh();
            _status.Text = string.Format(MainResources.Library_PurgeDone, removed, freed);
            if (hidden)
                await Dialogs.Show(this, MainResources.Library_PresetsHidden, Title);
        }

        private async Task ImportFolder()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = MainResources.Library_AddFolder, AllowMultiple = false });
            var folder = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
            if (folder == null)
                return;
            _import.IsEnabled = false;
            _progress.IsVisible = true;
            _importCts = new CancellationTokenSource();
            var importer = new ThemeImporter(_lib);
            var prog = new Progress<ImportProgress>(p =>
            {
                _progress.Value = p.Total == 0 ? 0 : (double) p.Done / p.Total;
                _status.Text = string.Format(MainResources.Library_Importing, p.Done, p.Total, p.Added);
            });
            int added = 0;
            try
            {
                var r = await Task.Run(() => importer.ImportFolder(folder, prog, _importCts.Token));
                added = r.Added;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = ex.Message; }
            _progress.IsVisible = false;
            _import.IsEnabled = true;
            Refresh();
            _status.Text = string.Format(MainResources.Library_Done, added);
        }
    }
}
