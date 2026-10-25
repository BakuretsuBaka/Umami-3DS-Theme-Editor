// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Archive.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using SkiaSharp;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Previews;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        public ICommand ExportZipCommand { get; private set; }

        private void SetupArchive()
        {
            ExportZipCommand = new RelayCommand(ExportZip_Execute, CanExecute_ViewModelLoaded);
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

            try
            {
                byte[] bgm = BgmBcstm;
                var themeDir = ThemePath == null ? null : Path.GetDirectoryName(ThemePath);
                if (bgm == null && vm.Flags.BackgroundMusic && themeDir != null && File.Exists(Path.Combine(themeDir, BGM_FILE_NAME)))
                    bgm = File.ReadAllBytes(Path.Combine(themeDir, BGM_FILE_NAME));

                byte[] smdh;
                using (var ms = new MemoryStream())
                {
                    vm.Info.Save(ms);
                    smdh = ms.ToArray();
                }
                byte[] icon;
                using (var img = SKImage.FromBitmap(vm.Info.LargeIcon.Pixels.Sk))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                    icon = data.ToArray();

                var zip = ThemeArchive.Create(name, CompressTheme(vm), smdh, vm.Flags.BackgroundMusic ? bgm : null, icon,
                    PreviewExporter.RenderPng(vm, PreviewKind.Both));
                File.WriteAllBytes(path, zip);
                await Dialogs.Show(this, MainResources.Message_ZipExported, WINDOW_TITLE);
            }
            catch (Exception ex)
            {
                await Dialogs.Show(this, ex.Message, WINDOW_TITLE, MessageKind.Error);
            }
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
