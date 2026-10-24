using System;
using System.ComponentModel;
using System.IO;

using ThemeEditor.Common.Audio;

using ThemeEditor.App.Properties;

namespace ThemeEditor.App.Experimental.CWAV
{
    public class CwavFile : INotifyPropertyChanged
    {
        public static readonly byte[] EmptyData = new byte[0];
        private byte[] _cwavData;

        private string _tag;

        private byte[] _wavData;

        public byte[] CwavData
        {
            get { return _cwavData; }
            set
            {
                _cwavData = value;
                _wavData = null;

                // Make Sure the Imported CWAV has a correct Size
                // This may result in chopped audio
                // However the file itself is broken in the first place for having the incorrect size header
                EnsureSize();

                OnPropertyChanged(nameof(CwavData));
                OnPropertyChanged(nameof(WavData));
                OnPropertyChanged(nameof(Size));
            }
        }

        private void EnsureSize()
        {
            if (Size == CwavData.Length)
                return;
            var bytes = BitConverter.GetBytes(_cwavData.Length);
            Buffer.BlockCopy(bytes, 0, _cwavData, 0x0C, 4);
            OnPropertyChanged(nameof(Size));
        }

        public int Size => CwavData?.Length > 0x10 ? BitConverter.ToInt32(_cwavData, 0x0C) : 0;

        public string Tag
        {
            get { return _tag; }
            set
            {
                if (value == _tag)
                    return;
                _tag = value;
                OnPropertyChanged(nameof(Tag));
            }
        }

        public byte[] WavData => _wavData ?? (_wavData = DecodeCwav(CwavData));

        public CwavFile(byte[] data)
        {
            CwavData = data;
        }

        public static byte[] DecodeCwav(byte[] cwavData)
        {
            if (cwavData == null || cwavData.Length == 0)
                return EmptyData;
            try
            {
                return CwavConverter.ToWav(cwavData);
            }
            catch (Exception)
            {
                return EmptyData;
            }
        }

        public static CwavFile Empty() => new CwavFile(EmptyData);

        /// <summary>Converts any supported audio file (WAV, MP3, OGG...) to a CWAV with DSP-ADPCM.</summary>
        public static byte[] EncodeCwav(string path)
        {
            try
            {
                var audio = BcstmConverter.Load(path);
                if (Settings.Default.EXP_ResampleWav)
                    audio = BcstmConverter.Transform(audio, true, 22050);
                return CwavConverter.Encode(audio, CwavCodec.DspAdpcm);
            }
            catch (Exception)
            {
                return EmptyData;
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
