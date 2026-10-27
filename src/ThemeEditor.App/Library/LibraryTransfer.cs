// --------------------------------------------------
// Umami 3DS Theme Editor - LibraryTransfer.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SkiaSharp;
using ThemeEditor.Common.Audio;

namespace ThemeEditor.App.Library
{
    public sealed class TransferResult
    {
        public int Added;
        public int Existing;
        public int Invalid;
        public string FirstError;

        public void Fail(string why)
        {
            Invalid++;
            if (FirstError == null)
                FirstError = why;
        }
    }

    /// <summary>Exports library items as shareable packs / plain files and imports packs and loose files.</summary>
    public static class LibraryTransfer
    {
        public const string PackExtension = ".umamipack";
        private const string IndexEntry = "index.json";
        private const int MaxPngBytes = 2 * 1024 * 1024;
        private const int MaxBlockBytes = 1024 * 1024;
        private const int MaxBgmBytes = ThemeImporter.MaxBgmBytes;

        private static readonly Regex SafeId = new Regex(@"^[A-Za-z0-9_-]{1,64}$");
        private static readonly Regex SafePath = new Regex(@"^(tex|sfx|bgm)/[A-Za-z0-9_.-]{1,100}$");

        // ---------------------------------------------------------------- Export

        /// <summary>Writes the items and their files into a pack (a ZIP with an index.json).</summary>
        public static void ExportPack(ResourceLibrary lib, IEnumerable<LibraryItem> items, string path, IProgress<ImportProgress> progress = null)
        {
            var list = items.ToList();
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var index = new LibraryIndex { Items = list };
                var e = zip.CreateEntry(IndexEntry, CompressionLevel.Optimal);
                using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                    w.Write(JsonConvert.SerializeObject(index, Formatting.None));
                var done = 0;
                foreach (var item in list)
                {
                    foreach (var f in new[] { item.FileA, item.FileB, item.DataFile }.Where(x => x != null))
                    {
                        // PNG, BCSTM and CWAV data does not compress further
                        var entry = zip.CreateEntry(f, CompressionLevel.NoCompression);
                        using (var src = File.OpenRead(lib.PathOf(item, f)))
                        using (var dst = entry.Open())
                            src.CopyTo(dst);
                    }
                    progress?.Report(new ImportProgress { Done = ++done, Total = list.Count, Added = done, Current = item.Name });
                }
            }
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Writes an item as ordinary files next to <paramref name="basePath" /> (no extension) and returns their paths.</summary>
        public static List<string> ExportFiles(ResourceLibrary lib, LibraryItem item, string basePath)
        {
            var written = new List<string>();
            void Copy(string file, string target)
            {
                File.Copy(lib.PathOf(item, file), target, true);
                written.Add(target);
            }
            switch (item.Kind)
            {
                case LibraryKind.FolderTexture:
                    Copy(item.FileA, basePath + "_closed.png");
                    Copy(item.FileB, basePath + "_open.png");
                    break;
                case LibraryKind.IconTexture:
                    Copy(item.FileA, basePath + "_large.png");
                    Copy(item.FileB, basePath + "_small.png");
                    break;
                case LibraryKind.SoundEffects:
                    Copy(item.DataFile, basePath + ".bin");
                    break;
                default:
                    Copy(item.DataFile, basePath + ".bcstm");
                    break;
            }
            return written;
        }

        // ---------------------------------------------------------------- Import of packs

        /// <summary>
        ///     Adds everything a pack contains that the library does not have yet. Packs come from other people, so every
        ///     path and every file is checked before anything is written.
        /// </summary>
        public static TransferResult ImportPack(ResourceLibrary lib, string path, IProgress<ImportProgress> progress = null)
        {
            var result = new TransferResult();
            LibraryIndex index;
            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(path);
            }
            catch (Exception)
            {
                result.Fail("not a resource pack");
                return result;
            }
            using (zip)
            {
                var indexEntry = zip.GetEntry(IndexEntry);
                if (indexEntry == null)
                {
                    result.Fail("not a resource pack (no index)");
                    return result;
                }
                try
                {
                    using (var r = new StreamReader(indexEntry.Open(), Encoding.UTF8))
                        index = JsonConvert.DeserializeObject<LibraryIndex>(r.ReadToEnd());
                }
                catch (Exception)
                {
                    result.Fail("damaged index");
                    return result;
                }
                if (index?.Items == null)
                {
                    result.Fail("damaged index");
                    return result;
                }

                var done = 0;
                foreach (var item in index.Items)
                {
                    progress?.Report(new ImportProgress { Done = ++done, Total = index.Items.Count, Added = result.Added, Current = item?.Name });
                    string why;
                    var files = ReadItemFiles(zip, item, out why);
                    if (files == null)
                    {
                        result.Fail(why);
                        continue;
                    }
                    if (!lib.TryClaim(item.Id))
                    {
                        result.Existing++;
                        continue;
                    }
                    var added = Store(lib, item.Kind, item.Id, SafeName(item.Name), item.Collection == "Umami" ? "Umami" : "Imported", files, item.FileA, item.FileB, item.DataFile);
                    result.Added += added ? 1 : 0;
                }
            }
            lib.Save();
            return result;
        }

        private static string SafeName(string name)
        {
            name = Regex.Replace(name ?? string.Empty, @"[\p{C}]", "").Trim();
            if (name.Length == 0)
                name = "Imported";
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        /// <summary>Validated file contents keyed by their library-relative name, or null (with the reason).</summary>
        private static Dictionary<string, byte[]> ReadItemFiles(ZipArchive zip, LibraryItem item, out string why)
        {
            why = null;
            if (item == null || item.Id == null || !SafeId.IsMatch(item.Id))
            {
                why = "invalid item id";
                return null;
            }
            var files = new Dictionary<string, byte[]>();
            var names = new[] { item.FileA, item.FileB, item.DataFile }.Where(x => x != null).ToList();
            var expectTex = item.Kind == LibraryKind.FolderTexture || item.Kind == LibraryKind.IconTexture;
            if ((expectTex && (item.FileA == null || item.FileB == null || item.DataFile != null)) || (!expectTex && (item.DataFile == null || item.FileA != null || item.FileB != null)))
            {
                why = "item files do not match its kind";
                return null;
            }
            foreach (var f in names)
            {
                if (!SafePath.IsMatch(f) || f.Contains(".."))
                {
                    why = "unsafe file name in pack";
                    return null;
                }
                var e = zip.GetEntry(f);
                var limit = expectTex ? MaxPngBytes : item.Kind == LibraryKind.Music ? MaxBgmBytes * 2 : MaxBlockBytes;
                if (e == null || e.Length > limit)
                {
                    why = "missing or oversized file in pack";
                    return null;
                }
                using (var s = e.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    files[f] = ms.ToArray();
                }
            }
            if (!ContentOk(item.Kind, names.Select(n => files[n]).ToList(), out why))
                return null;
            return files;
        }

        private static bool ContentOk(LibraryKind kind, List<byte[]> data, out string why)
        {
            why = null;
            try
            {
                switch (kind)
                {
                    case LibraryKind.FolderTexture:
                    case LibraryKind.IconTexture:
                        var sizes = kind == LibraryKind.FolderTexture ? new[] { (128, 64), (128, 64) } : new[] { (64, 128), (32, 64) };
                        for (var i = 0; i < 2; i++)
                            using (var bmp = SKBitmap.Decode(data[i]))
                                if (bmp == null || bmp.Width != sizes[i].Item1 || bmp.Height != sizes[i].Item2)
                                {
                                    why = "texture has the wrong size";
                                    return false;
                                }
                        return true;
                    case LibraryKind.SoundEffects:
                        if (!SoundMerge.Present(data[0]).Any(x => x))
                        {
                            why = "no sounds in the sound effect set";
                            return false;
                        }
                        return true;
                    default:
                        var info = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(data[0]));
                        if (info.SampleRate <= 0 || data[0].Length > MaxBgmBytes)
                        {
                            why = "music is invalid or larger than 3 MB";
                            return false;
                        }
                        return true;
                }
            }
            catch (Exception)
            {
                why = "unreadable file";
                return false;
            }
        }

        /// <summary>Writes validated files into the library and registers the item (the id must be claimed already).</summary>
        private static bool Store(ResourceLibrary lib, LibraryKind kind, string id, string name, string collection, Dictionary<string, byte[]> files, string a, string b, string data)
        {
            foreach (var kv in files)
            {
                var target = Path.Combine(lib.WritableRoot, kv.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, kv.Value);
            }
            double seconds = 0;
            long bytes = 0;
            if (kind == LibraryKind.Music)
            {
                var d = files[data];
                bytes = d.Length;
                try { seconds = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(d)).Duration; } catch (Exception) { }
            }
            else if (kind == LibraryKind.SoundEffects)
            {
                bytes = files[data].Length;
            }
            return lib.Add(new LibraryItem { Id = id, Kind = kind, Name = name, Collection = collection, FileA = a, FileB = b, DataFile = data, Seconds = seconds, Bytes = bytes });
        }

        // ---------------------------------------------------------------- Import of loose files

        /// <summary>
        ///     Imports plain files: .bcstm music, sound effect blocks (.bin) and PNG textures (a closed + open folder image
        ///     of 128x64, or a large 64x128 icon image with an optional small 32x64 one).
        /// </summary>
        public static TransferResult ImportFiles(ResourceLibrary lib, IEnumerable<string> paths, string collection = "Imported")
        {
            var result = new TransferResult();
            var images = new List<(string Path, SKBitmap Bmp)>();
            foreach (var path in paths)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    if (ext == ".bcstm" || ext == ".bin")
                    {
                        var raw = File.ReadAllBytes(path);
                        var kind = ext == ".bcstm" ? LibraryKind.Music : LibraryKind.SoundEffects;
                        var bytes = kind == LibraryKind.Music ? BcstmCompat.Normalize(raw) : raw;
                        string why;
                        if (bytes.Length > (kind == LibraryKind.Music ? MaxBgmBytes : MaxBlockBytes) || !ContentOk(kind, new List<byte[]> { bytes }, out why))
                        {
                            result.Fail(bytes.Length > MaxBgmBytes && kind == LibraryKind.Music ? name + ": larger than 3 MB" : name + ": not a valid " + (kind == LibraryKind.Music ? "BCSTM" : "sound effect block"));
                            continue;
                        }
                        var id = (kind == LibraryKind.Music ? "m-" : "s-") + ThemeImporter.Hash(bytes);
                        if (!lib.TryClaim(id)) { result.Existing++; continue; }
                        var rel = (kind == LibraryKind.Music ? "bgm/" : "sfx/") + id + (kind == LibraryKind.Music ? ".bcstm" : ".bin");
                        if (Store(lib, kind, id, SafeName(name), collection, new Dictionary<string, byte[]> { { rel, bytes } }, null, null, rel)) result.Added++;
                    }
                    else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp" || ext == ".gif")
                    {
                        var bmp = SKBitmap.Decode(path);
                        if (bmp == null) result.Fail(name + ": cannot read the image"); else images.Add((path, bmp));
                    }
                    else
                    {
                        result.Fail(name + ": unsupported file type");
                    }
                }
                catch (Exception)
                {
                    result.Fail(name + ": cannot be read");
                }
            }
            AddTextures(lib, images, collection, result);
            foreach (var i in images) i.Bmp.Dispose();
            lib.Save();
            return result;
        }

        private static void AddTextures(ResourceLibrary lib, List<(string Path, SKBitmap Bmp)> images, string collection, TransferResult result)
        {
            var folder = images.Where(i => i.Bmp.Width == 128 && i.Bmp.Height == 64).OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var large = images.Where(i => i.Bmp.Width == 64 && i.Bmp.Height == 128).ToList();
            var small = images.Where(i => i.Bmp.Width == 32 && i.Bmp.Height == 64).ToList();
            foreach (var i in images.Except(folder).Except(large).Except(small))
                result.Fail(Path.GetFileNameWithoutExtension(i.Path) + ": needs 128x64 (folder) or 64x128 / 32x64 (icon)");

            if (folder.Count > 0)
            {
                if (folder.Count < 2) { result.Fail(Path.GetFileNameWithoutExtension(folder[0].Path) + ": a folder texture needs two images (closed and open)"); }
                else
                {
                    // the image named "open" is the open folder, otherwise the first of the two by name is the closed one
                    var open = folder.FirstOrDefault(i => Path.GetFileNameWithoutExtension(i.Path).IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 && Path.GetFileNameWithoutExtension(i.Path).IndexOf("closed", StringComparison.OrdinalIgnoreCase) < 0);
                    var closed = folder.FirstOrDefault(i => i.Path != open.Path && open.Path != null) ;
                    if (open.Path == null) { closed = folder[0]; open = folder[1]; }
                    AddTexturePair(lib, LibraryKind.FolderTexture, closed, open, collection, result);
                }
            }
            foreach (var l in large)
            {
                var s = small.FirstOrDefault(x => Path.GetDirectoryName(x.Path) == Path.GetDirectoryName(l.Path));
                if (s.Bmp == null)
                {
                    var made = new SKBitmap(32, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var c = new SKCanvas(made)) using (var p = new SKPaint { FilterQuality = SKFilterQuality.High }) using (var img = SKImage.FromBitmap(l.Bmp)) c.DrawImage(img, new SKRect(0, 0, 64, 128), new SKRect(0, 0, 32, 64), p);
                    s = (l.Path, made);
                }
                AddTexturePair(lib, LibraryKind.IconTexture, l, s, collection, result);
            }
        }

        private static byte[] Png(SKBitmap b)
        {
            using (var img = SKImage.FromBitmap(b)) using (var d = img.Encode(SKEncodedImageFormat.Png, 100)) return d.ToArray();
        }

        private static void AddTexturePair(ResourceLibrary lib, LibraryKind kind, (string Path, SKBitmap Bmp) a, (string Path, SKBitmap Bmp) b, string collection, TransferResult result)
        {
            var pa = Png(a.Bmp); var pb = Png(b.Bmp);
            var id = (kind == LibraryKind.FolderTexture ? "f-" : "i-") + ThemeImporter.Hash(pa, pb);
            if (!lib.TryClaim(id)) { result.Existing++; return; }
            var ra = "tex/" + id + "-a.png"; var rb = "tex/" + id + "-b.png";
            var baseName = Regex.Replace(Path.GetFileNameWithoutExtension(a.Path), @"[_\- ]?(closed|open|large|small)$", "", RegexOptions.IgnoreCase);
            if (Store(lib, kind, id, SafeName(baseName), collection, new Dictionary<string, byte[]> { { ra, pa }, { rb, pb } }, ra, rb, null)) result.Added++;
        }
    }
}
