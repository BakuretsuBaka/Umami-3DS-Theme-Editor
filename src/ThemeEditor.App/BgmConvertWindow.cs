// --------------------------------------------------
// Umami 3DS Theme Editor - BgmConvertWindow.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Properties;
using ThemeEditor.Common.Audio;

namespace ThemeEditor.App
{
    /// <summary>Options dialog shown when importing audio as theme background music.</summary>
    internal sealed class BgmConvertWindow : Window
    {
        private static readonly int[] SampleRates = {0, 32728, 32000, 22050, 16000};

        private readonly BcstmInfo _info;
        private readonly ComboBox _codec;
        private readonly TextBlock _stereoNote;
        private readonly TextBlock _warning;
        private readonly ComboBox _rate;
        private readonly CheckBox _loop;
        private readonly NumericUpDown _loopStart;
        private readonly NumericUpDown _loopEnd;
        private readonly TextBlock _estimate;

        public BcstmOptions Result { get; private set; }

        public BgmConvertWindow(BcstmInfo info, string fileName)
        {
            _info = info;
            Title = MainResources.Bgm_Title;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            MinWidth = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var s = Settings.Default;
            _codec = new ComboBox
            {
                ItemsSource = new[] {MainResources.Bgm_Codec_Adpcm, MainResources.Bgm_Codec_Pcm16},
                SelectedIndex = s.BgmCodec == "Pcm16" ? 1 : 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _stereoNote = new TextBlock {Text = MainResources.Bgm_StereoOnly, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2)};
            _warning = new TextBlock {Foreground = Brushes.Firebrick, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, IsVisible = false, MaxWidth = 420, Margin = new Thickness(0, 4)};
            _rate = new ComboBox
            {
                ItemsSource = SampleRates.Select(r => r == 0 ? MainResources.Bgm_SampleRate_Keep : r + " Hz").ToList(),
                SelectedIndex = Math.Max(0, Array.IndexOf(SampleRates, s.BgmSampleRate)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _loop = new CheckBox {Content = MainResources.Bgm_Loop, IsChecked = info.Looping || s.BgmLoop};
            _loopStart = new NumericUpDown
            {
                Minimum = 0, Maximum = (decimal) Math.Max(0.1, info.Duration), Increment = 0.1m, FormatString = "0.00",
                Value = (decimal) (info.Looping ? (double) info.LoopStart / info.SampleRate : 0)
            };
            _loopEnd = new NumericUpDown
            {
                Minimum = 0, Maximum = (decimal) Math.Max(0.1, info.Duration), Increment = 0.1m, FormatString = "0.00",
                Value = (decimal) (info.Looping ? (double) info.LoopEnd / info.SampleRate : 0)
            };
            _estimate = new TextBlock {FontWeight = FontWeight.Bold};

            var ok = new Button {Content = MainResources.Bgm_Convert, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            var cancel = new Button {Content = MainResources.Dialog_Cancel, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center};
            ok.Click += (_, __) =>
            {
                Result = BuildOptions();
                var st = Settings.Default;
                st.BgmCodec = Result.Codec == BcstmCodec.Pcm16 ? "Pcm16" : "DspAdpcm";
                st.BgmSampleRate = Result.SampleRate ?? 0;
                st.BgmLoop = Result.Loop == true;
                Close();
            };
            cancel.Click += (_, __) => Close();

            Control Row(string label, Control editor)
            {
                var g = new Grid {ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new Thickness(0, 4)};
                var t = new TextBlock {Text = label, VerticalAlignment = VerticalAlignment.Center};
                Grid.SetColumn(editor, 1);
                g.Children.Add(t);
                g.Children.Add(editor);
                return g;
            }

            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 2,
                Children =
                {
                    Row(MainResources.Bgm_Source,
                        new TextBlock
                        {
                            Text = fileName + "\n" + string.Format(MainResources.Bgm_SourceInfo, info.SampleRate, info.Channels, info.Duration),
                            TextWrapping = TextWrapping.Wrap
                        }),
                    Row(MainResources.Bgm_Codec, _codec),
                    Row(MainResources.Bgm_SampleRate, _rate),
                    _stereoNote,
                    _loop,
                    Row(MainResources.Bgm_LoopStart, _loopStart),
                    Row(MainResources.Bgm_LoopEnd, _loopEnd),
                    Row(MainResources.Bgm_EstimatedSize, _estimate),
                    _warning,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 14, 0, 0),
                        Children = {ok, cancel}
                    }
                }
            };

            _codec.SelectionChanged += (_, __) => UpdateEstimate();
            _rate.SelectionChanged += (_, __) => UpdateEstimate();
            _loop.IsCheckedChanged += (_, __) =>
            {
                _loopStart.IsEnabled = _loopEnd.IsEnabled = _loop.IsChecked == true;
            };
            _loopStart.IsEnabled = _loopEnd.IsEnabled = _loop.IsChecked == true;
            UpdateEstimate();
        }

        private BcstmOptions BuildOptions()
        {
            var rate = SampleRates[Math.Max(0, _rate.SelectedIndex)];
            var loop = _loop.IsChecked == true;
            var sourceRate = rate == 0 ? _info.SampleRate : rate;
            return new BcstmOptions
            {
                Codec = _codec.SelectedIndex == 1 ? BcstmCodec.Pcm16 : BcstmCodec.DspAdpcm,
                SampleRate = rate == 0 ? (int?) null : rate,
                Loop = loop,
                LoopStart = (int) ((double) (_loopStart.Value ?? 0) * sourceRate),
                LoopEnd = (int) ((double) (_loopEnd.Value ?? 0) * sourceRate)
            };
        }

        private void UpdateEstimate()
        {
            var rate = SampleRates[Math.Max(0, _rate.SelectedIndex)];
            var effectiveRate = rate == 0 ? _info.SampleRate : rate;
            const int channels = 2;   // output is always stereo
            var samples = _info.Duration * effectiveRate;
            // DSP-ADPCM packs 14 samples into 8 bytes, PCM16 uses 2 bytes per sample, plus headers/seek tables
            var bytes = (_codec.SelectedIndex == 1 ? samples * 2 : samples * 8 / 14) * channels + 0x1000;
            var kb = (int) (bytes / 1024);
            _estimate.Text = kb + " kB";
            var tooLarge = bytes > BcstmConverter.MaxThemeBgmBytes;
            if (tooLarge)
                _estimate.Foreground = Brushes.Firebrick;
            else
                _estimate.ClearValue(TextBlock.ForegroundProperty);   // back to the theme colour (assigning null would make the text invisible)
            _warning.IsVisible = tooLarge;
            _warning.Text = string.Format(MainResources.Bgm_TooLargeWarning, kb, BcstmConverter.MaxThemeBgmBytes / 1024);
        }
    }
}
