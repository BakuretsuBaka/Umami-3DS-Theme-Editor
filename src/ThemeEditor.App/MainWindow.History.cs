// --------------------------------------------------
// Umami 3DS Theme Editor - MainWindow.History.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Audio;
using ThemeEditor.Common.SMDH;
using CommonTheme = ThemeEditor.Common.Themes.Theme;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Unsaved-change tracking and undo/redo. Every edit (colors, flags, textures, sound effects, SMDH) is recorded as a
    ///     snapshot of the uncompressed theme, taken once the user paused for a moment, so a dragged slider or a color
    ///     picker drag ends up as a single undo step.
    /// </summary>
    partial class MainWindow
    {
        private const int MAX_UNDO_STEPS = 30;
        private const int CWAV_MAX_BYTES = 0x2DC00;

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private Snapshot _current;
        private Snapshot _saved;
        private DispatcherTimer _snapshotTimer;
        private bool _snapshotPending;
        private bool _restoring;
        private string _statusText;
        private string _statusDetail;
        private bool _statusWarning;

        public GestureCommandWrapper UndoCommandWrapper { get; private set; }
        public GestureCommandWrapper RedoCommandWrapper { get; private set; }

        /// <summary>True when the theme differs from what was last loaded or saved (music included).</summary>
        public bool HasUnsavedChanges => !ReferenceEquals(_current, _saved) || _snapshotPending || BgmDirty;

        public string StatusText
        {
            get { return _statusText; }
            private set
            {
                if (value == _statusText)
                    return;
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusDetail
        {
            get { return _statusDetail; }
            private set
            {
                if (value == _statusDetail)
                    return;
                _statusDetail = value;
                OnPropertyChanged(nameof(StatusDetail));
            }
        }

        /// <summary>True when the music or the sound effects are over the HOME menu limits.</summary>
        public bool StatusWarning
        {
            get { return _statusWarning; }
            private set
            {
                if (value == _statusWarning)
                    return;
                _statusWarning = value;
                OnPropertyChanged(nameof(StatusWarning));
            }
        }

        private void SetupHistory()
        {
            _snapshotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _snapshotTimer.Tick += (s, e) => FlushPendingSnapshot();

            var undo = new RelayCommand(Undo_Execute, () => ViewModel != null && (_undo.Count > 0 || _snapshotPending));
            var redo = new RelayCommand(Redo_Execute, () => ViewModel != null && _redo.Count > 0 && !_snapshotPending);
            UndoCommandWrapper = GestureCommandWrapper.Create(undo, Key.Z);
            RedoCommandWrapper = GestureCommandWrapper.Create(redo, Key.Z, true);
        }

        /// <summary>Forgets the history and treats the current theme as saved (after New, Open and Save).</summary>
        private void ResetHistory()
        {
            _snapshotTimer?.Stop();
            _snapshotPending = false;
            _undo.Clear();
            _redo.Clear();
            _current = ViewModel == null ? null : TakeSnapshot(ViewModel);
            _saved = _current;
            UpdateTitleAndStatus();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Called for every change of the open theme.</summary>
        private void OnThemeEdited()
        {
            if (_restoring || _current == null)
                return;
            _snapshotPending = true;
            _snapshotTimer.Stop();
            _snapshotTimer.Start();
            UpdateTitleAndStatus();
        }

        /// <summary>Records the pending edit (if any) as an undo step right away.</summary>
        internal void FlushPendingSnapshot()
        {
            _snapshotTimer?.Stop();
            if (!_snapshotPending || ViewModel == null)
                return;
            _snapshotPending = false;
            var snap = TakeSnapshot(ViewModel);
            if (_current != null && snap.SameAs(_current))
            {
                UpdateTitleAndStatus();
                return;
            }
            if (_current != null)
                _undo.Add(_current);
            if (_undo.Count > MAX_UNDO_STEPS)
                _undo.RemoveAt(0);
            _redo.Clear();
            // Editing back to exactly the saved state counts as "no changes"
            _current = _saved != null && snap.SameAs(_saved) ? _saved : snap;
            UpdateTitleAndStatus();
            CommandManager.InvalidateRequerySuggested();
        }

        private void Undo_Execute()
        {
            FlushPendingSnapshot();
            if (_undo.Count == 0)
                return;
            _redo.Add(_current);
            _current = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Restore(_current);
        }

        private void Redo_Execute()
        {
            FlushPendingSnapshot();
            if (_redo.Count == 0)
                return;
            _undo.Add(_current);
            _current = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Restore(_current);
        }

        private void Restore(Snapshot snap)
        {
            _restoring = true;
            try
            {
                CommonTheme theme;
                using (var ms = new MemoryStream(snap.Body))
                    theme = CommonTheme.Read(ms);
                SMDH info;
                using (var ms = new MemoryStream(snap.Smdh))
                    info = SMDH.Read(ms);
                ViewModel = new ThemeViewModel(theme, info);
            }
            finally
            {
                _restoring = false;
            }
            UpdateTitleAndStatus();
            CommandManager.InvalidateRequerySuggested();
        }

        private static Snapshot TakeSnapshot(ThemeViewModel vm)
        {
            using (var body = new MemoryStream())
            using (var smdh = new MemoryStream())
            {
                vm.Save(body);
                vm.Info.Save(smdh);
                return new Snapshot(body.ToArray(), smdh.ToArray());
            }
        }

        /// <summary>Marks the current state as the saved one.</summary>
        private void MarkSaved()
        {
            FlushPendingSnapshot();
            _saved = _current;
            UpdateTitleAndStatus();
        }

        // ---------------------------------------------------------------- Title and status bar

        private void UpdateTitleAndStatus()
        {
            var dirty = HasUnsavedChanges;
            var name = ThemePath == null
                ? null
                : Path.GetFileName(Path.GetDirectoryName(ThemePath) ?? string.Empty) + Path.DirectorySeparatorChar + Path.GetFileName(ThemePath);
            Title = (dirty ? "* " : string.Empty) + (name == null ? WINDOW_TITLE : WINDOW_TITLE + " - " + name);

            var location = ThemePath ?? MainResources.Status_NewTheme;
            StatusText = dirty ? location + "  •  " + MainResources.Status_Unsaved : location;

            var parts = new List<string>();
            var warn = false;
            if (BgmBcstm != null)
            {
                parts.Add(string.Format(MainResources.Status_Bgm, BgmBcstm.Length / 1024, BcstmConverter.MaxThemeBgmBytes / 1024));
                warn |= BgmBcstm.Length > BcstmConverter.MaxThemeBgmBytes;
            }
            else
            {
                parts.Add(MainResources.Status_NoBgm);
            }
            var cwav = ViewModel?.Flags.SoundEffect == true ? ViewModel.CWavBytes : null;
            if (cwav != null && cwav.Length > 0)
            {
                parts.Add(string.Format(MainResources.Status_Sfx, cwav.Length / 1024, CWAV_MAX_BYTES / 1024));
                warn |= cwav.Length > CWAV_MAX_BYTES;
            }
            StatusDetail = string.Join("  •  ", parts);
            StatusWarning = warn;
        }

        // ---------------------------------------------------------------- Unsaved changes prompt

        /// <summary>
        ///     Asks what to do with unsaved changes before they would be lost. Returns false when the user cancelled
        ///     (or saving failed), true when it is fine to go on.
        /// </summary>
        private async Task<bool> ConfirmDiscardChanges()
        {
            FlushPendingSnapshot();
            if (ViewModel == null || !HasUnsavedChanges)
                return true;
            var choice = await Dialogs.Choose(this, MainResources.Message_UnsavedChanges, WINDOW_TITLE, MessageKind.Warning,
                MainResources.Dialog_Save, MainResources.Dialog_Discard, MainResources.Dialog_Cancel);
            switch (choice)
            {
                case 0:
                    var result = await SaveTheme_Execute(ThemePath);
                    return await FinishSave(result, false);
                case 1:
                    return true;
                default:
                    return false;
            }
        }

        private sealed class Snapshot
        {
            public readonly byte[] Body;
            public readonly byte[] Smdh;

            public Snapshot(byte[] body, byte[] smdh)
            {
                Body = body;
                Smdh = smdh;
            }

            public bool SameAs(Snapshot other)
            {
                return Body.AsSpan().SequenceEqual(other.Body) && Smdh.AsSpan().SequenceEqual(other.Smdh);
            }
        }
    }
}
