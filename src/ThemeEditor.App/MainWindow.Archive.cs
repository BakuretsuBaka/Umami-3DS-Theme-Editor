// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Archive.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using SkiaSharp;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        public ICommand ExportZipCommand => ExportZipCommandWrapper.Command;
        public GestureCommandWrapper ExportZipCommandWrapper { get; private set; }
        public ICommand UploadThemezerCommand { get; private set; }

        private void SetupArchive()
        {
            ExportZipCommandWrapper = GestureCommandWrapper.Create(
                new RelayCommand(ExportZip_Execute, () => CanExecute_ViewModelLoaded() && !IsBusy), Key.E);
            UploadThemezerCommand = new RelayCommand(UploadThemezer_Execute, () => CanExecute_ViewModelLoaded() && !IsBusy);
        }

        private static bool IsZip(string path)
        {
            return ".zip".Equals(Path.GetExtension(path ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Saves a Theme Plaza / Themezer ready package with a fresh Umami preview and the icon.</summary>
        private async void ExportZip_Execute()
        {
            var vm = ViewModel;
            if (vm == null)
                return;
            var name = vm.Info.ShortDescription;
            if (string.IsNullOrWhiteSpace(name))
                name = "theme";
            var safe = Regex.Replace(name, "[^A-Za-z0-9 _.-]+", "_").Trim();
            var path = await Dialogs.SaveFile(this, MainResources.Menu_File_ExportZip, safe + ".zip",
                Dialogs.FileType("ZIP", ".zip"));
            if (path == null)
                return;

            IsBusy = true;
            BusyText = MainResources.Busy_SavingTheme;
            try
            {
                var package = await BuildPackage(vm);
                await Task.Run(() => File.WriteAllBytes(path, ThemeArchive.Create(name, package.Body, package.Smdh,
                    package.Bgm, package.Icon, package.Preview)));
                IsBusy = false;
                await Dialogs.Show(this, MainResources.Message_ZipExported, WINDOW_TITLE);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                await Dialogs.Show(this, ex.Message, WINDOW_TITLE, MessageKind.Error);
            }
        }

        /// <summary>Opens the Themezer upload dialog for the open theme.</summary>
        private async void UploadThemezer_Execute()
        {
            var vm = ViewModel;
            if (vm == null)
                return;
            var hasBgm = vm.Flags.BackgroundMusic && (BgmBcstm != null || (ThemePath != null
                && File.Exists(Path.Combine(Path.GetDirectoryName(ThemePath) ?? ".", BGM_FILE_NAME))));
            var window = new ThemezerUploadWindow(ThemezerClient.Shared, vm.Info.ShortDescription, vm.Info.LongDescription,
                hasBgm, () => BuildPackage(vm));
            await window.ShowDialog(this);
        }

        /// <summary>The files of a theme package (as in the theme ZIP); <see cref="Bgm" /> is null without music.</summary>
        internal sealed class ThemePackage
        {
            public byte[] Body;
            public byte[] Smdh;
            public byte[] Bgm;
            public byte[] Icon;
            public byte[] Preview;
        }

        /// <summary>Compresses the theme and renders its icon and a fresh Umami preview, for the ZIP or an upload.</summary>
        internal async Task<ThemePackage> BuildPackage(ThemeViewModel vm)
        {
            FlushPendingSnapshot();
            byte[] bgm = BgmBcstm;
            var themeDir = ThemePath == null ? null : Path.GetDirectoryName(ThemePath);
            if (bgm == null && vm.Flags.BackgroundMusic && themeDir != null && File.Exists(Path.Combine(themeDir, BGM_FILE_NAME)))
                bgm = File.ReadAllBytes(Path.Combine(themeDir, BGM_FILE_NAME));

            var package = new ThemePackage { Bgm = vm.Flags.BackgroundMusic ? bgm : null };
            using (var ms = new MemoryStream())
            {
                vm.Info.Save(ms);
                package.Smdh = ms.ToArray();
            }
            using (var img = SKImage.FromBitmap(vm.Info.LargeIcon.Pixels.Sk))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                package.Icon = data.ToArray();

            // The preview has to be rendered on the UI thread; compressing the theme takes a moment, so it does not
            package.Preview = PreviewExporter.RenderPng(vm, PreviewKind.Both);
            package.Body = await Task.Run(() => CompressTheme(vm));
            return package;
        }

        /// <summary>
        ///     Unpacks a theme ZIP next to itself (into a new folder) and returns the path of the extracted body_LZ.bin,
        ///     or null (after telling the user) when the ZIP holds no theme.
        /// </summary>
        private async Task<string> ExtractThemeZip(string zipPath)
        {
            var contents = await Task.Run(() =>
            {
                try
                {
                    return ThemeArchive.Read(File.ReadAllBytes(zipPath));
                }
                catch (Exception)
                {
                    return null;
                }
            });
            if (contents == null)
            {
                await Dialogs.Show(this, MainResources.Message_ZipInvalid, WINDOW_TITLE, MessageKind.Error);
                return null;
            }
            try
            {
                return ThemeArchive.ExtractTo(contents, ThemeArchive.UniqueFolder(zipPath));
            }
            catch (Exception ex)
            {
                await Dialogs.Show(this, ex.Message, WINDOW_TITLE, MessageKind.Error);
                return null;
            }
        }
    }
}
