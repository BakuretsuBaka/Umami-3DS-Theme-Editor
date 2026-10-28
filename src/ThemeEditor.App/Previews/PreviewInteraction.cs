// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewInteraction.cs
// --------------------------------------------------

using System;
using SkiaSharp;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Localization.Enums;
using ThemeEditor.App.Properties;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App.Previews
{
    /// <summary>What a point on the bottom screen preview is over.</summary>
    public enum PreviewTarget
    {
        None,
        Cell,
        Open,
        Close,
        Arrow,
        Back,
        Home,
        CornerA,
        CornerB,
        ArrowBack
    }

    /// <summary>What sits in a grid cell of the preview.</summary>
    public enum PreviewItem
    {
        None,
        Cart,
        File,
        Folder
    }

    /// <summary>
    ///     Transient state of the interactive preview: which button is held down, the animated cursor, the folder
    ///     open/close transition, the manual page position and the lid. The drawing code reads it; when it is null the
    ///     renderers draw the static preview that the toggles and the exporter describe.
    /// </summary>
    public sealed class PreviewInteraction
    {
        private const double LidSeconds = 0.28;
        private const double FolderSeconds = 0.2;
        private const double CursorSeconds = 0.16;

        private double _last = -1;
        private bool _cursorFixQueued;
        private bool _cursorInit;
        private bool _cursorFolder;
        private SKPoint _cursorFrom;
        private SKPoint _cursorTarget;
        private SKPoint _cursorNow;
        private double _cursorStart;

        public static PreviewInteraction Shared { get; } = new PreviewInteraction();

        public PreviewTarget Pressed { get; private set; }
        public int PressedCol { get; private set; } = -1;
        public int PressedRow { get; private set; } = -1;

        /// <summary>True while the pointer is still over the pressed target (releasing there is a click).</summary>
        public bool PressedActive { get; private set; }

        /// <summary>Counts arrow-button presses; the bottom wallpaper pages or scrolls with it.</summary>
        public int Page { get; set; }

        public bool LidOpenTarget { get; set; } = true;

        /// <summary>0 = lid closed (screens dark), 1 = open.</summary>
        public double Lid { get; private set; } = 1;

        /// <summary>0 = folder closed, 1 = folder view fully open.</summary>
        public double FolderT { get; private set; }

        /// <summary>Where the icon grid is scrolled to, in pages; it glides to <see cref="Page" />.</summary>
        public double GridScroll { get; private set; }

        /// <summary>How far the grid is being dragged right now, in pages (positive = towards the previous page).</summary>
        public double DragPages { get; set; }

        /// <summary>What the grid shows: the gliding page position minus the live drag.</summary>
        public double GridView => GridScroll - DragPages;

        /// <summary>Ends a drag: the grid continues from where it was let go and glides to the page from there.</summary>
        public void SettleDrag()
        {
            GridScroll -= DragPages;
            DragPages = 0;
        }

        /// <summary>Horizontal wallpaper position in texture pixels when the animation is off.</summary>
        public double ScrollX { get; private set; }

        /// <summary>True while something is still moving, so the surface keeps asking for frames.</summary>
        public bool Busy { get; private set; }

        public SKPoint CursorPosition => _cursorNow;

        public bool IsPressed(PreviewTarget target)
        {
            return PressedActive && Pressed == target;
        }

        public bool IsCellPressed(int col, int row)
        {
            return PressedActive && Pressed == PreviewTarget.Cell && PressedCol == col && PressedRow == row;
        }

        public void Press(PreviewTarget target, int col, int row)
        {
            Pressed = target;
            PressedCol = col;
            PressedRow = row;
            PressedActive = target != PreviewTarget.None;
        }

        /// <summary>Marks whether the pointer is still over the pressed target (dragging off un-presses it).</summary>
        public void SetOver(bool over)
        {
            if (Pressed != PreviewTarget.None)
                PressedActive = over;
        }

        public void Release()
        {
            Pressed = PreviewTarget.None;
            PressedCol = PressedRow = -1;
            PressedActive = false;
        }

        /// <summary>Advances every animation to <paramref name="now" />. Safe to call several times per frame.</summary>
        public void Tick(double now, Settings s, ThemeViewModel t)
        {
            var dt = _last < 0 ? 0 : Math.Min(0.1, Math.Max(0, now - _last));
            _last = now;
            var busy = false;

            Lid = Approach(Lid, LidOpenTarget ? 1 : 0, dt / LidSeconds, ref busy);
            FolderT = Approach(FolderT, s.Preview_ModeToggle ? 1 : 0, dt / FolderSeconds, ref busy);

            // Icon pages glide like the wallpaper
            var pageTarget = (double) PreviewGrid.PageOf(this);
            if (Math.Abs(pageTarget - GridScroll) > 0.002)
            {
                GridScroll += (pageTarget - GridScroll) * Math.Min(1, dt * 10);
                busy = true;
            }
            else
            {
                GridScroll = pageTarget;
            }

            // Cursor: slides to the cell it is moved to; snaps when switching between the main grid and the folder
            var folder = s.Preview_ModeToggle;
            if (!folder && !_cursorFixQueued && !PreviewGrid.CursorIsOnPage(s, PreviewGrid.PageOf(this)))
            {
                // E.g. a cursor saved at a slot the current zoom does not show. Settings must not change while a frame
                // is being drawn (it would redraw mid-render), so the cursor is moved right after this frame.
                _cursorFixQueued = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _cursorFixQueued = false;
                    if (!s.Preview_ModeToggle)
                        PreviewGrid.KeepCursorOnPage(s, PreviewGrid.PageOf(this));
                });
            }
            var target = ItemRenderer.GridPosition(folder ? s.Preview_Folder_CursorX : s.Preview_Main_CursorX,
                folder ? s.Preview_Folder_CursorY : s.Preview_Main_CursorY, folder, folder ? 0 : PreviewGrid.PageOf(this));
            if (!_cursorInit || folder != _cursorFolder)
            {
                _cursorInit = true;
                _cursorFolder = folder;
                _cursorFrom = _cursorNow = _cursorTarget = target;
                _cursorStart = now;
            }
            else if (Math.Abs(target.X - _cursorTarget.X) > 0.01f || Math.Abs(target.Y - _cursorTarget.Y) > 0.01f)
            {
                _cursorFrom = _cursorNow;
                _cursorTarget = target;
                _cursorStart = now;
            }
            var k = Math.Min(1.0, (now - _cursorStart) / CursorSeconds);
            var e = 1 - Math.Pow(1 - k, 3);
            _cursorNow = new SKPoint((float) (_cursorFrom.X + (_cursorTarget.X - _cursorFrom.X) * e),
                (float) (_cursorFrom.Y + (_cursorTarget.Y - _cursorFrom.Y) * e));
            busy |= k < 1;

            // Wallpaper position for the manual page/scroll modes
            double scrollTarget;
            switch (t?.Flags.BottomFrameType)
            {
                case BottomFrameType.PageScroll:
                    scrollTarget = (Page % 3 + 3) % 3 * 320;
                    break;
                case BottomFrameType.BounceScroll:
                    scrollTarget = new[] { 0, 1, 2, 1 }[(Page % 4 + 4) % 4] * 320;
                    break;
                case BottomFrameType.SlowScroll:
                case BottomFrameType.FastScroll:
                    scrollTarget = Page * 168.0;
                    break;
                default:
                    scrollTarget = 0;
                    break;
            }
            var diff = scrollTarget - ScrollX;
            if (Math.Abs(diff) > 0.5)
            {
                ScrollX += diff * Math.Min(1, dt * 9);
                busy = true;
            }
            else
            {
                ScrollX = scrollTarget;
            }

            Busy = busy;
        }

        private static double Approach(double value, double target, double step, ref bool busy)
        {
            if (value < target)
                value = Math.Min(target, value + step);
            else if (value > target)
                value = Math.Max(target, value - step);
            busy |= Math.Abs(value - target) > 1e-6;
            return value;
        }
    }

    /// <summary>Maps points on the bottom screen to what the console would react to.</summary>
    public static class PreviewHit
    {
        public const int Columns = 5;

        public static int Rows(bool folder)
        {
            return folder ? 2 : 3;
        }

        private static readonly SKRect BackButton = new SKRect(27, 47, 93, 77);
        private static readonly SKRect HomeCorner = new SKRect(0, 0, 51, 30);
        private static readonly SKRect CornerARect = new SKRect(268, 0, 293, 30);
        private static readonly SKRect CornerBRect = new SKRect(293, 0, 320, 30);
        private static readonly SKRect ArrowRect = new SKRect(296, 100, 320, 146);

        private static readonly SKRect BackArrowRect = new SKRect(0, 100, 24, 146);

        public static PreviewTarget Hit(Settings s, float x, float y, out int col, out int row, int page = 0)
        {
            col = row = -1;
            var p = new SKPoint(x, y);
            if (p.X < 0 || p.Y < 0 || p.X >= 320 || p.Y >= 240)
                return PreviewTarget.None;

            // Controls drawn over the grid come first
            var closeEnabled = LeftButtonShown(s);
            if (p.Y >= 215)
            {
                if (closeEnabled && p.X < 106)
                    return PreviewTarget.Close;
                return PreviewTarget.Open;
            }
            if (HomeCorner.Contains(p))
                return PreviewTarget.Home;
            if (CornerARect.Contains(p))
                return PreviewTarget.CornerA;
            if (CornerBRect.Contains(p))
                return PreviewTarget.CornerB;
            // The page arrows only exist on the main grid, and only where there is a page to go to
            if (!s.Preview_ModeToggle && page < PreviewGrid.Pages - 1 && ArrowRect.Contains(p))
                return PreviewTarget.Arrow;
            if (!s.Preview_ModeToggle && page > 0 && BackArrowRect.Contains(p))
                return PreviewTarget.ArrowBack;

            var folder = s.Preview_ModeToggle;
            if (folder && BackButton.Contains(p))
                return PreviewTarget.Back;

            if (!folder)
            {
                // The main grid follows the zoom level; the hit returns the stored (default layout) position
                var rows = PreviewGrid.Rows;
                var half = PreviewGrid.Pitch(rows) / 2;
                for (var zc = 0; zc < PreviewGrid.Columns(rows); zc++)
                for (var zr = 0; zr < rows; zr++)
                {
                    var centre = PreviewGrid.Centre(zc, zr, rows);
                    if (Math.Abs(p.X - centre.X) <= half && Math.Abs(p.Y - centre.Y) <= half)
                    {
                        PreviewGrid.FromZoomCell(zc + page * PreviewGrid.Columns(rows), zr, rows, out col, out row);
                        return PreviewTarget.Cell;
                    }
                }
                return PreviewTarget.None;
            }

            for (var c = 0; c < Columns; c++)
            for (var r = 0; r < Rows(folder); r++)
            {
                var centre = ItemRenderer.GridPosition(c, r, folder);
                if (Math.Abs(p.X - centre.X) <= 26.5f && Math.Abs(p.Y - centre.Y) <= 26.5f)
                {
                    col = c;
                    row = r;
                    return PreviewTarget.Cell;
                }
            }
            return PreviewTarget.None;
        }

        /// <summary>The item under the cursor (in the open folder, or on the main grid).</summary>
        public static PreviewItem Selected(Settings s)
        {
            return s.Preview_ModeToggle
                ? ItemAt(s, s.Preview_Folder_CursorX, s.Preview_Folder_CursorY, true)
                : ItemAt(s, s.Preview_Main_CursorX, s.Preview_Main_CursorY, false);
        }

        /// <summary>
        ///     Whether the bottom bar is split: "Settings | Open" on a folder, "Close | Start" on a title (when the
        ///     preview option shows Close), and one wide bar on an empty slot.
        /// </summary>
        public static bool LeftButtonShown(Settings s)
        {
            var item = Selected(s);
            return item == PreviewItem.Folder || item != PreviewItem.None && s.Preview_CloseToggle;
        }

        /// <summary>What the current settings place in a grid cell.</summary>
        public static PreviewItem ItemAt(Settings s, int col, int row, bool folder)
        {
            if (folder)
            {
                if (s.Preview_Folder_FileEnable && s.Preview_Folder_FileX == col && s.Preview_Folder_FileY == row)
                    return PreviewItem.File;
                if (s.Preview_Folder_FolderEnable && s.Preview_Folder_FolderX == col && s.Preview_Folder_FolderY == row)
                    return PreviewItem.Folder;
                return PreviewItem.None;
            }
            if (s.Preview_Main_CartEnable && s.Preview_Main_CartX == col && s.Preview_Main_CartY == row)
                return PreviewItem.Cart;
            if (s.Preview_Main_FileEnable && s.Preview_Main_FileX == col && s.Preview_Main_FileY == row)
                return PreviewItem.File;
            if (s.Preview_Main_FolderEnable && s.Preview_Main_FolderX == col && s.Preview_Main_FolderY == row)
                return PreviewItem.Folder;
            return PreviewItem.None;
        }
    }

    /// <summary>What clicking and pressing keys in the preview does, including the theme's sound effects.</summary>
    public static class PreviewActions
    {
        /// <summary>Completes a click on <paramref name="target" /> (the pointer was released over it).</summary>
        public static void Click(ThemeViewModel t, Settings s, PreviewInteraction ia, PreviewTarget target, int col, int row)
        {
            switch (target)
            {
                case PreviewTarget.Cell:
                    ClickCell(t, s, col, row);
                    break;
                case PreviewTarget.Open:
                    Open(t, s);
                    break;
                case PreviewTarget.Close:
                    // On a folder the left button is "Settings", which the preview cannot open
                    if (PreviewHit.Selected(s) != PreviewItem.Folder)
                        Back(t, s);
                    break;
                case PreviewTarget.Back:
                    Back(t, s);
                    break;
                case PreviewTarget.Arrow:
                    NextPage(t, s, ia);
                    break;
                case PreviewTarget.ArrowBack:
                    PreviousPage(t, s, ia);
                    break;
                case PreviewTarget.CornerA:
                    Zoom(t, s, -1);
                    break;
                case PreviewTarget.CornerB:
                    Zoom(t, s, +1);
                    break;
            }
        }

        /// <summary>
        ///     The zoom buttons in the top-right corner: the big square shows fewer, larger icons, the four small squares
        ///     more and smaller ones. Returns true when the level changed. The folder view keeps its own layout.
        /// </summary>
        public static bool Zoom(ThemeViewModel t, Settings s, int delta)
        {
            if (s.Preview_ModeToggle)
                return false;
            var rows = Math.Max(PreviewGrid.MinRows, Math.Min(PreviewGrid.MaxRows, s.Preview_ZoomRows + delta));
            if (rows == s.Preview_ZoomRows)
                return false;
            s.Preview_ZoomRows = rows;
            // Keep the cursor on the page that is shown, like the console does
            PreviewGrid.KeepCursorOnPage(s, PreviewGrid.PageOf(PreviewInteraction.Shared));
            PreviewSounds.Play(t, CwavKind.Cursor);
            return true;
        }

        public static void PreviousPage(ThemeViewModel t, Settings s, PreviewInteraction ia)
        {
            // Pages stop at the first one (no wrapping), and the folder view has no pages
            if (s.Preview_ModeToggle || PreviewGrid.PageOf(ia) <= 0)
                return;
            if (s.Preview_AnimationToggle)
                s.Preview_AnimationToggle = false;
            ia.Page = PreviewGrid.PageOf(ia) - 1;
            MoveCursorWithPage(s, -1);
            var frame = (ia.Page % 3 + 3) % 3;
            PreviewSounds.Play(t, (CwavKind) ((int) CwavKind.Frame0 + frame));
        }

        /// <summary>
        ///     The icon grid scrolls a page with the arrow button; the cursor stays on the same spot of the screen, so it
        ///     moves to the matching slot of the new page (wrapping around after the last page).
        /// </summary>
        private static void MoveCursorWithPage(Settings s, int delta)
        {
            if (s.Preview_ModeToggle)
                return;
            var rows = PreviewGrid.Rows;
            var perPage = PreviewGrid.PerPage(rows);
            var total = perPage * PreviewGrid.Pages;
            var index = PreviewGrid.ToZoomIndex(PreviewGrid.IndexOf(s.Preview_Main_CursorX, s.Preview_Main_CursorY), rows);
            var moved = ((index + delta * perPage) % total + total) % total;
            int col, row;
            PreviewGrid.FromZoomCell(moved / rows, moved % rows, rows, out col, out row);
            SetCursor(s, false, col, row);
        }

        private static void ClickCell(ThemeViewModel t, Settings s, int col, int row)
        {
            var folder = s.Preview_ModeToggle;
            var cx = folder ? s.Preview_Folder_CursorX : s.Preview_Main_CursorX;
            var cy = folder ? s.Preview_Folder_CursorY : s.Preview_Main_CursorY;
            if (cx == col && cy == row)
            {
                // Tapping what is already selected activates it, like pressing Open
                Open(t, s);
                return;
            }
            SetCursor(s, folder, col, row);
            PreviewSounds.Play(t, CwavKind.Cursor);
        }

        /// <summary>The Open button: opens the selected folder or launches the selected title.</summary>
        public static void Open(ThemeViewModel t, Settings s)
        {
            var folder = s.Preview_ModeToggle;
            var cx = folder ? s.Preview_Folder_CursorX : s.Preview_Main_CursorX;
            var cy = folder ? s.Preview_Folder_CursorY : s.Preview_Main_CursorY;
            switch (PreviewHit.ItemAt(s, cx, cy, folder))
            {
                case PreviewItem.Folder:
                    if (!folder)
                    {
                        s.Preview_ModeToggle = true;
                        PreviewSounds.Play(t, CwavKind.Folder);
                    }
                    else
                    {
                        PreviewSounds.Play(t, CwavKind.Launch);
                    }
                    break;
                case PreviewItem.Cart:
                case PreviewItem.File:
                    PreviewSounds.Play(t, CwavKind.Launch);
                    break;
            }
        }

        /// <summary>The Close button and the folder's back button.</summary>
        public static void Back(ThemeViewModel t, Settings s)
        {
            if (s.Preview_ModeToggle)
                s.Preview_ModeToggle = false;
            PreviewSounds.Play(t, CwavKind.Close);
        }

        public static void NextPage(ThemeViewModel t, Settings s, PreviewInteraction ia)
        {
            // Pages stop at the last one (no wrapping), and the folder view has no pages
            if (s.Preview_ModeToggle || PreviewGrid.PageOf(ia) >= PreviewGrid.Pages - 1)
                return;
            // Manual paging takes over from the automatic animation
            if (s.Preview_AnimationToggle)
                s.Preview_AnimationToggle = false;
            var frame = (ia.Page % 3 + 3) % 3;
            ia.Page = PreviewGrid.PageOf(ia) + 1;
            MoveCursorWithPage(s, +1);
            PreviewSounds.Play(t, (CwavKind) ((int) CwavKind.Frame0 + frame));
        }

        public static void SetCursor(Settings s, bool folder, int col, int row)
        {
            if (folder)
            {
                s.Preview_Folder_CursorX = col;
                s.Preview_Folder_CursorY = row;
            }
            else
            {
                s.Preview_Main_CursorX = col;
                s.Preview_Main_CursorY = row;
            }
        }

        /// <summary>Arrow-key navigation of the cursor; returns true when it moved.</summary>
        public static bool MoveCursor(ThemeViewModel t, Settings s, int dx, int dy)
        {
            var folder = s.Preview_ModeToggle;
            var cx = folder ? s.Preview_Folder_CursorX : s.Preview_Main_CursorX;
            var cy = folder ? s.Preview_Folder_CursorY : s.Preview_Main_CursorY;
            int nx, ny;
            if (folder)
            {
                nx = Math.Max(0, Math.Min(PreviewHit.Columns - 1, cx + dx));
                ny = Math.Max(0, Math.Min(PreviewHit.Rows(folder) - 1, cy + dy));
            }
            else
            {
                // Move on the zoomed grid (dy > 0 is up), then store the result in default-layout terms
                int zc, zr;
                var rows = PreviewGrid.Rows;
                PreviewGrid.ToZoomCell(cx, cy, rows, out zc, out zr);
                var firstColumn = PreviewGrid.PageOf(PreviewInteraction.Shared) * PreviewGrid.Columns(rows);
                zc = Math.Max(firstColumn, Math.Min(firstColumn + PreviewGrid.Columns(rows) - 1, zc + dx));
                zr = Math.Max(0, Math.Min(rows - 1, zr - dy));
                PreviewGrid.FromZoomCell(zc, zr, rows, out nx, out ny);
            }
            if (nx == cx && ny == cy)
                return false;
            SetCursor(s, folder, nx, ny);
            PreviewSounds.Play(t, CwavKind.Cursor);
            return true;
        }

        public static void ToggleLid(ThemeViewModel t, PreviewInteraction ia)
        {
            ia.LidOpenTarget = !ia.LidOpenTarget;
            if (ia.LidOpenTarget)
                PreviewSounds.Play(t, CwavKind.OpenLid);
        }
    }
}
