// --------------------------------------------------
// Umami 3DS Theme Editor - ThemeCredit.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ThemeEditor.App.Library
{
    /// <summary>Where a part of the theme (music, folders, file icons, sound effects) was taken from, to credit its creator.</summary>
    public sealed class ThemeCredit
    {
        public LibraryKind Part;
        public string ThemeName;
        public string Creator;
        public string Url;
        /// <summary>What the part looked or sounded like right after it was taken: the credit only counts while it still does.</summary>
        public string Fingerprint;
        /// <summary>The whole theme was started from this one (opened from the web): credited however much it changed.</summary>
        public bool Base;

        public static ThemeCredit Of(LibraryItem item) =>
            item?.CreditCreator == null ? null : new ThemeCredit { Part = item.Kind, ThemeName = item.CreditTheme, Creator = item.CreditCreator, Url = item.CreditUrl };

        /// <summary>
        ///     The credits as a short Markdown list for the end of a Themezer description, in English as Themezer asks; empty
        ///     when there is nothing to credit. <paramref name="self" /> (the uploader) is not credited.
        /// </summary>
        public static string Markdown(IEnumerable<ThemeCredit> credits, string self)
        {
            var lines = credits
                .Where(c => c?.Creator != null && !string.Equals(c.Creator, self, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Base ? -1 : Order(c.Part))
                .Select(c =>
                {
                    var theme = c.Url != null ? "[" + c.ThemeName + "](" + c.Url + ")" : "\"" + c.ThemeName + "\"";
                    return "- " + (c.Base ? "Based on " : PartName(c.Part) + " from ") + theme + " by " + c.Creator;
                })
                .Distinct()
                .ToList();
            if (lines.Count == 0)
                return string.Empty;
            var text = new StringBuilder("**Credits**");
            foreach (var line in lines)
                text.Append('\n').Append(line);
            return text.ToString();
        }

        private static int Order(LibraryKind part) =>
            part == LibraryKind.Music ? 0 : part == LibraryKind.FolderTexture ? 1 : part == LibraryKind.IconTexture ? 2 : 3;

        private static string PartName(LibraryKind part) =>
            part == LibraryKind.Music ? "Music" : part == LibraryKind.FolderTexture ? "Folders" : part == LibraryKind.IconTexture ? "File icons" : "Sound effects";
    }
}
