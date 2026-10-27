// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.BGM.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ThemeEditor.App.Localization;
using ThemeEditor.Common.Audio;
using VGAudio.Formats;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        public ICommand ReloadBGMCommand { get; private set; }
        public ICommand ImportBgmCommand { get; private set; }
        public ICommand ExportBgmWavCommand { get; private set; }
        public ICommand ExportBgmBcstmCommand { get; private set; }
        public ICommand RemoveBgmCommand { get; private set; }

        /// <summary>The background music as a BCSTM file image (what ends up as BGM.BCSTM), or null.</summary>
        public byte[] BgmBcstm { get; private set; }

        /// <summary>True when <see cref="BgmBcstm" /> was imported and still has to be written next to the theme.</summary>
        public bool BgmDirty { get; private set; }

        private void ResetBgm()
        {
            BgmBcstm = null;
            BgmDirty = false;
            AudioPlayer.Instance.ClearAudioData();
        }

        private void SetupBgmCommands()
        {
            ReloadBGMCommand = new RelayCommandAsync<LoadBGMResults>(LoadBGM_Execute,
                CanExecute_LoadedFromFile, PreExecute_SetBusy, LoadBGM_PostExecute);
            ImportBgmCommand = new RelayCommandAsync<ImportBgmResults>(ImportBgm_Execute,
                CanExecute_ViewModelLoaded, PreExecute_SetBusy, ImportBgm_PostExecute);
            ExportBgmWavCommand = new RelayCommandAsync<bool>(() => ExportBgm(true),
                () => BgmBcstm != null, PreExecute_SetBusy, ExportBgm_PostExecute);
            ExportBgmBcstmCommand = new RelayCommandAsync<bool>(() => ExportBgm(false),
                () => BgmBcstm != null, PreExecute_SetBusy, ExportBgm_PostExecute);
            RemoveBgmCommand = new RelayCommand(RemoveBgm_Execute, () => BgmBcstm != null);
        }

        // ---------------------------------------------------------------- Reload from the theme folder

        private async Task<LoadBGMResults> LoadBGM_Execute()
        {
            BusyText = MainResources.Busy_LoadingBGM;
            var result = new LoadBGMResults {Loaded = false};
            var themeDir = Path.GetDirectoryName(ThemePath);
            if (themeDir == null)
                return result;
            var bgmFile = Path.Combine(themeDir, BGM_FILE_NAME);
            if (!File.Exists(bgmFile))
                return result;

            return await Task.Run(() =>
            {
                try
                {
                    var bytes = File.ReadAllBytes(bgmFile);
                    result.Audio = BcstmConverter.ReadBcstm(bytes);
                    result.Bcstm = bytes;
                    result.Loaded = true;
                }
                catch (Exception)
                {
                    // Not a valid BCSTM: treated as "no music"
                }
                return result;
            });
        }

        private async void LoadBGM_PostExecute(LoadBGMResults result)
        {
            IsBusy = false;
            if (result.Loaded)
            {
                BgmBcstm = result.Bcstm;
                BgmDirty = false;
                AudioPlayer.Instance.SetAudio(result.Audio);
                if (result.Bcstm.Length > BcstmConverter.MaxThemeBgmBytes)
                    await Dialogs.Show(this, string.Format(MainResources.Bgm_TooLargeWarning, result.Bcstm.Length / 1024, BcstmConverter.MaxThemeBgmBytes / 1024), WINDOW_TITLE, MessageKind.Warning);
                if (!ViewModel.Flags.BackgroundMusic)
                {
                    ViewModel.Flags.BackgroundMusic = true;
                    await Dialogs.Show(this, MainResources.Message_BackgroundMusicLoaded, WINDOW_TITLE, MessageKind.Warning);
                }
            }
            else
            {
                if (!BgmDirty)
                {
                    BgmBcstm = null;
                    AudioPlayer.Instance.ClearAudioData();
                }
                if (ViewModel.Flags.BackgroundMusic && BgmBcstm == null)
                {
                    ViewModel.Flags.BackgroundMusic = false;
                    await Dialogs.Show(this, MainResources.Message_NoBackgroundMusicOnLoad, WINDOW_TITLE, MessageKind.Warning);
                }
            }
            CommandManager.InvalidateRequerySuggested();
        }

        // ---------------------------------------------------------------- Import / convert

        private async Task<ImportBgmResults> ImportBgm_Execute()
        {
            var result = new ImportBgmResults {Imported = false};
            BusyText = MainResources.Busy_PickingFile;
            var file = await Dialogs.OpenFile(this, MainResources.Menu_Music_Import,
                Dialogs.FileType(MainResources.Filter_AudioFiles, BcstmConverter.ImportExtensions));
            if (file == null)
                return result;

            BusyText = MainResources.Busy_ConvertingAudio;
            AudioData audio;
            try
            {
                audio = await Task.Run(() => BcstmConverter.Load(file));
            }
            catch (Exception)
            {
                result.Error = MainResources.Message_BgmConvertFailed;
                return result;
            }

            var dialog = new BgmConvertWindow(BcstmConverter.GetInfo(audio), Path.GetFileName(file));
            IsBusy = false;
            await dialog.ShowDialog(this);
            if (dialog.Result == null)
                return result;

            IsBusy = true;
            BusyText = MainResources.Busy_ConvertingAudio;
            try
            {
                var options = dialog.Result;
                result.Bcstm = await Task.Run(() => BcstmConverter.Encode(audio, options));
                result.Audio = BcstmConverter.ReadBcstm(result.Bcstm);
                result.Info = BcstmConverter.GetInfo(result.Audio);
                result.Imported = true;
            }
            catch (Exception)
            {
                result.Error = MainResources.Message_BgmConvertFailed;
            }
            return result;
        }

        private async void ImportBgm_PostExecute(ImportBgmResults result)
        {
            IsBusy = false;
            if (result.Error != null)
            {
                await Dialogs.Show(this, result.Error, WINDOW_TITLE, MessageKind.Error);
                return;
            }
            if (!result.Imported)
                return;

            var kb = result.Bcstm.Length / 1024;
            if (result.Bcstm.Length > BcstmConverter.MaxThemeBgmBytes)
            {
                // The HOME menu will not play music over the limit: warn, and only continue when the user insists
                var proceed = await Dialogs.Confirm(this,
                    string.Format(MainResources.Message_BgmTooLargeConfirm, kb, BcstmConverter.MaxThemeBgmBytes / 1024),
                    WINDOW_TITLE);
                if (!proceed)
                    return;
            }

            BgmBcstm = result.Bcstm;
            BgmDirty = true;
            AudioPlayer.Instance.SetAudio(result.Audio);
            ViewModel.Flags.BackgroundMusic = true;
            CommandManager.InvalidateRequerySuggested();

            var summary = $"{result.Info.SampleRate} Hz, {result.Info.Channels} ch, {result.Info.Duration:0.0} s";
            await Dialogs.Show(this, string.Format(MainResources.Message_BgmImported, summary, kb), WINDOW_TITLE);
        }

        // ---------------------------------------------------------------- Export / remove

        private async Task<bool> ExportBgm(bool wav)
        {
            if (BgmBcstm == null)
            {
                await Dialogs.Show(this, MainResources.Message_NoBgm, WINDOW_TITLE, MessageKind.Warning);
                return false;
            }
            BusyText = MainResources.Busy_PickingFile;
            var path = wav
                ? await Dialogs.SaveFile(this, MainResources.Menu_Music_ExportWav, "BGM.wav", Dialogs.FileType(MainResources.Filter_WavFile, ".wav"))
                : await Dialogs.SaveFile(this, MainResources.Menu_Music_ExportBcstm, BGM_FILE_NAME, Dialogs.FileType(MainResources.Filter_BcstmFile, ".bcstm"));
            if (path == null)
                return false;

            BusyText = MainResources.Busy_ExportingAudio;
            var data = BgmBcstm;
            return await Task.Run(() =>
            {
                try
                {
                    File.WriteAllBytes(path, wav ? BcstmConverter.BcstmToWav(data) : data);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        private async void ExportBgm_PostExecute(bool ok)
        {
            IsBusy = false;
            if (ok)
                await Dialogs.Show(this, MainResources.Message_BgmExported, WINDOW_TITLE);
        }

        private void RemoveBgm_Execute()
        {
            BgmBcstm = null;
            BgmDirty = false;
            AudioPlayer.Instance.ClearAudioData();
            if (ViewModel != null)
                ViewModel.Flags.BackgroundMusic = false;
            CommandManager.InvalidateRequerySuggested();
        }

        private class LoadBGMResults
        {
            public AudioData Audio;
            public byte[] Bcstm;
            public bool Loaded;
        }

        private class ImportBgmResults
        {
            public AudioData Audio;
            public byte[] Bcstm;
            public string Error;
            public BcstmInfo Info;
            public bool Imported;
        }
    }
}
