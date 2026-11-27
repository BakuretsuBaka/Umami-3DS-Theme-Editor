// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.Checks.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ThemeEditor.App.Localization;
using ThemeEditor.Common.Audio;

namespace ThemeEditor.App
{
    partial class MainWindow
    {
        /// <summary>Largest sound effects block (the CWAVs) the HOME Menu accepts in a theme.</summary>
        internal const int MaxSoundEffectBytes = 0x2DC00;

        /// <summary>The problems already shown for the open theme and gone along with: each is only told once.</summary>
        private readonly HashSet<string> _acknowledgedProblems = new HashSet<string>();

        /// <summary>
        ///     What is broken in the open theme so it won't work right on a 3DS: music too large, mono or not looping, sound
        ///     effects too large. Only plain faults, nothing that could be a choice (mono sound effects are fine: most themes
        ///     have them).
        /// </summary>
        internal List<string> ThemeProblems()
        {
            var problems = new List<string>();
            var vm = ViewModel;
            if (vm == null)
                return problems;
            var bgm = vm.Flags.BackgroundMusic ? CurrentBgm() : null;
            if (bgm != null)
            {
                if (bgm.Length > BcstmConverter.MaxThemeBgmBytes)
                    problems.Add(string.Format(MainResources.Check_BgmTooLarge, bgm.Length / 1024, BcstmConverter.MaxThemeBgmBytes / 1024));
                BcstmInfo info = null;
                try
                {
                    info = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(bgm));
                }
                catch (Exception)
                {
                    // Not readable here: nothing to say about it
                }
                if (info?.Channels == 1)
                    problems.Add(MainResources.Check_BgmMono);
                if (info != null && !info.Looping)
                    problems.Add(MainResources.Check_BgmNotLooped);
            }
            if (vm.Flags.SoundEffect && vm.CWavBytes != null && vm.CWavBytes.Length > 0)
            {
                if (vm.CWavBytes.Length > MaxSoundEffectBytes)
                    problems.Add(string.Format(MainResources.Check_SfxTooLarge, vm.CWavBytes.Length / 1024, MaxSoundEffectBytes / 1024));
            }
            return problems;
        }

        /// <summary>The theme's music: imported, or the BGM.BCSTM next to the saved theme; null when there is none.</summary>
        private byte[] CurrentBgm()
        {
            if (BgmBcstm != null)
                return BgmBcstm;
            try
            {
                var file = ThemePath == null ? null : Path.Combine(Path.GetDirectoryName(ThemePath) ?? ".", BGM_FILE_NAME);
                return file != null && File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        ///     Before the theme leaves the editor (saved, exported, uploaded, sent): warns once about each new problem
        ///     (<see cref="ThemeProblems" />). True to go on.
        /// </summary>
        internal async Task<bool> ConfirmThemeProblems()
        {
            var fresh = ThemeProblems().Where(p => !_acknowledgedProblems.Contains(p)).ToList();
            if (fresh.Count == 0)
                return true;
            var message = MainResources.Check_Intro + "\n\n" + string.Join("\n", fresh.Select(p => "• " + p));
            if (await AskThemeProblems(message) != 0)
                return false;
            foreach (var p in fresh)
                _acknowledgedProblems.Add(p);
            return true;
        }

        /// <summary>Shows the warning; 0 is "continue anyway". Tests answer it themselves.</summary>
        internal Func<string, Task<int>> AskThemeProblems;

        private Task<int> AskThemeProblemsDialog(string message) =>
            Dialogs.Choose(this, message, WINDOW_TITLE, MessageKind.Warning, MainResources.Check_Continue, MainResources.Dialog_Cancel);
    }
}
