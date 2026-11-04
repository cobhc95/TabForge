using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the score editor's pure cursor-move rules: where the caret goes for a beat, bar, string, bar end, last bar and
//   entered-note move, and the song-end test. Each rule returns the target; null means the caret stays.
// Does not own: the per-bar cursor stops (IBarCursorStops, implemented by TabForge.Core Score/ScoreCursorStops.cs), applying the
//   target, selection events, playback seeking and scrolling (Views/TabEditorControl.Navigation.cs), the line move (needs layout).
// Tests: no dedicated test class.

/// <summary>The cells the cursor may sit on in each bar of one track.</summary>
public interface IBarCursorStops
{
    int MeasureCount { get; }
    IReadOnlyList<TabCell> Cells(int measure);
    /// <summary>The allowed cell for grid cell <paramref name="cell"/>; 0 for a bar outside the track.</summary>
    int Snap(int measure, int cell);
    /// <summary>The next stop to the right, padding the bar; -1 at the bar's end.</summary>
    int RightTarget(int measure, int cell);
    bool AtEnd(int measure, int cell);
    int Previous(int measure, int cell);
    int BarEndCursor(int measure);
    int EndTarget(int measure, int cell);
    int LastWritten(int measure);
    int LastCell(int measure);
}

public readonly record struct CursorTarget(int Measure, int Cell);

public static class ScoreCursorNavigator
{
    /// <summary>True when Right at the cursor in the last bar adds a bar.</summary>
    public static bool AtSongEnd(IBarCursorStops stops, int measure, int cell)
    {
        if (stops.MeasureCount == 0 || measure < stops.MeasureCount - 1) return false;
        if (measure >= stops.MeasureCount) return true;   // the caret is past a shorter track: Right goes on to a new bar
        return stops.AtEnd(measure, cell);
    }

    public static CursorTarget? ForwardBeat(IBarCursorStops stops, int measure, int cell)
    {
        if (measure >= stops.MeasureCount) return null;
        var next = stops.RightTarget(measure, cell);
        if (next >= 0) return new(measure, next);
        return measure + 1 < stops.MeasureCount ? new(measure + 1, stops.Snap(measure + 1, 0)) : null;
    }

    public static CursorTarget? BackBeat(IBarCursorStops stops, int measure, int cell)
    {
        if (measure >= stops.MeasureCount) return null;
        var previous = stops.Previous(measure, cell);
        if (previous >= 0) return new(measure, previous);
        return measure > 0 ? new(measure - 1, stops.BarEndCursor(measure - 1)) : new(0, stops.Snap(0, 0));
    }

    public static CursorTarget Bar(IBarCursorStops stops, int measure, int cell, int direction)
    {
        var target = Math.Clamp(measure + direction, 0, Math.Max(0, stops.MeasureCount - 1));
        return new(target, stops.Snap(target, cell));
    }

    /// <summary>Up/down past the top or bottom string wraps round within the same beat, as GP5.</summary>
    public static int WrapString(int stringIndex, int direction, int stringCount)
    {
        var count = Math.Max(1, stringCount);
        return ((stringIndex + direction) % count + count) % count;
    }

    public static int BarEnd(IBarCursorStops stops, int measure, int cell)
    {
        var bar = Math.Clamp(measure, 0, stops.MeasureCount - 1);
        return stops.EndTarget(bar, cell);
    }

    /// <summary>The last written beat of the last bar, as GP5.</summary>
    public static CursorTarget LastBar(IBarCursorStops stops) => new(stops.MeasureCount - 1, stops.LastWritten(stops.MeasureCount - 1));

}
