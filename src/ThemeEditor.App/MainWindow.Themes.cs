// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Themes.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Compression;
using ThemeEditor.Common.Graphics;
using ThemeEditor.Common.SMDH;
using ThemeEditor.Common.Themes;
using CommonTheme = ThemeEditor.Common.Themes.Theme;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        private const string BGM_FILE_NAME = "BGM.BCSTM";

        private string _themePath;

        public GestureCommandWrapper LoadThemeCommandWrapper { get; private set; }
        public GestureCommandWrapper NewThemeCommandWrapper { get; private set; }
        public GestureCommandWrapper SaveAsThemeCommandWrapper { get; private set; }
        public GestureCommandWrapper SaveThemeCommandWrapper { get; private set; }

        public ICommand SendThemeCHMM2Command { get; private set; }
        public ICommand ExportMetadataCommand { get; private set; }
        public ICommand ImportMetadataCommand { get; private set; }

        public string ThemePath
        {
            get { return _themePath; }
            set
            {
                if (value == _themePath)
                    return;
                _themePath = value;
                OnPropertyChanged(nameof(ThemePath));
                Title = value == null
                    ? WINDOW_TITLE
                    : WINDOW_TITLE + " - " + Path.GetFileName(Path.GetDirectoryName(value) ?? string.Empty) + Path.DirectorySeparatorChar + Path.GetFileName(value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool CanExecute_LoadedFromFile()
        {
            return ThemePath != null;
        }

        // ---------------------------------------------------------------- Loading

        private async Task<LoadThemeResults> LoadNullTheme_Execute()
        {
            BusyText = MainResources.Busy_LoadingTheme;
            var result = new LoadThemeResults { Loaded = false, Path = null };
            try
            {
                byte[] data;
                if (File.Exists("default_body_lz.bin"))
                    data = File.ReadAllBytes("default_body_lz.bin");
                else
                    using (var s = AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/empty_body_lz.bin")))
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        data = ms.ToArray();
                    }

                result.Theme = await Task.Run(() => ReadTheme(data));
                result.Loaded = true;
            }
            catch (Exception)
            {
                // Falls through with Loaded = false
            }
            return result;
        }

        private static CommonTheme ReadTheme(byte[] compressed)
        {
            using (var fs = new MemoryStream(compressed))
            using (var ms = new MemoryStream())
            {
                LZ11.Decompress(fs, fs.Length, ms);
                ms.Position = 0;
                return CommonTheme.Read(ms);
            }
        }

        private async Task<LoadThemeResults> LoadTheme_Execute(string path)
        {
            var result = new LoadThemeResults { Loaded = false, Path = path };

            if (string.IsNullOrEmpty(result.Path))
            {
                BusyText = MainResources.Busy_PickingFile;
                result.Path = await Dialogs.OpenFile(this, MainResources.Menu_File_Open,
                    Dialogs.FileType(MainResources.Filter_ThemeFile, ".bin"));
            }
            if (string.IsNullOrEmpty(result.Path))
                return result;

            BusyText = MainResources.Busy_LoadingTheme;
            var themePath = result.Path;
            var parsed = await Task.Run(() =>
            {
                var r = new LoadThemeResults { Path = themePath };
                var themeDir = Path.GetDirectoryName(themePath) ?? ".";
                try
                {
                    r.Theme = ReadTheme(File.ReadAllBytes(themePath));
                    r.Loaded = true;
                }
                catch (Exception)
                {
                    return r;
                }

                var smdhPath = Path.Combine(themeDir, "info.smdh");
                if (File.Exists(smdhPath))
                {
                    try
                    {
                        using (var fs = File.OpenRead(smdhPath))
                            r.Info = SMDH.Read(fs);
                    }
                    catch (Exception)
                    {
                        r.Info = null;
                    }
                }
                return r;
            });

            result.Loaded = parsed.Loaded;
            result.Theme = parsed.Theme;
            result.Info = parsed.Info;
            if (!result.Loaded)
                await Dialogs.Show(this, MainResources.Message_InvalidTheme, WINDOW_TITLE, MessageKind.Error);
            return result;
        }

        private void LoadTheme_PostExecute(LoadThemeResults result)
        {
            if (result.Loaded)
            {
                ViewModel = new ThemeViewModel(result.Theme, result.Info);
                UpdateRecentsList(result.Path);

                if (result.Info == null)
                {
                    ViewModel.Info.LargeIcon.EncodeTexture(LoadDefaultIcon(48), RawTexture.DataFormat.Bgr565);
                    ViewModel.Info.SmallIcon.EncodeTexture(LoadDefaultIcon(24), RawTexture.DataFormat.Bgr565);
                }

                ThemePath = result.Path;
                ResetBgm();
                if (ReloadBGMCommand.CanExecute(null))
                    ReloadBGMCommand.Execute(null);
            }
            IsBusy = false;
        }

        private static PixelBuffer LoadDefaultIcon(int size)
        {
            using (var s = AssetLoader.Open(new Uri($"avares://UmamiThemeEditor/Resources/Icons/app_icn_{size}.png")))
                return PixelBuffer.Load(s);
        }

        // ---------------------------------------------------------------- Saving

        private async Task<SaveThemeResults> SaveTheme_Execute(string path)
        {
            var viewModel = ViewModel;
            var result = new SaveThemeResults { Saved = false, Path = path };

            if (string.IsNullOrEmpty(result.Path))
            {
                BusyText = MainResources.Busy_PickingFile;
                result.Path = await Dialogs.SaveFile(this, MainResources.Menu_File_SaveAs, "body_LZ.bin",
                    Dialogs.FileType(MainResources.Filter_ThemeFile, ".bin"));
                if (string.IsNullOrEmpty(result.Path))
                    return result;
            }

            BusyText = MainResources.Busy_SavingTheme;
            var target = result.Path;
            result.Saved = await Task.Run(() =>
            {
                try
                {
                    var compressed = CompressTheme(viewModel);
                    File.WriteAllBytes(target, compressed);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
            return result;
        }

        private static byte[] CompressTheme(ThemeViewModel viewModel)
        {
            using (var decBuffer = new MemoryStream())
            using (var comBuffer = new MemoryStream())
            {
                viewModel.Save(decBuffer);
                decBuffer.Position = 0;
                LZ11.Compress(decBuffer, decBuffer.Length, comBuffer, true);
                return comBuffer.ToArray();
            }
        }

        private async void SaveTheme_PostExecute(SaveThemeResults result)
        {
            if (result.Saved)
            {
                ThemePath = result.Path;
                var themeDir = Path.GetDirectoryName(result.Path) ?? ".";
                try
                {
                    File.WriteAllBytes(Path.Combine(themeDir, "preview.png"), PreviewExporter.RenderPng(ViewModel, PreviewKind.Both));
                    using (var stream = File.Create(Path.Combine(themeDir, "info.smdh")))
                        ViewModel.Info.Save(stream);

                    var bgmPath = Path.Combine(themeDir, BGM_FILE_NAME);
                    if (BgmDirty && BgmBcstm != null)
                    {
                        File.WriteAllBytes(bgmPath, BgmBcstm);
                        BgmDirty = false;
                    }

                    IsBusy = false;
                    await Dialogs.Show(this, MainResources.Message_ThemeSaved, WINDOW_TITLE);

                    if (ViewModel.Flags.BackgroundMusic && !File.Exists(bgmPath))
                        await Dialogs.Show(this, MainResources.Message_NoBackgroundMusicOnSave, WINDOW_TITLE, MessageKind.Warning);
                }
                catch (Exception ex)
                {
                    IsBusy = false;
                    await Dialogs.Show(this, ex.Message, WINDOW_TITLE, MessageKind.Error);
                }
            }
            IsBusy = false;
        }

        // ---------------------------------------------------------------- Zip for CHMM2

        private ZipThemeResults ZipTheme(string bgmPath)
        {
            var viewModel = ViewModel;
            var previewPng = PreviewExporter.RenderPng(viewModel, PreviewKind.Both);
            var bgmData = BgmBcstm;
            var results = new ZipThemeResults { Saved = false, BGMPath = bgmPath, Data = null };

            var themeName = viewModel.Info.ShortDescription;
            if (string.IsNullOrEmpty(themeName))
            {
                var bytes = new byte[4];
                new Random().NextBytes(bytes);
                themeName = "UmamiThemeEditor-" + BitConverter.ToString(bytes);
            }
            themeName = Regex.Replace(themeName, "[^ -~]+", "_");

            using (var ms = new MemoryStream())
            {
                using (var arch = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var bodyLz = arch.CreateEntry($"{themeName}/body_LZ.bin");
                    using (var tgt = bodyLz.Open())
                    {
                        var data = CompressTheme(viewModel);
                        tgt.Write(data, 0, data.Length);
                    }

                    var info = arch.CreateEntry($"{themeName}/info.smdh");
                    using (var tgt = info.Open())
                        viewModel.Info.Save(tgt);

                    var preview = arch.CreateEntry($"{themeName}/preview.png");
                    using (var tgt = preview.Open())
                        tgt.Write(previewPng, 0, previewPng.Length);

                    if (viewModel.Flags.BackgroundMusic)
                    {
                        byte[] bgm = bgmData;
                        if (bgm == null && File.Exists(results.BGMPath))
                            bgm = File.ReadAllBytes(results.BGMPath);
                        if (bgm != null)
                        {
                            var entry = arch.CreateEntry($"{themeName}/BGM.BCSTM");
                            using (var tgt = entry.Open())
                                tgt.Write(bgm, 0, bgm.Length);
                        }
                    }
                }
                results.Data = ms.ToArray();
                results.Saved = true;
            }
            return results;
        }

        // ---------------------------------------------------------------- Send to CHMM2

        private async Task<SendThemeResults> SendTheme_Execute(string ip)
        {
            var themeDir = ThemePath == null ? "." : Path.GetDirectoryName(ThemePath) ?? ".";
            var bgmPath = Path.Combine(themeDir, BGM_FILE_NAME);
            var result = new SendThemeResults { IP = ip, Sent = false };

            IPAddress addr;
            if (!IPAddress.TryParse(ip, out addr))
                return result;

            // Rendering the preview needs the UI thread; zip it first
            result.Zip = ZipTheme(bgmPath);

            var sendingTheme = MainResources.Busy_Sender_SendingTheme;
            var connecting = MainResources.Busy_Sender_Connecting;
            var connectionError = MainResources.Busy_Sender_ConnectionError;
            var maxTries = Settings.Default.SocketMaxTries;

            return await Task.Run(() =>
            {
                var endPoint = new IPEndPoint(addr, 5000);
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var tries = 0;
                    var connected = false;
                    while (!connected)
                    {
                        try
                        {
                            BusyText = connecting + $" [{++tries}/{maxTries}]";
                            socket.Connect(endPoint);
                            connected = true;
                        }
                        catch (SocketException)
                        {
                            if (tries >= maxTries)
                                return result;
                        }
                    }

                    BusyText = sendingTheme;
                    var socketBuffer = new byte[16];

                    socket.Receive(socketBuffer, 11, SocketFlags.None);
                    if (Encoding.ASCII.GetString(socketBuffer, 0, 11) != "YATA SENDER")
                    {
                        BusyText = connectionError;
                        return result;
                    }

                    var toWrite = result.Zip.Data.Length;
                    var offset = 0;
                    var fLen = (float) result.Zip.Data.Length / 100;
                    var sw = Stopwatch.StartNew();
                    float averageSample = 0;
                    var lastOffset = 0;
                    while (toWrite > 0)
                    {
                        var step = Math.Min(toWrite, 8192);
                        socket.Send(result.Zip.Data, offset, step, 0);
                        toWrite -= step;
                        offset += step;

                        if (sw.ElapsedMilliseconds > 100)
                        {
                            float trans = sw.ElapsedMilliseconds;
                            var delta = offset - lastOffset;
                            lastOffset = offset;
                            averageSample = averageSample * 0.75f + delta / trans * 0.25f;
                            BusyText = $"Sending: {offset / fLen:f2}%" + Environment.NewLine +
                                       $"Rate: {(int) (averageSample / 1.024)}KB/s";
                            sw.Restart();
                        }
                    }

                    socket.Receive(socketBuffer, 9, SocketFlags.None);
                    if (Encoding.ASCII.GetString(socketBuffer, 0, 9) != "YATA TERM")
                    {
                        BusyText = connectionError;
                        return result;
                    }
                    result.Sent = true;
                    return result;
                }
            });
        }

        private async void SendTheme_PostExecute(SendThemeResults obj)
        {
            IsBusy = false;
            if (obj.Sent)
                await Dialogs.Show(this, MainResources.Message_ThemeSent, WINDOW_TITLE);
        }

        // ---------------------------------------------------------------- Commands

        private void SetupThemeCommands()
        {
            var loadCommand = new RelayCommandAsync<string, LoadThemeResults>(LoadTheme_Execute,
                null, str => PreExecute_SetBusy(), LoadTheme_PostExecute);

            var saveAsCommand = new RelayCommandAsync<string, SaveThemeResults>(p => SaveTheme_Execute(null),
                str => CanExecute_ViewModelLoaded(), str => PreExecute_SetBusy(), SaveTheme_PostExecute);

            var saveCommand = new RelayCommandAsync<SaveThemeResults>(() => SaveTheme_Execute(ThemePath),
                CanExecute_ViewModelLoaded, PreExecute_SetBusy, SaveTheme_PostExecute);

            var newCommand = new RelayCommandAsync<LoadThemeResults>(LoadNullTheme_Execute,
                null, PreExecute_SetBusy, LoadTheme_PostExecute);

            var sendCommand = new RelayCommandAsync<string, SendThemeResults>(SendTheme_Prompt,
                str => CanExecute_ViewModelLoaded(), str => PreExecute_SetBusy(), SendTheme_PostExecute);

            LoadThemeCommandWrapper = GestureCommandWrapper.Create(loadCommand, Key.O);
            SaveThemeCommandWrapper = GestureCommandWrapper.Create(saveCommand, Key.S);
            SaveAsThemeCommandWrapper = GestureCommandWrapper.Create(saveAsCommand, Key.S, true);
            NewThemeCommandWrapper = GestureCommandWrapper.Create(newCommand, Key.N);
            SendThemeCHMM2Command = sendCommand;

            ExportMetadataCommand = new RelayCommandAsync<string, SaveMetadataResults>(SaveMetadata_Execute,
                str => CanExecute_ViewModelLoaded(), str => PreExecute_SetBusy(), r => IsBusy = false);

            ImportMetadataCommand = new RelayCommandAsync<string, LoadMetadataResults>(LoadMetadata_Execute,
                str => CanExecute_ViewModelLoaded(), str => PreExecute_SetBusy(), ImportMetadata_PostExecute);
        }

        private async Task<SendThemeResults> SendTheme_Prompt(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                ip = await Dialogs.Prompt(this, WINDOW_TITLE, MainResources.Menu_SendTo_Prompt, Settings.Default.SocketIP);
            if (string.IsNullOrEmpty(ip))
                return new SendThemeResults { Sent = false };
            Settings.Default.SocketIP = ip;
            return await SendTheme_Execute(ip);
        }

        // ---------------------------------------------------------------- Metadata

        private void ImportMetadata_PostExecute(LoadMetadataResults r)
        {
            if (r.Loaded)
            {
                ViewModel.Rules.Pause();
                ViewModel.SetMetadata(r.Data);
                ViewModel.Rules.Apply(ViewModel.Flags);
                ViewModel.Rules.Apply(ViewModel.Colors);
                ViewModel.Rules.Resume();
            }
            IsBusy = false;
        }

        private async Task<LoadMetadataResults> LoadMetadata_Execute(string rootProps)
        {
            BusyText = MainResources.Busy_PickingFile;
            var result = new LoadMetadataResults { Loaded = false, Data = new Dictionary<string, string>() };

            var dataFile = await Dialogs.OpenFile(this, MainResources.Menu_Metadata_Import,
                Dialogs.FileType(MainResources.Filter_MetadataFile, ".meta"));
            if (dataFile == null)
                return result;

            BusyText = MainResources.Busy_LoadingTheme;
            try
            {
                foreach (var line in File.ReadAllLines(dataFile))
                {
                    if (line.StartsWith(";") || string.IsNullOrWhiteSpace(line))
                        continue;
                    var idx = line.IndexOf(':');
                    if (idx < 0)
                        continue;
                    result.Data[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }
                result.Loaded = true;
            }
            catch (Exception)
            {
                // Unreadable file
            }
            return result;
        }

        private async Task<SaveMetadataResults> SaveMetadata_Execute(string rootProps)
        {
            var viewModel = ViewModel;
            var splProps = (rootProps ?? string.Empty).Split('.');
            BusyText = MainResources.Busy_PickingFile;
            var result = new SaveMetadataResults { Saved = false };

            result.Path = await Dialogs.SaveFile(this, MainResources.Menu_Metadata, (splProps.LastOrDefault()?.ToLower() ?? "metadata") + ".meta",
                Dialogs.FileType(MainResources.Filter_MetadataFile, ".meta"));
            if (result.Path == null)
                return result;

            BusyText = MainResources.Busy_SavingTheme;
            try
            {
                var data = viewModel.GetMetadata(splProps);
                var maxl = data.Keys.Max(s => s.Length);
                using (var fs = File.CreateText(result.Path))
                    foreach (var pair in data)
                        fs.WriteLine("{0}: {1}", pair.Key.PadRight(maxl), pair.Value);
                result.Saved = true;
            }
            catch (Exception)
            {
                // Could not write
            }
            return result;
        }

        // ---------------------------------------------------------------- Drag and drop

        private void SetupDragDrop()
        {
            var tabs = this.FindControl<TabControl>("EditorTabs");
            DragDrop.SetAllowDrop(tabs, true);
            tabs.AddHandler(DragDrop.DragOverEvent, DragTheme_Execute);
            tabs.AddHandler(DragDrop.DropEvent, DropTheme_Execute);
        }

        private static string FirstDroppedFile(DragEventArgs e)
        {
            var files = e.Data.GetFiles()?.ToList();
            if (files == null || files.Count != 1)
                return null;
            return files[0].TryGetLocalPath();
        }

        private void DragTheme_Execute(object sender, DragEventArgs e)
        {
            var file = FirstDroppedFile(e);
            e.DragEffects = file != null && ".bin".Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropTheme_Execute(object sender, DragEventArgs e)
        {
            var file = FirstDroppedFile(e);
            e.Handled = true;
            if (file != null && ".bin".Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase))
                LoadThemeCommandWrapper.Command.Execute(file);
        }

        // ---------------------------------------------------------------- Recent files

        private void UpdateRecentsList(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            var idx = RecentsList.IndexOf(path);
            if (idx != -1)
                RecentsList.RemoveAt(idx);
            RecentsList.Insert(0, path);
            while (RecentsList.Count > 10)
                RecentsList.RemoveAt(10);
            RebuildRecentMenu();
        }

        // ---------------------------------------------------------------- Result types

        private class LoadThemeResults
        {
            public SMDH Info;
            public bool Loaded;
            public string Path;
            public CommonTheme Theme;
        }

        private class ZipThemeResults
        {
            public string BGMPath;
            public byte[] Data;
            public bool Saved;
        }

        private class SendThemeResults
        {
            public string IP;
            public bool Sent;
            public ZipThemeResults Zip;
        }

        private class SaveThemeResults
        {
            public string Path;
            public bool Saved;
        }

        private class LoadMetadataResults
        {
            public Dictionary<string, string> Data;
            public bool Loaded;
        }

        private class SaveMetadataResults
        {
            public string Path;
            public bool Saved;
        }
    }
}
