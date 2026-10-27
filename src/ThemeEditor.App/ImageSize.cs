// --------------------------------------------------
// 3DS Theme Editor - ImageSize.cs
// --------------------------------------------------

using ThemeEditor.Common.Graphics;

namespace ThemeEditor.App
{
    internal struct ImageSize
    {
        public readonly int X;
        public readonly int Y;
        public readonly RawTexture.DataFormat Format;

        /// <summary>Part of the texture the HOME menu actually shows (the rest is padding up to the power of two).</summary>
        public readonly int VisibleX;
        public readonly int VisibleY;

        public ImageSize(int x, int y, RawTexture.DataFormat format) : this(x, y, format, x, y) {}

        public ImageSize(int x, int y, RawTexture.DataFormat format, int visibleX, int visibleY) : this()
        {
            X = x;
            Y = y;
            Format = format;
            VisibleX = visibleX;
            VisibleY = visibleY;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public bool Equals(ImageSize other)
        {
            return X == other.X && Y == other.Y && Format == other.Format;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = X;
                hashCode = (hashCode * 397) ^ Y;
                hashCode = (hashCode * 397) ^ (int) Format;
                return hashCode;
            }
        }
    }
}
