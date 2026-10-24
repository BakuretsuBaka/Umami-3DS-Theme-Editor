// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewSurface.cs
// --------------------------------------------------

using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Controls
{
    /// <summary>
    ///     Base for the live theme previews. Content is rasterised with SkiaSharp on the UI thread into a bitmap
    ///     (at the display's pixel density) and blitted, so the same drawing code serves the screen, the
    ///     "save preview" export and the unit tests.
    /// </summary>
    public abstract class PreviewSurface : Control
    {
        public static readonly StyledProperty<ThemeViewModel> ThemeProperty =
            AvaloniaProperty.Register<PreviewSurface, ThemeViewModel>(nameof(Theme));

        public static readonly StyledProperty<bool> AnimateProperty =
            AvaloniaProperty.Register<PreviewSurface, bool>(nameof(Animate));

        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _frameTimer;

        private WriteableBitmap _bitmap;
        private bool _attached;

        static PreviewSurface()
        {
            AffectsRender<PreviewSurface>(ThemeProperty, AnimateProperty);
            AnimateProperty.Changed.AddClassHandler<PreviewSurface>((s, e) => s.UpdateTimers());
        }

        protected PreviewSurface()
        {
            _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _frameTimer.Tick += (s, e) => InvalidateVisual();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => InvalidateVisual();
            ClipToBounds = true;
        }

        public ThemeViewModel Theme
        {
            get { return GetValue(ThemeProperty); }
            set { SetValue(ThemeProperty, value); }
        }

        public bool Animate
        {
            get { return GetValue(AnimateProperty); }
            set { SetValue(AnimateProperty, value); }
        }

        protected abstract Size NaturalSize { get; }

        /// <summary>Draws the preview with the origin at the top-left of the control, in natural (96 dpi) units.</summary>
        protected abstract void DrawContent(SKCanvas canvas, ThemeViewModel theme, bool animate, double time);

        protected override Size MeasureOverride(Size availableSize)
        {
            return NaturalSize;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            ViewModelBase.ViewModelChanged += OnViewModelChanged;
            Settings.Default.PropertyChanged += OnSettingChanged;
            UpdateTimers();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            ViewModelBase.ViewModelChanged -= OnViewModelChanged;
            Settings.Default.PropertyChanged -= OnSettingChanged;
            _frameTimer.Stop();
            _clockTimer.Stop();
        }

        private void UpdateTimers()
        {
            if (!_attached)
                return;
            if (Animate)
            {
                _clockTimer.Stop();
                _frameTimer.Start();
            }
            else
            {
                _frameTimer.Stop();
                _clockTimer.Start();
            }
            InvalidateVisual();
        }

        private void OnViewModelChanged(ViewModelBase.ViewModelChangedArgs args)
        {
            var theme = Theme;
            if (theme == null || args.ViewModel.Tag != theme.Tag)
                return;
            if (Dispatcher.UIThread.CheckAccess())
                InvalidateVisual();
            else
                Dispatcher.UIThread.Post(InvalidateVisual);
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess())
                InvalidateVisual();
            else
                Dispatcher.UIThread.Post(InvalidateVisual);
        }

        public override void Render(DrawingContext context)
        {
            var size = NaturalSize;
            var scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
            var pw = (int) Math.Ceiling(size.Width * scale);
            var ph = (int) Math.Ceiling(size.Height * scale);

            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var surface = SKSurface.Create(info))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float) scale);
                DrawContent(canvas, Theme, Animate, PreviewClock.Seconds);

                if (_bitmap == null || _bitmap.PixelSize.Width != pw || _bitmap.PixelSize.Height != ph)
                {
                    _bitmap?.Dispose();
                    _bitmap = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96 * scale, 96 * scale),
                        PixelFormat.Bgra8888, AlphaFormat.Premul);
                }

                using (var pixmap = surface.PeekPixels())
                using (var fb = _bitmap.Lock())
                {
                    var src = pixmap.GetPixels();
                    for (var row = 0; row < ph; row++)
                    {
                        var rowBytes = pw * 4;
                        var buffer = new byte[rowBytes];
                        System.Runtime.InteropServices.Marshal.Copy(src + row * pixmap.RowBytes, buffer, 0, rowBytes);
                        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, fb.Address + row * fb.RowBytes, rowBytes);
                    }
                }
            }
            context.DrawImage(_bitmap, new Rect(0, 0, pw, ph), new Rect(0, 0, size.Width, size.Height));
        }

        /// <summary>Renders the preview at 1x into a standalone bitmap (used for exports and tests).</summary>
        public static SKBitmap RenderToBitmap(Action<SKCanvas> draw, int width, int height)
        {
            var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                draw(canvas);
            }
            return bmp;
        }
    }

    public sealed class TopScreenPreview : PreviewSurface
    {
        private readonly TopRenderer _renderer = new TopRenderer();

        protected override Size NaturalSize => new Size(TopRenderer.Width, TopRenderer.Height);

        protected override void DrawContent(SKCanvas canvas, ThemeViewModel theme, bool animate, double time)
        {
            _renderer.Draw(canvas, theme, animate, time);
        }
    }

    public sealed class BottomScreenPreview : PreviewSurface
    {
        protected override Size NaturalSize => new Size(BottomRenderer.Width, BottomRenderer.Height);

        protected override void DrawContent(SKCanvas canvas, ThemeViewModel theme, bool animate, double time)
        {
            BottomRenderer.Draw(canvas, theme, animate, time);
        }
    }
}
