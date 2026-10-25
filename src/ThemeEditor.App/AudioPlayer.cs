// --------------------------------------------------
// Umami 3DS Theme Editor - AudioPlayer.cs
// --------------------------------------------------

using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Avalonia.Threading;
using Silk.NET.OpenAL;
using ThemeEditor.Common.Audio;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Cross-platform audio output on top of OpenAL Soft (bundled for Windows, Linux and macOS, x64 and ARM).
    ///     Used for the BGM preview and the sound effect manager. Degrades to a silent no-op if no device exists.
    /// </summary>
    public sealed unsafe class AudioPlayer : INotifyPropertyChanged, IDisposable
    {
        private static AL _al;
        private static ALContext _alc;
        private static Device* _device;
        private static Context* _context;
        private static bool _initTried;
        private static bool _available;

        private readonly DispatcherTimer _poll;

        private uint _buffer;
        private uint _source;
        private float _volume = 1f;
        private bool _wasPlaying;

        public static AudioPlayer Instance { get; } = new AudioPlayer { Loop = true };

        public bool HasData { get; private set; }
        public bool Loop { get; set; }

        public bool IsPlaying
        {
            get
            {
                if (!HasData)
                    return false;
                int state;
                _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out state);
                return state == (int) SourceState.Playing;
            }
        }

        public ICommand PlayCommand { get; }
        public ICommand StopCommand { get; }

        public float Volume
        {
            get { return _volume; }
            set
            {
                if (Math.Abs(_volume - value) < 0.001f)
                    return;
                _volume = value;
                if (HasData)
                    _al.SetSourceProperty(_source, SourceFloat.Gain, _volume);
                OnPropertyChanged(nameof(Volume));
            }
        }

        public AudioPlayer()
        {
            PlayCommand = new RelayCommand(Play_Execute, () => HasData);
            StopCommand = new RelayCommand(Stop_Execute, () => HasData);
            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _poll.Tick += (s, e) => PollState();
        }

        public static bool IsAvailable
        {
            get
            {
                EnsureInit();
                return _available;
            }
        }

        private static void EnsureInit()
        {
            if (_initTried)
                return;
            _initTried = true;
            try
            {
                _alc = ALContext.GetApi(true);
                _al = AL.GetApi(true);
                _device = _alc.OpenDevice(string.Empty);
                if (_device == null)
                    return;
                _context = _alc.CreateContext(_device, null);
                _alc.MakeContextCurrent(_context);
                _available = true;
            }
            catch (Exception)
            {
                _available = false;
            }
        }

        public void ClearAudioData()
        {
            _poll.Stop();
            if (HasData)
            {
                _al.SourceStop(_source);
                _al.DeleteSource(_source);
                _al.DeleteBuffer(_buffer);
            }
            HasData = false;
            _wasPlaying = false;
            OnPropertyChanged(nameof(IsPlaying));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Loads a RIFF/WAVE file (any PCM the importer understands).</summary>
        public void SetAudioData(byte[] wavData)
        {
            ClearAudioData();
            if (wavData == null || wavData.Length == 0)
                return;
            SetAudio(BcstmConverter.Load(new MemoryStream(wavData), ".wav"));
        }

        private static readonly System.Collections.Generic.List<(uint Source, uint Buffer)> OneShots =
            new System.Collections.Generic.List<(uint, uint)>();

        /// <summary>Plays a short sound once, on its own source, without touching the BGM player. Overlapping calls mix.</summary>
        public static void PlayOneShot(AudioData audio, float gain = 1f)
        {
            EnsureInit();
            if (!_available || audio == null)
                return;

            // Free the sources that finished
            for (var i = OneShots.Count - 1; i >= 0; i--)
            {
                int state;
                _al.GetSourceProperty(OneShots[i].Source, GetSourceInteger.SourceState, out state);
                if (state == (int) SourceState.Playing)
                    continue;
                _al.DeleteSource(OneShots[i].Source);
                _al.DeleteBuffer(OneShots[i].Buffer);
                OneShots.RemoveAt(i);
            }
            if (OneShots.Count >= 24)
                return;

            var pcm = audio.GetFormat<Pcm16Format>();
            var channels = pcm.Channels;
            var frames = channels[0].Length;
            var count = Math.Min(channels.Length, 2);
            var data = new short[frames * count];
            for (var i = 0; i < frames; i++)
            for (var c = 0; c < count; c++)
                data[i * count + c] = channels[c][i];

            var buffer = _al.GenBuffer();
            fixed (short* p = data)
                _al.BufferData(buffer, count == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16, p, data.Length * 2, pcm.SampleRate);
            var source = _al.GenSource();
            _al.SetSourceProperty(source, SourceInteger.Buffer, (int) buffer);
            _al.SetSourceProperty(source, SourceBoolean.Looping, false);
            _al.SetSourceProperty(source, SourceFloat.Gain, gain);
            _al.SourcePlay(source);
            OneShots.Add((source, buffer));
        }

        public void SetAudio(AudioData audio)
        {
            ClearAudioData();
            EnsureInit();
            if (!_available || audio == null)
                return;

            var pcm = audio.GetFormat<Pcm16Format>();
            var channels = pcm.Channels;
            var frames = channels[0].Length;
            var count = Math.Min(channels.Length, 2);
            var data = new short[frames * count];
            for (var i = 0; i < frames; i++)
            for (var c = 0; c < count; c++)
                data[i * count + c] = channels[c][i];

            _buffer = _al.GenBuffer();
            fixed (short* p = data)
                _al.BufferData(_buffer, count == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16, p, data.Length * 2, pcm.SampleRate);
            _source = _al.GenSource();
            _al.SetSourceProperty(_source, SourceInteger.Buffer, (int) _buffer);
            _al.SetSourceProperty(_source, SourceBoolean.Looping, Loop);
            _al.SetSourceProperty(_source, SourceFloat.Gain, _volume);

            HasData = true;
            _poll.Start();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Starts playback from the beginning.</summary>
        public void Restart()
        {
            if (!HasData)
                return;
            _al.SourceStop(_source);
            _al.SourcePlay(_source);
            PollState();
        }

        private void Play_Execute()
        {
            if (IsPlaying)
                _al.SourcePause(_source);
            else
                _al.SourcePlay(_source);
            PollState();
        }

        private void Stop_Execute()
        {
            _al.SourceStop(_source);
            PollState();
        }

        private void PollState()
        {
            var playing = IsPlaying;
            if (playing != _wasPlaying)
            {
                _wasPlaying = playing;
                OnPropertyChanged(nameof(IsPlaying));
            }
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            ClearAudioData();
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
