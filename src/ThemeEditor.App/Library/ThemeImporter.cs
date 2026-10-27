// --------------------------------------------------
// Umami 3DS Theme Editor - ThemeImporter.cs
// --------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ThemeEditor.Common.Audio;
using ThemeEditor.Common.Compression;
using ThemeEditor.Common.Graphics;
using ThemeEditor.Common.Themes;

namespace ThemeEditor.App.Library
{
    /// <summary>
    ///     Pulls folder textures, icon textures, sound effects and music out of theme ZIPs / theme folders into a
    ///     <see cref="ResourceLibrary" />. Everything is copied (nothing points back at the source), identical content is stored once.
    /// </summary>
    public sealed class ThemeImporter
    {
        public const int MaxBgmBytes = 3371008;

        private readonly ResourceLibrary _library;
        private readonly string _collection;
        private readonly bool _music;

        public ThemeImporter(ResourceLibrary library, string collection = "Imported", bool includeMusic = true)
        {
            _library = library;
            _collection = collection;
            _music = includeMusic;
        }

        /// <summary>A theme's raw parts, from a ZIP entry set or a folder.</summary>
        public sealed class ThemeParts
        {
            public string Name;
            public byte[] Body;
            public byte[] Bgm;
        }

        public static string CleanName(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName) ?? "Theme";
            name = Regex.Replace(name, @"^\d{3,5}\s*-\s*", "");                       // "0260 - " collection numbering
            name = Regex.Replace(name, @"\s*\(Themezer-[A-Za-z0-9]+\)\s*$", "");      // Themezer id suffix
            name = Regex.Replace(name, @"\s+by\s+[^()]+\(\d+\)\s*$", "");            // Theme Plaza "by X (id)"
            return name.Trim();
        }

        public static IEnumerable<ThemeParts> ReadZip(string zipPath)
        {
            var result = new List<ThemeParts>();
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var groups = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).GroupBy(e => (Path.GetDirectoryName(e.FullName.Replace('\\', '/')) ?? "").Replace('\\', '/'));
                foreach (var g in groups)
                {
                    var body = g.FirstOrDefault(e => string.Equals(e.Name, "body_LZ.bin", StringComparison.OrdinalIgnoreCase));
                    if (body == null)
                        continue;
                    var parts = new ThemeParts { Body = ReadAll(body) };
                    var name = g.FirstOrDefault(e => string.Equals(e.Name, "name.txt", StringComparison.OrdinalIgnoreCase));
                    parts.Name = name != null ? Encoding.UTF8.GetString(ReadAll(name)).Trim() : null;
                    if (string.IsNullOrWhiteSpace(parts.Name))
                        parts.Name = CleanName(zipPath);
                    var bgm = g.FirstOrDefault(e => string.Equals(e.Name, "bgm.bcstm", StringComparison.OrdinalIgnoreCase));
                    if (bgm != null && bgm.Length <= MaxBgmBytes * 2)
                        parts.Bgm = ReadAll(bgm);
                    result.Add(parts);
                }
            }
            return result;
        }

        public static ThemeParts ReadFolder(string folder)
        {
            var bodyPath = Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "body_LZ.bin", StringComparison.OrdinalIgnoreCase));
            if (bodyPath == null)
                return null;
            var parts = new ThemeParts { Body = File.ReadAllBytes(bodyPath), Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) };
            var nameFile = Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "name.txt", StringComparison.OrdinalIgnoreCase));
            if (nameFile != null && !string.IsNullOrWhiteSpace(File.ReadAllText(nameFile)))
                parts.Name = File.ReadAllText(nameFile).Trim();
            var bgm = Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "bgm.bcstm", StringComparison.OrdinalIgnoreCase));
            if (bgm != null)
                parts.Bgm = File.ReadAllBytes(bgm);
            return parts;
        }

        private static byte[] ReadAll(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        internal static string Hash(params byte[][] parts)
        {
            using (var sha = SHA1.Create())
            {
                foreach (var p in parts)
                    sha.TransformBlock(p, 0, p.Length, null, 0);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
            }
        }

        private static Theme ParseBody(byte[] body)
        {
            using (var input = new MemoryStream(body))
            using (var plain = new MemoryStream())
            {
                LZ11.Decompress(input, input.Length, plain);
                plain.Position = 0;
                return Theme.Read(plain);
            }
        }

        private static bool Usable(RawTexture t, int w, int h)
        {
            return t != null && t.Width == w && t.Height == h && t.Format == RawTexture.DataFormat.Bgr888 && t.Data != null && t.Data.Length > 0;
        }

        private static byte[] Png(RawTexture t)
        {
            return PixelBuffer.FromBgr24(t.Width, t.Height, t.Decode()).EncodePng();
        }

        /// <summary>Adds what one theme offers; returns how many new items it contributed.</summary>
        public int ImportTheme(ThemeParts parts)
        {
            Theme theme;
            try
            {
                theme = ParseBody(parts.Body);
            }
            catch (Exception)
            {
                return 0;
            }
            var added = 0;
            var root = _library.WritableRoot;
            Directory.CreateDirectory(Path.Combine(root, "tex"));
            Directory.CreateDirectory(Path.Combine(root, "sfx"));
            Directory.CreateDirectory(Path.Combine(root, "bgm"));

            var f = theme.Flags;
            var tx = theme.Textures;
            if (f.FolderTexture && Usable(tx.FolderClosed, 128, 64) && Usable(tx.FolderOpen, 128, 64))
            {
                var id = "f-" + Hash(tx.FolderClosed.Data, tx.FolderOpen.Data);
                if (_library.TryClaim(id))
                {
                    File.WriteAllBytes(Path.Combine(root, "tex", id + "-a.png"), Png(tx.FolderClosed));
                    File.WriteAllBytes(Path.Combine(root, "tex", id + "-b.png"), Png(tx.FolderOpen));
                    if (_library.Add(new LibraryItem { Id = id, Kind = LibraryKind.FolderTexture, Name = parts.Name, Collection = _collection, FileA = "tex/" + id + "-a.png", FileB = "tex/" + id + "-b.png" }))
                        added++;
                }
            }
            if (f.FileTexture && Usable(tx.FileLarge, 64, 128) && Usable(tx.FileSmall, 32, 64))
            {
                var id = "i-" + Hash(tx.FileLarge.Data, tx.FileSmall.Data);
                if (_library.TryClaim(id))
                {
                    File.WriteAllBytes(Path.Combine(root, "tex", id + "-a.png"), Png(tx.FileLarge));
                    File.WriteAllBytes(Path.Combine(root, "tex", id + "-b.png"), Png(tx.FileSmall));
                    if (_library.Add(new LibraryItem { Id = id, Kind = LibraryKind.IconTexture, Name = parts.Name, Collection = _collection, FileA = "tex/" + id + "-a.png", FileB = "tex/" + id + "-b.png" }))
                        added++;
                }
            }
            if (f.SoundEffect && theme.CWAV != null && theme.CWAV.Length > 0x100)
            {
                var id = "s-" + Hash(theme.CWAV);
                if (_library.TryClaim(id))
                {
                    File.WriteAllBytes(Path.Combine(root, "sfx", id + ".bin"), theme.CWAV);
                    if (_library.Add(new LibraryItem { Id = id, Kind = LibraryKind.SoundEffects, Name = parts.Name, Collection = _collection, DataFile = "sfx/" + id + ".bin", Bytes = theme.CWAV.Length }))
                        added++;
                }
            }
            if (_music && parts.Bgm != null && parts.Bgm.Length > 0x100 && parts.Bgm.Length <= MaxBgmBytes)
            {
                var bgm = BcstmCompat.Normalize(parts.Bgm);
                var id = "m-" + Hash(bgm);
                if (_library.TryClaim(id))
                {
                    double seconds = 0;
                    try { seconds = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(bgm)).Duration; } catch (Exception) { }
                    File.WriteAllBytes(Path.Combine(root, "bgm", id + ".bcstm"), bgm);
                    if (_library.Add(new LibraryItem { Id = id, Kind = LibraryKind.Music, Name = parts.Name, Collection = _collection, DataFile = "bgm/" + id + ".bcstm", Bytes = bgm.Length, Seconds = seconds }))
                        added++;
                }
            }
            return added;
        }

        /// <summary>Imports every theme ZIP and theme folder below <paramref name="folder" />.</summary>
        public ImportProgress ImportFolder(string folder, IProgress<ImportProgress> progress = null, CancellationToken cancel = default(CancellationToken))
        {
            var zips = Directory.EnumerateFiles(folder, "*.zip", SearchOption.AllDirectories).ToList();
            var dirs = Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).Where(d => File.Exists(Path.Combine(d, "body_LZ.bin"))).ToList();
            var state = new ImportProgress { Total = zips.Count + dirs.Count };
            var gate = new object();
            _library.AddSource(folder);

            void Step(Func<IEnumerable<ThemeParts>> read, string label)
            {
                var added = 0;
                try
                {
                    foreach (var p in read())
                        added += ImportTheme(p);
                }
                catch (Exception)
                {
                    // A broken ZIP just contributes nothing
                }
                lock (gate)
                {
                    state.Done++;
                    state.Added += added;
                    if (added == 0)
                        state.Skipped++;
                    state.Current = label;
                    progress?.Report(new ImportProgress { Done = state.Done, Total = state.Total, Added = state.Added, Skipped = state.Skipped, Current = label });
                }
            }

            // Library writes are serialized by file system independence (distinct ids) and the library's own lock; ids are checked before writing
            Parallel.ForEach(zips, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = cancel }, z => Step(() => ReadZip(z), Path.GetFileName(z)));
            foreach (var d in dirs)
            {
                cancel.ThrowIfCancellationRequested();
                Step(() => new[] { ReadFolder(d) }.Where(p => p != null), Path.GetFileName(d));
            }
            _library.Save();
            return state;
        }
    }
}
