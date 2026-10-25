using System;

namespace ThemeEditor.Common.Audio
{
    /// <summary>
    ///     Rewrites a BCSTM into the layout the 3DS HOME menu accepts for theme music.
    ///     VGAudio writes format version 2.1 whose stream-info block carries an extra 12 byte extension (region info
    ///     and original loop points). The HOME menu rejects such files (the theme plays without music), while the
    ///     version 2.0 layout used by Nintendo's own tools and by working Theme Plaza themes plays fine.
    /// </summary>
    public static class BcstmCompat
    {
        private const int CompactStreamInfoSize = 0x38;

        private static int U16(byte[] d, int o) { return d[o] | (d[o + 1] << 8); }
        private static int U32(byte[] d, int o) { return d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24); }
        private static void Put32(byte[] d, int o, int v) { d[o] = (byte) v; d[o + 1] = (byte) (v >> 8); d[o + 2] = (byte) (v >> 16); d[o + 3] = (byte) (v >> 24); }

        /// <summary>Returns the data converted to the compact 2.0 layout (or unchanged if it already is, or is no BCSTM).</summary>
        public static byte[] Normalize(byte[] bcstm)
        {
            if (bcstm == null || bcstm.Length < 0x80 || bcstm[0] != 'C' || bcstm[1] != 'S' || bcstm[2] != 'T' || bcstm[3] != 'M')
                return bcstm;

            // Locate the INFO block through the section table
            var sections = U16(bcstm, 0x10);
            var infoOff = -1;
            var infoSize = 0;
            for (var i = 0; i < sections; i++)
            {
                var o = 0x14 + i * 12;
                if (U16(bcstm, o) == 0x4000)
                {
                    infoOff = U32(bcstm, o + 4);
                    infoSize = U32(bcstm, o + 8);
                }
            }
            if (infoOff < 0 || infoOff + infoSize > bcstm.Length || infoSize < 0x40)
                return bcstm;

            // INFO: magic, size, then three references (stream, track, channel); offsets are relative to INFO + 8
            var stream = U32(bcstm, infoOff + 12);
            var track = U32(bcstm, infoOff + 20);
            var streamLen = track - stream;
            if (streamLen <= CompactStreamInfoSize)
            {
                SetVersion20(bcstm);
                return bcstm;
            }

            var extra = streamLen - CompactStreamInfoSize;
            var result = (byte[]) bcstm.Clone();
            var streamAbs = infoOff + 8 + stream;
            // Pull everything after the dropped extension forward; the block keeps its size (the tail becomes zero padding)
            var tailStart = streamAbs + streamLen;
            var infoEnd = infoOff + infoSize;
            Buffer.BlockCopy(bcstm, tailStart, result, streamAbs + CompactStreamInfoSize, infoEnd - tailStart);
            for (var i = infoEnd - extra; i < infoEnd; i++)
                result[i] = 0;

            // Track and channel reference tables moved forward by 'extra' bytes
            Put32(result, infoOff + 20, track - extra);
            Put32(result, infoOff + 28, U32(bcstm, infoOff + 28) - extra);
            SetVersion20(result);
            return result;
        }

        private static void SetVersion20(byte[] d)
        {
            d[8] = 0;
            d[9] = 0;
            d[10] = 0;
            d[11] = 2;
        }
    }
}
