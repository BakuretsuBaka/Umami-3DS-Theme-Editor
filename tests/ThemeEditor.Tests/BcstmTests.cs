using System;
using System.IO;
using System.Linq;
using System.Text;
using ThemeEditor.Common.Audio;
using Xunit;

namespace ThemeEditor.Tests
{
    public class BcstmTests
    {
        private static byte[] MakeSineWav(int rate, int channels, double seconds, double freq = 440)
        {
            var frames = (int) (rate * seconds);
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                var dataLen = frames * channels * 2;
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataLen);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16);
                bw.Write((short) 1);
                bw.Write((short) channels);
                bw.Write(rate);
                bw.Write(rate * channels * 2);
                bw.Write((short) (channels * 2));
                bw.Write((short) 16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataLen);
                for (var i = 0; i < frames; i++)
                for (var c = 0; c < channels; c++)
                    bw.Write((short) (Math.Sin(2 * Math.PI * freq * (c + 1) * i / rate) * 12000));
                return ms.ToArray();
            }
        }

        [Theory]
        [InlineData(BcstmCodec.DspAdpcm)]
        [InlineData(BcstmCodec.Pcm16)]
        public void WavRoundTripsThroughBcstm(BcstmCodec codec)
        {
            var wav = MakeSineWav(32000, 2, 1.0);
            var bcstm = BcstmConverter.ConvertToBcstm(new MemoryStream(wav), ".wav", new BcstmOptions { Codec = codec });

            Assert.Equal("CSTM", Encoding.ASCII.GetString(bcstm, 0, 4));

            var audio = BcstmConverter.ReadBcstm(bcstm);
            var info = BcstmConverter.GetInfo(audio);
            Assert.Equal(32000, info.SampleRate);
            Assert.Equal(2, info.Channels);
            Assert.Equal(32000, info.SampleCount);

            var outWav = BcstmConverter.BcstmToWav(bcstm);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(outWav, 0, 4));
        }

        [Fact]
        public void AdpcmIsMuchSmallerThanPcm()
        {
            var wav = MakeSineWav(32000, 2, 2.0);
            var adpcm = BcstmConverter.ConvertToBcstm(new MemoryStream(wav), ".wav");
            var pcm = BcstmConverter.ConvertToBcstm(new MemoryStream(wav), ".wav", new BcstmOptions { Codec = BcstmCodec.Pcm16 });
            Assert.True(adpcm.Length * 3 < pcm.Length, $"{adpcm.Length} vs {pcm.Length}");
        }

        [Fact]
        public void AdpcmDecodeStaysCloseToSource()
        {
            var wav = MakeSineWav(32000, 1, 0.5);
            var src = BcstmConverter.Load(new MemoryStream(wav), ".wav");
            var bcstm = BcstmConverter.Encode(src);
            var dec = BcstmConverter.ReadBcstm(bcstm).GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>().Channels[0];
            var orig = src.GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>().Channels[0];
            double err = 0, sig = 0;
            for (var i = 0; i < orig.Length; i++)
            {
                err += Math.Pow(orig[i] - dec[i], 2);
                sig += Math.Pow(orig[i], 2);
            }
            var snr = 10 * Math.Log10(sig / err);
            Assert.True(snr > 25, "SNR " + snr);
        }

        [Fact]
        public void ResampleIsAppliedAndOutputIsAlwaysStereo()
        {
            var wav = MakeSineWav(44100, 2, 1.0);
            // Even when mono is asked for, theme music is written as stereo
            var bcstm = BcstmConverter.ConvertToBcstm(new MemoryStream(wav), ".wav",
                new BcstmOptions { Mono = true, SampleRate = 32728 });
            var info = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(bcstm));
            Assert.Equal(2, info.Channels);
            Assert.Equal(32728, info.SampleRate);
            Assert.InRange(info.SampleCount, 32700, 32730);
        }

        [Theory]
        [InlineData(BcstmCodec.DspAdpcm)]
        [InlineData(BcstmCodec.Pcm16)]
        public void MonoSourcesAreDuplicatedToStereo(BcstmCodec codec)
        {
            var wav = MakeSineWav(32000, 1, 1.0);
            var bcstm = BcstmConverter.ConvertToBcstm(new MemoryStream(wav), ".wav", new BcstmOptions { Codec = codec });
            var audio = BcstmConverter.ReadBcstm(bcstm);
            Assert.Equal(2, BcstmConverter.GetInfo(audio).Channels);
            var ch = audio.GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>().Channels;
            Assert.Equal(ch[0], ch[1]);   // identical left and right
        }

        [Fact]
        public void LoopPointsAreWritten()
        {
            var wav = MakeSineWav(32000, 2, 2.0);
            var bcstm = BcstmConverter.ConvertToBcstm(new MemoryStream(wav), ".wav",
                new BcstmOptions { Loop = true, LoopStart = 14000, LoopEnd = 60000 });
            var info = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(bcstm));
            Assert.True(info.Looping);
            Assert.Equal(14000, info.LoopStart);
            Assert.Equal(60000, info.LoopEnd);
        }
    }
}

namespace ThemeEditor.Tests
{
    public class CwavTests
    {
        [Theory]
        [InlineData(ThemeEditor.Common.Audio.CwavCodec.DspAdpcm)]
        [InlineData(ThemeEditor.Common.Audio.CwavCodec.Pcm16)]
        [InlineData(ThemeEditor.Common.Audio.CwavCodec.Pcm8)]
        public void CwavRoundTrips(ThemeEditor.Common.Audio.CwavCodec codec)
        {
            var samples = new short[22050];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (short) (System.Math.Sin(i * 0.05) * 9000);
            var src = ThemeEditor.Common.Audio.BcstmConverter.FromPcm(new[] { samples }, 22050);
            var cwav = ThemeEditor.Common.Audio.CwavConverter.Encode(src, codec);
            Assert.Equal("CWAV", System.Text.Encoding.ASCII.GetString(cwav, 0, 4));
            Assert.Equal(cwav.Length, System.BitConverter.ToInt32(cwav, 0x0C));
            Assert.Equal(0, cwav.Length % 0x20);

            var back = ThemeEditor.Common.Audio.CwavConverter.Decode(cwav);
            var info = ThemeEditor.Common.Audio.BcstmConverter.GetInfo(back);
            Assert.Equal(22050, info.SampleRate);
            Assert.Equal(1, info.Channels);
            Assert.Equal(22050, info.SampleCount);

            var dec = back.GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>().Channels[0];
            double err = 0, sig = 0;
            for (var i = 0; i < samples.Length; i++) { err += System.Math.Pow(samples[i] - dec[i], 2); sig += System.Math.Pow(samples[i], 2); }
            Assert.True(10 * System.Math.Log10(sig / (err + 1)) > 20);
        }
    }
}
