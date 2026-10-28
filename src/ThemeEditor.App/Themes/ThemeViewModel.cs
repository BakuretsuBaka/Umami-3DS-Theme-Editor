// --------------------------------------------------
// 3DS Theme Editor - ThemeViewModel.cs
// --------------------------------------------------

using System;
using System.IO;
using Avalonia.Media;

using ThemeEditor.Common.Graphics;
using ThemeEditor.Common.SMDH;
using ThemeEditor.Common.Themes;

namespace ThemeEditor.App.Themes
{
    public sealed partial class ThemeViewModel : ViewModelBase
    {
        public ColorsViewModel Colors { get; }

        public FlagsViewModel Flags { get; }
        private new Theme Model => (Theme) base.Model;

        public TexturesViewModel Textures { get; }

        public ThemeInfoViewModel Info { get; set; }

        public byte[] CWavBytes
        {
            get { return Model.CWAV; }
            set
            {
                var old = Model.CWAV;
                Model.CWAV = value;
                RaiseViewModelChanged(nameof(CWavBytes), old, value);
            }
        }

        public ThemeViewModel(Theme model, SMDH info = null) : base(model, Guid.NewGuid().ToString())
        {
            Flags = new FlagsViewModel(model.Flags, Tag);
            Colors = new ColorsViewModel(model.Colors, Tag);
            Textures = new TexturesViewModel(model.Textures, Tag);

            Info = new ThemeInfoViewModel(info ?? new SMDH(), Tag);

            SetupRules();
            ViewModelChanged += EnableFlagOnColorEdit;
            // Subscribed last: whatever the constructor and the rules set up is not a user switching a flag on
            ViewModelChanged += FillUnsetColorsOnFlag;
        }

        /// <summary>
        ///     Switching a colour flag on used to show its colours as they were stored, which for a theme that never set
        ///     them is pure black (black folders, buttons, text boxes). If the colours of that set are all still black,
        ///     they are filled with colours matching the wallpapers (the "Light" auto colours) instead.
        /// </summary>
        private void FillUnsetColorsOnFlag(ViewModelChangedArgs args)
        {
            if (!ReferenceEquals(args.ViewModel, Flags) || Rules == null || Rules.Paused)
                return;
            string[] sets;
            if (!AutoColors.SetsOfFlag.TryGetValue(args.Property, out sets))
                return;
            var turnedOn = args.NewValue is bool on
                ? on && !(args.OldValue is bool was && was)
                : Equals(args.NewValue, Localization.Enums.GameTextDrawType.Colored) && !Equals(args.OldValue, Localization.Enums.GameTextDrawType.Colored);
            if (!turnedOn)
                return;
            // The rules may already have refused the change (the bottom background colours only exist in solid colour
            // mode). Filling the colours then would switch the flag back on behind the rules' back.
            var now = typeof(FlagsViewModel).GetProperty(args.Property)?.GetValue(Flags);
            if (!(now is bool stillOn ? stillOn : Equals(now, Localization.Enums.GameTextDrawType.Colored)))
                return;
            AutoColors.Scheme scheme = null;
            foreach (var set in sets)
            {
                if (!AutoColors.IsUnset(this, set))
                    continue;
                scheme = scheme ?? AutoColors.Suggest(this)[(int) AutoColors.Style.Light];
                AutoColors.FillSet(this, scheme, set);
            }
        }

        /// <summary>
        ///     Editing a color of a color set whose "custom colors" flag is off would otherwise change nothing in the
        ///     preview or the saved theme, so the matching flag is switched on for the user.
        /// </summary>
        private void EnableFlagOnColorEdit(ViewModelChangedArgs args)
        {
            if (args.ViewModel.Tag != Tag || Rules == null || Rules.Paused || args.ViewModel.Enabled)
                return;
            var prop = args.ViewModel.GetType().GetProperty(args.Property);
            if (prop == null || prop.PropertyType != typeof(Avalonia.Media.Color))
                return;

            var set = args.ViewModel;
            if (set == Colors.TopBackground) { if (Flags.TopDrawType == Localization.Enums.TopDrawType.None) Flags.TopDrawType = Localization.Enums.TopDrawType.SolidColor; }
            else if (set == Colors.TopCorner) Flags.TopCornerButtonColor = true;
            else if (set == Colors.BottomBackgroundInner || set == Colors.BottomBackgroundOuter)
            {
                // These areas only exist in "solid color" mode, and the outer one also needs the inner one
                if (Flags.BottomDrawType == Localization.Enums.BottomDrawType.None)
                    Flags.BottomDrawType = Localization.Enums.BottomDrawType.SolidColor;
                Flags.BottomBackgroundInnerColor = true;
                if (set == Colors.BottomBackgroundOuter)
                    Flags.BottomBackgroundOuterColor = true;
            }
            else if (set == Colors.BottomCorner) Flags.BottomCornerButtonColor = true;
            else if (set == Colors.Open || set == Colors.Close) Flags.OpenCloseColor = true;
            else if (set == Colors.FolderBackground) Flags.FolderBackgroundColor = true;
            else if (set == Colors.Folder) Flags.FolderColor = true;
            else if (set == Colors.FolderArrow) Flags.FolderArrowColor = true;
            else if (set == Colors.File) Flags.FileColor = true;
            else if (set == Colors.Cursor) Flags.CursorColor = true;
            else if (set == Colors.ArrowButton) Flags.ArrowButtonColor = true;
            else if (set == Colors.Arrow) Flags.ArrowColor = true;
            else if (set == Colors.GameText) Flags.GameTextDrawType = Localization.Enums.GameTextDrawType.Colored;
            else if (set == Colors.DemoText) Flags.DemoTextColor = true;
        }

        public void Save(Stream stream)
        {
            Theme.Write(Model, stream);
        }

        public override void Dispose()
        {   
            ViewModelChanged -= EnableFlagOnColorEdit;
            ViewModelChanged -= FillUnsetColorsOnFlag;

            Rules.Dispose();
            Flags.Dispose();
            Colors.Dispose();
            Textures.Dispose();

            base.Dispose();
        }

        partial void SetupRules();
    }
}
