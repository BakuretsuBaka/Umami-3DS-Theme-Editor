// --------------------------------------------------
// 3DS Theme Editor - TextureViewModel.cs
// --------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using ThemeEditor.Common.Graphics;

namespace ThemeEditor.App.Themes
{
    public sealed class TextureViewModel : ViewModelBase
    {
        private IEnumerable<Color> _palette;
        private PixelBuffer _pixels;
        private Bitmap _src;

        /// <summary>Display bitmap of the decoded texture, or null when the slot is empty.</summary>
        public Bitmap Bitmap
        {
            get { return _src ?? (_src = DecodeTexture(Model)?.ToBitmap()); }
        }

        /// <summary>CPU-side copy of the decoded texture, or null when the slot is empty.</summary>
        public PixelBuffer Pixels
        {
            get
            {
                if (_pixels == null)
                    _pixels = DecodeTexture(Model);
                return _pixels;
            }
        }

        public void ReplacePixels(PixelBuffer value)
        {
            if (value.Width != Width || value.Height != Height)
                return;
            Model.Encode(value.GetBgr24Data());
            Invalidate();
        }

        public RawTexture.DataFormat DataFormat => Model.Format;

        public bool Exists => !Model.Format.Equals(RawTexture.DataFormat.Invalid);

        public int Height => Model.Height;

        private new RawTexture Model => (RawTexture) base.Model;

        public IEnumerable<Color> Palette => _palette ?? (_palette = GeneratePalette());

        public int Width => Model.Width;

        public TextureViewModel(RawTexture model, string tag) : base(model, tag) {}

        public void ClearTexture()
        {
            Model.Encode(null, 0, 0, RawTexture.DataFormat.Invalid);
            Invalidate();
        }

        public override void Dispose()
        {
            _src = null;
            _pixels = null;
            _palette = null;

            base.Dispose();
        }

        public void EncodeTexture(PixelBuffer bitmap, RawTexture.DataFormat targetFormat)
        {
            var bgrData = bitmap.GetBgr24Data();
            Model.Encode(bgrData, bitmap.Width, bitmap.Height, targetFormat);
            Invalidate();
        }

        public void EdgeBleed(int x, int y, int sx, int sy)
        {
            Model.EdgeBleed(x, y, sx, sy);
            Invalidate();
        }

        public void Invalidate()
        {
            var oldBitmap = _src;
            var oldWidth = Width;
            var oldHeight = Height;
            var oldPal = _palette;
            _src = null;
            _pixels = null;
            _palette = null;
            RaiseViewModelChanged(nameof(Bitmap), oldBitmap, null);
            RaiseViewModelChanged(nameof(Width), oldWidth, 0);
            RaiseViewModelChanged(nameof(Height), oldHeight, 0);
            RaiseViewModelChanged(nameof(Palette), oldPal, 0);
        }

        private PixelBuffer DecodeTexture(RawTexture model)
        {
            switch (model.Format)
            {
                default:
                case RawTexture.DataFormat.Invalid:
                    return null;
                case RawTexture.DataFormat.Bgr888:
                case RawTexture.DataFormat.Bgr565:
                    return PixelBuffer.FromBgr24(model.Width, model.Height, model.Decode());
                case RawTexture.DataFormat.A8:
                {
                    // The decoder yields gray (R=G=B=alpha); that gray is the mask
                    var gray = model.Decode();
                    var mask = new byte[model.Width * model.Height];
                    for (var i = 0; i < mask.Length; i++)
                        mask[i] = gray[i * 3];
                    return PixelBuffer.FromAlphaMask(model.Width, model.Height, mask);
                }
            }
        }

        private IEnumerable<Color> GeneratePalette()
        {
            var pixels = Pixels;
            if (pixels == null)
                return Enumerable.Empty<Color>();
            PixelBuffer area;
            switch (Width)
            {
                case 512:
                    area = pixels.Crop(0, 0, 412, 240);
                    break;
                case 1024:
                    // It Has a 920 Version, but the Max data is 1008, bear with a little gray
                    area = pixels.Crop(0, 0, 1008, 240);
                    break;
                default:
                    area = pixels;
                    break;
            }

            return GraphicUtils.PaletteGen(area.GetBgr24Data(), 20).Select(Extensions.ToMediaColor).ToList();
        }
    }
}
