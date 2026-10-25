// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.axaml.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ThemeEditor.App.Controls;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Graphics;

namespace ThemeEditor.App
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public const string WINDOW_TITLE = "Umami 3DS Theme Editor";

        private static Dictionary<TargetImage, List<ImageSize>> _validImageSizes;

        private string _busyText;
        private bool _isBusy;
        private ThemeViewModel _viewModel;
        private PropertyEditors _editors;

        public string StartupTheme { get; set; }

        public ICommand AboutCommand { get; }
        public ICommand CWavManagerCommand { get; }
        public ICommand ExportPreviewCommand { get; }
        public ICommand PreviewOptionsCommand { get; }
        public ICommand ToggleLidCommand { get; }

        public ObservableCollection<string> RecentsList { get; } = new ObservableCollection<string>();

        public string BusyText
        {
            get { return _busyText; }
            set
            {
                _busyText = value;
                OnPropertyChanged(nameof(BusyText));
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (value == _isBusy)
                    return;
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
            }
        }

        public ThemeViewModel ViewModel
        {
            get { return _viewModel; }
            set
            {
                if (ReferenceEquals(value, _viewModel))
                    return;
                var old = _viewModel;
                _viewModel = value;
                OnPropertyChanged(nameof(ViewModel));
                RebuildEditors();
                old?.Dispose();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        static MainWindow()
        {
            InitValidSizes();
        }

        public MainWindow()
        {
            InitializeComponent();
            RestoreWindowState();

            AboutCommand = new RelayCommand(AboutCommand_Execute);
            CWavManagerCommand = new RelayCommand(CWavManager_Execute, CanExecute_ViewModelLoaded);
            ExportPreviewCommand = new RelayCommand<PreviewKind>(ExportPreview_Execute, k => CanExecute_ViewModelLoaded());
            PreviewOptionsCommand = new RelayCommand(PreviewOptionsCommand_Execute);
            ToggleLidCommand = new RelayCommand(() => PreviewActions.ToggleLid(ViewModel, PreviewInteraction.Shared));

            SetupThemeCommands();
            SetupImageCommands();
            SetupBgmCommands();
            SetupArchive();

            DataContext = this;
            BuildMenus();
            SetupHotkeys();
            SetupDragDrop();
            SetupIconPickers();

            ViewModelBase.ViewModelChanged += OnAnyViewModelChanged;
            Opened += MainWindow_OnOpened;
            Closing += MainWindow_OnClosing;
            Title = WINDOW_TITLE;
        }

        // ---------------------------------------------------------------- Lifecycle

        private async void MainWindow_OnOpened(object sender, EventArgs e)
        {
            if (Settings.Default.MRU_List != null)
                foreach (var s in Settings.Default.MRU_List)
                    RecentsList.Add(s);
            RebuildRecentMenu();

            if (StartupTheme != null)
                LoadThemeCommandWrapper.Command.Execute(StartupTheme);
            else
                NewThemeCommandWrapper.Command.Execute(null);

            if (Settings.Default.CheckUpdatesOnStartup)
            {
                var hasUpdates = await Update.CheckUpdateAvailable();
                if (hasUpdates)
                    await Dialogs.Show(this, MainResources.Message_UpdateAvailable, WINDOW_TITLE);
            }
        }

        private void RestoreWindowState()
        {
            var st = Settings.Default;
            if (st.Window_Width >= MinWidth && st.Window_Height >= MinHeight)
            {
                Width = st.Window_Width;
                Height = st.Window_Height;
            }
            if (st.Window_Maximized)
                WindowState = WindowState.Maximized;
        }

        private void MainWindow_OnClosing(object sender, WindowClosingEventArgs e)
        {
            var st = Settings.Default;
            st.Window_Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal && Bounds.Width >= MinWidth && Bounds.Height >= MinHeight)
            {
                st.Window_Width = Bounds.Width;
                st.Window_Height = Bounds.Height;
            }
            Settings.Default.MRU_List = RecentsList.ToArray();
            Settings.Default.Save();
            AudioPlayer.Instance.ClearAudioData();
            ViewModelBase.ViewModelChanged -= OnAnyViewModelChanged;
        }

        // ---------------------------------------------------------------- Commands

        private bool CanExecute_ViewModelLoaded()
        {
            return ViewModel != null;
        }

        private async void AboutCommand_Execute()
        {
            await new AboutWindow().ShowDialog(this);
        }

        private async void PreviewOptionsCommand_Execute()
        {
            await new PreviewOptionsWindow().ShowDialog(this);
        }

        private async void CWavManager_Execute()
        {
            var block = new CwavBlock();
            var imported = block.TryImport(ViewModel.CWavBytes);
            if (!imported)
                await Dialogs.Show(this, MainResources.Cwav_ParseError, WINDOW_TITLE, MessageKind.Warning);

            var wnd = new CwavWindow(block);
            var accepted = await wnd.ShowDialog<bool>(this);
            if (accepted)
            {
                var data = wnd.ViewModel.Generate();
                if (data.Length > 0x2DC00)
                    await Dialogs.Show(this, string.Format(MainResources.Cwav_TooBig, 0x2DC00, data.Length), WINDOW_TITLE, MessageKind.Error);
                else
                    ViewModel.CWavBytes = data;
            }
        }

        private async void ExportPreview_Execute(PreviewKind previewKind)
        {
            string name;
            switch (previewKind)
            {
                case PreviewKind.Top:
                    name = "preview_top";
                    break;
                case PreviewKind.Bottom:
                    name = "preview_bottom";
                    break;
                default:
                    name = "preview";
                    break;
            }

            var path = await Dialogs.SaveFile(this, MainResources.Menu_File_SavePreview, name + ".png",
                Dialogs.FileType(MainResources.Filter_PngImage, ".png"));
            if (path == null)
                return;

            try
            {
                File.WriteAllBytes(path, PreviewExporter.RenderPng(ViewModel, previewKind));
                await Dialogs.Show(this, MainResources.Message_PreviewSaved, WINDOW_TITLE);
            }
            catch (Exception ex)
            {
                await Dialogs.Show(this, ex.Message, WINDOW_TITLE, MessageKind.Error);
            }
        }

        private void PreExecute_SetBusy()
        {
            IsBusy = true;
            BusyText = null;
        }

        // ---------------------------------------------------------------- Editors

        private void RebuildEditors()
        {
            _editors?.Dispose();
            _editors = null;
            var colorsHost = this.FindControl<ContentControl>("ColorsHost");
            var flagsHost = this.FindControl<ContentControl>("FlagsHost");
            if (_viewModel == null)
            {
                colorsHost.Content = null;
                flagsHost.Content = null;
                return;
            }
            _editors = new PropertyEditors();
            colorsHost.Content = _editors.BuildColors(_viewModel);
            flagsHost.Content = _editors.BuildFlags(_viewModel);
        }

        private void OnAnyViewModelChanged(ViewModelBase.ViewModelChangedArgs args)
        {
            var vm = _viewModel;
            if (vm == null || args.ViewModel.Tag != vm.Tag)
                return;
            if (args.Property == nameof(TexturesViewModel.Top) || args.Property == nameof(TexturesViewModel.Bottom))
                Dispatcher.UIThread.Post(() => _editors?.RefreshPalettes());
            CommandManager.InvalidateRequerySuggested();
        }

        private void SetupIconPickers()
        {
            LargeIconPicker.Captured += (s, buffer) =>
            {
                if (ViewModel == null)
                    return;
                ViewModel.Info.LargeIcon.EncodeTexture(buffer.Resize(48, 48), RawTexture.DataFormat.Bgr565);
            };
            SmallIconPicker.Captured += (s, buffer) =>
            {
                if (ViewModel == null)
                    return;
                ViewModel.Info.SmallIcon.EncodeTexture(buffer.Resize(24, 24), RawTexture.DataFormat.Bgr565);
            };
        }

        private void SetupHotkeys()
        {
            foreach (var wrapper in new[]
                     {
                         LoadThemeCommandWrapper, SaveThemeCommandWrapper, SaveAsThemeCommandWrapper, NewThemeCommandWrapper
                     })
                KeyBindings.Add(new KeyBinding { Gesture = wrapper.Gesture, Command = wrapper.Command });
        }

        // ---------------------------------------------------------------- Valid texture sizes

        private static void InitValidSizes()
        {
            _validImageSizes = new Dictionary<TargetImage, List<ImageSize>>
            {
                {
                    TargetImage.Top, new List<ImageSize>
                    {
                        new ImageSize(1024, 256, RawTexture.DataFormat.Bgr565),
                        new ImageSize(512, 256, RawTexture.DataFormat.Bgr565),
                        new ImageSize(64, 64, RawTexture.DataFormat.A8)
                    }
                },
                {
                    TargetImage.Bottom, new List<ImageSize>
                    {
                        new ImageSize(1024, 256, RawTexture.DataFormat.Bgr565),
                        new ImageSize(512, 256, RawTexture.DataFormat.Bgr565)
                    }
                },
                {TargetImage.TopAlt, new List<ImageSize> {new ImageSize(64, 64, RawTexture.DataFormat.A8)}},
                {TargetImage.FileSmall, new List<ImageSize> {new ImageSize(32, 64, RawTexture.DataFormat.Bgr888)}},
                {TargetImage.FileLarge, new List<ImageSize> {new ImageSize(64, 128, RawTexture.DataFormat.Bgr888)}},
                {TargetImage.FolderClosed, new List<ImageSize> {new ImageSize(128, 64, RawTexture.DataFormat.Bgr888)}},
                {TargetImage.FolderOpen, new List<ImageSize> {new ImageSize(128, 64, RawTexture.DataFormat.Bgr888)}},
                {TargetImage.SmallIcon, new List<ImageSize> {new ImageSize(24, 24, RawTexture.DataFormat.Bgr565)}},
                {TargetImage.LargeIcon, new List<ImageSize> {new ImageSize(48, 48, RawTexture.DataFormat.Bgr565)}}
            };
        }

        // ---------------------------------------------------------------- INotifyPropertyChanged

        public new event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (Dispatcher.UIThread.CheckAccess())
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            else
                Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
    }
}
