// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Update.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ThemeEditor.App.Localization;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        /// <summary>
        ///     Tells about a newer release and, where this install allows it, downloads and installs it on request: the
        ///     editor then quits and the installer starts it again. Portable and Linux copies get the download page.
        /// </summary>
        /// <param name="ask">False when the user already chose to update (from the About window).</param>
        internal async Task OfferUpdate(bool ask = true)
        {
            var release = Update.Latest;
            if (release == null)
                return;
            var kind = Update.Kind;
            var asset = Update.AssetFor(release, kind, RuntimeInformation.ProcessArchitecture);
            var text = string.Format(MainResources.Update_Available, release.Version, Update.AppVersion);
            if (asset == null)
            {
                if (await Dialogs.Choose(this, text + "\n\n" + MainResources.Update_Manual, WINDOW_TITLE, MessageKind.Information,
                        MainResources.Update_OpenPage, MainResources.Update_Later) == 0)
                    Update.OpenUrl(Update.ReleasesUrl);
                return;
            }
            if (ask && await Dialogs.Choose(this, text, WINDOW_TITLE, MessageKind.Information,
                    MainResources.Update_Now, MainResources.Update_Later) != 0)
                return;
            // The editor quits for the update, so unsaved work is dealt with first
            if (!await ConfirmDiscardChanges())
                return;

            IsBusy = true;
            BusyText = string.Format(MainResources.Update_Downloading, 0);
            string download;
            try
            {
                var progress = new Progress<double>(p => BusyText = string.Format(MainResources.Update_Downloading, (int) (p * 100)));
                download = await Update.Download(asset, Path.Combine(TemporaryRoot, "update-" + release.Version), progress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                await Dialogs.Show(this, string.Format(MainResources.Update_Failed, ex.Message), WINDOW_TITLE, MessageKind.Error);
                return;
            }
            IsBusy = false;

            bool installing;
            try
            {
                installing = Update.Install(download, kind);
            }
            catch (Exception ex)
            {
                await Dialogs.Show(this, string.Format(MainResources.Update_Failed, ex.Message), WINDOW_TITLE, MessageKind.Error);
                return;
            }
            if (!installing)
            {
                // The app folder cannot be written: the .dmg is open, the user drags the app over
                await Dialogs.Show(this, MainResources.Update_DragApp, WINDOW_TITLE);
                return;
            }
            _closeConfirmed = true;
            Close();
        }
    }
}
