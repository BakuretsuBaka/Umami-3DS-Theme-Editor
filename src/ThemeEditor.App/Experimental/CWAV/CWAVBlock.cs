using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ThemeEditor.App.Experimental.CWAV
{
    public class SoundDictionary : Dictionary<CwavKind, CwavFile> {}

    public partial class CwavBlock : IDisposable, INotifyPropertyChanged
    {
        public List<CwavFile> Scanned { get; }

        public SoundDictionary Sounds { get; }

        /// <summary>
        ///     Many themes carry a ninth sound in front of the page flip sounds: the 44 byte block before them starts
        ///     with its size and its data follows directly. It is kept so that re-generating the block does not drop it.
        /// </summary>
        public byte[] ExtraSound { get; private set; }

        private byte[] _ext;
        private int _headerVolume = 0x64;
        private int _headerKind = 0x02;
        private readonly Dictionary<int, int> _volumes = new Dictionary<int, int>();


        public CwavBlock()
        {
            Sounds = new SoundDictionary
            {
                {CwavKind.Cursor, CwavFile.Empty()},
                {CwavKind.Launch, CwavFile.Empty()},
                {CwavKind.Folder, CwavFile.Empty()},
                {CwavKind.Close, CwavFile.Empty()},
                {CwavKind.Frame0, CwavFile.Empty()},
                {CwavKind.Frame1, CwavFile.Empty()},
                {CwavKind.Frame2, CwavFile.Empty()},
                {CwavKind.OpenLid, CwavFile.Empty()},
            };
            Scanned = new List<CwavFile>(7);

            foreach (var pair in Sounds)
            {
                pair.Value.PropertyChanged += SoundEffectOnPropertyChanged;
            }
        }

        private void SoundEffectOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            OnPropertyChanged(nameof(EstimatedSize));
        }

        public int EstimatedSize
        {
            get
            {
                // 8 + 8*8 + 44
                return 116 + Sounds.Sum(pair => pair.Value.Size);
            }
        }

        private static bool PatternCheck(byte[] buffer, int nOffset, byte[] btPattern)
        {
            for (int x = 0; x < btPattern.Length; x++)
                if (btPattern[x] != buffer[nOffset + x])
                    return false;
            return true;
        }

        private static List<int> ScanBuffer(byte[] buffer, byte[] pattern)
        {
            List<int> positions = new List<int>();
            for (int i = 0; i < buffer.Length - pattern.Length; i++)
                if (PatternCheck(buffer, i, pattern))
                    positions.Add(i);
            return positions;
        }

        public byte[] Generate()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(_headerKind);
                bw.Write(_headerVolume);
                for (int i = 0; i <= 7; i++)
                {
                    var kind = (CwavKind) i;
                    var file = Sounds[kind];
                    if (kind == CwavKind.Frame0)
                    {
                        if (_ext != null)
                        {
                            bw.Write(_ext);
                            if (ExtraSound != null)
                                bw.Write(ExtraSound);
                        }
                        else
                        {
                            int[] extData =
                            {
                                0x00, 0x64, //
                                0x00, 0x64, //
                                0x64, 0x00, //
                                0x64, 0x64, //
                                0x00, 0x00, //
                                0x00
                            };
                            foreach (var j in extData)
                                bw.Write(j);
                        }
                    }
                    bw.Write(file.Size);
                    // Keep the volume a theme came with; new sounds get the editor's defaults
                    bw.Write(_volumes.TryGetValue(i, out var vol) ? vol : file.Size > 0 ? 0x50 : 1);
                    bw.Write(file.CwavData);
                }
                return ms.ToArray();
            }
        }

        public bool TryImport(byte[] data)
        {
            var retVal = true;
            if (data.Length > 0)
            {
                try
                {
                    using (var ms = new MemoryStream(data))
                    using (var br = new BinaryReader(ms))
                    {
                        _headerKind = br.ReadInt32();
                        _headerVolume = br.ReadInt32();
                        for (int i = 0; i <= 7; i++)
                        {
                            if (ms.Position == ms.Length)
                                break;

                            var kind = (CwavKind) i;
                            if (kind == CwavKind.Frame0)
                            {
                                var ext = br.ReadBytes(44); // ExtData
                                var extSize = ext.Length == 44 ? BitConverter.ToInt32(ext, 0) : 0;
                                if (ext.Length == 44)
                                    _ext = ext;
                                // A non zero first value is the size of an extra sound whose data follows right away
                                if (extSize > 0 && ms.Position + extSize <= ms.Length && Encoding.ASCII.GetString(data, (int) ms.Position, 4) == "CWAV")
                                    ExtraSound = br.ReadBytes(extSize);
                            }

                            var sz = br.ReadInt32();
                            var volume = br.ReadInt32();
                            _volumes[i] = volume;
                            if (sz == 0)
                                continue;
                            // Probably a Mismatch
                            // Or an invalid CWAV File Header
                            // Read to End of Block and pray
                            if (ms.Position + sz > ms.Length)
                            {
                                retVal = false;
                                sz = (int)(ms.Length - ms.Position);
                            }
                            var magic = Encoding.ASCII.GetString(data, (int) ms.Position, 4);
                            if (magic != "CWAV")
                                break;
                            var cData = br.ReadBytes(sz);
                            
                            Sounds[kind].CwavData = cData;
                            Sounds[kind].Tag = kind.ToString();
                        }
                    }
                }
                catch
                {
                    retVal = false;
                    // Ignore
                }
            }
            PatternScan(data);
            return retVal;
        }

        private void PatternScan(byte[] blockData)
        {
            var magic = "CWAV".Select(c => (byte) c).ToArray();
            var indexes = ScanBuffer(blockData, magic);
            foreach (int index in indexes)
            {
                var sz = BitConverter.ToInt32(blockData, index + 0x0C);

                // Probably a Mismatch
                // Or an invalid CWAV File Header
                // Read to End of Block and pray
                if (index + sz > blockData.Length)
                {
                    sz = blockData.Length - index;
                }

                byte[] fileData = new byte[sz];
                Buffer.BlockCopy(blockData, index, fileData, 0, sz);
                var cwav = new CwavFile(fileData)
                {
                    Tag = "0x" + index.ToString("X8")
                };
                Scanned.Add(cwav);
            }
        }

        public void Dispose()
        {
            foreach (var pair in Sounds)
            {
                pair.Value.PropertyChanged -= SoundEffectOnPropertyChanged;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
