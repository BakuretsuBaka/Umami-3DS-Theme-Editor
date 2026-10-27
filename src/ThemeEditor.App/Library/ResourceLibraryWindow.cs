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
        void ApplyTextures(LibraryItem item, float hue, float saturation, float brightness);
        void ApplySoundEffects(LibraryItem item);
        void ApplySound(LibraryItem item, CwavKind kind);
        void ApplyMusic(LibraryItem item);
    }

    /// <summary>Browse ready-made folder/icon textures, sound effects and music and apply them to the open theme.</summary>
    internal sealed class ResourceLibraryWindow : Window
    {
        private static readonly string[] SoundNames = { "Cursor", "Launch", "Folder", "Close", "Frame0", "Frame1", "Frame2", "OpenLid" };

        private readonly ResourceLibrary _lib;
        private readonly ILibraryTarget _target;
        private readonly AudioPlayer _player = new AudioPlayer { Loop = false };
        private readonly RadioButton[] _tabs = new RadioButton[4];
        private readonly TextBox _search = new TextBox { Watermark = MainResources.Library_Search, MinWidth = 150, Width = 170 };
        private readonly ComboBox _show = new ComboBox { MinWidth = 130 };
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
        private Image _imgA, _imgB;
        private SKBitmap _srcA, _srcB;
        private bool _updatingSliders;
        private CancellationTokenSource _importCts;

        public ResourceLibraryWindow(ResourceLibrary lib, ILibraryTarget target)
        {
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
                rb.Click += (_, __) => { _kind = kind; Refresh(); };
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
            _import.Flyout = new MenuFlyout { Items = { importPack, importFiles, new Separator(), importThemes, new Separator(), purge } };

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
            _search.TextChanged += (_, __) => Refresh();
            _list.SelectionChanged += (_, __) => Select(_list.SelectedItem as LibraryItem);
            _list.ItemTemplate = new FuncDataTemplate<LibraryItem>((item, _) => BuildRow(item));

            var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(10, 8) };
            var showLabel = new TextBlock { Text = MainResources.Library_Show_Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(showLabel, 2);
            Grid.SetColumn(_show, 3);
            Grid.SetColumn(_search, 4);
            Grid.SetColumn(_import, 5);
            Grid.SetColumn(_export, 6);
            _show.Margin = new Thickness(0, 0, 8, 0);
            _search.Margin = new Thickness(0, 0, 8, 0);
            toolbar.Children.Add(showLabel);
            toolbar.Children.Add(_show);
            _import.Margin = new Thickness(0, 0, 6, 0);
            toolbar.Children.Add(tabs);
            toolbar.Children.Add(_search);
            toolbar.Children.Add(_import);
            toolbar.Children.Add(_export);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("340,Auto,*") };
            Grid.SetColumn(_list, 0);
            var splitter = new GridSplitter { Width = 4, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            var scroll = new ScrollViewer { Content = _detail };
            Grid.SetColumn(scroll, 2);
            body.Children.Add(_list);
            body.Children.Add(splitter);
            body.Children.Add(scroll);

            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 6) };
            Grid.SetColumn(_progress, 1);
            bottom.Children.Add(_status);
            bottom.Children.Add(_progress);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(toolbar);
            root.Children.Add(bottom);
            root.Children.Add(body);
            Content = root;

            Closed += (_, __) =>
            {
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
            _list.ItemsSource = items;
            _status.Text = items.Count == 0 ? MainResources.Library_Empty : string.Format(MainResources.Library_Count, items.Count);
            for (var i = 0; i < 4; i++)
            {
                var kind = (LibraryKind) i;
                var count = show == "All" ? _lib.Count(kind) : _lib.Of(kind).Count(x => _lib.IsRemovable(x) == (show == "Mine"));
                _tabs[i].Content = new[] { MainResources.Library_Folders, MainResources.Library_Icons, MainResources.Library_Sounds, MainResources.Library_Music }[i] + "  (" + count + ")";
            }
            Select(null);
        }

        // ---------------------------------------------------------------- Detail

        private void Select(LibraryItem item)
        {
            _selected = item;
            _exportItemPack.IsEnabled = _exportItemFiles.IsEnabled = item != null;
            _player.StopCommand.Execute(null);
            _detail.Children.Clear();
            _srcA?.Dispose();
            _srcB?.Dispose();
            _srcA = _srcB = null;
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
                        _target.ApplyTextures(item, (float) (_hue?.Value ?? 0), (float) (_sat?.Value ?? 1), (float) (_bright?.Value ?? 0));
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

            _detail.Children.Add(new TextBlock { Text = MainResources.Library_Variants, FontWeight = FontWeight.SemiBold });
            var strip = new WrapPanel();
            foreach (var h in TextureRecolor.PresetHues)
            {
                var hue = h;
                using (var variant = TextureRecolor.Apply(_srcA, hue))
                using (var vis = TextureRecolor.Visible(variant, item.Kind, false))
                {
                    var b = new Button { Padding = new Thickness(2), Margin = new Thickness(0, 0, 4, 4), Content = new Image { Source = ToBitmap(vis), Width = icon ? 44 : 58, Height = icon ? 44 : 50, Stretch = Stretch.Uniform } };
                    b.Click += (_, __) => { _updatingSliders = true; _hue.Value = hue > 180 ? hue - 360 : hue; _sat.Value = 1; _bright.Value = 0; _updatingSliders = false; UpdateTexturePreview(); };
                    strip.Children.Add(b);
                }
            }
            _detail.Children.Add(strip);

            _hue = new Slider { Minimum = -180, Maximum = 180, Value = 0, Width = 280 };
            _sat = new Slider { Minimum = 0, Maximum = 2, Value = 1, Width = 280 };
            _bright = new Slider { Minimum = -0.5, Maximum = 0.5, Value = 0, Width = 280 };
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
            _detail.Children.Add(Row(MainResources.Library_Saturation, _sat));
            _detail.Children.Add(Row(MainResources.Library_Brightness, _bright));
            var reset = new Button { Content = MainResources.Library_Reset, HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += (_, __) => { _updatingSliders = true; _hue.Value = 0; _sat.Value = 1; _bright.Value = 0; _updatingSliders = false; UpdateTexturePreview(); };
            _detail.Children.Add(reset);
            UpdateTexturePreview();
        }

        private void UpdateTexturePreview()
        {
            if (_srcA == null || _srcB == null || _hue == null)
                return;
            var h = (float) _hue.Value; var s = (float) _sat.Value; var b = (float) _bright.Value;
            var kind = _selected.Kind;
            using (var a = TextureRecolor.Apply(_srcA, h, s, b))
            using (var c = TextureRecolor.Apply(_srcB, h, s, b))
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
            var play = new Button { Content = "▶ " + MainResources.Control_AudioPlayer_Play, MinWidth = 100 };
            var stop = new Button { Content = "■ " + MainResources.Control_AudioPlayer_Stop, MinWidth = 100 };
            play.Click += async (_, __) =>
            {
                if (_player.HasData && _selected == item)
                {
                    _player.PlayCommand.Execute(null);
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
                    _player.PlayCommand.Execute(null);
                }
            };
            stop.Click += (_, __) => _player.StopCommand.Execute(null);
            var vol = new Slider { Minimum = 0, Maximum = 1, Value = _player.Volume, Width = 140 };
            vol.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) _player.Volume = (float) vol.Value; };
            _detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { play, stop, vol } });
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
                if (ResourceLibrary.CanDeleteBundled())
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
