// --------------------------------------------------
// Umami 3DS Theme Editor - BcstmConverter.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.GcAdpcm;
using VGAudio.Formats.Pcm16;

namespace ThemeEditor.Common.Audio
{
    public enum BcstmCodec
    {
        /// <summary>Nintendo DSP-ADPCM, ~3.5x smaller than PCM16. Recommended for themes.</summary>
        DspAdpcm,

        /// <summary>Uncompressed 16-bit PCM. Lossless but large.</summary>
        Pcm16
    }

    public sealed class BcstmOptions
    {
        public BcstmCodec Codec { get; set; } = BcstmCodec.DspAdpcm;

        /// <summary>Mix all channels down to a single channel.</summary>
        public bool Mono { get; set; }

        /// <summary>Resample to this rate (Hz). Null or 0 keeps the source rate.</summary>
        public int? SampleRate { get; set; }

        /// <summary>When set the source's own loop points are discarded and this setting is used instead.</summary>
        public bool? Loop { get; set; }

        /// <summary>Loop start in samples. Ignored unless Loop is true.</summary>
        public int LoopStart { get; set; }

        /// <summary>Loop end in samples (exclusive). Values &lt;= 0 mean "end of stream".</summary>
        public int LoopEnd { get; set; }

        /// <summary>Keep this many seconds at most; 0 disables trimming.</summary>
        public double MaxSeconds { get; set; }
    }

    public sealed class BcstmInfo
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int SampleCount { get; set; }
        public bool Looping { get; set; }
        public int LoopStart { get; set; }
        public int LoopEnd { get; set; }
        public double Duration => SampleRate == 0 ? 0 : (double) SampleCount / SampleRate;
    }

    /// <summary>
    ///     Converts between BCSTM (the 3DS HOME-menu theme music container) and common audio formats.
    /// </summary>
    public static class BcstmConverter
    {
        /// <summary>Largest BGM.BCSTM the HOME menu will accept in a theme.</summary>
        public const int MaxThemeBgmBytes = 3 * 1024 * 1024;

        public static readonly string[] ImportExtensions =
        {
            ".wav", ".mp3", ".ogg", ".bcstm", ".bfstm", ".brstm", ".bcwav", ".dsp", ".idsp", ".adx", ".hps"
        };

        public static bool IsSupportedInput(string path)
        {
            var ext = Path.GetExtension(path);
            return ext != null && Array.IndexOf(ImportExtensions, ext.ToLowerInvariant()) >= 0;
        }

        // -------------------------------------------------- Reading

        public static AudioData Load(string path)
        {
            using (var fs = File.OpenRead(path))
                return Load(fs, Path.GetExtension(path));
        }

        public static AudioData Load(Stream stream, string extension)
        {
            var ext = (extension ?? string.Empty).ToLowerInvariant();
            switch (ext)
            {
                case ".mp3":
                    return DecodeMp3(stream);
                case ".ogg":
                    return DecodeOgg(stream);
            }

            var data = ReadAll(stream);
            switch (ext)
            {
                case ".wav":
                    return new WaveReader().Read(data);
                case ".bcstm":
                case ".bfstm":
                    return new BCFstmReader().Read(data);
                case ".brstm":
                    return new BrstmReader().Read(data);
                case ".bcwav":
                case ".bfwav":
                    return new BCFstmReader().Read(data);
                case ".dsp":
                    return new VGAudio.Containers.Dsp.DspReader().Read(data);
                case ".idsp":
                    return new VGAudio.Containers.Idsp.IdspReader().Read(data);
                case ".adx":
                    return new VGAudio.Containers.Adx.AdxReader().Read(data);
                case ".hps":
                    return new VGAudio.Containers.Hps.HpsReader().Read(data);
                default:
                    throw new NotSupportedException("Unsupported audio format: " + extension);
            }
        }

        public static AudioData ReadBcstm(byte[] bcstm)
        {
            return new BCFstmReader().Read(bcstm);
        }

        public static BcstmInfo GetInfo(AudioData audio)
        {
            var f = audio.GetAllFormats().GetEnumerator();
            f.MoveNext();
            IAudioFormat fmt = f.Current;
            return new BcstmInfo
            {
                SampleRate = fmt.SampleRate,
                Channels = fmt.ChannelCount,
                SampleCount = fmt.SampleCount,
                Looping = fmt.Looping,
                LoopStart = fmt.LoopStart,
                LoopEnd = fmt.LoopEnd
            };
        }

        // -------------------------------------------------- Writing

        public static byte[] ConvertToBcstm(string path, BcstmOptions options = null)
        {
            return Encode(Load(path), options);
        }

        public static byte[] ConvertToBcstm(Stream stream, string extension, BcstmOptions options = null)
        {
            return Encode(Load(stream, extension), options);
        }

        public static byte[] Encode(AudioData audio, BcstmOptions options = null)
        {
            options = options ?? new BcstmOptions();

            var pcm = audio.GetFormat<Pcm16Format>();
            var channels = CloneChannels(pcm.Channels);
            var rate = pcm.SampleRate;

            // Loop information from source (in source samples)
            var loop = pcm.Looping;
            var loopStart = pcm.LoopStart;
            var loopEnd = pcm.LoopEnd;
            var srcCount = channels[0].Length;

            if (options.Mono && channels.Length > 1)
                channels = new[] { MixDown(channels) };

            if (options.SampleRate.HasValue && options.SampleRate.Value > 0 && options.SampleRate.Value != rate)
            {
                var ratio = (double) options.SampleRate.Value / rate;
                for (var i = 0; i < channels.Length; i++)
                    channels[i] = Resample(channels[i], ratio);
                loopStart = (int) Math.Round(loopStart * ratio);
                loopEnd = (int) Math.Round(loopEnd * ratio);
                rate = options.SampleRate.Value;
                srcCount = channels[0].Length;
            }

            if (options.MaxSeconds > 0)
            {
                var max = (int) (options.MaxSeconds * rate);
                if (srcCount > max)
                {
                    for (var i = 0; i < channels.Length; i++)
                        Array.Resize(ref channels[i], max);
                    srcCount = max;
                    if (loopEnd > max)
                        loopEnd = max;
                }
            }

            if (options.Loop.HasValue)
            {
                loop = options.Loop.Value;
                loopStart = options.LoopStart;
                loopEnd = options.LoopEnd;
            }
            if (loopEnd <= 0 || loopEnd > srcCount)
                loopEnd = srcCount;
            if (loopStart < 0 || loopStart >= loopEnd)
                loopStart = 0;

            var outAudio = new AudioData(new Pcm16Format(channels, rate));
            outAudio.SetLoop(loop, loopStart, loopEnd);

            var config = new BxstmConfiguration
            {
                Codec = options.Codec == BcstmCodec.Pcm16 ? NwCodec.Pcm16Bit : NwCodec.GcAdpcm,
                // GC-ADPCM frames are 14 samples; keep loop points aligned so no encoder drift occurs.
                LoopPointAlignment = options.Codec == BcstmCodec.DspAdpcm ? 14 : 1
            };
            // The HOME menu only plays the compact 2.0 layout, not the 2.1 one VGAudio writes
            return BcstmCompat.Normalize(new BCFstmWriter(NwTarget.Ctr).GetFile(outAudio, config));
        }

        /// <summary>Mixes down and/or resamples audio (used for small sound effects).</summary>
        public static AudioData Transform(AudioData audio, bool mono, int? sampleRate)
        {
            var pcm = audio.GetFormat<Pcm16Format>();
            var channels = CloneChannels(pcm.Channels);
            var rate = pcm.SampleRate;
            if (mono && channels.Length > 1)
                channels = new[] { MixDown(channels) };
            if (sampleRate.HasValue && sampleRate.Value > 0 && sampleRate.Value != rate)
            {
                var ratio = (double) sampleRate.Value / rate;
                for (var i = 0; i < channels.Length; i++)
                    channels[i] = Resample(channels[i], ratio);
                rate = sampleRate.Value;
            }
            return FromPcm(channels, rate);
        }

        /// <summary>Decodes BCSTM bytes (or any supported container) into a 16-bit PCM WAV file.</summary>
        public static byte[] BcstmToWav(byte[] bcstm)
        {
            return ToWav(ReadBcstm(bcstm));
        }

        public static byte[] ToWav(AudioData audio)
        {
            return new WaveWriter().GetFile(audio, new WaveConfiguration { Codec = WaveCodec.Pcm16Bit });
        }

        // -------------------------------------------------- Helpers

        public static AudioData FromPcm(short[][] channels, int sampleRate)
        {
            return new AudioData(new Pcm16Format(channels, sampleRate));
        }

        private static short[][] CloneChannels(short[][] src)
        {
            var dst = new short[src.Length][];
            for (var i = 0; i < src.Length; i++)
                dst[i] = (short[]) src[i].Clone();
            return dst;
        }

        private static short[] MixDown(short[][] channels)
        {
            var n = channels[0].Length;
            var mix = new short[n];
            for (var i = 0; i < n; i++)
            {
                var sum = 0;
                for (var c = 0; c < channels.Length; c++)
                    sum += channels[c][i];
                mix[i] = (short) (sum / channels.Length);
            }
            return mix;
        }

        /// <summary>Catmull-Rom resampler. Plenty for music previews/themes.</summary>
        private static short[] Resample(short[] src, double ratio)
        {
            if (src.Length < 4)
                return src;
            var outLen = (int) Math.Floor(src.Length * ratio);
            var dst = new short[outLen];
            // Low-pass by box filter when downsampling to limit aliasing
            if (ratio < 1)
                src = BoxFilter(src, (int) Math.Ceiling(1 / ratio));
            for (var i = 0; i < outLen; i++)
            {
                var pos = i / ratio;
                var i1 = (int) pos;
                var t = pos - i1;
                float p0 = src[Math.Max(i1 - 1, 0)];
                float p1 = src[i1];
                float p2 = src[Math.Min(i1 + 1, src.Length - 1)];
                float p3 = src[Math.Min(i1 + 2, src.Length - 1)];
                var v = 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                               (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                dst[i] = (short) Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
            }
            return dst;
        }

        private static short[] BoxFilter(short[] src, int width)
        {
            if (width <= 1)
                return src;
            var dst = new short[src.Length];
            long sum = 0;
            var half = width / 2;
            // Sliding window average, centred
            var count = 0;
            for (var i = 0; i < Math.Min(src.Length, half); i++)
            {
                sum += src[i];
                count++;
            }
            for (var i = 0; i < src.Length; i++)
            {
                var add = i + half;
                if (add < src.Length)
                {
                    sum += src[add];
                    count++;
                }
                var rem = i - half - 1;
                if (rem >= 0)
                {
                    sum -= src[rem];
                    count--;
                }
                dst[i] = (short) (sum / Math.Max(count, 1));
            }
            return dst;
        }

        private static byte[] ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static AudioData DecodeMp3(Stream stream)
        {
            using (var mpeg = new NLayer.MpegFile(stream))
                return FromFloat(mpeg.SampleRate, mpeg.Channels, (buf, n) => mpeg.ReadSamples(buf, 0, n));
        }

        private static AudioData DecodeOgg(Stream stream)
        {
            using (var vorbis = new NVorbis.VorbisReader(stream, false))
                return FromFloat(vorbis.SampleRate, vorbis.Channels, (buf, n) => vorbis.ReadSamples(buf, 0, n));
        }

        private static AudioData FromFloat(int sampleRate, int channelCount, Func<float[], int, int> read)
        {
            var chunks = new List<float[]>();
            var total = 0;
            var buf = new float[channelCount * 4096];
            int got;
            while ((got = read(buf, buf.Length)) > 0)
            {
                var c = new float[got];
                Array.Copy(buf, c, got);
                chunks.Add(c);
                total += got;
            }

            var frames = total / channelCount;
            var channels = new short[channelCount][];
            for (var c = 0; c < channelCount; c++)
                channels[c] = new short[frames];

            var frame = 0;
            var ch = 0;
            foreach (var chunk in chunks)
            {
                foreach (var f in chunk)
                {
                    if (frame >= frames)
                        break;
                    var v = (int) Math.Round(f * 32767.0);
                    channels[ch][frame] = (short) Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
                    if (++ch == channelCount)
                    {
                        ch = 0;
                        frame++;
                    }
                }
            }
            return FromPcm(channels, sampleRate);
        }
    }
}
