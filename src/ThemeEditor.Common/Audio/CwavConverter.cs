// --------------------------------------------------
// Umami 3DS Theme Editor - CwavConverter.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Text;
using VGAudio.Formats;
using VGAudio.Formats.GcAdpcm;
using VGAudio.Formats.Pcm16;

namespace ThemeEditor.Common.Audio
{
    public enum CwavCodec
    {
        DspAdpcm,
        Pcm16,
        Pcm8
    }

    /// <summary>
    ///     Reads and writes CWAV (BCWAV), the 3DS sound effect container used in theme sound effect packs.
    ///     Layout per https://www.3dbrew.org/wiki/BCWAV. Little endian only (what the 3DS uses).
    /// </summary>
    public static class CwavConverter
    {
        private const int HeaderSize = 0x40;

        public static AudioData Decode(byte[] data)
        {
            if (data == null || data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != "CWAV")
                throw new InvalidDataException("Not a CWAV file");

            var infoOffset = ReadInt(data, 0x18);
            var dataOffset = ReadInt(data, 0x24);
            if (infoOffset <= 0 || infoOffset + 0x20 > data.Length || dataOffset <= 0 || dataOffset + 8 > data.Length)
                throw new InvalidDataException("Corrupt CWAV header");

            var encoding = data[infoOffset + 8];
            var loop = data[infoOffset + 9] != 0;
            var rate = ReadInt(data, infoOffset + 0x0C);
            var loopStart = ReadInt(data, infoOffset + 0x10);
            var sampleCount = ReadInt(data, infoOffset + 0x14);

            var tableStart = infoOffset + 0x1C;
            var channelCount = ReadInt(data, tableStart);
            if (channelCount < 1 || channelCount > 8)
                throw new InvalidDataException("Bad channel count");

            var dataStart = dataOffset + 8;
            var channels = new short[channelCount][];
            var adpcmChannels = new GcAdpcmChannel[channelCount];

            for (var c = 0; c < channelCount; c++)
            {
                var chanInfo = tableStart + ReadInt(data, tableStart + 4 + c * 8 + 4);
                var sampleRefPos = chanInfo;
                var samplesOffset = dataStart + ReadInt(data, sampleRefPos + 4);
                var adpcmRefPos = chanInfo + 8;
                var adpcmType = (ushort) (data[adpcmRefPos] | data[adpcmRefPos + 1] << 8);
                var adpcmOffsetRaw = (uint) ReadInt(data, adpcmRefPos + 4);

                switch (encoding)
                {
                    case 0:
                    {
                        var pcm = new short[sampleCount];
                        for (var i = 0; i < sampleCount; i++)
                            pcm[i] = (short) ((sbyte) data[samplesOffset + i] << 8);
                        channels[c] = pcm;
                        break;
                    }
                    case 1:
                    {
                        var pcm = new short[sampleCount];
                        for (var i = 0; i < sampleCount; i++)
                            pcm[i] = BitConverter.ToInt16(data, samplesOffset + i * 2);
                        channels[c] = pcm;
                        break;
                    }
                    case 2:
                    {
                        if (adpcmType != 0x0300 || adpcmOffsetRaw == 0xFFFFFFFF)
                            throw new InvalidDataException("Missing DSP-ADPCM parameters");
                        var infoPos = chanInfo + (int) adpcmOffsetRaw;
                        var coefs = new short[16];
                        for (var i = 0; i < 16; i++)
                            coefs[i] = BitConverter.ToInt16(data, infoPos + i * 2);
                        var byteCount = (sampleCount + 13) / 14 * 8;
                        var adpcm = new byte[byteCount];
                        Buffer.BlockCopy(data, samplesOffset, adpcm, 0, Math.Min(byteCount, data.Length - samplesOffset));
                        var ctx = infoPos + 0x20;
                        var start = new GcAdpcmContext(data[ctx], BitConverter.ToInt16(data, ctx + 2), BitConverter.ToInt16(data, ctx + 4));
                        adpcmChannels[c] = new GcAdpcmChannelBuilder(adpcm, coefs, sampleCount) { StartContext = start }.Build();
                        channels[c] = adpcmChannels[c].GetPcmAudio();
                        break;
                    }
                    default:
                        throw new NotSupportedException("IMA-ADPCM CWAV files are not supported");
                }
            }

            var audio = new AudioData(new Pcm16Format(channels, rate));
            if (loop && loopStart < sampleCount)
                audio.SetLoop(true, loopStart, sampleCount);
            return audio;
        }

        public static byte[] ToWav(byte[] cwav)
        {
            return BcstmConverter.ToWav(Decode(cwav));
        }

        public static byte[] Encode(AudioData audio, CwavCodec codec = CwavCodec.DspAdpcm)
        {
            var pcm = audio.GetFormat<Pcm16Format>();
            var channelCount = pcm.Channels.Length;
            var sampleCount = pcm.Channels[0].Length;

            byte[][] channelData = new byte[channelCount][];
            short[][] coefs = null;
            GcAdpcmChannel[] adpcm = null;
            byte encoding;

            switch (codec)
            {
                case CwavCodec.DspAdpcm:
                {
                    encoding = 2;
                    var format = audio.GetFormat<GcAdpcmFormat>();
                    adpcm = format.Channels;
                    coefs = new short[channelCount][];
                    for (var c = 0; c < channelCount; c++)
                    {
                        channelData[c] = adpcm[c].GetAdpcmAudio();
                        coefs[c] = adpcm[c].Coefs;
                    }
                    break;
                }
                case CwavCodec.Pcm8:
                {
                    encoding = 0;
                    for (var c = 0; c < channelCount; c++)
                    {
                        channelData[c] = new byte[sampleCount];
                        for (var i = 0; i < sampleCount; i++)
                            channelData[c][i] = (byte) (sbyte) (pcm.Channels[c][i] >> 8);
                    }
                    break;
                }
                default:
                {
                    encoding = 1;
                    for (var c = 0; c < channelCount; c++)
                    {
                        channelData[c] = new byte[sampleCount * 2];
                        Buffer.BlockCopy(pcm.Channels[c], 0, channelData[c], 0, sampleCount * 2);
                    }
                    break;
                }
            }

            // ---- INFO block layout
            var tableSize = 4 + channelCount * 8;
            var channelInfoSize = 0x14 * channelCount;
            var dspInfoSize = encoding == 2 ? 0x2E * channelCount : 0;
            var infoContent = 0x14 + tableSize + channelInfoSize + dspInfoSize; // after block header (8) ... relative to +8
            var infoSize = Align(8 + infoContent, 0x20);

            // ---- DATA block layout: channel streams aligned to 0x20
            var dataStart = Align(HeaderSize + infoSize, 0x20); // DATA block offset
            var offsets = new int[channelCount];
            var cursor = 0;
            for (var c = 0; c < channelCount; c++)
            {
                cursor = Align(cursor, 0x20);
                offsets[c] = cursor;
                cursor += channelData[c].Length;
            }
            var dataBlockSize = Align(0x20 + cursor, 0x20); // header padded so sample data starts at +0x20
            var fileSize = dataStart + dataBlockSize;

            var file = new byte[fileSize];
            using (var ms = new MemoryStream(file))
            using (var w = new BinaryWriter(ms))
            {
                // File header
                w.Write(Encoding.ASCII.GetBytes("CWAV"));
                w.Write((ushort) 0xFEFF);
                w.Write((ushort) HeaderSize);
                w.Write(0x02010000);
                w.Write(fileSize);
                w.Write((ushort) 2);
                w.Write((ushort) 0);
                WriteRef(w, 0x7000, HeaderSize);
                w.Write(infoSize);
                WriteRef(w, 0x7001, dataStart);
                w.Write(dataBlockSize);

                // INFO
                ms.Position = HeaderSize;
                w.Write(Encoding.ASCII.GetBytes("INFO"));
                w.Write(infoSize);
                w.Write(encoding);
                w.Write((byte) (pcm.Looping ? 1 : 0));
                w.Write((ushort) 0);
                w.Write(pcm.SampleRate);
                w.Write(pcm.Looping ? pcm.LoopStart : 0);
                w.Write(sampleCount);
                w.Write(0); // reserved

                var tablePos = (int) ms.Position;
                w.Write(channelCount);
                var infoBase = tablePos; // offsets relative to the count field
                var chanInfoStart = tablePos + tableSize;
                for (var c = 0; c < channelCount; c++)
                    WriteRef(w, 0x7100, chanInfoStart - infoBase + c * 0x14);

                var dspStart = chanInfoStart + channelInfoSize;
                for (var c = 0; c < channelCount; c++)
                {
                    var entry = chanInfoStart + c * 0x14;
                    ms.Position = entry;
                    WriteRef(w, 0x1F00, offsets[c] + 0x18); // relative to the DATA block's data field (+8); samples start at +0x20
                    if (encoding == 2)
                    {
                        var dsp = dspStart + c * 0x2E;
                        WriteRef(w, 0x0300, dsp - entry); // relative to the channel info entry
                    }
                    else
                    {
                        WriteRef(w, 0, unchecked((int) 0xFFFFFFFF));
                    }
                    w.Write(0);
                }

                if (encoding == 2)
                {
                    for (var c = 0; c < channelCount; c++)
                    {
                        ms.Position = dspStart + c * 0x2E;
                        foreach (var coef in coefs[c])
                            w.Write(coef);
                        var ch = adpcm[c];
                        WriteContext(w, ch.GetPredScale(0), ch.GetHist1(0), ch.GetHist2(0));
                        var loopSample = pcm.Looping ? pcm.LoopStart : 0;
                        WriteContext(w, ch.GetPredScale(loopSample), ch.GetHist1(loopSample), ch.GetHist2(loopSample));
                        w.Write((ushort) 0);
                    }
                }

                // DATA
                ms.Position = dataStart;
                w.Write(Encoding.ASCII.GetBytes("DATA"));
                w.Write(dataBlockSize);
                for (var c = 0; c < channelCount; c++)
                {
                    ms.Position = dataStart + 0x20 + offsets[c];
                    w.Write(channelData[c]);
                }
            }
            return file;
        }

        private static void WriteRef(BinaryWriter w, int type, int offset)
        {
            w.Write((ushort) type);
            w.Write((ushort) 0);
            w.Write(offset);
        }

        private static void WriteContext(BinaryWriter w, byte predScale, short hist1, short hist2)
        {
            w.Write(predScale);
            w.Write((byte) 0);
            w.Write(hist1);
            w.Write(hist2);
        }

        private static int ReadInt(byte[] d, int pos)
        {
            return BitConverter.ToInt32(d, pos);
        }

        private static int Align(int v, int a)
        {
            return (v + a - 1) / a * a;
        }
    }
}
