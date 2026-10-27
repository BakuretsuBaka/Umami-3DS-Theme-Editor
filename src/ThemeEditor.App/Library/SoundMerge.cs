// --------------------------------------------------
// Umami 3DS Theme Editor - SoundMerge.cs
// --------------------------------------------------

using System;
using ThemeEditor.App.Experimental.CWAV;

namespace ThemeEditor.App.Library
{
    /// <summary>Takes single sounds out of a sound effect block and puts them into another one.</summary>
    public static class SoundMerge
    {
        public const int MaxBlockBytes = 0x2DC00;

        /// <summary>The sound of one slot, or null when the block has none.</summary>
        public static byte[] Extract(byte[] block, CwavKind kind)
        {
            var b = new CwavBlock();
            b.TryImport(block ?? new byte[0]);
            var d = b.Sounds[kind].CwavData;
            return d != null && d.Length > 0x10 ? d : null;
        }

        /// <summary>Which of the eight slots of a block hold a sound.</summary>
        public static bool[] Present(byte[] block)
        {
            var b = new CwavBlock();
            b.TryImport(block ?? new byte[0]);
            var r = new bool[8];
            for (var i = 0; i < 8; i++)
                r[i] = b.Sounds[(CwavKind) i].CwavData != null && b.Sounds[(CwavKind) i].CwavData.Length > 0x10;
            return r;
        }

        /// <summary>
        ///     Returns <paramref name="themeBlock" /> (may be empty) with the sound of <paramref name="kind" /> replaced by the one
        ///     of <paramref name="sourceBlock" />. Throws when the source has no such sound or the result would be too large.
        /// </summary>
        public static byte[] Replace(byte[] themeBlock, byte[] sourceBlock, CwavKind kind)
        {
            var sound = Extract(sourceBlock, kind);
            if (sound == null)
                throw new InvalidOperationException("The sound effect set has no sound for this slot.");
            var target = new CwavBlock();
            if (themeBlock != null && themeBlock.Length > 0)
                target.TryImport(themeBlock);
            target.Sounds[kind].CwavData = sound;
            target.Sounds[kind].Tag = kind.ToString();
            var result = target.Generate();
            if (result.Length > MaxBlockBytes)
                throw new InvalidOperationException(string.Format("The sound effects would take {0} bytes, the limit is {1}.", result.Length, MaxBlockBytes));
            return result;
        }
    }
}
