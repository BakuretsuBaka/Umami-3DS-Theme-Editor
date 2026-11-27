// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Library.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

        /// <summary>Parts of the open theme taken from other people's themes, each with what it was like when taken.</summary>
        private readonly List<ThemeCredit> _credits = new List<ThemeCredit>();

        /// <summary>The credits for the parts that are still as they were taken (edited or replaced parts need none).</summary>
        internal List<ThemeCredit> CurrentCredits() =>
            ViewModel == null ? new List<ThemeCredit>() : _credits.Where(c => c.Fingerprint == Fingerprint(c.Part)).ToList();

        /// <summary>A hash of what the theme's part of this kind is now (the folder or icon pictures, sound effects, music).</summary>
        internal string Fingerprint(LibraryKind part)
        {
            var vm = ViewModel;
            if (vm == null)
                return null;
            byte[][] data;
            switch (part)
            {
                case LibraryKind.FolderTexture:
                    data = new[] { vm.Textures.FolderClosed.Pixels?.Bgra, vm.Textures.FolderOpen.Pixels?.Bgra };
                    break;
                case LibraryKind.IconTexture:
                    data = new[] { vm.Textures.FileLarge.Pixels?.Bgra, vm.Textures.FileSmall.Pixels?.Bgra };
                    break;
                case LibraryKind.SoundEffects:
                    data = new[] { vm.CWavBytes };
                    break;
                default:
                    data = new[] { BgmBcstm };
                    break;
            }
            using (var sha = SHA256.Create())
            {
                foreach (var d in data)
                {
                    var bytes = d ?? Array.Empty<byte>();
                    var length = BitConverter.GetBytes(bytes.Length);
                    sha.TransformBlock(length, 0, length.Length, null, 0);
                    sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Convert.ToBase64String(sha.Hash);
            }
        }

        /// <summary>
        ///     Notes who to credit for a part just taken from a theme. <paramref name="whole" />: it replaced all of that part
        ///     (otherwise, for a single sound effect, the earlier credits still apply to the rest).
        /// </summary>
        internal void Credit(LibraryKind part, ThemeCredit credit, bool whole = true)
        {
            var fingerprint = Fingerprint(part);
            if (whole)
                _credits.RemoveAll(c => c.Part == part);
            else
            {
                // The others' fingerprints were of the sounds before this one came in
                _credits.RemoveAll(c => c.Part == part && c.Fingerprint != _creditBefore);
                foreach (var c in _credits.Where(c => c.Part == part))
                    c.Fingerprint = fingerprint;
            }
            if (credit != null)
            {
                credit.Part = part;
                credit.Fingerprint = fingerprint;
                _credits.Add(credit);
            }
        }

        /// <summary>The fingerprint of a part right before a single sound effect is swapped in (see <see cref="Credit" />).</summary>
        private string _creditBefore;

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
        internal sealed class LibraryTarget : ILibraryTarget
        {
            private readonly MainWindow _w;

            public LibraryTarget(MainWindow w) { _w = w; }

            public bool HasTheme => _w.ViewModel != null;

            public void ApplyTextures(LibraryItem item, TextureColors colors)
            {
                var vm = _w.ViewModel;
                var lib = ResourceLibrary.Instance;
                var icon = item.Kind == LibraryKind.IconTexture;
                Action<string, TextureViewModelSlot> put = (file, slot) =>
                {
                    using (var src = SKBitmap.Decode(lib.PathOf(item, file)))
                    using (var tinted = TextureRecolor.Apply(src, colors))
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
                // Recoloured, it is still their drawing
                _w.Credit(item.Kind, ThemeCredit.Of(item));
            }

            public void ApplySoundEffects(LibraryItem item)
            {
                var vm = _w.ViewModel;
                vm.CWavBytes = ResourceLibrary.Instance.ReadAllBytes(item, item.DataFile);
                vm.Flags.SoundEffect = true;
                _w.Credit(LibraryKind.SoundEffects, ThemeCredit.Of(item));
            }

            public void ApplySound(LibraryItem item, ThemeEditor.App.Experimental.CWAV.CwavKind kind)
            {
                var vm = _w.ViewModel;
                _w._creditBefore = _w.Fingerprint(LibraryKind.SoundEffects);
                vm.CWavBytes = SoundMerge.Replace(vm.CWavBytes, ResourceLibrary.Instance.ReadAllBytes(item, item.DataFile), kind);
                vm.Flags.SoundEffect = true;
                _w.Credit(LibraryKind.SoundEffects, ThemeCredit.Of(item), false);
            }

            public void ApplyMusic(LibraryItem item)
            {
                ApplyMusicData(ResourceLibrary.Instance.ReadAllBytes(item, item.DataFile), item.BgmInfo, ThemeCredit.Of(item));
            }

            public void ApplyMusicData(byte[] bcstm, string bgmInfo, ThemeCredit credit = null)
            {
                var bytes = BcstmCompat.Normalize(bcstm);
                if (bytes.Length > BcstmConverter.MaxThemeBgmBytes)
                    throw new InvalidOperationException(string.Format(Localization.MainResources.Message_BgmTooLarge, bytes.Length / 1024, BcstmConverter.MaxThemeBgmBytes / 1024));
                _w.BgmBcstm = bytes;
                _w.BgmInfo = bgmInfo;
                _w.BgmDirty = true;
                _w.Credit(LibraryKind.Music, credit);
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
