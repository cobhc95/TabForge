using System.Windows;

namespace TabForge.Visualization;

public enum FretboardHorizontalPosition { Left, Centre, Right }

/// <summary>
/// Single source of truth for fretboard geometry, shared by the renderer and by
/// hit testing so clicking always matches what is drawn.
/// </summary>
public static class FretboardGeometry
{
    public readonly record struct Layout(Rect Board, int FirstFret, int LastFret, double FretWidth, double StringGap, int Strings, bool LeftHanded);

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

    /// <summary>Smallest spacing between strings that leaves each string's markers and label readable.</summary>
    public const double MinStringGap = 26;
    /// <summary>Natural proportion: string gap is at most this fraction of the (average) fret width. Landscape panes
    /// already sit near it, so only tall / narrow panes are affected.</summary>
    public const double MaxGapToFretWidth = 0.8;
    /// <summary>Hard limit of the user's "Wide" string spacing, as a multiple of the natural gap.</summary>
    public const double MaxSpacingFactor = 1.5;

    public static Layout Compute(
        InstrumentVisualState state,
        Rect content,
        FretboardHorizontalPosition position = FretboardHorizontalPosition.Centre,
        double horizontalOffset = 0,
        double placementWidth = 0)
    {
        var strings = Math.Max(1, state.Tuning.Count);
        var total = state.DisplayFrets is 12 or 24 ? state.DisplayFrets : 24;
        total = Math.Max(12, Math.Min(total, Math.Max(12, state.FretCount)));

        // Keep dimensions readable on wide windows; the selected anchor only changes its horizontal placement.
        var available = Math.Max(80, content.Width - 28);
        var boardWidth = Math.Max(80, Math.Min(available, 1180) - LeftGutter);
        var minLeft = content.X + LeftGutter;
        var fullWidth = placementWidth > 0 && double.IsFinite(placementWidth) ? placementWidth : content.Width;
        var maxLeft = Math.Max(minLeft, content.X + fullWidth - LegendWidth - boardWidth);
        var centredLeft = minLeft + Math.Max(0, (available - (boardWidth + LeftGutter)) / 2);
        var anchoredLeft = position switch
        {
            FretboardHorizontalPosition.Left => minLeft,
            FretboardHorizontalPosition.Right => maxLeft,
            _ => Math.Clamp(centredLeft, minLeft, maxLeft)
        };
        var left = Math.Clamp(anchoredLeft + (double.IsFinite(horizontalOffset) ? horizontalOffset : 0), minLeft, maxLeft);
        var availableHeight = Math.Max(40, content.Height - TopPad - BottomPad);

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
            var anchor = state.Current?.Fret ?? state.Next?.Fret ?? 0;
            firstFret = Math.Clamp(anchor - visible / 2, 1, Math.Max(1, total - visible + 1));
            lastFret = Math.Min(total, firstFret + visible - 1);
        }

        var fretWidth = boardWidth / (lastFret - firstFret + 1);
        var gaps = Math.Max(1, strings - 1);

        // The natural string gap fills the pane height only up to a natural proportion of the fret width; a taller
        // pane centres the board (with its labels, which are placed relative to the board) instead of stretching it.
        // The user's spacing factor then scales that natural gap (Compact tighter, Wide up to 1.5x), never past the
        // pane height (nothing clips; the panel raises its natural height for Wide so the dock gives it the room).
        var spacing = double.IsFinite(state.StringSpacing) ? Math.Clamp(state.StringSpacing, 0.75, MaxSpacingFactor) : 1.0;
        var heightGap = availableHeight / gaps;
        var naturalGap = Math.Min(heightGap, Math.Max(MinStringGap, MaxGapToFretWidth * fretWidth));
        var floorGap = Math.Min(naturalGap, MinStringGap * Math.Min(1.0, spacing));
        var stringGap = Math.Min(Math.Clamp(naturalGap * spacing, floorGap, naturalGap * MaxSpacingFactor), heightGap);
        var boardHeight = stringGap * gaps;
        var top = content.Y + TopPad + Math.Max(0, (availableHeight - boardHeight) / 2);
        var board = new Rect(left, top, boardWidth, boardHeight);
        return new Layout(board, firstFret, lastFret, fretWidth, stringGap, strings, state.LeftHanded);
    }

    public static double StringY(in Layout layout, int stringIndex)
    {
        var i = layout.LeftHanded ? layout.Strings - 1 - stringIndex : stringIndex;
        return layout.Board.Top + i * layout.StringGap;
    }

    public static double FretX(in Layout layout, double fret)
    {
        var pos = layout.Board.Left + (fret - layout.FirstFret + 0.5) * layout.FretWidth;
        return layout.LeftHanded ? layout.Board.Right - (pos - layout.Board.Left) : pos;
    }

    public static Point PositionOf(in Layout layout, int stringIndex, int fret)
        => new(fret <= 0 ? layout.Board.Left - 14 : FretX(layout, fret), StringY(layout, stringIndex));

    /// <summary>Maps a click inside the board to a string/fret. Returns false outside the board.</summary>
    public static bool HitTest(
        InstrumentVisualState state,
        Rect content,
        Point point,
        out int stringIndex,
        out int fret,
        FretboardHorizontalPosition position = FretboardHorizontalPosition.Centre,
        double horizontalOffset = 0,
        double placementWidth = 0)
    {
        stringIndex = 0; fret = 0;
        if (state.Tuning.Count == 0) return false;
        var layout = Compute(state, content, position, horizontalOffset, placementWidth);
        var board = layout.Board;
        // Allow the open-string strip just left of the nut and a little slack around the board.
        if (point.X < board.Left - 26 || point.X > board.Right + 2) return false;
        if (point.Y < board.Top - layout.StringGap * 0.6 || point.Y > board.Bottom + layout.StringGap * 0.6) return false;

        var row = (int)Math.Round((point.Y - board.Top) / layout.StringGap);
        row = Math.Clamp(row, 0, layout.Strings - 1);
        stringIndex = layout.LeftHanded ? layout.Strings - 1 - row : row;

        if (point.X < board.Left)
        {
            fret = 0;
            return true;
        }
        var local = layout.LeftHanded ? board.Right - point.X : point.X - board.Left;
        var index = (int)Math.Floor(local / layout.FretWidth);
        fret = Math.Clamp(layout.FirstFret + index, 1, state.FretCount);
        return true;
    }
}
