// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewSounds.cs
// --------------------------------------------------

using System.Collections.Generic;
using System.IO;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Themes;
using ThemeEditor.Common.Audio;
using VGAudio.Formats;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Plays the theme's own HOME menu sound effects (the CWAV block) when the interactive preview is used,
    ///     so cursor moves, folders and page flips sound the way they will on the console.
    /// </summary>
    public static class PreviewSounds
    {
        private static byte[] _source;
        private static CwavBlock _block;
        private static readonly Dictionary<CwavKind, AudioData> Cache = new Dictionary<CwavKind, AudioData>();

        /// <summary>Master switch (the "Sound" toggle of the preview).</summary>
        public static bool Enabled => Properties.Settings.Default.Preview_SoundToggle;

        /// <summary>Raised with the kind of every sound the preview asked for, played or not (used by tests).</summary>
        public static event System.Action<CwavKind> Requested;

        /// <summary>True when the theme carries sound effects that can be played.</summary>
        public static bool HasSounds(ThemeViewModel theme)
        {
            return Load(theme) != null;
        }

        public static void Play(ThemeViewModel theme, CwavKind kind)
        {
            Requested?.Invoke(kind);
            if (!Enabled)
                return;
            var block = Load(theme);
            if (block == null)
                return;
            AudioData audio;
            if (!Cache.TryGetValue(kind, out audio))
            {
                CwavFile file;
                if (!block.Sounds.TryGetValue(kind, out file) || file.CwavData == null || file.CwavData.Length <= 0x10)
                    return;
                var wav = file.WavData;
                audio = wav == null || wav.Length == 0 ? null : BcstmConverter.Load(new MemoryStream(wav), ".wav");
                Cache[kind] = audio;
            }
            if (audio != null)
                AudioPlayer.PlayOneShot(audio, 0.85f);
        }

        private static CwavBlock Load(ThemeViewModel theme)
        {
            if (theme == null || !theme.Flags.SoundEffect)
                return null;
            var bytes = theme.CWavBytes;
            if (bytes == null || bytes.Length == 0)
                return null;
            if (!ReferenceEquals(bytes, _source))
            {
                _source = bytes;
                Cache.Clear();
                var block = new CwavBlock();
                _block = block.TryImport(bytes) ? block : null;
            }
            return _block;
        }
    }
}
