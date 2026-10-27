// --------------------------------------------------
// Umami 3DS Theme Editor - ResourceLibrary.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App.Library
{
    /// <summary>
    ///     The resource library: ready-made folder/icon textures, sound effect sets and music a theme can use.
    ///     It merges the read-only pack that ships next to the editor with the user's own library
    ///     (<c>settings folder/Library</c>), which receives everything imported from theme collections.
    /// </summary>
    public sealed class ResourceLibrary
    {
        private const string IndexName = "index.json";

        private readonly Dictionary<string, string> _rootOf = new Dictionary<string, string>();
        private readonly object _lock = new object();
        private readonly HashSet<string> _ids = new HashSet<string>();

        public static ResourceLibrary Instance { get; } = new ResourceLibrary();

        /// <summary>Folder of the pack shipped with the editor (may not exist, e.g. in tests).</summary>
        public static string BundledRoot
        {
            get
            {
                // Next to the executable; inside a macOS .app the pack lives in Contents/Resources (thousands of data files
                // inside Contents/MacOS would be signed and verified as code, which takes minutes on first launch)
                var beside = Path.Combine(AppContext.BaseDirectory, "Library");
                var resources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Library"));
                return !File.Exists(Path.Combine(beside, "index.json")) && File.Exists(Path.Combine(resources, "index.json")) ? resources : beside;
            }
        }

        public static string UserRoot => Path.Combine(Settings.SettingsDirectory, "Library");

        public string WritableRoot { get; }
        public List<LibraryItem> Items { get; private set; } = new List<LibraryItem>();
        public List<string> Sources { get; private set; } = new List<string>();

        public ResourceLibrary() : this(null) {}

        /// <summary>Creates a library that writes into <paramref name="writableRoot" /> (the user library when null).</summary>
        public ResourceLibrary(string writableRoot)
        {
            WritableRoot = writableRoot ?? UserRoot;
        }

        public IEnumerable<LibraryItem> Of(LibraryKind kind)
        {
            lock (_lock)
                return Items.Where(i => i.Kind == kind).ToList();
        }

        public int Count(LibraryKind kind)
        {
            lock (_lock)
                return Items.Count(i => i.Kind == kind);
        }

        /// <summary>Full path of one of an item's files.</summary>
        public string PathOf(LibraryItem item, string file)
        {
            if (string.IsNullOrEmpty(file))
                return null;
            string root;
            lock (_lock)
                root = _rootOf.TryGetValue(item.Id, out var r) ? r : WritableRoot;
            return Path.Combine(root, file);
        }

        /// <summary>(Re)loads the bundled pack and the user library.</summary>
        public void Load(bool includeBundled = true)
        {
            var items = new List<LibraryItem>();
            var sources = new List<string>();
            var roots = new Dictionary<string, string>();
            var seen = new HashSet<string>();

            foreach (var root in includeBundled ? new[] { BundledRoot, WritableRoot } : new[] { WritableRoot })
            {
                var index = ReadIndex(root);
                if (index == null)
                    continue;
                foreach (var item in index.Items)
                {
                    if (!seen.Add(item.Id))
                        continue;
                    items.Add(item);
                    roots[item.Id] = root;
                }
                sources.AddRange(index.Sources);
            }
            lock (_lock)
            {
                Items = items;
                Sources = sources.Distinct().ToList();
                _rootOf.Clear();
                _ids.Clear();
                foreach (var i in items)
                    _ids.Add(i.Id);
                foreach (var kv in roots)
                    _rootOf[kv.Key] = kv.Value;
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(WritableRoot);
            List<LibraryItem> mine;
            lock (_lock)
                mine = Items.Where(i => !_rootOf.TryGetValue(i.Id, out var r) || string.Equals(r, WritableRoot, StringComparison.Ordinal)).ToList();
            var index = new LibraryIndex { Items = mine, Sources = Sources };
            File.WriteAllText(Path.Combine(WritableRoot, IndexName), JsonConvert.SerializeObject(index, Formatting.None));
        }

        private static LibraryIndex ReadIndex(string root)
        {
            try
            {
                var path = Path.Combine(root, IndexName);
                return File.Exists(path) ? JsonConvert.DeserializeObject<LibraryIndex>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool Contains(string id)
        {
            lock (_lock)
                return _ids.Contains(id);
        }

        /// <summary>
        ///     Atomically reserves an id for the caller: true for the first caller only. Parallel imports use it so
        ///     identical content is written by one thread (concurrent writes to the same file fail on Windows).
        /// </summary>
        public bool TryClaim(string id)
        {
            lock (_lock)
                return _ids.Add(id);
        }

        /// <summary>Adds an item (already written under <see cref="WritableRoot" />) unless it is a duplicate.</summary>
        public bool Add(LibraryItem item)
        {
            lock (_lock)
            {
                // The id may already be reserved by TryClaim (the importer claims it before writing the files)
                if (Items.Any(i => i.Id == item.Id))
                    return false;
                _ids.Add(item.Id);
                Items.Add(item);
                _rootOf[item.Id] = WritableRoot;
                return true;
            }
        }

        public void AddSource(string path)
        {
            lock (_lock)
                if (!Sources.Contains(path))
                    Sources.Add(path);
        }

        /// <summary>True when the item lives in the user's own (writable) library, i.e. it can be removed.</summary>
        public bool IsRemovable(LibraryItem item)
        {
            lock (_lock)
                return !_rootOf.TryGetValue(item.Id, out var r) || string.Equals(r, WritableRoot, StringComparison.Ordinal);
        }

        /// <summary>Removes an item (and its files) from the user's library. Items of the shipped pack cannot be removed.</summary>
        public bool Remove(LibraryItem item)
        {
            if (!IsRemovable(item))
                return false;
            foreach (var f in new[] { item.FileA, item.FileB, item.DataFile })
            {
                try
                {
                    var path = f == null ? null : PathOf(item, f);
                    if (path != null && File.Exists(path))
                        File.Delete(path);
                }
                catch (IOException) { }
            }
            lock (_lock)
            {
                Items.RemoveAll(i => i.Id == item.Id);
                _ids.Remove(item.Id);
                _rootOf.Remove(item.Id);
            }
            Save();
            return true;
        }

        public byte[] ReadAllBytes(LibraryItem item, string file)
        {
            return File.ReadAllBytes(PathOf(item, file));
        }
    }
}
