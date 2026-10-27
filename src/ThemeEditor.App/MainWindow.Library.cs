// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Library.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Windows.Input;
using SkiaSharp;
using ThemeEditor.App.Library;
using ThemeEditor.Common.Audio;
using ThemeEditor.Common.Graphics;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        private ResourceLibraryWindow _libraryWindow;

        public ICommand OpenLibraryCommand { get; private set; }

        private void SetupLibrary()
        {
            OpenLibraryCommand = new RelayCommand(OpenLibrary_Execute);
        }

        private void OpenLibrary_Execute()
        {
            if (_libraryWindow != null)
            {
                _libraryWindow.Activate();
                return;
            }
            ResourceLibrary.Instance.Load(!Properties.Settings.Default.Library_HideBundled);
            _libraryWindow = new ResourceLibraryWindow(ResourceLibrary.Instance, new LibraryTarget(this));
            _libraryWindow.Closed += (_, __) => _libraryWindow = null;
            _libraryWindow.Show(this);
        }

        /// <summary>Applies library items to whatever theme is open.</summary>
        private sealed class LibraryTarget : ILibraryTarget
        {
            private readonly MainWindow _w;

            public LibraryTarget(MainWindow w) { _w = w; }

            public bool HasTheme => _w.ViewModel != null;

            public void ApplyTextures(LibraryItem item, float hue, float saturation, float brightness)
            {
                var vm = _w.ViewModel;
                var lib = ResourceLibrary.Instance;
                var icon = item.Kind == LibraryKind.IconTexture;
                Action<string, TextureViewModelSlot> put = (file, slot) =>
                {
                    using (var src = SKBitmap.Decode(lib.PathOf(item, file)))
                    using (var tinted = TextureRecolor.Apply(src, hue, saturation, brightness))
                    using (var img = SKImage.FromBitmap(tinted))
                    using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                    using (var ms = new MemoryStream(data.ToArray()))
                    {
                        var tex = slot.Texture;
                        tex.EncodeTexture(PixelBuffer.Load(ms), RawTexture.DataFormat.Bgr888);
                        tex.EdgeBleed(0, 0, slot.Width, slot.Height);
                    }
                };
                if (icon)
                {
                    put(item.FileA, new TextureViewModelSlot(vm.Textures.FileLarge, 36, 72));
                    put(item.FileB, new TextureViewModelSlot(vm.Textures.FileSmall, 25, 50));
                    vm.Flags.FileTexture = true;
                }
                else
                {
                    put(item.FileA, new TextureViewModelSlot(vm.Textures.FolderClosed, 74, 64));
                    put(item.FileB, new TextureViewModelSlot(vm.Textures.FolderOpen, 82, 64));
                    vm.Flags.FolderTexture = true;
                }
            }

            public void ApplySoundEffects(LibraryItem item)
            {
                var vm = _w.ViewModel;
                vm.CWavBytes = ResourceLibrary.Instance.ReadAllBytes(item, item.DataFile);
                vm.Flags.SoundEffect = true;
            }

            public void ApplySound(LibraryItem item, ThemeEditor.App.Experimental.CWAV.CwavKind kind)
            {
                var vm = _w.ViewModel;
                vm.CWavBytes = SoundMerge.Replace(vm.CWavBytes, ResourceLibrary.Instance.ReadAllBytes(item, item.DataFile), kind);
                vm.Flags.SoundEffect = true;
            }

            public void ApplyMusic(LibraryItem item)
            {
                var bytes = BcstmCompat.Normalize(ResourceLibrary.Instance.ReadAllBytes(item, item.DataFile));
                if (bytes.Length > BcstmConverter.MaxThemeBgmBytes)
                    throw new InvalidOperationException(string.Format(Localization.MainResources.Message_BgmTooLarge, bytes.Length / 1024, BcstmConverter.MaxThemeBgmBytes / 1024));
                _w.BgmBcstm = bytes;
                _w.BgmDirty = true;
                AudioPlayer.Instance.SetAudio(BcstmConverter.ReadBcstm(bytes));
                _w.ViewModel.Flags.BackgroundMusic = true;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private struct TextureViewModelSlot
        {
            public readonly Themes.TextureViewModel Texture;
            public readonly int Width;
            public readonly int Height;

            public TextureViewModelSlot(Themes.TextureViewModel texture, int width, int height)
            {
                Texture = texture;
                Width = width;
                Height = height;
            }
        }
    }
}
