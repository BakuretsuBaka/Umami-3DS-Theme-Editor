// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewSurface.cs
// --------------------------------------------------

using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

        public static readonly StyledProperty<PreviewInteraction> InteractionProperty =
            AvaloniaProperty.Register<PreviewSurface, PreviewInteraction>(nameof(Interaction));

        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _frameTimer;
        private readonly DispatcherTimer _busyTimer;

        private WriteableBitmap _bitmap;
        private bool _attached;

        static PreviewSurface()
        {
            AffectsRender<PreviewSurface>(ThemeProperty, AnimateProperty, InteractionProperty);
            AnimateProperty.Changed.AddClassHandler<PreviewSurface>((s, e) => s.UpdateTimers());
        }

        protected PreviewSurface()
        {
            _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _frameTimer.Tick += (s, e) => InvalidateVisual();
            // Runs only while the interactive state is still moving (cursor slide, folder, lid, paging)
            _busyTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _busyTimer.Tick += (s, e) =>
            {
                // Advance the animation here too, so it always finishes even if no frame gets rendered in between
                Interaction?.Tick(PreviewClock.Seconds, Settings.Default, Theme);
                InvalidateVisual();
                if (!(Interaction?.Busy ?? false))
                    _busyTimer.Stop();
            };
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

        /// <summary>Interactive state shared by both screens; null keeps the preview static.</summary>
        public PreviewInteraction Interaction
        {
            get { return GetValue(InteractionProperty); }
            set { SetValue(InteractionProperty, value); }
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
            _busyTimer.Stop();
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
                Interaction?.Tick(PreviewClock.Seconds, Settings.Default, Theme);
                DrawContent(canvas, Theme, Animate, PreviewClock.Seconds);

                // Closed lid: both screens go dark
                var ia = Interaction;
                if (ia != null && ia.Lid < 0.999)
                    using (var shade = new SKPaint { Color = new SKColor(0, 0, 0, (byte) Math.Round(255 * (1 - ia.Lid))) })
                        canvas.DrawRect(0, 0, (float) size.Width, (float) size.Height, shade);

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

            // Keep drawing frames while the cursor, folder, lid or wallpaper are still moving
            if (Interaction != null && Interaction.Busy && _attached && !_busyTimer.IsEnabled)
                _busyTimer.Start();
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
        private static readonly Cursor Hand = new Cursor(StandardCursorType.Hand);

        public BottomScreenPreview()
        {
            Focusable = true;
        }

        protected override Size NaturalSize => new Size(BottomRenderer.Width, BottomRenderer.Height);

        protected override void DrawContent(SKCanvas canvas, ThemeViewModel theme, bool animate, double time)
        {
            BottomRenderer.Draw(canvas, theme, animate, time, Interaction);
        }

        // Swiping the icon grid sideways flips pages like the arrow button
        private const double SwipeDistance = 40;
        private Point? _swipeStart;
        private bool _swiping;

        private PreviewTarget HitTest(Point p, out int col, out int row)
        {
            return PreviewHit.Hit(Settings.Default, (float) p.X, (float) p.Y, out col, out row);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var ia = Interaction;
            var point = e.GetCurrentPoint(this);
            if (ia == null || Theme == null || !point.Properties.IsLeftButtonPressed)
                return;
            Focus();
            int col, row;
            var target = HitTest(point.Position, out col, out row);
            ia.Press(target, col, row);
            // A press on the grid (an item or an empty spot) may turn into a swipe
            var onGrid = (target == PreviewTarget.Cell || target == PreviewTarget.None) && !Settings.Default.Preview_ModeToggle
                         && point.Position.Y > 30 && point.Position.Y < 212;
            _swipeStart = onGrid ? point.Position : (Point?) null;
            _swiping = false;
            if (target != PreviewTarget.None || onGrid)
                e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var ia = Interaction;
            if (ia == null)
                return;
            int col, row;
            var position = e.GetPosition(this);
            var target = HitTest(position, out col, out row);
            Cursor = target != PreviewTarget.None ? Hand : null;
            if (_swipeStart.HasValue && !_swiping && Math.Abs(position.X - _swipeStart.Value.X) > SwipeDistance / 2)
            {
                // Dragging sideways: no longer a click on the item it started on
                _swiping = true;
                ia.SetOver(false);
                InvalidateVisual();
                return;
            }
            if (_swiping)
                return;
            if (ia.Pressed != PreviewTarget.None)
            {
                ia.SetOver(target == ia.Pressed && col == ia.PressedCol && row == ia.PressedRow);
                InvalidateVisual();
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            var ia = Interaction;
            var start = _swipeStart;
            var swiping = _swiping;
            _swipeStart = null;
            _swiping = false;
            if (ia != null && swiping && start.HasValue && Theme != null)
            {
                var dx = e.GetPosition(this).X - start.Value.X;
                ia.Release();
                e.Pointer.Capture(null);
                if (dx <= -SwipeDistance)
                    PreviewActions.NextPage(Theme, Settings.Default, ia);
                else if (dx >= SwipeDistance)
                    PreviewActions.PreviousPage(Theme, Settings.Default, ia);
                InvalidateVisual();
                return;
            }
            if (ia == null || ia.Pressed == PreviewTarget.None)
            {
                e.Pointer.Capture(null);
                return;
            }
            var clicked = ia.PressedActive;
            var target = ia.Pressed;
            int col = ia.PressedCol, row = ia.PressedRow;
            ia.Release();
            e.Pointer.Capture(null);
            if (clicked && Theme != null)
                PreviewActions.Click(Theme, Settings.Default, ia, target, col, row);
            InvalidateVisual();
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _swipeStart = null;
            _swiping = false;
            Interaction?.Release();
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Interaction == null || Theme == null)
                return;
            var s = Settings.Default;
            switch (e.Key)
            {
                case Key.Left: e.Handled = true; PreviewActions.MoveCursor(Theme, s, -1, 0); break;
                case Key.Right: e.Handled = true; PreviewActions.MoveCursor(Theme, s, 1, 0); break;
                case Key.Up: e.Handled = true; PreviewActions.MoveCursor(Theme, s, 0, 1); break;
                case Key.Down: e.Handled = true; PreviewActions.MoveCursor(Theme, s, 0, -1); break;
                case Key.Enter:
                case Key.Space: e.Handled = true; PreviewActions.Open(Theme, s); break;
                case Key.Escape:
                case Key.Back:
                    e.Handled = true;
                    if (s.Preview_ModeToggle)
                        PreviewActions.Back(Theme, s);
                    break;
                case Key.PageDown: e.Handled = true; PreviewActions.NextPage(Theme, s, Interaction); break;
                case Key.PageUp: e.Handled = true; PreviewActions.PreviousPage(Theme, s, Interaction); break;
                // + shows fewer, larger icons and - more, smaller ones (the zoom buttons)
                case Key.OemPlus:
                case Key.Add: e.Handled = true; PreviewActions.Zoom(Theme, s, -1); break;
                case Key.OemMinus:
                case Key.Subtract: e.Handled = true; PreviewActions.Zoom(Theme, s, +1); break;
            }
            InvalidateVisual();
        }
    }
}
