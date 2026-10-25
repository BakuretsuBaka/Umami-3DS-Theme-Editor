using System;
using System.IO;
using System.Linq;
using ThemeEditor.Common.Audio;
using Xunit;

namespace ThemeEditor.Tests
{
    /// <summary>The HOME menu plays theme music only in the compact BCSTM 2.0 layout (VGAudio writes the 2.1 one).</summary>
    public class BcstmCompatTests
    {
        private static byte[] Tone(int channels)
        {
            var rate = 32000; var n = rate * 3; var ch = new short[channels][];
            for (var c = 0; c < channels; c++) { ch[c] = new short[n]; for (var i = 0; i < n; i++) ch[c][i] = (short) (Math.Sin(i * 2 * Math.PI * (330 + 110 * c) / rate) * 9000); }
            return BcstmConverter.Encode(BcstmConverter.FromPcm(ch, rate), new BcstmOptions { Codec = BcstmCodec.DspAdpcm, Loop = true, LoopStart = 0, LoopEnd = n });
        }

        private static int U32(byte[] d, int o) { return d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24); }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void EncodedFilesUseTheCompactLayout(int channels)
        {
            var b = Tone(channels);
            Assert.Equal(new byte[] { 0, 0, 0, 2 }, b.Skip(8).Take(4).ToArray());   // version 2.0
            var infoOff = U32(b, 0x18);
            Assert.Equal(0x18, U32(b, infoOff + 12));                                 // stream info
            Assert.Equal(0x50, U32(b, infoOff + 20) );                                // track table straight after a 0x38 byte stream info
            Assert.Equal(U32(b, 12), b.Length);                                       // header size field matches the file
        }

        [Fact]
        public void NormalizingKeepsTheAudioAndIsIdempotent()
        {
            var b = Tone(2);
            Assert.Equal(b, BcstmCompat.Normalize(b));
            var wav = BcstmConverter.BcstmToWav(b);
            Assert.True(wav.Length > 100000);
        }

        [Fact]
        public void RejectsNonBcstmUntouched()
        {
            var junk = new byte[] { 1, 2, 3, 4, 5 };
            Assert.Same(junk, BcstmCompat.Normalize(junk));
        }
    }
}
