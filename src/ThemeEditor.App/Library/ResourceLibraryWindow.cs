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
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ProgressBar _progress = new ProgressBar { Minimum = 0, Maximum = 1, Width = 160, IsVisible = false };
        private readonly Button _import = new Button { Content = MainResources.Library_AddFolder };
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
            Width = 1060;
            Height = 640;
            MinWidth = 900;
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

            _import.Click += async (_, __) => await ImportFolder();
            _search.TextChanged += (_, __) => Refresh();
            _list.SelectionChanged += (_, __) => Select(_list.SelectedItem as LibraryItem);
            _list.ItemTemplate = new FuncDataTemplate<LibraryItem>((item, _) => BuildRow(item));

            var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(10, 8) };
            Grid.SetColumn(_search, 2);
            Grid.SetColumn(_import, 3);
            _search.Margin = new Thickness(0, 0, 8, 0);
            toolbar.Children.Add(tabs);
            toolbar.Children.Add(_search);
            toolbar.Children.Add(_import);

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
            var items = _lib.Of(_kind)
                .Where(i => q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(i => i.Collection == "Umami" ? 0 : 1).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _list.ItemsSource = items;
            _status.Text = items.Count == 0 ? MainResources.Library_Empty : string.Format(MainResources.Library_Count, items.Count);
            for (var i = 0; i < 4; i++)
                _tabs[i].Content = new[] { MainResources.Library_Folders, MainResources.Library_Icons, MainResources.Library_Sounds, MainResources.Library_Music }[i] + "  (" + _lib.Count((LibraryKind) i) + ")";
            Select(null);
        }

        // ---------------------------------------------------------------- Detail

        private void Select(LibraryItem item)
        {
            _selected = item;
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

            var apply = new Button { Content = MainResources.Library_Apply, IsEnabled = _target.HasTheme, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 6), Margin = new Thickness(0, 10, 0, 0) };
            apply.Click += (_, __) => Apply(item);
            _detail.Children.Add(apply);
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
                block.TryImport(data);   // also scans the block for every embedded CWAV
            }
            catch (Exception)
            {
                return;
            }
            var named = new[] { MainResources.Library_Snd_Cursor, MainResources.Library_Snd_Launch, MainResources.Library_Snd_Folder, MainResources.Library_Snd_Close, MainResources.Library_Snd_Page + " 1", MainResources.Library_Snd_Page + " 2", MainResources.Library_Snd_Page + " 3", MainResources.Library_Snd_Lid };
            var clips = new List<(string Label, CwavFile File)>();
            var all8 = Enumerable.Range(0, 8).All(i => block.Sounds.TryGetValue((CwavKind) i, out var f) && f.CwavData != null && f.CwavData.Length > 0x10);
            if (all8)
                for (var i = 0; i < 8; i++)
                    clips.Add((named[i], block.Sounds[(CwavKind) i]));
            else
                for (var i = 0; i < block.Scanned.Count; i++)
                    clips.Add((string.Format(MainResources.Library_Snd_Number, i + 1), block.Scanned[i]));

            var wrap = new WrapPanel();
            foreach (var (label, file) in clips)
            {
                var f = file;
                var b = new Button { Content = "▶ " + label, Margin = new Thickness(0, 0, 6, 6), MinWidth = 90 };
                b.Click += (_, __) =>
                {
                    try
                    {
                        var wav = f.WavData;
                        if (wav != null && wav.Length > 0)
                            AudioPlayer.PlayOneShot(BcstmConverter.Load(new MemoryStream(wav), ".wav"), 0.9f);
                    }
                    catch (Exception) { }
                };
                wrap.Children.Add(b);
            }
            _detail.Children.Add(wrap);
            _detail.Children.Add(new TextBlock { Text = string.Format(MainResources.Library_SfxSize, data.Length / 1024), Opacity = 0.7 });
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
