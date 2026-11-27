// --------------------------------------------------
// Umami 3DS Theme Editor - AudioTags.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Names a song from its file, the way Themezer's BGM info is usually written ("Title - Artist"): from its tags (ID3 in
    ///     MP3, Vorbis or Opus comments in OGG, INFO in WAV), otherwise from the file name.
    /// </summary>
    public static class AudioTags
    {
        public static string Describe(string path)
        {
            string title = null, artist = null;
            try
            {
                var data = File.ReadAllBytes(path);
                ReadId3v2(data, ref title, ref artist);
                if (title == null)
                    ReadId3v1(data, ref title, ref artist);
                if (title == null)
                    ReadVorbisComments(data, ref title, ref artist);
                if (title == null)
                    ReadWavInfo(data, ref title, ref artist);
            }
            catch (Exception)
            {
                // A broken tag is no reason to fail: the file name still says something
            }
            if (string.IsNullOrWhiteSpace(title))
                return Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(artist) ? title.Trim() : title.Trim() + " - " + artist.Trim();
        }

        private static void ReadId3v2(byte[] d, ref string title, ref string artist)
        {
            if (d.Length < 10 || d[0] != 'I' || d[1] != 'D' || d[2] != '3')
                return;
            var version = d[3];
            var end = Math.Min(d.Length, 10 + Syncsafe(d, 6));
            var p = 10;
            if ((d[5] & 0x40) != 0 && version >= 3)
                p += version == 4 ? Syncsafe(d, p) : BigEndian(d, p) + 4;
            var idLength = version == 2 ? 3 : 4;
            var headerLength = version == 2 ? 6 : 10;
            while (p + headerLength <= end && d[p] != 0)
            {
                var id = Encoding.ASCII.GetString(d, p, idLength);
                var size = version == 2 ? (d[p + 3] << 16) | (d[p + 4] << 8) | d[p + 5]
                    : version == 4 ? Syncsafe(d, p + 4) : BigEndian(d, p + 4);
                var body = p + headerLength;
                if (size <= 0 || body + size > end)
                    break;
                if (id == "TIT2" || id == "TT2")
                    title = title ?? Text(d, body, size);
                else if (id == "TPE1" || id == "TP1")
                    artist = artist ?? Text(d, body, size);
                p = body + size;
            }
        }

        /// <summary>An ID3v2 text frame: an encoding byte, then the text.</summary>
        private static string Text(byte[] d, int start, int size)
        {
            var encoding = d[start] == 0 ? Encoding.Latin1
                : d[start] == 1 ? Encoding.Unicode
                : d[start] == 2 ? Encoding.BigEndianUnicode
                : Encoding.UTF8;
            var offset = start + 1;
            var length = size - 1;
            // UTF-16 with a byte order mark
            if (d[start] == 1 && length >= 2)
            {
                encoding = d[offset] == 0xFE && d[offset + 1] == 0xFF ? Encoding.BigEndianUnicode : Encoding.Unicode;
                if ((d[offset] == 0xFF && d[offset + 1] == 0xFE) || (d[offset] == 0xFE && d[offset + 1] == 0xFF))
                {
                    offset += 2;
                    length -= 2;
                }
            }
            var text = encoding.GetString(d, offset, Math.Max(0, length)).Split('\0')[0];
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static void ReadId3v1(byte[] d, ref string title, ref string artist)
        {
            if (d.Length < 128)
                return;
            var t = d.Length - 128;
            if (d[t] != 'T' || d[t + 1] != 'A' || d[t + 2] != 'G')
                return;
            string Field(int at) => Encoding.Latin1.GetString(d, t + at, 30).Split('\0')[0].Trim();
            var name = Field(3);
            if (name.Length > 0)
            {
                title = name;
                var by = Field(33);
                artist = by.Length > 0 ? by : null;
            }
        }

        /// <summary>The comment header of an OGG Vorbis ("\x03vorbis") or Opus ("OpusTags") stream.</summary>
        private static void ReadVorbisComments(byte[] d, ref string title, ref string artist)
        {
            var p = Find(d, Encoding.ASCII.GetBytes("\u0003vorbis"));
            var skip = 7;
            if (p < 0)
            {
                p = Find(d, Encoding.ASCII.GetBytes("OpusTags"));
                skip = 8;
            }
            if (p < 0)
                return;
            p += skip;
            p += 4 + LittleEndian(d, p);
            var count = LittleEndian(d, p);
            p += 4;
            for (var i = 0; i < count && p + 4 <= d.Length; i++)
            {
                var length = LittleEndian(d, p);
                p += 4;
                if (length < 0 || p + length > d.Length)
                    break;
                var entry = Encoding.UTF8.GetString(d, p, length);
                p += length;
                var eq = entry.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = entry.Substring(0, eq).ToUpperInvariant();
                if (key == "TITLE")
                    title = title ?? entry.Substring(eq + 1);
                else if (key == "ARTIST")
                    artist = artist ?? entry.Substring(eq + 1);
            }
        }

        /// <summary>A WAV file's LIST/INFO chunk (INAM = title, IART = artist).</summary>
        private static void ReadWavInfo(byte[] d, ref string title, ref string artist)
        {
            if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "WAVE")
                return;
            var info = Find(d, Encoding.ASCII.GetBytes("INFO"));
            if (info < 0)
                return;
            var p = info + 4;
            while (p + 8 <= d.Length)
            {
                var id = Encoding.ASCII.GetString(d, p, 4);
                var size = LittleEndian(d, p + 4);
                if (size < 0 || p + 8 + size > d.Length || !char.IsLetter(id[0]))
                    break;
                var value = Encoding.UTF8.GetString(d, p + 8, size).Split('\0')[0];
                if (id == "INAM")
                    title = value;
                else if (id == "IART")
                    artist = value;
                p += 8 + size + (size & 1);
            }
        }

        private static int Syncsafe(byte[] d, int p) => (d[p] & 0x7F) << 21 | (d[p + 1] & 0x7F) << 14 | (d[p + 2] & 0x7F) << 7 | (d[p + 3] & 0x7F);
        private static int BigEndian(byte[] d, int p) => d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3];
        private static int LittleEndian(byte[] d, int p) => p + 4 > d.Length ? -1 : d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24;

        private static int Find(byte[] d, byte[] what)
        {
            // Tags sit near the start; there is no need to look through megabytes of audio
            var limit = Math.Min(d.Length, 256 * 1024) - what.Length;
            for (var i = 0; i <= limit; i++)
            {
                var j = 0;
                while (j < what.Length && d[i + j] == what[j])
                    j++;
                if (j == what.Length)
                    return i;
            }
            return -1;
        }
    }
}
