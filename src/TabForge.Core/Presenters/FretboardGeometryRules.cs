using TabForge.Platform;
using TabForge.Visualization;

namespace TabForge.Presenters;

// Owns: the fretboard layout (board rectangle, visible fret window, string gap), string and fret positions and the click-to-fret hit test,
//   in plain doubles over UiRect and UiPoint. One source of truth for the renderer and for hit testing. With FretboardBoardLimits.RealProportions
//   the board keeps the real aspect of its shown frets and strings at every size (one scale, centred, spare room left empty).
// Does not own: drawing (FretboardRenderer, the Avalonia InstrumentPanelView), the sounding notes (InstrumentVisualState) or the
//   pane's height rules (InstrumentPanel).
// Tests: TabForge.Avalonia.Tests.FretboardRealProportionTests.
public static class FretboardGeometryRules
{
    /// <summary>The fields of an instrument state that decide the fretboard layout. AnchorFret is the current (else next) note's fret, 0 when none.
    /// Bass: the instrument is a bass (its real scale and string pitch apply under RealProportions); false for a guitar.</summary>
    public readonly record struct Input(int Strings, int DisplayFrets, int FretCount, double StringSpacing, bool LeftHanded, int AnchorFret, bool Bass = false);

    public readonly record struct Layout(UiRect Board, int FirstFret, int LastFret, double FretWidth, double StringGap, int Strings, bool LeftHanded);

    public const double LeftGutter = 54;
    public const double RightPad = 14;
    /// <summary>Room above the top string for its sounding marker (radius 16 + halo 4) so nothing is clipped whatever the docking
    /// position. A technique tag that would not fit above the top string's marker is drawn beside it instead.</summary>
    public const double TopPad = 16 + 4 + 2;
    public const double BottomPad = 26;
    /// <summary>Top-right strip kept clear for the pane's hide (X) button.</summary>
    public const double CornerReserve = 24;
    /// <summary>Legend column (108) plus the hide-button corner strip, so neither overlaps the board.</summary>
    public const double LegendWidth = 108 + CornerReserve;
    /// <summary>The margin kept between the board's room and the content's right edge.</summary>
    public const double BoardMargin = 28;

    /// <summary>Smallest spacing between strings that leaves each string's markers and label readable.</summary>
    public const double MinStringGap = 26;
    /// <summary>Natural proportion: string gap is at most this fraction of the (average) fret width. Landscape panes
    /// already sit near it, so only tall / narrow panes are affected.</summary>
    public const double MaxGapToFretWidth = 0.8;
    /// <summary>Hard limit of the user's "Wide" string spacing, as a multiple of the natural gap.</summary>
    public const double MaxSpacingFactor = 1.5;

    /// <summary>The frets a board shows: the display range (12 or 24, else 24) within the track's fret count, at least 12.</summary>
    public static int ShownFrets(Input state)
    {
        var total = state.DisplayFrets is 12 or 24 ? state.DisplayFrets : 24;
        return Math.Max(12, Math.Min(total, Math.Max(12, state.FretCount)));
    }

    public static Layout Compute(
        Input state,
        UiRect content,
        FretboardHorizontalPosition position = FretboardHorizontalPosition.Centre,
        double horizontalOffset = 0,
        double placementWidth = 0,
        FretboardBoardLimits? limits = null)
    {
        var caps = limits ?? FretboardBoardLimits.Default;
        var strings = Math.Max(1, state.Strings);
        var total = ShownFrets(state);

        // Keep dimensions readable on wide windows; the selected anchor only changes its horizontal placement.
        var fullWidth = placementWidth > 0 && double.IsFinite(placementWidth) ? placementWidth : content.Width;
        // A board that clears the legend column gives way to it before it can run under it; the caps leave the board the whole content width.
        var available = caps.ClearsLegend
            ? Math.Max(80, Math.Min(content.Width, fullWidth - LegendWidth) - BoardMargin)
            : Math.Max(80, content.Width - BoardMargin);
        var availableHeight = Math.Max(40, content.Height - TopPad - BottomPad);
        if (caps.RealProportions) return ProportionalLayout(state, content, position, horizontalOffset, fullWidth, available, availableHeight);

        var boardWidth = Math.Max(80, Math.Min(available, caps.MaxBoardWidth) - LeftGutter);
        var left = BoardLeft(content, fullWidth, boardWidth, available, position, horizontalOffset);

        // Show every fret of the chosen range when they fit; otherwise slide a window that keeps
        // the current position visible (narrow windows only).
        var minFretWidth = 13.0;
        var canShowAll = boardWidth / total >= minFretWidth;
        int firstFret, lastFret;
        if (canShowAll)
        {
            firstFret = 1;
            lastFret = total;
        }
        else
        {
            var visible = Math.Max(6, (int)Math.Floor(boardWidth / minFretWidth));
            visible = Math.Min(visible, total);
            firstFret = Math.Clamp(state.AnchorFret - visible / 2, 1, Math.Max(1, total - visible + 1));
            lastFret = Math.Min(total, firstFret + visible - 1);
        }

        var fretWidth = boardWidth / (lastFret - firstFret + 1);
        var gaps = Math.Max(1, strings - 1);

        // The natural string gap fills the pane height only up to a natural proportion of the fret width; a taller
        // pane centres the board (with its labels, which are placed relative to the board) instead of stretching it.
        // The user's spacing factor then scales that natural gap (Compact tighter, Wide up to 1.5x), never past the
        // pane height (nothing clips; the panel raises its natural height for Wide so the dock gives it the room).
        var spacing = SpacingOf(state);
        var heightGap = availableHeight / gaps;
        var naturalGap = Math.Min(heightGap, Math.Max(MinStringGap, MaxGapToFretWidth * fretWidth));
        var floorGap = Math.Min(naturalGap, MinStringGap * Math.Min(1.0, spacing));
        var stringGap = Math.Min(FretboardSizeRules.ClampStringGap(Math.Clamp(naturalGap * spacing, floorGap, naturalGap * MaxSpacingFactor), strings, boardWidth, caps), heightGap);
        var boardHeight = stringGap * gaps;
        var top = content.Y + TopPad + Math.Max(0, (availableHeight - boardHeight) / 2);
        return new Layout(new UiRect(left, top, boardWidth, boardHeight), firstFret, lastFret, fretWidth, stringGap, strings, state.LeftHanded);
    }

    /// <summary>The board at its real proportions: every shown fret keeps the real cell and the strings the real pitch, at one scale, so the board's
    /// width : height is the real aspect (<see cref="FretboardSizeRules.RealAspect"/>) at any pane size. The scale is the largest that fits the board
    /// in the pane (the narrower of the two limits); the spare room stays empty and the board is centred. The board's height is strings × pitch, so
    /// its strings sit one board-gap apart (strings / (strings - 1) of the pitch), the same board the owner's proportions describe.
    /// The user's string spacing stretches that pitch (Natural leaves the real board); the height limit then takes the stretched pitch.</summary>
    private static Layout ProportionalLayout(Input state, UiRect content, FretboardHorizontalPosition position, double horizontalOffset,
        double fullWidth, double available, double availableHeight)
    {
        var strings = Math.Max(1, state.Strings);
        var total = ShownFrets(state);
        var gaps = Math.Max(1, strings - 1);
        var cell = FretboardSizeRules.FretCellMm(state.Bass);
        var pitch = FretboardSizeRules.StringPitchMm(state.Bass) * SpacingOf(state);
        var scale = Math.Min(Math.Max(80, available - LeftGutter) / (total * cell), availableHeight / (strings * pitch));
        var boardWidth = total * cell * scale;
        var boardHeight = strings * pitch * scale;
        var left = BoardLeft(content, fullWidth, boardWidth, available, position, horizontalOffset);
        var top = content.Y + TopPad + Math.Max(0, (availableHeight - boardHeight) / 2);
        return new Layout(new UiRect(left, top, boardWidth, boardHeight), 1, total, cell * scale, boardHeight / gaps, strings, state.LeftHanded);
    }

    /// <summary>The user's string spacing factor (Compact to Wide), 1 (Natural) when it is not finite.</summary>
    private static double SpacingOf(Input state) => double.IsFinite(state.StringSpacing) ? Math.Clamp(state.StringSpacing, 0.75, MaxSpacingFactor) : 1.0;

    /// <summary>The pane width (DIPs, the close reserve not included) that lays a board of this shape out at its full real size in a pane
    /// <paramref name="paneHeight"/> tall: the board's real width at the height the board gets, plus the gutter, the legend column and the margin.</summary>
    public static double PaneWidthForHeight(Input shape, double paneHeight)
    {
        var boardHeight = Math.Max(40, paneHeight - TopPad - BottomPad);
        return boardHeight * FretboardSizeRules.RealAspect(ShownFrets(shape), shape.Strings, shape.Bass) + LeftGutter + LegendWidth + BoardMargin;
    }

    /// <summary>The board's left edge: the placement (Left, Centre or Right) inside the room beside the legend column, moved by the drag offset
    /// and kept inside the room.</summary>
    private static double BoardLeft(UiRect content, double fullWidth, double boardWidth, double available, FretboardHorizontalPosition position, double horizontalOffset)
    {
        var minLeft = content.X + LeftGutter;
        var maxLeft = Math.Max(minLeft, content.X + fullWidth - LegendWidth - boardWidth);
        var centredLeft = minLeft + Math.Max(0, (available - (boardWidth + LeftGutter)) / 2);
        var anchoredLeft = position switch
        {
            FretboardHorizontalPosition.Left => minLeft,
            FretboardHorizontalPosition.Right => maxLeft,
            _ => Math.Clamp(centredLeft, minLeft, maxLeft)
        };
        return Math.Clamp(anchoredLeft + (double.IsFinite(horizontalOffset) ? horizontalOffset : 0), minLeft, maxLeft);
    }

    public static double StringY(in Layout layout, int stringIndex)
    {
        var i = layout.LeftHanded ? layout.Strings - 1 - stringIndex : stringIndex;
        return layout.Board.Y + i * layout.StringGap;
    }

    public static double FretX(in Layout layout, double fret)
    {
        var pos = layout.Board.X + (fret - layout.FirstFret + 0.5) * layout.FretWidth;
        return layout.LeftHanded ? layout.Board.Right - (pos - layout.Board.X) : pos;
    }

    public static UiPoint PositionOf(in Layout layout, int stringIndex, int fret)
        => new(fret <= 0 ? layout.Board.X - 14 : FretX(layout, fret), StringY(layout, stringIndex));

    /// <summary>The area the board is laid out in: the pane, narrowed when the board with its legend would run past the pane's right edge.</summary>
    public static UiRect ContentArea(Input state, UiRect pane, FretboardHorizontalPosition position = FretboardHorizontalPosition.Centre, FretboardBoardLimits? limits = null)
    {
        var overflow = Compute(state, pane, position, limits: limits).Board.Right + LegendWidth - pane.Right;
        return overflow > 0 ? new UiRect(pane.X, pane.Y, Math.Max(80, pane.Width - overflow), pane.Height) : pane;
    }

    /// <summary>The place (Left, Centre or Right) whose board lies nearest the board dragged by <paramref name="horizontalOffset"/> from
    /// <paramref name="current"/>: where a fretboard drag lands when the pointer is released. Ties keep the first place in enum order.</summary>
    public static FretboardHorizontalPosition NearestPlacement(Input state, UiRect content, FretboardHorizontalPosition current, double horizontalOffset, FretboardBoardLimits? limits = null)
    {
        var draggedLeft = Compute(state, content, current, horizontalOffset, limits: limits).Board.X;
        var nearest = FretboardHorizontalPosition.Left;
        var nearestDistance = double.PositiveInfinity;
        foreach (var place in Enum.GetValues<FretboardHorizontalPosition>())
        {
            var distance = Math.Abs(Compute(state, content, place, limits: limits).Board.X - draggedLeft);
            if (distance >= nearestDistance) continue;
            nearest = place;
            nearestDistance = distance;
        }
        return nearest;
    }

    /// <summary>Maps a click inside the board to a string/fret. Returns false outside the board.</summary>
    public static bool HitTest(
        Input state,
        UiRect content,
        UiPoint point,
        out int stringIndex,
        out int fret,
        FretboardHorizontalPosition position = FretboardHorizontalPosition.Centre,
        double horizontalOffset = 0,
        double placementWidth = 0,
        FretboardBoardLimits? limits = null)
    {
        stringIndex = 0; fret = 0;
        if (state.Strings <= 0) return false;
        var layout = Compute(state, content, position, horizontalOffset, placementWidth, limits);
        var board = layout.Board;
        // Allow the open-string strip just left of the nut and a little slack around the board.
        if (point.X < board.X - 26 || point.X > board.Right + 2) return false;
        if (point.Y < board.Y - layout.StringGap * 0.6 || point.Y > board.Bottom + layout.StringGap * 0.6) return false;

        var row = (int)Math.Round((point.Y - board.Y) / layout.StringGap);
        row = Math.Clamp(row, 0, layout.Strings - 1);
        stringIndex = layout.LeftHanded ? layout.Strings - 1 - row : row;

        if (point.X < board.X)
        {
            fret = 0;
            return true;
        }
        var local = layout.LeftHanded ? board.Right - point.X : point.X - board.X;
        var index = (int)Math.Floor(local / layout.FretWidth);
        fret = Math.Clamp(layout.FirstFret + index, 1, state.FretCount);
        return true;
    }
}
