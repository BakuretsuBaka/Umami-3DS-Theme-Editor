// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Images.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Graphics;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        private static readonly string[] VALID_IMAGE_EXT = {".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"};

        public ICommand ExportImageCommand { get; private set; }
        public ICommand RemoveImageCommand { get; private set; }
        public ICommand ReplaceImageCommand { get; private set; }
        public ICommand CopyResizeSMDHIconCommand { get; private set; }

        private bool CanExecute_ImageExists(TargetImage args)
        {
            if (!CanExecute_ViewModelLoaded())
                return false;
            switch (args)
            {
                case TargetImage.Top: return ViewModel.Textures.Top.Exists;
                case TargetImage.Bottom: return ViewModel.Textures.Bottom.Exists;
                case TargetImage.FileLarge: return ViewModel.Textures.FileLarge.Exists;
                case TargetImage.FileSmall: return ViewModel.Textures.FileSmall.Exists;
                case TargetImage.FolderOpen: return ViewModel.Textures.FolderOpen.Exists;
                case TargetImage.FolderClosed: return ViewModel.Textures.FolderClosed.Exists;
                case TargetImage.TopAlt: return ViewModel.Textures.TopAlt.Exists;
                case TargetImage.SmallIcon: return ViewModel.Info.SmallIcon.Exists;
                case TargetImage.LargeIcon: return ViewModel.Info.LargeIcon.Exists;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        private TextureViewModel TextureFor(TargetImage target)
        {
            switch (target)
            {
                case TargetImage.Top: return ViewModel.Textures.Top;
                case TargetImage.Bottom: return ViewModel.Textures.Bottom;
                case TargetImage.FileLarge: return ViewModel.Textures.FileLarge;
                case TargetImage.FileSmall: return ViewModel.Textures.FileSmall;
                case TargetImage.FolderOpen: return ViewModel.Textures.FolderOpen;
                case TargetImage.FolderClosed: return ViewModel.Textures.FolderClosed;
                case TargetImage.TopAlt: return ViewModel.Textures.TopAlt;
                case TargetImage.SmallIcon: return ViewModel.Info.SmallIcon;
                case TargetImage.LargeIcon: return ViewModel.Info.LargeIcon;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        private static bool IsIcon(TargetImage t)
        {
            return t == TargetImage.SmallIcon || t == TargetImage.LargeIcon;
        }

        // ---------------------------------------------------------------- Import

        private Task<LoadImageResults> DecodeImage(string file, TargetImage target)
        {
            BusyText = MainResources.Busy_OpeningImage;
            return Task.Run(() =>
            {
                var results = new LoadImageResults {Loaded = false, Target = target};
                try
                {
                    var bmp = PixelBuffer.Load(file);
                    results.OriginalWidth = bmp.Width;
                    results.OriginalHeight = bmp.Height;
                    results.Image = IsIcon(target) ? bmp : bmp.ResizeNextPot();
                    results.Loaded = true;
                }
                catch (Exception)
                {
                    // Unsupported or corrupt image
                }
                return results;
            });
        }

        private async Task<LoadImageResults> LoadImage_Execute(TargetImage targetImage)
        {
            BusyText = MainResources.Busy_PickingFile;
            var file = await Dialogs.OpenFile(this, MainResources.Menu_Image_Any_Load,
                Dialogs.FileType(MainResources.Filter_ImageFiles, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"));
            if (file == null)
                return new LoadImageResults {Loaded = false, Target = targetImage};
            return await DecodeImage(file, targetImage);
        }

        private async void LoadImage_PostExecute(LoadImageResults args)
        {
            IsBusy = false;
            if (!args.Loaded)
                return;

            var bmp = args.Image;
            var target = args.Target;
            var validSizes = _validImageSizes[target];
            var targetSize = validSizes.FirstOrDefault(pair => pair.X == bmp.Width && pair.Y == bmp.Height);

            if (targetSize.X == 0)
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{2}: {0}x{1}\n", bmp.Width, bmp.Height, MainResources.Message_InvalidImageSize);
                sb.AppendFormat("{0}:\n", MainResources.Message_InvalidImageSize_AllowedSizes);
                foreach (var pair in validSizes)
                    sb.AppendFormat("\t- {0}x{1}\n", pair.X, pair.Y);
                await Dialogs.Show(this, sb.ToString(), Title, MessageKind.Error);
                return;
            }

            var tex = TextureFor(target);
            tex.EncodeTexture(bmp, targetSize.Format);
            if (!IsIcon(target))
                tex.EdgeBleed(0, 0, args.OriginalWidth, args.OriginalHeight);
            else if (target == TargetImage.LargeIcon)
                ViewModel.Info.SmallIcon.EncodeTexture(bmp.Resize(24, 24), targetSize.Format);
        }

        // ---------------------------------------------------------------- Export

        private async Task<SaveImageResults> ExportImage_Execute(TargetImage targetImage)
        {
            var pixels = TextureFor(targetImage).Pixels;
            var results = new SaveImageResults {Saved = false, Target = targetImage};
            if (pixels == null)
                return results;

            BusyText = MainResources.Busy_PickingFile;
            results.Path = await Dialogs.SaveFile(this, MainResources.Menu_Image_Any_Export,
                targetImage.ToString().ToLower() + ".png", Dialogs.FileType(MainResources.Filter_PngFiles, ".png"));
            if (results.Path == null)
                return results;

            BusyText = MainResources.Busy_SavingImage;
            try
            {
                var path = results.Path;
                await Task.Run(() => pixels.SavePng(path));
                results.Saved = true;
            }
            catch (Exception)
            {
                // Could not write the file
            }
            return results;
        }

        private void RemoveImage_Execute(TargetImage args)
        {
            switch (args)
            {
                case TargetImage.SmallIcon:
                    ViewModel.Info.SmallIcon.EncodeTexture(LoadDefaultIcon(24), RawTexture.DataFormat.Bgr565);
                    break;
                case TargetImage.LargeIcon:
                    ViewModel.Info.LargeIcon.EncodeTexture(LoadDefaultIcon(48), RawTexture.DataFormat.Bgr565);
                    break;
                default:
                    TextureFor(args).ClearTexture();
                    break;
            }
        }

        private void SetupImageCommands()
        {
            RemoveImageCommand = new RelayCommand<TargetImage>(RemoveImage_Execute, CanExecute_ImageExists);
            ReplaceImageCommand = new RelayCommandAsync<TargetImage, LoadImageResults>(LoadImage_Execute,
                image => CanExecute_ViewModelLoaded(), image => PreExecute_SetBusy(), LoadImage_PostExecute);
            ExportImageCommand = new RelayCommandAsync<TargetImage, SaveImageResults>(ExportImage_Execute,
                CanExecute_ImageExists, image => PreExecute_SetBusy(), r => IsBusy = false);
            CopyResizeSMDHIconCommand = new RelayCommand<bool>(CopySMDHLargeToSmall_Execute, b => CanExecute_ViewModelLoaded());

            // Drop targets in the toolbar double as buttons that open the file picker
            WireDropTarget("DropTop", TargetImage.Top);
            WireDropTarget("DropTopAlt", TargetImage.TopAlt);
            WireDropTarget("DropBottom", TargetImage.Bottom);
            WireDropTarget("DropFileLarge", TargetImage.FileLarge);
            WireDropTarget("DropFileSmall", TargetImage.FileSmall);
            WireDropTarget("DropFolderClosed", TargetImage.FolderClosed);
            WireDropTarget("DropFolderOpen", TargetImage.FolderOpen);
        }

        private void WireDropTarget(string name, TargetImage target)
        {
            var button = this.FindControl<Button>(name);
            ToolTip.SetTip(button, (MainResources.ResourceManager.GetString("Menu_Image_" + target.ToString().Replace("TopAlt", "TopStatic")) ?? target.ToString()).Replace("_", ""));
            button.Click += (s, e) =>
            {
                if (ReplaceImageCommand.CanExecute(target))
                    ReplaceImageCommand.Execute(target);
            };
            DragDrop.SetAllowDrop(button, true);
            button.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                var files = e.Data.GetFiles()?.ToList();
                var file = files != null && files.Count == 1 ? files[0].TryGetLocalPath() : null;
                e.DragEffects = CanExecute_ViewModelLoaded() && file != null &&
                                VALID_IMAGE_EXT.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
                e.Handled = true;
            });
            button.AddHandler(DragDrop.DropEvent, async (s, e) =>
            {
                e.Handled = true;
                var files = e.Data.GetFiles()?.ToList();
                var file = files != null && files.Count > 0 ? files[0].TryGetLocalPath() : null;
                if (file == null || !CanExecute_ViewModelLoaded())
                    return;
                PreExecute_SetBusy();
                var result = await DecodeImage(file, target);
                LoadImage_PostExecute(result);
            });
        }

        // ---------------------------------------------------------------- SMDH icon helpers

        private void LargeToSmall_OnClick(object sender, RoutedEventArgs e)
        {
            CopyResizeSMDHIconCommand.Execute(true);
        }

        private void SmallToLarge_OnClick(object sender, RoutedEventArgs e)
        {
            CopyResizeSMDHIconCommand.Execute(false);
        }

        private void CopySMDHLargeToSmall_Execute(bool direction)
        {
            if (ViewModel == null)
                return;
            if (direction)
            {
                var sml = ViewModel.Info.LargeIcon.Pixels?.Resize(24, 24);
                if (sml != null)
                    ViewModel.Info.SmallIcon.EncodeTexture(sml, ViewModel.Info.SmallIcon.DataFormat);
            }
            else
            {
                var lrg = ViewModel.Info.SmallIcon.Pixels?.Resize(48, 48);
                if (lrg != null)
                    ViewModel.Info.LargeIcon.EncodeTexture(lrg, ViewModel.Info.LargeIcon.DataFormat);
            }
        }

        // ---------------------------------------------------------------- Result types

        private class SaveImageResults
        {
            public string Path;
            public bool Saved;
            public TargetImage Target;
        }

        private class LoadImageResults
        {
            public int OriginalWidth;
            public int OriginalHeight;
            public PixelBuffer Image;
            public bool Loaded;
            public TargetImage Target;
        }
    }
}
