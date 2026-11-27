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
        ThemePlaza
    }

    /// <summary>A theme on one of the sites, as recognised from what the user typed.</summary>
    public sealed class WebThemeId
    {
        public WebThemeSource Source;
        /// <summary>Themezer's hexadecimal ID, or the decimal theme number of 3DSThem.es and Theme Plaza.</summary>
        public string Id;

        public override string ToString() => (Source == WebThemeSource.Themezer ? "T" : "#") + Id;
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

        private static Dictionary<string, string> _index;

        /// <summary>
        ///     What <paramref name="input" /> points at: a link to any of the sites (also their archive.org copies), a Themezer
        ///     quick ID (T1A20D), or a bare number, which belongs to <paramref name="numbers" />. Null when it is none of these.
        /// </summary>
        public static WebThemeId Parse(string input, WebThemeSource numbers)
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
            // The sites themselves (also inside a web.archive.org link)
            if ((m = Regex.Match(text, @"themeplaza\.(?:art|eu)/(?:[a-z]+/)*?(\d+)(?:[/?#]|$)", RegexOptions.IgnoreCase)).Success)
                return new WebThemeId { Source = WebThemeSource.ThemePlaza, Id = Number(m) };
            if ((m = Regex.Match(text, @"3dsthem\.es/\D*?(\d+)", RegexOptions.IgnoreCase)).Success)
                return new WebThemeId { Source = WebThemeSource.ThreeDsThemes, Id = Number(m) };
            if ((m = Regex.Match(text, @"^#?(\d{1,7})$")).Success && numbers != WebThemeSource.Themezer)
                return new WebThemeId { Source = numbers, Id = Number(m) };
            return null;
        }

        private static string Number(Match m) => long.Parse(m.Groups[1].Value).ToString();

        public static string SiteName(WebThemeSource source) =>
            source == WebThemeSource.Themezer ? "Themezer" : source == WebThemeSource.ThreeDsThemes ? "3DSThem.es" : "Theme Plaza";

        /// <summary>The file a theme number has in the archive.org copy of its site (relative to its folder), or null.</summary>
        public static string ArchivedFile(WebThemeSource source, string id)
        {
            var index = _index ?? (_index = LoadIndex());
            return index.TryGetValue((source == WebThemeSource.ThreeDsThemes ? "3dst" : "tp") + "\t" + id, out var file) ? file : null;
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
