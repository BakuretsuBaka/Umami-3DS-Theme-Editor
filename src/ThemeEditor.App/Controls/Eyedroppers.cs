// --------------------------------------------------
// Umami 3DS Theme Editor - Eyedroppers.cs
// --------------------------------------------------

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ThemeEditor.App.Controls
{
    /// <summary>
    ///     Base for the two eyedroppers: press and drag to sample the screen (or this window if the platform can't
    ///     capture the screen), release to commit.
    /// </summary>
    public abstract class EyedropperBase : Button
    {
        private ScreenShot _shot;
        private bool _capturing;
        private bool _active;
        private IPointer _pointer;

        protected abstract void OnSample(PixelPoint screenPoint, ScreenShot shot);
        protected abstract void OnCommit();

        protected EyedropperBase()
        {
            Focusable = false;
        }

        protected override async void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            _active = true;
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Cross);

            var window = TopLevel.GetTopLevel(this) as Window;
            if (!_capturing)
            {
                _capturing = true;
                var shot = await ScreenGrab.CaptureAsync(window);
                _capturing = false;
                _shot = shot;
                if (_active)
                    Sample(e.GetPosition(this));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_active || _shot == null)
                return;
            Sample(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_active)
                return;
            _active = false;
            _pointer?.Capture(null);
            Cursor = null;
            if (_shot != null)
                OnCommit();
            _shot = null;
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _active = false;
            Cursor = null;
        }

        private void Sample(Point local)
        {
            var screen = this.PointToScreen(local);
            OnSample(screen, _shot);
        }
    }

    /// <summary>Picks a single color from the screen.</summary>
    public sealed class ColorEyedropper : EyedropperBase
    {
        public static readonly StyledProperty<Color> PickedColorProperty =
            AvaloniaProperty.Register<ColorEyedropper, Color>(nameof(PickedColor), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public Color PickedColor
        {
            get { return GetValue(PickedColorProperty); }
            set { SetValue(PickedColorProperty, value); }
        }

        public ColorEyedropper()
        {
            Width = 24;
            Height = 24;
            Padding = new Thickness(0);
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            Content = new Image
            {
                Width = 16,
                Height = 16,
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/PICKER.png")))
            };
            ToolTip.SetTip(this, Localization.MainResources.Eyedropper_Unavailable == null ? null : "Eyedropper");
        }

        protected override void OnSample(PixelPoint screenPoint, ScreenShot shot)
        {
            byte r, g, b;
            if (shot.TryGetPixel(screenPoint, out r, out g, out b))
                PickedColor = Color.FromRgb(r, g, b);
        }

        protected override void OnCommit() {}
    }

    /// <summary>Captures a rectangle of the screen around the pointer into an icon.</summary>
    public sealed class IconEyedropper : EyedropperBase
    {
        public static readonly StyledProperty<IImage> PreviewProperty =
            AvaloniaProperty.Register<IconEyedropper, IImage>(nameof(Preview));

        public static readonly StyledProperty<int> CaptureWidthProperty =
            AvaloniaProperty.Register<IconEyedropper, int>(nameof(CaptureWidth), 48);

        public static readonly StyledProperty<int> CaptureHeightProperty =
            AvaloniaProperty.Register<IconEyedropper, int>(nameof(CaptureHeight), 48);

        public static readonly StyledProperty<Point> CaptureCenterProperty =
            AvaloniaProperty.Register<IconEyedropper, Point>(nameof(CaptureCenter));

        private PixelBuffer _pending;
        private readonly Image _image = new Image { Stretch = Stretch.None };

        public IImage Preview
        {
            get { return GetValue(PreviewProperty); }
            set { SetValue(PreviewProperty, value); }
        }

        public int CaptureWidth
        {
            get { return GetValue(CaptureWidthProperty); }
            set { SetValue(CaptureWidthProperty, value); }
        }

        public int CaptureHeight
        {
            get { return GetValue(CaptureHeightProperty); }
            set { SetValue(CaptureHeightProperty, value); }
        }

        public Point CaptureCenter
        {
            get { return GetValue(CaptureCenterProperty); }
            set { SetValue(CaptureCenterProperty, value); }
        }

        /// <summary>Raised on release with the captured pixels.</summary>
        public event EventHandler<PixelBuffer> Captured;

        static IconEyedropper()
        {
            PreviewProperty.Changed.AddClassHandler<IconEyedropper>((s, e) => s._image.Source = e.NewValue as IImage);
        }

        public IconEyedropper()
        {
            Padding = new Thickness(2);
            Content = _image;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
        }

        protected override void OnSample(PixelPoint screenPoint, ScreenShot shot)
        {
            var scale = (TopLevel.GetTopLevel(this))?.RenderScaling ?? 1.0;
            var topLeft = new PixelPoint(screenPoint.X - (int) (CaptureCenter.X * scale), screenPoint.Y - (int) (CaptureCenter.Y * scale));
            var w = (int) (CaptureWidth * scale);
            var h = (int) (CaptureHeight * scale);
            var crop = shot.Crop(topLeft, w, h);
            _pending = scale > 1.01 ? crop.Resize(CaptureWidth, CaptureHeight) : crop;
            _image.Source = _pending.ToBitmap();
        }

        protected override void OnCommit()
        {
            if (_pending != null)
                Captured?.Invoke(this, _pending);
            _pending = null;
        }
    }
}
