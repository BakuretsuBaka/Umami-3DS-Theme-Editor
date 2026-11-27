// --------------------------------------------------
// Umami 3DS Theme Editor - RemoteInstallWindow.cs
// --------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using QRCoder;
using ThemeEditor.App.Localization;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Puts the open theme on Themezer for a short while (not published) and shows a QR code of its download, for
    ///     Anemone3DS to scan and install: the quickest way to try a theme on the console.
    /// </summary>
    internal sealed class RemoteInstallWindow : Window
    {
        private readonly ThemezerClient _client;
        private readonly string _name;
        private readonly Func<Task<byte[]>> _buildZip;
        private readonly TextBlock _status;
        private readonly Image _qr;
        private readonly TextBox _link;
        private readonly TextBlock _code;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        /// <summary>What Themezer answered, once it is ready.</summary>
        internal ThemezerRemoteInstall Install { get; private set; }

        /// <summary>The upload running, for tests to wait for.</summary>
        internal Task Running { get; private set; } = Task.CompletedTask;

        /// <param name="buildZip">Packs the theme as a ZIP (as Export ZIP does).</param>
        public RemoteInstallWindow(ThemezerClient client, string name, Func<Task<byte[]>> buildZip)
        {
            _client = client;
            _name = name;
            _buildZip = buildZip;
            Title = MainResources.Remote_Title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _status = new TextBlock { Text = MainResources.Remote_Uploading, TextWrapping = TextWrapping.Wrap };
            // Dark modules on white, with the quiet zone, whatever the theme of the editor
            _qr = new Image { Width = 280, Height = 280, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapInterpolationMode(_qr, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            _link = new TextBox { IsReadOnly = true, IsVisible = false, FontSize = 11 };
            _code = new TextBlock { IsVisible = false, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
            var close = new Button { Content = MainResources.Dialog_Close, IsCancel = true, IsDefault = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (s, e) => Close();

            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = MainResources.Remote_Hint, TextWrapping = TextWrapping.Wrap },
                    new Border { Background = Brushes.White, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Child = _qr, IsVisible = false },
                    _link,
                    _code,
                    _status,
                    close
                }
            };
            Opened += (s, e) => Running = Start();
            Closed += (s, e) => _cancel.Cancel();
        }

        private async Task Start()
        {
            try
            {
                var zip = await _buildZip();
                var install = await _client.RemoteInstall(_name, zip, _cancel.Token);
                if (_cancel.IsCancellationRequested)
                    return;
                Install = install;
                _qr.Source = QrBitmap(install.DownloadUrl);
                _qr.IsVisible = true;
                ((Border) _qr.Parent).IsVisible = true;
                _link.Text = install.DownloadUrl;
                _link.IsVisible = true;
                if (!string.IsNullOrEmpty(install.Code))
                {
                    _code.Text = string.Format(MainResources.Remote_Code, install.Code);
                    _code.IsVisible = true;
                }
                _status.Text = MainResources.Remote_Ready;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (_cancel.IsCancellationRequested)
                    return;
                _status.Text = string.Format(MainResources.Themezer_Error, ex.Message);
                _status.Foreground = Brushes.Firebrick;
            }
        }

        /// <summary>A QR code of <paramref name="text" /> as a bitmap (a few pixels per module; the image scales it up sharply).</summary>
        internal static Bitmap QrBitmap(string text)
        {
            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
            {
                var png = new PngByteQRCode(data).GetGraphic(8);
                using (var ms = new MemoryStream(png))
                    return new Bitmap(ms);
            }
        }
    }
}
