// --------------------------------------------------
// ThemeEditor.Common - LZUtil.cs
// --------------------------------------------------

namespace ThemeEditor.Common.Compression
{
    //  Taken from YATA Project
    //  https://github.com/Reisyukaku/YATA/blob/master/LZ11.cs
    //  That may have been taken from pk3DS
    //  https://github.com/kwsch/pk3DS/blob/master/pk3DS/3DS/LZSS.cs
    //  That was a possible port of the DSDecmp Code
    //  https://github.com/Barubary/dsdecmp/tree/master/CSharp/DSDecmp
    //  Credits for all those guys

    /// <summary>
    ///     Class for I/O-related utility methods.
    /// </summary>
    internal static class LZUtil
    {
        /// <summary>
        ///     Determine the maximum size of a LZ-compressed block starting at newPtr, using the already compressed data
        ///     starting at oldPtr. Takes O(inLength * oldLength) = O(n^2) time.
        /// </summary>
        /// <param name="newPtr">The start of the data that needs to be compressed.</param>
        /// <param name="newLength">
        ///     The number of bytes that still need to be compressed.
        ///     (or: the maximum number of bytes that _may_ be compressed into one block)
        /// </param>
        /// <param name="oldPtr">The start of the raw file.</param>
        /// <param name="oldLength">The number of bytes already compressed.</param>
        /// <param name="disp">The offset of the start of the longest block to refer to.</param>
        /// <param name="minDisp">The minimum allowed value for 'disp'.</param>
        /// <returns>The length of the longest sequence of bytes that can be copied from the already decompressed data.</returns>
        internal static unsafe int GetOccurrenceLength(
            byte* newPtr,
            int newLength,
            byte* oldPtr,
            int oldLength,
            out int disp,
            int minDisp = 1)
        {
            disp = 0;
            if (newLength == 0)
                return 0;
            int maxLength = 0;
            // try every possible 'disp' value (disp = oldLength - i)
            for (int i = 0; i < oldLength - minDisp; i++)
            {
                // work from the start of the old data to the end, to mimic the original implementation's behaviour
                // (and going from start to end or from end to start does not influence the compression ratio anyway)
                byte* currentOldStart = oldPtr + i;
                int currentLength = 0;
                // determine the length we can copy if we go back (oldLength - i) bytes
                // always check the next 'newLength' bytes, and not just the available 'old' bytes,
                // as the copied data can also originate from what we're currently trying to compress.
                for (int j = 0; j < newLength; j++)
                {
                    // stop when the bytes are no longer the same
                    if (*(currentOldStart + j) != *(newPtr + j))
                        break;
                    currentLength++;
                }
                // update the optimal value
                if (currentLength > maxLength)
                {
                    maxLength = currentLength;
                    disp = oldLength - i;
                    // if we cannot do better anyway, stop trying.
                    if (maxLength == newLength)
                        break;
                }
            }
            return maxLength;
        }
    }

    /// <summary>
    ///     Hash-chain replacement for <see cref="LZUtil.GetOccurrenceLength" /> (with minDisp = 1) that yields exactly
    ///     the same length and disp whenever the length is 3 or more, so the compressed output stays byte-identical.
    ///     Only candidates whose first three bytes match are compared, and the comparison itself is vectorized.
    /// </summary>
    internal sealed class LZMatchFinder
    {
        private const int HASH_BITS = 16;

        private readonly byte[] _data;
        private readonly int _length;
        private readonly int[] _head;
        private readonly int[] _prev;
        private int _inserted;

        public LZMatchFinder(byte[] data, int length)
        {
            _data = data;
            _length = length;
            _head = new int[1 << HASH_BITS];
            _prev = new int[System.Math.Max(length, 1)];
            System.Array.Fill(_head, -1);
        }

        private int Hash(int pos)
        {
            var v = (uint) (_data[pos] << 16 | _data[pos + 1] << 8 | _data[pos + 2]);
            return (int) ((v * 2654435761u) >> (32 - HASH_BITS));
        }

        /// <param name="pos">Position to compress from.</param>
        /// <param name="maxLength">Longest match allowed.</param>
        /// <param name="window">Number of bytes back that may be referenced.</param>
        /// <param name="disp">Displacement of the longest match.</param>
        public int Find(int pos, int maxLength, int window, out int disp)
        {
            disp = 0;
            // The reference implementation never uses disp 1, so the newest usable start is pos - 2
            var newest = pos - 2;
            while (_inserted <= newest && _inserted + 2 < _length)
            {
                var h = Hash(_inserted);
                _prev[_inserted] = _head[h];
                _head[h] = _inserted;
                _inserted++;
            }
            if (maxLength < 3 || newest < 0 || pos + 2 >= _length)
                return 0;

            var oldest = pos - window;
            var target = new System.ReadOnlySpan<byte>(_data, pos, maxLength);
            var best = 0;
            var bestStart = -1;
            for (var cand = _head[Hash(pos)]; cand >= oldest; cand = _prev[cand])
            {
                if (cand > newest)
                    continue;
                if (_data[cand] != _data[pos] || _data[cand + 1] != _data[pos + 1] || _data[cand + 2] != _data[pos + 2])
                    continue;
                // Candidates are visited newest first; ">=" keeps the oldest one among equal lengths, like the brute force search
                var len = System.MemoryExtensions.CommonPrefixLength(new System.ReadOnlySpan<byte>(_data, cand, maxLength), target);
                if (len >= best)
                {
                    best = len;
                    bestStart = cand;
                }
            }
            if (best < 3)
                return 0;
            disp = pos - bestStart;
            return best;
        }
    }
}
