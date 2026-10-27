using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using SkiaSharp;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Library;
using ThemeEditor.Common.Audio;
using Xunit;

namespace ThemeEditor.Tests
{
    public class LibraryTransferTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "umami-xfer-" + Guid.NewGuid().ToString("N"));

        public LibraryTransferTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        private string P(string name) { return Path.Combine(_dir, name); }

        private string Png(string name, int w, int h, byte shade)
        {
            using (var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                bmp.Erase(new SKColor(shade, (byte) (255 - shade), 90));
                using (var img = SKImage.FromBitmap(bmp)) using (var d = img.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(P(name), d.ToArray());
            }
            return P(name);
        }

        private static short[][] Tone(int seconds, double freq)
        {
            var n = 32000 * seconds; var ch = new short[2][];
            for (var c = 0; c < 2; c++) { ch[c] = new short[n]; for (var i = 0; i < n; i++) ch[c][i] = (short) (Math.Sin(i * 2 * Math.PI * freq / 32000) * 8000); }
            return ch;
        }

        private string Bcstm(string name, int seconds, BcstmCodec codec = BcstmCodec.DspAdpcm)
        {
            File.WriteAllBytes(P(name), BcstmConverter.Encode(BcstmConverter.FromPcm(Tone(seconds, 440), 32000), new BcstmOptions { Codec = codec }));
            return P(name);
        }

        private string SoundBlock(string name)
        {
            var pcm = new short[1][]; pcm[0] = new short[4000]; for (var i = 0; i < 4000; i++) pcm[0][i] = (short) (Math.Sin(i * 0.1) * 9000);
            var b = new CwavBlock();
            b.Sounds[CwavKind.Cursor].CwavData = CwavConverter.Encode(BcstmConverter.FromPcm(pcm, 22050), CwavCodec.DspAdpcm);
            File.WriteAllBytes(P(name), b.Generate());
            return P(name);
        }

        private ResourceLibrary Lib(string name) { var l = new ResourceLibrary(P(name)); l.Load(false); return l; }

        [Fact]
        public void ImportsLooseFilesAndFindsTheTexturePairs()
        {
            var lib = Lib("a");
            var r = LibraryTransfer.ImportFiles(lib, new[]
            {
                Bcstm("Song.bcstm", 2), SoundBlock("Chimes.bin"),
                Png("Neon_closed.png", 128, 64, 10), Png("Neon_open.png", 128, 64, 200),
                Png("Card_large.png", 64, 128, 50),                           // small icon is derived
                Png("wrong.png", 100, 100, 5),
            });
            Assert.Equal(4, r.Added);
            Assert.Equal(1, r.Invalid);
            Assert.Equal(1, lib.Count(LibraryKind.Music));
            Assert.Equal(1, lib.Count(LibraryKind.SoundEffects));
            Assert.Equal(1, lib.Count(LibraryKind.FolderTexture));
            Assert.Equal(1, lib.Count(LibraryKind.IconTexture));
            var folder = lib.Of(LibraryKind.FolderTexture).Single();
            Assert.Equal("Neon", folder.Name);
            using (var closed = SKBitmap.Decode(lib.PathOf(folder, folder.FileA))) using (var open = SKBitmap.Decode(lib.PathOf(folder, folder.FileB)))
                Assert.NotEqual(closed.GetPixel(1, 1), open.GetPixel(1, 1));   // closed is the one without "open" in the name
            var icon = lib.Of(LibraryKind.IconTexture).Single();
            using (var small = SKBitmap.Decode(lib.PathOf(icon, icon.FileB))) { Assert.Equal(32, small.Width); Assert.Equal(64, small.Height); }

            // Again: nothing new
            var again = LibraryTransfer.ImportFiles(lib, new[] { P("Song.bcstm"), P("Chimes.bin") });
            Assert.Equal(0, again.Added);
            Assert.Equal(2, again.Existing);
        }

        [Fact]
        public void RejectsMusicOverTheLimitAndNonsense()
        {
            var lib = Lib("a");
            File.WriteAllText(P("fake.bcstm"), "nope");
            var big = Bcstm("big.bcstm", 60, BcstmCodec.Pcm16);      // far more than 3 MB
            var r = LibraryTransfer.ImportFiles(lib, new[] { big, P("fake.bcstm"), P("notes.txt") });
            Assert.Equal(0, r.Added);
            Assert.Equal(3, r.Invalid);
            Assert.Empty(lib.Items);
        }

        [Fact]
        public void PackRoundTripsBetweenLibraries()
        {
            var a = Lib("a");
            LibraryTransfer.ImportFiles(a, new[] { Bcstm("Song.bcstm", 2), SoundBlock("Chimes.bin"), Png("N_closed.png", 128, 64, 10), Png("N_open.png", 128, 64, 99), Png("C_large.png", 64, 128, 70) });
            var pack = P("share" + LibraryTransfer.PackExtension);
            LibraryTransfer.ExportPack(a, a.Items, pack);
            Assert.True(File.Exists(pack));

            var b = Lib("b");
            var r = LibraryTransfer.ImportPack(b, pack);
            Assert.Equal(4, r.Added);
            Assert.Equal(0, r.Invalid);
            Assert.Equal(a.Items.Select(i => i.Id).OrderBy(x => x), b.Items.Select(i => i.Id).OrderBy(x => x));
            foreach (var item in b.Items)
                foreach (var f in new[] { item.FileA, item.FileB, item.DataFile }.Where(x => x != null))
                    Assert.Equal(File.ReadAllBytes(a.PathOf(a.Items.Single(i => i.Id == item.Id), f)), File.ReadAllBytes(b.PathOf(item, f)));
            Assert.True(b.Of(LibraryKind.Music).Single().Seconds > 1);

            // It was saved: a fresh library sees the items; importing again adds nothing
            var reopened = Lib("b");
            Assert.Equal(4, reopened.Items.Count);
            var second = LibraryTransfer.ImportPack(reopened, pack);
            Assert.Equal(0, second.Added);
            Assert.Equal(4, second.Existing);
        }

        private string Evil(string name, Action<LibraryIndex, ZipArchive> build)
        {
            var path = P(name);
            using (var fs = File.Create(path)) using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var index = new LibraryIndex();
                build(index, zip);
                using (var w = new StreamWriter(zip.CreateEntry("index.json").Open())) w.Write(JsonConvert.SerializeObject(index));
            }
            return path;
        }

        [Fact]
        public void RefusesUnsafeAndBrokenPacks()
        {
            var good = File.ReadAllBytes(Png("t.png", 128, 64, 1));
            Func<ZipArchive, string, byte[], int> add = (z, n, d) => { using (var s = z.CreateEntry(n).Open()) s.Write(d, 0, d.Length); return 0; };

            var traversal = Evil("evil1.umamipack", (i, z) =>
            {
                i.Items.Add(new LibraryItem { Id = "f-aaaa", Kind = LibraryKind.FolderTexture, Name = "x", FileA = "tex/../../escaped.png", FileB = "tex/b.png" });
                add(z, "tex/../../escaped.png", good); add(z, "tex/b.png", good);
            });
            var badId = Evil("evil2.umamipack", (i, z) =>
            {
                i.Items.Add(new LibraryItem { Id = "../../x", Kind = LibraryKind.FolderTexture, Name = "x", FileA = "tex/a.png", FileB = "tex/b.png" });
                add(z, "tex/a.png", good); add(z, "tex/b.png", good);
            });
            var wrongSize = Evil("evil3.umamipack", (i, z) =>
            {
                var small = File.ReadAllBytes(Png("s.png", 10, 10, 1));
                i.Items.Add(new LibraryItem { Id = "f-bbbb", Kind = LibraryKind.FolderTexture, Name = "x", FileA = "tex/a.png", FileB = "tex/b.png" });
                add(z, "tex/a.png", small); add(z, "tex/b.png", good);
            });
            var missing = Evil("evil4.umamipack", (i, z) => i.Items.Add(new LibraryItem { Id = "m-cccc", Kind = LibraryKind.Music, Name = "x", DataFile = "bgm/none.bcstm" }));
            var wrongKind = Evil("evil5.umamipack", (i, z) =>
            {
                i.Items.Add(new LibraryItem { Id = "m-dddd", Kind = LibraryKind.Music, Name = "x", FileA = "tex/a.png", DataFile = "bgm/a.bcstm" });
                add(z, "tex/a.png", good); add(z, "bgm/a.bcstm", good);
            });
            File.WriteAllText(P("garbage.umamipack"), "definitely not a zip");

            foreach (var name in new[] { "evil1", "evil2", "evil3", "evil4", "evil5" })
            {
                var lib = Lib("t_" + name);
                var r = LibraryTransfer.ImportPack(lib, P(name + ".umamipack"));
                Assert.Equal(0, r.Added);
                Assert.Equal(1, r.Invalid);
                Assert.Empty(lib.Items);
            }
            Assert.Equal(1, LibraryTransfer.ImportPack(Lib("g"), P("garbage.umamipack")).Invalid);
            Assert.False(File.Exists(Path.Combine(_dir, "escaped.png")) || File.Exists(Path.Combine(Path.GetDirectoryName(_dir), "escaped.png")));
        }

        [Fact]
        public void PurgeDeletesEverythingTheUserImported()
        {
            var lib = Lib("purge");
            Directory.CreateDirectory(Path.Combine(lib.WritableRoot, "sfx"));
            for (var i = 0; i < 3; i++)
            {
                var file = "sfx/s-test" + i + ".bin";
                File.WriteAllBytes(Path.Combine(lib.WritableRoot, file), new byte[1000 + i]);
                Assert.True(lib.Add(new LibraryItem { Id = "s-test" + i, Kind = LibraryKind.SoundEffects, Name = "Test " + i, DataFile = file, Bytes = 1000 + i }));
            }
            lib.AddSource("/some/themes");
            lib.Save();
            Assert.Equal(3, lib.Mine().Count);
            Assert.Equal(3003, lib.MyBytes());

            Assert.Equal(3, lib.PurgeMine());
            Assert.Empty(lib.Items);
            Assert.Empty(lib.Sources);
            Assert.Empty(Directory.GetFiles(Path.Combine(lib.WritableRoot, "sfx")));
            // The emptied index is what a fresh start sees
            Assert.Empty(Lib("purge").Items);
        }

        [Fact]
        public void PurgeCanAlsoDeleteTheBuiltInPresets()
        {
            var bundled = ResourceLibrary.BundledRoot;
            if (File.Exists(Path.Combine(bundled, "index.json")))
                return; // a real pack was built into the test output; never touch it
            try
            {
                Directory.CreateDirectory(Path.Combine(bundled, "sfx"));
                File.WriteAllBytes(Path.Combine(bundled, "sfx", "s-preset.bin"), new byte[500]);
                var index = new LibraryIndex();
                index.Items.Add(new LibraryItem { Id = "s-preset", Kind = LibraryKind.SoundEffects, Name = "Preset", Collection = "Umami", DataFile = "sfx/s-preset.bin", Bytes = 500 });
                File.WriteAllText(Path.Combine(bundled, "index.json"), JsonConvert.SerializeObject(index));

                var lib = new ResourceLibrary(P("mine"));
                lib.Load(true);
                Assert.Single(lib.Bundled());
                Assert.False(lib.IsRemovable(lib.Bundled()[0]));
                Assert.Equal(500, lib.BytesOf(lib.Bundled()));
                Assert.True(ResourceLibrary.CanDeleteBundled());

                Assert.Equal(1, lib.PurgeBundled());
                Assert.Empty(lib.Items);
                Assert.False(File.Exists(Path.Combine(bundled, "sfx", "s-preset.bin")));
                var reloaded = new ResourceLibrary(P("mine"));
                reloaded.Load(true);
                Assert.Empty(reloaded.Items);
            }
            finally
            {
                try { Directory.Delete(bundled, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void ExportsPlainFilesAndRemovesItems()
        {
            var lib = Lib("a");
            LibraryTransfer.ImportFiles(lib, new[] { Bcstm("Song.bcstm", 2), SoundBlock("Chimes.bin"), Png("N_closed.png", 128, 64, 10), Png("N_open.png", 128, 64, 99) });
            var music = lib.Of(LibraryKind.Music).Single(); var folder = lib.Of(LibraryKind.FolderTexture).Single(); var sfx = lib.Of(LibraryKind.SoundEffects).Single();

            Assert.Equal(new[] { "out.bcstm" }, LibraryTransfer.ExportFiles(lib, music, P("out")).Select(Path.GetFileName));
            Assert.Equal(new[] { "out_closed.png", "out_open.png" }, LibraryTransfer.ExportFiles(lib, folder, P("out")).Select(Path.GetFileName));
            Assert.Equal(new[] { "out.bin" }, LibraryTransfer.ExportFiles(lib, sfx, P("out")).Select(Path.GetFileName));
            Assert.Equal(File.ReadAllBytes(lib.PathOf(music, music.DataFile)), File.ReadAllBytes(P("out.bcstm")));

            var file = lib.PathOf(folder, folder.FileA);
            Assert.True(lib.IsRemovable(folder));
            Assert.True(lib.Remove(folder));
            Assert.False(File.Exists(file));
            Assert.Equal(0, lib.Count(LibraryKind.FolderTexture));
            Assert.Equal(2, Lib("a").Items.Count);       // saved
            Assert.False(lib.Contains(folder.Id));       // can be imported again
        }
    }
}
