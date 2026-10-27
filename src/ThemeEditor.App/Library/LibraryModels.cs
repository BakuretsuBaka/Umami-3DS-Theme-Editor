// --------------------------------------------------
// Umami 3DS Theme Editor - LibraryModels.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;

namespace ThemeEditor.App.Library
{
    public enum LibraryKind
    {
        FolderTexture,
        IconTexture,
        SoundEffects,
        Music
    }

    /// <summary>One entry of the resource library (a texture pair, a sound effect set or a piece of music).</summary>
    public sealed class LibraryItem
    {
        public string Id { get; set; }
        public LibraryKind Kind { get; set; }

        /// <summary>Display name, usually the theme it comes from.</summary>
        public string Name { get; set; }

        /// <summary>"Umami" for the items that ship with / were made for the editor, otherwise "Imported".</summary>
        public string Collection { get; set; } = "Imported";

        /// <summary>Textures: PNG file names inside the library folder (closed + open folder, or large + small icon).</summary>
        public string FileA { get; set; }
        public string FileB { get; set; }

        /// <summary>Sound effects: the CWAV block; music: a stored BCSTM (when it is not read from a ZIP).</summary>
        public string DataFile { get; set; }

        /// <summary>Music read straight from a theme ZIP (saves copying gigabytes of music).</summary>
        public string ZipPath { get; set; }
        public string ZipEntry { get; set; }
        public string PreviewEntry { get; set; }

        public double Seconds { get; set; }
        public long Bytes { get; set; }

        public override string ToString() { return Name; }
    }

    public sealed class LibraryIndex
    {
        public int Version { get; set; } = 1;
        public List<LibraryItem> Items { get; set; } = new List<LibraryItem>();
        public List<string> Sources { get; set; } = new List<string>();
    }

    /// <summary>Progress of an import run.</summary>
    public sealed class ImportProgress
    {
        public int Done;
        public int Total;
        public int Added;
        public int Skipped;
        public string Current;
    }
}
