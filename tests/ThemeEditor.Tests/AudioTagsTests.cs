using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ThemeEditor.App;
using Xunit;

namespace ThemeEditor.Tests
{
    public class AudioTagsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "umami-tags-" + Guid.NewGuid().ToString("N"));

        public AudioTagsTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, true);

        private string Write(string name, byte[] data)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        private static byte[] Id3v2(byte version, params (string Id, byte Encoding, byte[] Text)[] frames)
        {
            var body = new List<byte>();
            foreach (var (id, encoding, text) in frames)
            {
                var size = text.Length + 1;
                body.AddRange(Encoding.ASCII.GetBytes(id));
                body.AddRange(version == 4
                    ? new[] {(byte) ((size >> 21) & 0x7F), (byte) ((size >> 14) & 0x7F), (byte) ((size >> 7) & 0x7F), (byte) (size & 0x7F)}
                    : BitConverter.GetBytes(size).Reverse());
                body.AddRange(new byte[] {0, 0, encoding});
                body.AddRange(text);
            }
            var n = body.Count;
            var header = new List<byte> {(byte) 'I', (byte) 'D', (byte) '3', version, 0, 0,
                (byte) ((n >> 21) & 0x7F), (byte) ((n >> 14) & 0x7F), (byte) ((n >> 7) & 0x7F), (byte) (n & 0x7F)};
            // Some audio after the tag
            return header.Concat(body).Concat(new byte[] {0xFF, 0xFB, 0x90, 0x00}).ToArray();
        }

        [Fact]
        public void Mp3TagsGiveTitleAndArtist()
        {
            var latin = Write("track01.mp3", Id3v2(3, ("TIT2", 0, Encoding.Latin1.GetBytes("Gwyn, Lord of Cinder")), ("TPE1", 0, Encoding.Latin1.GetBytes("Motoi Sakuraba"))));
            Assert.Equal("Gwyn, Lord of Cinder - Motoi Sakuraba", AudioTags.Describe(latin));

            // ID3v2.4 in UTF-8, title only
            var utf8 = Write("x.mp3", Id3v2(4, ("TIT2", 3, Encoding.UTF8.GetBytes("Pokémon Center"))));
            Assert.Equal("Pokémon Center", AudioTags.Describe(utf8));

            // UTF-16 with a byte order mark
            var utf16 = Write("y.mp3", Id3v2(3, ("TIT2", 1, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Megalovania")).ToArray()),
                ("TPE1", 1, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Toby Fox")).ToArray())));
            Assert.Equal("Megalovania - Toby Fox", AudioTags.Describe(utf16));
        }

        [Fact]
        public void OldId3v1TagsCount()
        {
            var tag = new byte[128];
            Encoding.ASCII.GetBytes("TAG").CopyTo(tag, 0);
            Encoding.Latin1.GetBytes("Gerudo Valley").CopyTo(tag, 3);
            Encoding.Latin1.GetBytes("Koji Kondo").CopyTo(tag, 33);
            var path = Write("z.mp3", new byte[] {0xFF, 0xFB, 0x90, 0x00}.Concat(tag).ToArray());
            Assert.Equal("Gerudo Valley - Koji Kondo", AudioTags.Describe(path));
        }

        [Fact]
        public void OggCommentsCount()
        {
            var header = new List<byte>(Encoding.ASCII.GetBytes("OggS\0\u0002stuff"));
            header.Add(3);
            header.AddRange(Encoding.ASCII.GetBytes("vorbis"));
            void Str(string s) { var b = Encoding.UTF8.GetBytes(s); header.AddRange(BitConverter.GetBytes(b.Length)); header.AddRange(b); }
            Str("Xiph.Org libVorbis");
            header.AddRange(BitConverter.GetBytes(3));
            Str("ARTIST=Lena Raine");
            Str("album=Celeste");
            Str("title=Resurrections");
            Assert.Equal("Resurrections - Lena Raine", AudioTags.Describe(Write("song.ogg", header.ToArray())));
        }

        [Fact]
        public void WavInfoCounts()
        {
            var info = new List<byte>(Encoding.ASCII.GetBytes("INFO"));
            void Chunk(string id, string text)
            {
                var b = Encoding.ASCII.GetBytes(text + "\0");
                info.AddRange(Encoding.ASCII.GetBytes(id));
                info.AddRange(BitConverter.GetBytes(b.Length));
                info.AddRange(b);
                if (b.Length % 2 == 1) info.Add(0);
            }
            Chunk("INAM", "Ballad of the Windfish");
            Chunk("IART", "Nintendo");
            var wav = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVELIST").Concat(BitConverter.GetBytes(info.Count)).Concat(info).ToArray();
            Assert.Equal("Ballad of the Windfish - Nintendo", AudioTags.Describe(Write("a.wav", wav)));
        }

        [Fact]
        public void WithoutTagsTheFileNameIsUsed()
        {
            Assert.Equal("Dark Souls - Firelink Shrine", AudioTags.Describe(Write("Dark Souls - Firelink Shrine.mp3", new byte[] {0xFF, 0xFB, 0x90, 0x00, 1, 2, 3})));
            Assert.Equal("bgm", AudioTags.Describe(Write("bgm.bcstm", Encoding.ASCII.GetBytes("CSTMÿþ"))));
            Assert.Equal("missing", AudioTags.Describe(Path.Combine(_dir, "missing.ogg")));
        }
    }
}
