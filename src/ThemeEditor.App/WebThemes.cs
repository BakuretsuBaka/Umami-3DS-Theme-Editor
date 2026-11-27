// --------------------------------------------------
// Umami 3DS Theme Editor - WebThemes.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using ThemeEditor.App.Localization;

namespace ThemeEditor.App
{
    /// <summary>Where a theme opened from the web comes from.</summary>
    public enum WebThemeSource
    {
        Themezer,
        ThreeDsThemes,
        ThemePlaza,
        /// <summary>Nintendo's own themes (the archive.org copy of them), found by name.</summary>
        Official
    }

    /// <summary>A theme on one of the sites, as recognised from what the user typed.</summary>
    public sealed class WebThemeId
    {
        public WebThemeSource Source;
        /// <summary>
        ///     Themezer's hexadecimal ID, the decimal theme number of 3DSThem.es and Theme Plaza, or an official theme's region
        ///     and number ("EU-0006").
        /// </summary>
        public string Id;
        /// <summary>The theme's name (and creator), when known.</summary>
        public string Title;
        /// <summary>Where it is listed beyond the site, e.g. Theme Plaza's "NSFW" section; or null.</summary>
        public string Section;

        public override string ToString() =>
            Title ?? (Source == WebThemeSource.Official ? WebThemes.TitleOf(Source, Id) : (Source == WebThemeSource.Themezer ? "T" : "#") + Id);
    }

    /// <summary>
    ///     Opens 3DS themes from the web without a browser: Themezer (its API), 3DSThem.es (its archive.org copy) and Theme
    ///     Plaza (the archive.org copy of its adult content section, otherwise the Wayback Machine's copy of the download).
    ///     The archives name files by title and author, so a small index shipped with the editor maps numbers to files.
    /// </summary>
    public static class WebThemes
    {
        public const string ThreeDsThemesArchive = "https://archive.org/download/3dsthem.es-archive/Themes/Themes/";
        public const string ThemePlazaArchive = "https://archive.org/download/3ds-theme-plaza-archive/Theme%20Plaza%20NSFW%20Archive/";
        public const string Wayback = "https://web.archive.org/web/2026id_/";
        public const string OfficialArchive = "https://archive.org/download/3ds-official-themes-archive/";

        private static Dictionary<string, string> _index;

        /// <summary>The regions of the official themes, as their IDs start.</summary>
        public static readonly string[] Regions = { "EU", "JP", "US" };

        /// <summary>
        ///     What <paramref name="input" /> points at: a link to any of the sites (also their archive.org copies), a Themezer
        ///     quick ID (T1A20D), an official theme's region and number (EU-6), or a bare number, which belongs to
        ///     <paramref name="numbers" /> (for official themes, of <paramref name="region" />). Null when it is none of these.
        /// </summary>
        public static WebThemeId Parse(string input, WebThemeSource numbers, string region = "EU")
        {
            var text = (input ?? string.Empty).Trim();
            if (text.Length == 0)
                return null;
            var themezer = ThemezerClient.ThemeIdOf(text);
            if (themezer != null)
                return new WebThemeId { Source = WebThemeSource.Themezer, Id = themezer };

            Match m;
            // archive.org copies: "[10001] Title by Author.zip" and "Title by Author (12345).zip"
            if ((m = Regex.Match(text, @"3dsthem\.es-archive/.*?(?:\[|%5B)(\d+)(?:\]|%5D)", RegexOptions.IgnoreCase)).Success)
                return new WebThemeId { Source = WebThemeSource.ThreeDsThemes, Id = Number(m) };
            if ((m = Regex.Match(text, @"3ds-theme-plaza-archive/.*?(?:\(|%28)(\d+)(?:\)|%29)\.zip", RegexOptions.IgnoreCase)).Success)
                return new WebThemeId { Source = WebThemeSource.ThemePlaza, Id = Number(m) };
            // Nintendo's themes: "European Themes/0006 - Mighty Mushrooms.zip"
            if ((m = Regex.Match(text, @"3ds-official-themes-archive/(European|Japanese|North(?:%20|\+| )American)(?:%20|\+| )Themes/(\d+)", RegexOptions.IgnoreCase)).Success)
            {
                var linked = Official(m.Groups[1].Value.StartsWith("E", StringComparison.OrdinalIgnoreCase) ? "EU"
                    : m.Groups[1].Value.StartsWith("J", StringComparison.OrdinalIgnoreCase) ? "JP" : "US", m.Groups[2].Value);
                if (linked != null)
                    return linked;
            }
            if ((m = Regex.Match(text, @"^(EU|JP|US)[\s\-#]*(\d{1,4})$", RegexOptions.IgnoreCase)).Success)
                return Official(m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value);
            // The sites themselves (also inside a web.archive.org link)
            if ((m = Regex.Match(text, @"themeplaza\.(?:art|eu)/(?:[a-z]+/)*?(\d+)(?:[/?#]|$)", RegexOptions.IgnoreCase)).Success)
                return new WebThemeId { Source = WebThemeSource.ThemePlaza, Id = Number(m) };
            if ((m = Regex.Match(text, @"3dsthem\.es/\D*?(\d+)", RegexOptions.IgnoreCase)).Success)
                return new WebThemeId { Source = WebThemeSource.ThreeDsThemes, Id = Number(m) };
            if ((m = Regex.Match(text, @"^#?(\d{1,7})$")).Success && numbers == WebThemeSource.Official)
                return Official(region, m.Groups[1].Value);
            if (m.Success && numbers != WebThemeSource.Themezer)
                return new WebThemeId { Source = numbers, Id = Number(m) };
            return null;
        }

        /// <summary>An official theme by region and number, or null when the archive has no such theme.</summary>
        private static WebThemeId Official(string region, string number)
        {
            if (!long.TryParse(number, out var n) || n > 9999)
                return null;
            var id = region + "-" + n.ToString("0000");
            return ArchivedFile(WebThemeSource.Official, id) != null ? new WebThemeId { Source = WebThemeSource.Official, Id = id } : null;
        }

        private static string Number(Match m) => long.Parse(m.Groups[1].Value).ToString();

        public static string SiteName(WebThemeSource source) =>
            source == WebThemeSource.Themezer ? "Themezer" : source == WebThemeSource.ThreeDsThemes ? "3DSThem.es"
            : source == WebThemeSource.ThemePlaza ? "Theme Plaza" : MainResources.Web_Official;

        private static string Prefix(WebThemeSource source) =>
            source == WebThemeSource.ThreeDsThemes ? "3dst" : source == WebThemeSource.ThemePlaza ? "tp" : "off";

        /// <summary>
        ///     A theme's name as its archive file has it: "Rawk Hawk by Fayne_Aldan" (3DSThem.es, Theme Plaza) or "Mighty
        ///     Mushrooms (EU)" (official). The official archive's file names lost a few characters ("Yoshi_s", "Black _ White");
        ///     they are put back where it is clear. The ID itself when the theme is not in the index.
        /// </summary>
        public static string TitleOf(WebThemeSource source, string id)
        {
            var file = ArchivedFile(source, id);
            if (file == null)
                return id;
            var name = Path.GetFileNameWithoutExtension(file.Substring(file.LastIndexOf('/') + 1));
            switch (source)
            {
                case WebThemeSource.ThreeDsThemes:
                    return Regex.Replace(name, @"^\[\d+\]\s*", "");
                case WebThemeSource.ThemePlaza:
                    return Regex.Replace(name, @"\s*\(\d+\)$", "");
                default:
                    name = Regex.Replace(name, @"^\d+ - ", "");
                    name = Regex.Replace(name, @"(?<=\w)_s\b", "'s");
                    name = name.Replace(" _ ", " & ");
                    return name + " (" + id.Substring(0, 2) + ")";
            }
        }

        private sealed class Entry
        {
            public WebThemeId Theme;
            /// <summary>The title's words, lower case without accents, each with a space in front (" mighty mushrooms eu").</summary>
            public string Words;
        }

        private static List<Entry> _entries;

        /// <summary>
        ///     The themes in the archives (official first, then 3DSThem.es and Theme Plaza) whose name has a word starting with
        ///     each word of <paramref name="query" />, at most <paramref name="max" /> per archive. Themezer is searched online
        ///     instead (<see cref="ThemezerClient.SearchThemes" />).
        /// </summary>
        public static List<WebThemeId> Search(string query, int max = 50)
        {
            var found = new List<WebThemeId>();
            var terms = Words(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0 || terms.Length == 1 && terms[0].Length < 2)
                return found;
            var counts = new Dictionary<WebThemeSource, int>();
            foreach (var entry in _entries ?? (_entries = LoadEntries()))
            {
                counts.TryGetValue(entry.Theme.Source, out var count);
                if (count >= max)
                    continue;
                var all = true;
                foreach (var t in terms)
                    if (entry.Words.IndexOf(" " + t, StringComparison.Ordinal) < 0)
                    {
                        all = false;
                        break;
                    }
                if (!all)
                    continue;
                counts[entry.Theme.Source] = count + 1;
                found.Add(entry.Theme);
            }
            return found;
        }

        private static List<Entry> LoadEntries()
        {
            var entries = new List<Entry>();
            var index = _index ?? (_index = LoadIndex());
            foreach (var source in new[] { WebThemeSource.Official, WebThemeSource.ThreeDsThemes, WebThemeSource.ThemePlaza })
            {
                var prefix = Prefix(source) + "\t";
                foreach (var key in index.Keys)
                {
                    if (!key.StartsWith(prefix, StringComparison.Ordinal))
                        continue;
                    var id = key.Substring(prefix.Length);
                    var title = TitleOf(source, id);
                    var file = index[key];
                    var section = source == WebThemeSource.ThemePlaza && file.Contains("/") ? file.Substring(0, file.IndexOf('/')) : null;
                    entries.Add(new Entry
                    {
                        Theme = new WebThemeId { Source = source, Id = id, Title = title, Section = section },
                        Words = " " + Words(title)
                    });
                }
            }
            return entries;
        }

        /// <summary>The words of a text, lower case without accents or punctuation, separated by single spaces.</summary>
        private static string Words(string text)
        {
            var simple = new System.Text.StringBuilder();
            foreach (var c in (text ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                    continue;
                simple.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return Regex.Replace(simple.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>The file a theme number has in the archive.org copy of its site (relative to its folder), or null.</summary>
        public static string ArchivedFile(WebThemeSource source, string id)
        {
            var index = _index ?? (_index = LoadIndex());
            return index.TryGetValue(Prefix(source) + "\t" + id, out var file) ? file : null;
        }

        private static Dictionary<string, string> LoadIndex()
        {
            var index = new Dictionary<string, string>();
            using (var gz = new GZipStream(AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/web-themes.txt.gz")), CompressionMode.Decompress))
            using (var reader = new StreamReader(gz))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 3)
                        index[parts[0] + "\t" + parts[1]] = parts[2];
                }
            }
            return index;
        }

        /// <summary>The addresses a theme can be downloaded from, the most reliable first.</summary>
        public static List<string> SourcesOf(WebThemeId theme)
        {
            var urls = new List<string>();
            if (theme.Source == WebThemeSource.ThreeDsThemes || theme.Source == WebThemeSource.ThemePlaza)
            {
                var file = ArchivedFile(theme.Source, theme.Id);
                if (file != null)
                    urls.Add((theme.Source == WebThemeSource.ThreeDsThemes ? ThreeDsThemesArchive : ThemePlazaArchive) + Escape(file));
            }
            if (theme.Source == WebThemeSource.Official)
            {
                var file = ArchivedFile(theme.Source, theme.Id);
                if (file != null)
                    urls.Add(OfficialArchive + Escape(file));
            }
            if (theme.Source == WebThemeSource.ThemePlaza)
            {
                // Theme Plaza itself is gone behind a bot check; the Wayback Machine kept many of its downloads
                urls.Add(Wayback + "https://themeplaza.art/download/" + theme.Id);
                urls.Add(Wayback + "https://themeplaza.eu/download/" + theme.Id);
            }
            return urls;
        }

        private static string Escape(string path)
        {
            var parts = path.Split('/');
            for (var i = 0; i < parts.Length; i++)
                parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        /// <summary>
        ///     Downloads a 3DSThem.es or Theme Plaza theme into <paramref name="folder" /> and returns the ZIP's path; throws a
        ///     <see cref="ThemezerException" /> with code NOT_FOUND when no copy of it exists.
        /// </summary>
        public static async Task<string> Download(WebThemeId theme, string folder, CancellationToken cancel = default, HttpMessageHandler handler = null)
        {
            var urls = SourcesOf(theme);
            using (var http = handler == null ? new HttpClient() : new HttpClient(handler, false))
            {
                http.Timeout = TimeSpan.FromMinutes(3);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("UmamiThemeEditor/" + Update.AppVersion);
                foreach (var url in urls)
                {
                    try
                    {
                        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
                        {
                            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                                continue;
                            response.EnsureSuccessStatusCode();
                            var data = await response.Content.ReadAsByteArrayAsync();
                            // An archived error page is no theme: only a ZIP counts
                            if (data.Length < 4 || data[0] != 'P' || data[1] != 'K')
                                continue;
                            var name = response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                                       ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                            var archived = ArchivedFile(theme.Source, theme.Id);
                            name = !string.IsNullOrWhiteSpace(name) ? name
                                : archived != null ? archived.Substring(archived.LastIndexOf('/') + 1)
                                : SiteName(theme.Source).Replace(" ", "") + "-" + theme.Id + ".zip";
                            foreach (var c in Path.GetInvalidFileNameChars())
                                name = name.Replace(c, '_');
                            Directory.CreateDirectory(folder);
                            var path = Path.Combine(folder, name);
                            File.WriteAllBytes(path, data);
                            return path;
                        }
                    }
                    catch (HttpRequestException) when (url != urls[urls.Count - 1])
                    {
                        // The next copy may still answer
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new ThemezerException(ex.Message, "NETWORK", ex);
                    }
                }
            }
            throw new ThemezerException(string.Format(theme.Source == WebThemeSource.ThemePlaza ? MainResources.Web_NotArchivedPlaza : MainResources.Web_NotFound,
                SiteName(theme.Source), theme.Id), "NOT_FOUND");
        }
    }
}
