// --------------------------------------------------
// Umami 3DS Theme Editor - ThemeArchive.cs
// --------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ThemeEditor.App
{
    /// <summary>What a theme ZIP (the Theme Plaza / Themezer package format) carries.</summary>
    public sealed class ThemeArchiveContents
    {
        public byte[] Body;
        public byte[] Smdh;
        public byte[] Bgm;
        public string Name;
    }

    /// <summary>
    ///     Reads and writes theme ZIP packages: a flat archive holding <c>body_LZ.bin</c>, <c>info.smdh</c>,
    ///     <c>bgm.bcstm</c>, <c>icon.png</c>, <c>preview.png</c> and <c>name.txt</c>, as Theme Plaza and Themezer expect.
    /// </summary>
    public static class ThemeArchive
    {
        public static byte[] Create(string name, byte[] body, byte[] smdh, byte[] bgm, byte[] iconPng, byte[] previewPng)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    Add(zip, "body_LZ.bin", body);
                    Add(zip, "info.smdh", smdh);
                    if (bgm != null && bgm.Length > 0)
                        Add(zip, "bgm.bcstm", bgm);
                    Add(zip, "icon.png", iconPng);
                    Add(zip, "preview.png", previewPng);
                    Add(zip, "name.txt", Encoding.UTF8.GetBytes(name ?? string.Empty));
                }
                return ms.ToArray();
            }
        }

        private static void Add(ZipArchive zip, string name, byte[] data)
        {
            if (data == null)
                return;
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = entry.Open())
                s.Write(data, 0, data.Length);
        }

        /// <summary>Reads the theme parts out of a ZIP, wherever they sit in it; null when there is no theme body.</summary>
        public static ThemeArchiveContents Read(byte[] zipBytes)
        {
            try
            {
                using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
                {
                    var result = new ThemeArchiveContents();
                    foreach (var entry in zip.Entries)
                    {
                        var file = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(file))
                            continue;
                        switch (file.ToLowerInvariant())
                        {
                            case "body_lz.bin":
                                result.Body = result.Body ?? ReadAll(entry);
                                break;
                            case "info.smdh":
                                result.Smdh = result.Smdh ?? ReadAll(entry);
                                break;
                            case "bgm.bcstm":
                                result.Bgm = result.Bgm ?? ReadAll(entry);
                                break;
                            case "name.txt":
                                result.Name = result.Name ?? Encoding.UTF8.GetString(ReadAll(entry)).Trim();
                                break;
                        }
                    }
                    return result.Body == null ? null : result;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using (var s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        ///     Writes the theme parts into <paramref name="folder" /> under the names the editor expects
        ///     (<c>body_LZ.bin</c>, <c>info.smdh</c>, <c>BGM.BCSTM</c>) and returns the path of the body.
        /// </summary>
        public static string ExtractTo(ThemeArchiveContents contents, string folder)
        {
            Directory.CreateDirectory(folder);
            var body = Path.Combine(folder, "body_LZ.bin");
            File.WriteAllBytes(body, contents.Body);
            if (contents.Smdh != null)
                File.WriteAllBytes(Path.Combine(folder, "info.smdh"), contents.Smdh);
            if (contents.Bgm != null)
                File.WriteAllBytes(Path.Combine(folder, "BGM.BCSTM"), contents.Bgm);
            return body;
        }

        /// <summary>A folder name next to the ZIP that does not exist yet.</summary>
        public static string UniqueFolder(string zipPath)
        {
            var dir = Path.GetDirectoryName(zipPath) ?? ".";
            var name = Path.GetFileNameWithoutExtension(zipPath);
            var candidate = Path.Combine(dir, name);
            for (var i = 2; Directory.Exists(candidate); i++)
                candidate = Path.Combine(dir, name + " (" + i + ")");
            return candidate;
        }
    }
}
