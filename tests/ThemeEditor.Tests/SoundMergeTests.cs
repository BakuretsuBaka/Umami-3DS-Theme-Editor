using System;
using System.IO;
using System.Linq;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Library;
using ThemeEditor.Common.Audio;
using Xunit;

namespace ThemeEditor.Tests
{
    public class SoundMergeTests
    {
        private static byte[] Cwav(double freq, double seconds = 0.2)
        {
            var n = (int) (22050 * seconds); var pcm = new short[1][]; pcm[0] = new short[n];
            for (var i = 0; i < n; i++) pcm[0][i] = (short) (Math.Sin(i * 2 * Math.PI * freq / 22050) * 9000);
            return CwavConverter.Encode(BcstmConverter.FromPcm(pcm, 22050), CwavCodec.DspAdpcm);
        }

        private static byte[] Block(params (CwavKind kind, byte[] data)[] sounds)
        {
            var b = new CwavBlock();
            foreach (var (k, d) in sounds) b.Sounds[k].CwavData = d;
            return b.Generate();
        }

        /// <summary>The layout most real themes use: the 44 byte block in front of the page sounds starts with the size of an extra sound whose data follows it.</summary>
        private static byte[] BlockWithExtraSound(byte[] cursor, byte[] extra, byte[] lid)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(2); bw.Write(0x28);
                bw.Write(cursor.Length); bw.Write(0x64); bw.Write(cursor);
                for (var i = 0; i < 3; i++) { bw.Write(0); bw.Write(0x64); }          // launch, folder, close empty
                foreach (var v in new[] { extra.Length, 0x64, 0, 0x64, 0x64, 0, 0x64, 0x64, 0, 0, 1 }) bw.Write(v);
                bw.Write(extra);
                for (var i = 0; i < 3; i++) { bw.Write(0); bw.Write(0x64); }          // page sounds empty
                bw.Write(lid.Length); bw.Write(0x64); bw.Write(lid);
                return ms.ToArray();
            }
        }

        [Fact]
        public void ReadsTheExtraSoundLayoutAndKeepsTheExtraSoundWhenRegenerating()
        {
            byte[] cursor = Cwav(900), extra = Cwav(500), lid = Cwav(300);
            var data = BlockWithExtraSound(cursor, extra, lid);
            var b = new CwavBlock();
            Assert.True(b.TryImport(data));
            Assert.Equal(cursor, b.Sounds[CwavKind.Cursor].CwavData);
            Assert.Equal(lid, b.Sounds[CwavKind.OpenLid].CwavData);
            Assert.Equal(extra, b.ExtraSound);
            Assert.Equal(new[] { true, false, false, false, false, false, false, true }, SoundMerge.Present(data));

            // Regenerating gives the same block back, extra sound included
            Assert.Equal(data, b.Generate());
        }

        [Fact]
        public void ReplacesOnlyTheChosenSlot()
        {
            byte[] c1 = Cwav(800), l1 = Cwav(400), f2 = Cwav(600), c2 = Cwav(1000);
            var theme = Block((CwavKind.Cursor, c1), (CwavKind.Launch, l1));
            var preset = Block((CwavKind.Folder, f2), (CwavKind.Cursor, c2));

            var withFolder = SoundMerge.Replace(theme, preset, CwavKind.Folder);
            Assert.Equal(c1, SoundMerge.Extract(withFolder, CwavKind.Cursor));      // untouched
            Assert.Equal(l1, SoundMerge.Extract(withFolder, CwavKind.Launch));      // untouched
            Assert.Equal(f2, SoundMerge.Extract(withFolder, CwavKind.Folder));      // new

            var swapped = SoundMerge.Replace(withFolder, preset, CwavKind.Cursor);
            Assert.Equal(c2, SoundMerge.Extract(swapped, CwavKind.Cursor));
            Assert.Equal(l1, SoundMerge.Extract(swapped, CwavKind.Launch));
            Assert.Equal(f2, SoundMerge.Extract(swapped, CwavKind.Folder));
        }

        [Fact]
        public void WorksOnAThemeWithoutSoundEffectsAndRejectsMissingOrTooBigSounds()
        {
            var preset = Block((CwavKind.Launch, Cwav(700)));
            var fresh = SoundMerge.Replace(new byte[0], preset, CwavKind.Launch);
            Assert.NotNull(SoundMerge.Extract(fresh, CwavKind.Launch));
            Assert.Null(SoundMerge.Extract(fresh, CwavKind.Cursor));

            Assert.Throws<InvalidOperationException>(() => SoundMerge.Replace(fresh, preset, CwavKind.Close));   // preset has no such sound

            var big = Block((CwavKind.Launch, Cwav(300, 12)), (CwavKind.Folder, Cwav(310, 12)));
            Assert.Throws<InvalidOperationException>(() => SoundMerge.Replace(big, Block((CwavKind.Close, Cwav(320, 12))), CwavKind.Close));
        }
    }
}
