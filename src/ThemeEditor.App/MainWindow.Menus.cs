// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Menus.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        private MenuItem _recentMenu;

        /// <summary>Languages offered in the Language menu (culture name, native name).</summary>
        public static readonly (string Culture, string Name)[] Languages =
        {
            ("en", "English"),
            ("de", "Deutsch"),
            ("es", "Español"),
            ("fr", "Français"),
            ("it", "Italiano"),
            ("nl", "Nederlands"),
            ("pl", "Polski"),
            ("pt-BR", "Português (Brasil)"),
            ("sv", "Svenska"),
            ("tr", "Türkçe"),
            ("ru", "Русский"),
            ("ja", "日本語"),
            ("ko", "한국어"),
            ("zh-Hans", "简体中文"),
            ("zh-Hant", "繁體中文")
        };

        private static MenuItem Item(string header, ICommand command = null, object parameter = null, KeyGesture gesture = null)
        {
            var item = new MenuItem {Header = header, Command = command, CommandParameter = parameter};
            if (gesture != null)
                item.InputGesture = gesture;
            return item;
        }

        private void BuildMenus()
        {
            var menu = this.FindControl<Menu>("MainMenu");
            menu.Items.Clear();

            // ---- File
            _recentMenu = new MenuItem {Header = MainResources.Menu_File_Recent};
            var sendMenu = Item(MainResources.Menu_SendTo, SendThemeCHMM2Command, null);
            sendMenu.Header = MainResources.Menu_SendTo + " CHMM2...";

            var save = new MenuItem {Header = MainResources.Menu_File_SavePreview};
            save.Items.Add(Item(MainResources.Menu_File_SavePreview_Top, ExportPreviewCommand, PreviewKind.Top));
            save.Items.Add(Item(MainResources.Menu_File_SavePreview_Bottom, ExportPreviewCommand, PreviewKind.Bottom));
            save.Items.Add(Item(MainResources.Menu_File_SavePreview_Both, ExportPreviewCommand, PreviewKind.Both));

            var exit = Item(MainResources.Menu_File_Exit);
            exit.Click += (s, e) => Close();

            var file = new MenuItem {Header = MainResources.Menu_File};
            file.Items.Add(Item(MainResources.Menu_File_New, NewThemeCommandWrapper.Command, null, NewThemeCommandWrapper.Gesture));
            file.Items.Add(Item(MainResources.Menu_File_Open, LoadThemeCommandWrapper.Command, null, LoadThemeCommandWrapper.Gesture));
            file.Items.Add(_recentMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Item(MainResources.Menu_File_Save, SaveThemeCommandWrapper.Command, null, SaveThemeCommandWrapper.Gesture));
            file.Items.Add(Item(MainResources.Menu_File_SaveAs, SaveAsThemeCommandWrapper.Command, null, SaveAsThemeCommandWrapper.Gesture));
            file.Items.Add(save);
            file.Items.Add(new Separator());
            file.Items.Add(sendMenu);
            file.Items.Add(new Separator());
            file.Items.Add(exit);
            menu.Items.Add(file);

            // ---- Image
            var image = new MenuItem {Header = MainResources.Menu_Image};
            AddImageMenu(image, TargetImage.Top, "Menu_Image_Top", "1024x256 (1008x240)", "512x256 (412x240)", "64x64");
            AddImageMenu(image, TargetImage.TopAlt, "Menu_Image_TopStatic", "64x64");
            AddImageMenu(image, TargetImage.Bottom, "Menu_Image_Bottom", "1024x256 (1008x240)", "1024x256 (960x240)", "512x256 (320x240)");
            AddImageMenu(image, TargetImage.FileLarge, "Menu_Image_FileLarge", "64x128 (36x72)");
            AddImageMenu(image, TargetImage.FileSmall, "Menu_Image_FileSmall", "32x64 (25x50)");
            AddImageMenu(image, TargetImage.FolderClosed, "Menu_Image_FolderClosed", "64x128 (74x64)");
            AddImageMenu(image, TargetImage.FolderOpen, "Menu_Image_FolderOpen", "64x128 (82x64)");
            image.Items.Add(new Separator());
            AddImageMenu(image, TargetImage.LargeIcon, "Menu_Image_LargeIcon", "48x48");
            AddImageMenu(image, TargetImage.SmallIcon, "Menu_Image_SmallIcon", "24x24");
            menu.Items.Add(image);

            // ---- Metadata
            var meta = new MenuItem {Header = MainResources.Menu_Metadata};
            meta.Items.Add(Item(MainResources.Menu_Metadata_ExportColors, ExportMetadataCommand, "Colors"));
            meta.Items.Add(Item(MainResources.Menu_Metadata_ExportFlags, ExportMetadataCommand, "Flags"));
            meta.Items.Add(new Separator());
            meta.Items.Add(Item(MainResources.Menu_Metadata_Import, ImportMetadataCommand));
            menu.Items.Add(meta);

            // ---- Music (BCSTM)
            var music = new MenuItem {Header = MainResources.Menu_Music};
            music.Items.Add(Item(MainResources.Menu_Music_Import, ImportBgmCommand));
            music.Items.Add(Item(MainResources.Menu_Music_Reload, ReloadBGMCommand));
            music.Items.Add(new Separator());
            music.Items.Add(Item(MainResources.Menu_Music_ExportWav, ExportBgmWavCommand));
            music.Items.Add(Item(MainResources.Menu_Music_ExportBcstm, ExportBgmBcstmCommand));
            music.Items.Add(new Separator());
            music.Items.Add(Item(MainResources.Menu_Music_Remove, RemoveBgmCommand));
            menu.Items.Add(music);

            // ---- Experimental
            var exp = new MenuItem {Header = MainResources.Menu_Experimental};
            exp.Items.Add(Item(MainResources.Menu_CwavManager, CWavManagerCommand));
            menu.Items.Add(exp);

            // ---- Language
            menu.Items.Add(BuildLanguageMenu());

            // ---- About
            menu.Items.Add(Item(MainResources.Menu_About, AboutCommand));
        }

        private void AddImageMenu(MenuItem parent, TargetImage target, string headerKey, params string[] sizes)
        {
            var header = MainResources.ResourceManager.GetString(headerKey) ?? target.ToString();
            var sub = new MenuItem {Header = header};

            sub.Items.Add(Item(MainResources.Menu_Image_Any_Load, ReplaceImageCommand, target));

            var remove = Item(MainResources.Menu_Image_Any_Remove, RemoveImageCommand, target);
            remove.Bind(Visual.IsVisibleProperty, remove.GetObservable(IsEnabledProperty));
            sub.Items.Add(remove);

            var export = Item(MainResources.Menu_Image_Any_Export, ExportImageCommand, target);
            export.Bind(Visual.IsVisibleProperty, export.GetObservable(IsEnabledProperty));
            sub.Items.Add(export);

            sub.Items.Add(new Separator());
            foreach (var size in sizes)
                sub.Items.Add(new MenuItem {Header = size, IsEnabled = false});
            parent.Items.Add(sub);
        }

        private MenuItem BuildLanguageMenu()
        {
            var langMenu = new MenuItem {Header = MainResources.Menu_Language};
            var current = Settings.Default.Language ?? string.Empty;

            MenuItem Choice(string culture, string name)
            {
                var item = new MenuItem
                {
                    Header = name,
                    ToggleType = MenuItemToggleType.Radio,
                    GroupName = "language",
                    IsChecked = string.Equals(current, culture, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += async (s, e) =>
                {
                    Settings.Default.Language = culture;
                    Settings.Default.Save();
                    await Dialogs.Show(this, MainResources.Message_LanguageRestart, WINDOW_TITLE);
                };
                return item;
            }

            langMenu.Items.Add(Choice(string.Empty, MainResources.Menu_Language_System));
            langMenu.Items.Add(new Separator());
            foreach (var l in Languages)
                langMenu.Items.Add(Choice(l.Culture, l.Name));
            return langMenu;
        }

        private void RebuildRecentMenu()
        {
            if (_recentMenu == null)
                return;
            _recentMenu.Items.Clear();
            foreach (var path in RecentsList)
            {
                var item = new MenuItem {Header = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty) + Path.DirectorySeparatorChar + Path.GetFileName(path).Replace("_", "__")};
                ToolTip.SetTip(item, path);
                item.Command = LoadThemeCommandWrapper.Command;
                item.CommandParameter = path;
                _recentMenu.Items.Add(item);
            }
            _recentMenu.IsEnabled = RecentsList.Count > 0;
        }
    }
}
