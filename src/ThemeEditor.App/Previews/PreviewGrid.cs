// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewGrid.cs
// --------------------------------------------------

using System;
using SkiaSharp;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Layout of the HOME Menu icon grid for the console's zoom levels (1 to 6 rows). Item and cursor positions are
    ///     stored for the default 3-row layout; they are mapped through their slot index, which runs down each column and
    ///     then on to the next one, the way the console reflows icons when zooming.
    /// </summary>
    public static class PreviewGrid
    {
        public const int BaseRows = 3;
        public const int MinRows = 1;
        public const int MaxRows = 6;

        // Distance between cell centres per row count; 3 rows is the default layout (53 px cells, items at scale 1)
        private static readonly float[] Pitches = { 0, 80, 80, 53, 37.75f, 31.5f, 26.5f };

        private const float GridLeft = 27.5f;
        private const float GridWidth = 265;
        private const float GridMiddle = 123;

        // With one row the description bubble sits above the icons, so the row moves down
        private const float SingleRowY = 155;

        public static int Rows => Math.Max(MinRows, Math.Min(MaxRows, Settings.Default.Preview_ZoomRows));

        public static float Pitch(int rows) => Pitches[Math.Max(MinRows, Math.Min(MaxRows, rows))];

        /// <summary>Item scale relative to the default layout.</summary>
        public static float Scale(int rows) => Pitch(rows) / Pitches[BaseRows];

        /// <summary>Columns that fit on one page.</summary>
        public static int Columns(int rows) => (int) Math.Floor(GridWidth / Pitch(rows));

        /// <summary>Slot index of a stored position (row 0 is the bottom row of the default layout).</summary>
        public static int IndexOf(int column, int row) => column * BaseRows + (BaseRows - 1 - row);

        public static void FromIndex(int index, out int column, out int row)
        {
            column = index / BaseRows;
            row = BaseRows - 1 - index % BaseRows;
        }

        /// <summary>Cell of a stored position on a grid with <paramref name="rows" /> rows (row counted from the top).</summary>
        public static void ToZoomCell(int column, int row, int rows, out int zoomColumn, out int rowFromTop)
        {
            var index = Math.Max(0, IndexOf(column, row));
            zoomColumn = index / rows;
            rowFromTop = index % rows;
        }

        /// <summary>Stored position of a cell on a grid with <paramref name="rows" /> rows.</summary>
        public static void FromZoomCell(int zoomColumn, int rowFromTop, int rows, out int column, out int row)
        {
            FromIndex(zoomColumn * rows + rowFromTop, out column, out row);
        }

        public static SKPoint Centre(int zoomColumn, int rowFromTop, int rows)
        {
            var pitch = Pitch(rows);
            var x = GridLeft + pitch / 2 + zoomColumn * pitch;
            var y = rows == 1 ? SingleRowY : GridMiddle + (rowFromTop - (rows - 1) / 2f) * pitch;
            return new SKPoint(x, y);
        }

        /// <summary>True when a stored position lands on the first page at the current zoom level.</summary>
        public static bool IsVisible(int column, int row)
        {
            int zc, zr;
            var rows = Rows;
            ToZoomCell(column, row, rows, out zc, out zr);
            return zc < Columns(rows);
        }
    }
}
