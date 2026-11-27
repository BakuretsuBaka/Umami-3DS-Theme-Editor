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
        public ICommand OpenThemezerCommand { get; private set; }

        private void SetupArchive()
        {
            ExportZipCommandWrapper = GestureCommandWrapper.Create(
                new RelayCommand(ExportZip_Execute, () => CanExecute_ViewModelLoaded() && !IsBusy), Key.E);
            UploadThemezerCommand = new RelayCommand(UploadThemezer_Execute, () => CanExecute_ViewModelLoaded() && !IsBusy);
            OpenThemezerCommand = new RelayCommand(OpenThemezer_Execute, () => !IsBusy);
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

        /// <summary>Asks which theme to open from Themezer, 3DSThem.es or Theme Plaza, downloads it and opens it.</summary>
        private async void OpenThemezer_Execute()
        {
            var dialog = new WebThemeWindow();
            await dialog.ShowDialog(this);
            var theme = dialog.Result;
            if (theme == null)
                return;
            string zip;
            IsBusy = true;
            BusyText = string.Format(MainResources.Web_Downloading, WebThemes.SiteName(theme.Source));
            try
            {
                var folder = Path.Combine(TemporaryRoot, Guid.NewGuid().ToString("N"));
                zip = theme.Source == WebThemeSource.Themezer
                    ? await ThemezerClient.Shared.DownloadTheme(theme.Id, folder)
                    : await WebThemes.Download(theme, folder);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                await Dialogs.Show(this, ex is ThemezerException && ((ThemezerException) ex).Code == "NOT_FOUND"
                    ? ex.Message : string.Format(MainResources.Web_Error, ex.Message), WINDOW_TITLE, MessageKind.Error);
                return;
            }
            IsBusy = false;
            // Opens like any theme ZIP: unpacked into a folder next to it, then loaded
            LoadThemeCommandWrapper.Command.Execute(zip);
        }

        // ---- Themes opened from the web live in a temporary folder until they are saved somewhere

        /// <summary>Where themes opened from the web are unpacked (a folder per download).</summary>
        internal static string TemporaryRoot => Path.Combine(Path.GetTempPath(), "UmamiThemeEditor");

        /// <summary>Whether <paramref name="path" /> lies in a temporary folder of <see cref="TemporaryRoot" />.</summary>
        internal static bool IsTemporary(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            var root = Path.GetFullPath(TemporaryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal);
        }

        /// <summary>Deletes the temporary folder <paramref name="path" /> is in (the folder of one download).</summary>
        internal static void DeleteTemporary(string path)
        {
            if (!IsTemporary(path))
                return;
            var root = Path.GetFullPath(TemporaryRoot);
            var dir = Path.GetFullPath(path);
            while (Path.GetDirectoryName(dir) is string parent && !string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                dir = parent;
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // Still in use: cleared on a later start
            }
        }

        /// <summary>Removes temporary folders that an earlier run left behind (e.g. after a crash).</summary>
        internal static void CleanUpTemporary(TimeSpan olderThan)
        {
            try
            {
                if (!Directory.Exists(TemporaryRoot))
                    return;
                foreach (var dir in Directory.GetDirectories(TemporaryRoot))
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > olderThan)
                        try { Directory.Delete(dir, true); } catch (Exception) { }
            }
            catch (Exception)
            {
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
            var window = new ThemezerUploadWindow(ThemezerClient.Shared, CredentialStore.System, vm.Info.ShortDescription, vm.Info.LongDescription,
                hasBgm, () => BuildPackage(vm), bgmInfo: BgmInfo);
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
