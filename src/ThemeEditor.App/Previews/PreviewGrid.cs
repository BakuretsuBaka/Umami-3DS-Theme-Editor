// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewGrid.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App.Previews
{
    /// <summary>
    ///     Layout of the HOME Menu icon grid for the console's zoom levels (1 to 6 rows), over three pages. Item and
    ///     cursor positions are stored for the default 3-row layout; the stored positions of the preview's items (game
    ///     card, the theme's file icon, the folder) only set their order. At every zoom level the items take the first
    ///     slots in that order and every other slot follows them; slots run down each column and on to the next one,
    ///     like the console. So the icons keep one order whatever the zoom (they never swap places), and all of them
    ///     are on the first page (the one-row layout shows exactly three).
    /// </summary>
    public static class PreviewGrid
    {
        public const int BaseRows = 3;

        /// <summary>Pages of icons the arrow button scrolls through (as many as the theme has wallpaper pages).</summary>
        public const int Pages = 3;

        /// <summary>The icon page a live preview shows (0 for exports and static previews).</summary>
        public static int PageOf(PreviewInteraction ia) => ia == null ? 0 : Math.Max(0, Math.Min(Pages - 1, ia.Page));

        /// <summary>
        ///     Where the grid is scrolled to, in pages: the page while it rests, a fraction while it slides or is dragged.
        /// </summary>
        public static double ViewOf(PreviewInteraction ia) => ia == null ? 0 : ia.GridView;

        /// <summary>Slots on one page at the given zoom level.</summary>
        public static int PerPage(int rows) => Columns(rows) * rows;
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

        /// <summary>Slot indices of the enabled preview items, in order.</summary>
        private static List<int> ItemIndices()
        {
            var s = Settings.Default;
            var items = new List<int>();
            if (s.Preview_Main_CartEnable)
                items.Add(IndexOf(s.Preview_Main_CartX, s.Preview_Main_CartY));
            if (s.Preview_Main_FileEnable)
                items.Add(IndexOf(s.Preview_Main_FileX, s.Preview_Main_FileY));
            if (s.Preview_Main_FolderEnable)
                items.Add(IndexOf(s.Preview_Main_FolderX, s.Preview_Main_FolderY));
            return items.Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();
        }

        /// <summary>Slot on the zoomed grid of a stored slot: items first (in order), then every other slot in order.</summary>
        public static int ToZoomIndex(int index, int rows)
        {
            if (index < 0)
                return 0;
            var items = ItemIndices();
            var pos = items.IndexOf(index);
            return pos >= 0 ? pos : items.Count + index - items.Count(i => i < index);
        }

        /// <summary>Inverse of <see cref="ToZoomIndex" />.</summary>
        public static int FromZoomIndex(int zoomIndex, int rows)
        {
            var items = ItemIndices();
            if (zoomIndex < items.Count)
                return items[zoomIndex];
            // The n-th slot that is not an item
            var index = zoomIndex - items.Count;
            foreach (var i in items)
                if (i <= index)
                    index++;
            return index;
        }

        /// <summary>Cell of a stored position on a grid with <paramref name="rows" /> rows (row counted from the top).</summary>
        public static void ToZoomCell(int column, int row, int rows, out int zoomColumn, out int rowFromTop)
        {
            var index = ToZoomIndex(IndexOf(column, row), rows);
            zoomColumn = index / rows;
            rowFromTop = index % rows;
        }

        /// <summary>Stored position of a cell on a grid with <paramref name="rows" /> rows.</summary>
        public static void FromZoomCell(int zoomColumn, int rowFromTop, int rows, out int column, out int row)
        {
            FromIndex(FromZoomIndex(zoomColumn * rows + rowFromTop, rows), out column, out row);
        }

        public static SKPoint Centre(int zoomColumn, int rowFromTop, int rows)
        {
            var pitch = Pitch(rows);
            var x = GridLeft + pitch / 2 + zoomColumn * pitch;
            var y = rows == 1 ? SingleRowY : GridMiddle + (rowFromTop - (rows - 1) / 2f) * pitch;
            return new SKPoint(x, y);
        }

        /// <summary>True when a stored position is on the shown page at the current zoom level.</summary>
        public static bool IsVisible(int column, int row, int page = 0)
        {
            int zc, zr;
            var rows = Rows;
            ToZoomCell(column, row, rows, out zc, out zr);
            var first = page * Columns(rows);
            return zc >= first && zc < first + Columns(rows);
        }

        /// <summary>Screen position of a stored position when the grid is scrolled to <paramref name="view" /> (in pages).</summary>
        public static SKPoint Position(int column, int row, double view)
        {
            int zc, zr;
            var rows = Rows;
            ToZoomCell(column, row, rows, out zc, out zr);
            var p = Centre(zc, zr, rows);
            return new SKPoint((float) (p.X - view * Columns(rows) * Pitch(rows)), p.Y);
        }

        /// <summary>True when (part of) the item at a stored position is on screen at <paramref name="view" />.</summary>
        public static bool IsOnScreen(int column, int row, double view)
        {
            var x = Position(column, row, view).X;
            var half = Pitch(Rows) / 2;
            return x > -half && x < 320 + half;
        }

        public static bool CursorIsOnPage(Settings s, int page)
        {
            var rows = Rows;
            var perPage = PerPage(rows);
            var index = ToZoomIndex(IndexOf(s.Preview_Main_CursorX, s.Preview_Main_CursorY), rows);
            return index >= page * perPage && index < (page + 1) * perPage;
        }

        /// <summary>Moves the main cursor onto <paramref name="page" /> when it is elsewhere (it keeps its row).</summary>
        public static bool KeepCursorOnPage(Settings s, int page)
        {
            var rows = Rows;
            var perPage = PerPage(rows);
            var index = ToZoomIndex(IndexOf(s.Preview_Main_CursorX, s.Preview_Main_CursorY), rows);
            var first = page * perPage;
            if (index >= first && index < first + perPage)
                return false;
            var target = first + index % rows;
            int col, row;
            FromZoomCell(target / rows, target % rows, rows, out col, out row);
            s.Preview_Main_CursorX = col;
            s.Preview_Main_CursorY = row;
            return true;
        }
    }
}
