// --------------------------------------------------
// Umami 3DS Theme Editor - CWAVWindow.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ThemeEditor.App.Localization;
using ThemeEditor.Common.Audio;

namespace ThemeEditor.App.Experimental.CWAV
{
    /// <summary>Manager for the eight sound effects of a theme (CWAV files).</summary>
    public sealed class CwavWindow : Window
    {
        private const int MaxSize = 187392;

        private readonly AudioPlayer _player = new AudioPlayer {Loop = false};
        private readonly StackPanel _rows = new StackPanel();
        private readonly ProgressBar _progress = new ProgressBar {Maximum = MaxSize, Height = 22, Margin = new Thickness(2)};
        private readonly TextBlock _progressText = new TextBlock {HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center};

        public CwavBlock ViewModel { get; }

        public CwavWindow(CwavBlock block)
        {
            ViewModel = block;
            Title = MainResources.Cwav_Title;
            Width = 560;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var ok = new Button {Content = MainResources.Dialog_OK, IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center};
            var cancel = new Button {Content = MainResources.Dialog_Cancel, IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center};
            ok.Click += async (s, e) =>
            {
                var size = ViewModel.EstimatedSize;
                if (size > 0x2DC00)
                {
                    await Dialogs.Show(this, string.Format(MainResources.Cwav_TooBig, 0x2DC00, size), Title, MessageKind.Error);
                    return;
                }
                Close(true);
            };
            cancel.Click += (s, e) => Close(false);

            var bar = new Grid {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(6)};
            var progressHost = new Grid {Children = {_progress, _progressText}};
            Grid.SetColumn(cancel, 1);
            Grid.SetColumn(ok, 2);
            cancel.Margin = new Thickness(4, 0);
            bar.Children.Add(progressHost);
            bar.Children.Add(cancel);
            bar.Children.Add(ok);

            Content = new StackPanel {Margin = new Thickness(8), Children = {_rows, new Separator(), bar}};
            Closing += (s, e) => _player.Dispose();

            ViewModel.PropertyChanged += (s, e) => UpdateProgress();
            foreach (var pair in ViewModel.Sounds)
                pair.Value.PropertyChanged += (s, e) => Refresh();
            Refresh();
        }

        private void UpdateProgress()
        {
            var size = ViewModel.EstimatedSize;
            _progress.Value = Math.Min(size, MaxSize);
            _progress.Foreground = size > MaxSize ? Brushes.DarkRed : null;
            _progressText.Text = $"{size}/{MaxSize} bytes";
        }

        private void Refresh()
        {
            UpdateProgress();
            _rows.Children.Clear();
            foreach (var pair in ViewModel.Sounds.OrderBy(p => (int) p.Key))
                _rows.Children.Add(BuildRow(pair.Key, pair.Value));
        }

        private Control BuildRow(CwavKind kind, CwavFile file)
        {
            var hasAudio = file.WavData.Length > 0;
            var grid = new Grid {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto")};
            grid.Children.Add(new TextBlock
            {
                Text = string.Format(MainResources.Cwav_Size, kind, file.Size),
                Margin = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center
            });

            Button Make(string label, int col, bool enabled, Action click)
            {
                var b = new Button {Content = label, Margin = new Thickness(3), Padding = new Thickness(6, 3), IsEnabled = enabled};
                b.Click += (s, e) => click();
                Grid.SetColumn(b, col);
                grid.Children.Add(b);
                return b;
            }

            Make(MainResources.Cwav_Remove, 1, hasAudio, () => file.CwavData = CwavFile.EmptyData);
            Make(MainResources.Cwav_Play, 2, hasAudio, () =>
            {
                _player.SetAudioData(file.WavData);
                _player.Restart();
            });
            Make(MainResources.Cwav_Export, 3, hasAudio, async () => await Export(kind, file));
            var import = Make(MainResources.Cwav_Import, 4, true, async () => await Import(kind));

            if (ViewModel.Scanned.Count > 0)
            {
                var menu = new ContextMenu();
                foreach (var scanned in ViewModel.Scanned)
                {
                    var s = scanned;
                    var item = new MenuItem {Header = s.Tag};
                    item.Click += (_, __) => ViewModel.Sounds[kind].CwavData = s.CwavData;
                    menu.Items.Add(item);
                }
                import.ContextMenu = menu;
            }
            return grid;
        }

        private async System.Threading.Tasks.Task Export(CwavKind kind, CwavFile file)
        {
            var path = await Dialogs.SaveFile(this, MainResources.Cwav_Export, kind.ToString().ToLower() + ".bcwav",
                Dialogs.FileType(MainResources.Filter_CwavFile, ".bcwav"), Dialogs.FileType(MainResources.Filter_WavFile, ".wav"));
            if (path == null)
                return;
            try
            {
                var wav = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase);
                File.WriteAllBytes(path, wav ? file.WavData : file.CwavData);
                await Dialogs.Show(this, MainResources.Cwav_Exported, Title);
            }
            catch (Exception ex)
            {
                await Dialogs.Show(this, ex.Message, Title, MessageKind.Error);
            }
        }

        private async System.Threading.Tasks.Task Import(CwavKind kind)
        {
            var exts = new List<string>(BcstmConverter.ImportExtensions) {".bcwav", ".cwav"};
            var path = await Dialogs.OpenFile(this, MainResources.Cwav_Import,
                Dialogs.FileType(MainResources.Filter_SupportedFiles, exts.ToArray()));
            if (path == null)
                return;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                byte[] cwav = ext == ".bcwav" || ext == ".cwav"
                    ? File.ReadAllBytes(path)
                    : await System.Threading.Tasks.Task.Run(() => CwavFile.EncodeCwav(path));
                if (cwav.Length == 0)
                {
                    await Dialogs.Show(this, MainResources.Cwav_ConvertFailed, Title, MessageKind.Error);
                    return;
                }
                ViewModel.Sounds[kind].CwavData = cwav;
            }
            catch (Exception)
            {
                await Dialogs.Show(this, MainResources.Cwav_ConvertFailed, Title, MessageKind.Error);
            }
        }
    }
}
