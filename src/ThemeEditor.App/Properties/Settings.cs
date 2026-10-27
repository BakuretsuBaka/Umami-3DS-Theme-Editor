// --------------------------------------------------
// Umami 3DS Theme Editor - Settings.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ThemeEditor.App.Properties
{
    /// <summary>
    ///     User settings, persisted as JSON in the per-user application data directory.
    ///     Replaces the Windows-only System.Configuration based settings of the original editor.
    /// </summary>
    public sealed class Settings : INotifyPropertyChanged
    {
        private static Settings _default;

        private bool _checkUpdatesOnStartup = true;
        private bool _expResampleWav;
        private string _language = string.Empty;
        private string[] _mruList = new string[0];
        private bool _previewAnimationToggle;
        private bool _previewCloseToggle = true;
        private bool _previewFolderCartEnable = true;
        private bool _previewModeToggle;
        private bool _previewPressedToggle;
        private bool _previewSoundToggle = true;
        private string _lastDirectory = string.Empty;
        private double _windowWidth;
        private double _windowHeight;
        private bool _windowMaximized;
        private string _socketIp = "127.0.0.1";
        private int _socketMaxTries = 5;
        private string _bgmCodec = "DspAdpcm";
        private string _libraryShow = "All";
        private bool _libraryHideBundled;
        private bool _bgmMono;
        private int _bgmSampleRate;
        private bool _bgmLoop;

        public static Settings Default => _default ?? (_default = Load());

        public static string SettingsDirectory
        {
            get
            {
                var overrideDir = Environment.GetEnvironmentVariable("UMAMI_THEME_EDITOR_DATA");
                if (!string.IsNullOrEmpty(overrideDir))
                    return overrideDir;
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = AppContext.BaseDirectory;
                return Path.Combine(baseDir, "UmamiThemeEditor");
            }
        }

        public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

        // ---- General

        public bool CheckUpdatesOnStartup
        {
            get { return _checkUpdatesOnStartup; }
            set { Set(ref _checkUpdatesOnStartup, value); }
        }

        /// <summary>UI language as a culture name ("de", "pt-BR"...). Empty follows the operating system.</summary>
        public string Language
        {
            get { return _language; }
            set { Set(ref _language, value ?? string.Empty); }
        }

        public bool EXP_ResampleWav
        {
            get { return _expResampleWav; }
            set { Set(ref _expResampleWav, value); }
        }

        public string[] MRU_List
        {
            get { return _mruList; }
            set { Set(ref _mruList, value ?? new string[0]); }
        }

        // ---- Preview toggles

        public bool Preview_AnimationToggle
        {
            get { return _previewAnimationToggle; }
            set { Set(ref _previewAnimationToggle, value); }
        }

        public bool Preview_ModeToggle
        {
            get { return _previewModeToggle; }
            set { Set(ref _previewModeToggle, value); }
        }

        public bool Preview_PressedToggle
        {
            get { return _previewPressedToggle; }
            set { Set(ref _previewPressedToggle, value); }
        }

        /// <summary>Folder the file dialogs start in (the one of the last file opened or saved).</summary>
        public string LastDirectory
        {
            get { return _lastDirectory; }
            set { Set(ref _lastDirectory, value ?? string.Empty); }
        }

        public double Window_Width
        {
            get { return _windowWidth; }
            set { Set(ref _windowWidth, value); }
        }

        /// <summary>Which resource library items are listed: "All", "Presets" (shipped with the editor) or "Mine" (imported).</summary>
        public string Library_Show
        {
            get { return _libraryShow; }
            set { Set(ref _libraryShow, string.IsNullOrEmpty(value) ? "All" : value); }
        }

        /// <summary>The shipped presets were "deleted" where they cannot be removed from disk (inside the macOS app): they are not loaded.</summary>
        public bool Library_HideBundled
        {
            get { return _libraryHideBundled; }
            set { Set(ref _libraryHideBundled, value); }
        }

        public double Window_Height
        {
            get { return _windowHeight; }
            set { Set(ref _windowHeight, value); }
        }

        public bool Window_Maximized
        {
            get { return _windowMaximized; }
            set { Set(ref _windowMaximized, value); }
        }

        /// <summary>Plays the theme's sound effects when the interactive preview is used.</summary>
        public bool Preview_SoundToggle
        {
            get { return _previewSoundToggle; }
            set { Set(ref _previewSoundToggle, value); }
        }

        public bool Preview_CloseToggle
        {
            get { return _previewCloseToggle; }
            set { Set(ref _previewCloseToggle, value); }
        }

        // ---- Preview layout (folder view)

        public int Preview_Folder_CursorX { get { return _i[nameof(Preview_Folder_CursorX)]; } set { SetInt(nameof(Preview_Folder_CursorX), value); } }
        public int Preview_Folder_CursorY { get { return _i[nameof(Preview_Folder_CursorY)]; } set { SetInt(nameof(Preview_Folder_CursorY), value); } }
        public bool Preview_Folder_CursorEnable { get { return _b[nameof(Preview_Folder_CursorEnable)]; } set { SetBool(nameof(Preview_Folder_CursorEnable), value); } }
        public int Preview_Folder_FolderX { get { return _i[nameof(Preview_Folder_FolderX)]; } set { SetInt(nameof(Preview_Folder_FolderX), value); } }
        public int Preview_Folder_FolderY { get { return _i[nameof(Preview_Folder_FolderY)]; } set { SetInt(nameof(Preview_Folder_FolderY), value); } }
        public bool Preview_Folder_FolderEnable { get { return _b[nameof(Preview_Folder_FolderEnable)]; } set { SetBool(nameof(Preview_Folder_FolderEnable), value); } }
        public int Preview_Folder_FileX { get { return _i[nameof(Preview_Folder_FileX)]; } set { SetInt(nameof(Preview_Folder_FileX), value); } }
        public int Preview_Folder_FileY { get { return _i[nameof(Preview_Folder_FileY)]; } set { SetInt(nameof(Preview_Folder_FileY), value); } }
        public bool Preview_Folder_FileEnable { get { return _b[nameof(Preview_Folder_FileEnable)]; } set { SetBool(nameof(Preview_Folder_FileEnable), value); } }

        // ---- Preview layout (main view)

        public int Preview_Main_CursorX { get { return _i[nameof(Preview_Main_CursorX)]; } set { SetInt(nameof(Preview_Main_CursorX), value); } }
        public int Preview_Main_CursorY { get { return _i[nameof(Preview_Main_CursorY)]; } set { SetInt(nameof(Preview_Main_CursorY), value); } }
        public bool Preview_Main_CursorEnable { get { return _b[nameof(Preview_Main_CursorEnable)]; } set { SetBool(nameof(Preview_Main_CursorEnable), value); } }
        public int Preview_Main_FolderX { get { return _i[nameof(Preview_Main_FolderX)]; } set { SetInt(nameof(Preview_Main_FolderX), value); } }
        public int Preview_Main_FolderY { get { return _i[nameof(Preview_Main_FolderY)]; } set { SetInt(nameof(Preview_Main_FolderY), value); } }
        public bool Preview_Main_FolderEnable { get { return _b[nameof(Preview_Main_FolderEnable)]; } set { SetBool(nameof(Preview_Main_FolderEnable), value); } }
        public int Preview_Main_CartX { get { return _i[nameof(Preview_Main_CartX)]; } set { SetInt(nameof(Preview_Main_CartX), value); } }
        public int Preview_Main_CartY { get { return _i[nameof(Preview_Main_CartY)]; } set { SetInt(nameof(Preview_Main_CartY), value); } }
        public bool Preview_Main_CartEnable { get { return _b[nameof(Preview_Main_CartEnable)]; } set { SetBool(nameof(Preview_Main_CartEnable), value); } }
        public int Preview_Main_FileX { get { return _i[nameof(Preview_Main_FileX)]; } set { SetInt(nameof(Preview_Main_FileX), value); } }
        public int Preview_Main_FileY { get { return _i[nameof(Preview_Main_FileY)]; } set { SetInt(nameof(Preview_Main_FileY), value); } }
        public bool Preview_Main_FileEnable { get { return _b[nameof(Preview_Main_FileEnable)]; } set { SetBool(nameof(Preview_Main_FileEnable), value); } }

        // ---- Theme sender

        public string SocketIP
        {
            get { return _socketIp; }
            set { Set(ref _socketIp, value); }
        }

        public int SocketMaxTries
        {
            get { return _socketMaxTries; }
            set { Set(ref _socketMaxTries, value); }
        }

        // ---- BGM conversion defaults

        public string BgmCodec
        {
            get { return _bgmCodec; }
            set { Set(ref _bgmCodec, value); }
        }

        public bool BgmMono
        {
            get { return _bgmMono; }
            set { Set(ref _bgmMono, value); }
        }

        /// <summary>0 keeps the source rate.</summary>
        public int BgmSampleRate
        {
            get { return _bgmSampleRate; }
            set { Set(ref _bgmSampleRate, value); }
        }

        public bool BgmLoop
        {
            get { return _bgmLoop; }
            set { Set(ref _bgmLoop, value); }
        }

        // The preview layout values default to what the original editor shipped with.
        private readonly Dictionary<string, int> _i = new Dictionary<string, int>
        {
            {nameof(Preview_Folder_CursorX), 2}, {nameof(Preview_Folder_CursorY), 1},
            {nameof(Preview_Folder_FolderX), 0}, {nameof(Preview_Folder_FolderY), 1},
            {nameof(Preview_Folder_FileX), 0}, {nameof(Preview_Folder_FileY), 0},
            {nameof(Preview_Main_CursorX), 2}, {nameof(Preview_Main_CursorY), 2},
            {nameof(Preview_Main_FolderX), 0}, {nameof(Preview_Main_FolderY), 1},
            {nameof(Preview_Main_CartX), 0}, {nameof(Preview_Main_CartY), 2},
            {nameof(Preview_Main_FileX), 1}, {nameof(Preview_Main_FileY), 2},
        };

        private readonly Dictionary<string, bool> _b = new Dictionary<string, bool>
        {
            {nameof(Preview_Folder_CursorEnable), true}, {nameof(Preview_Folder_FolderEnable), true},
            {nameof(Preview_Folder_FileEnable), false},
            {nameof(Preview_Main_CursorEnable), true}, {nameof(Preview_Main_FolderEnable), true},
            {nameof(Preview_Main_CartEnable), true}, {nameof(Preview_Main_FileEnable), true},
        };

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetInt(string name, int value)
        {
            if (_i[name] == value)
                return;
            _i[name] = value;
            OnPropertyChanged(name);
        }

        private void SetBool(string name, bool value)
        {
            if (_b[name] == value)
                return;
            _b[name] = value;
            OnPropertyChanged(name);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            OnPropertyChanged(name);
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var data = new Dictionary<string, object>();
                foreach (var p in typeof(Settings).GetProperties())
                {
                    if (!p.CanWrite || p.GetIndexParameters().Length != 0 || p.GetMethod.IsStatic)
                        continue;
                    data[p.Name] = p.GetValue(this);
                }
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception)
            {
                // Settings are a convenience, never fail the app because of them
            }
        }

        private static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(SettingsPath))
                    return s;
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(SettingsPath));
                foreach (var p in typeof(Settings).GetProperties())
                {
                    if (!p.CanWrite || p.GetMethod.IsStatic)
                        continue;
                    var token = json[p.Name];
                    if (token == null)
                        continue;
                    try
                    {
                        p.SetValue(s, token.ToObject(p.PropertyType));
                    }
                    catch (Exception)
                    {
                        // Ignore malformed value, keep default
                    }
                }
            }
            catch (Exception)
            {
                // Corrupt file: fall back to defaults
            }
            return s;
        }
    }
}
