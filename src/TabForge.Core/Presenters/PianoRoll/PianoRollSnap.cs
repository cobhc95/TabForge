using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the snap of a note edge or a click in the piano roll, in beats: the grid line of the bar under it (the transport's SnapSettings grid,
//       straight or triplet), the starts and ends of the other notes (SnapSettings "items" in the piano roll), and the playhead. Distances are
//       measured in screen pixels at the view's zoom, so the snap distance means what it means on the timeline. With snapping off, or Alt held,
//       nothing snaps.
// Does not own: the settings (SnapSettings, held by the transport bar), the grid maths (SnapCore, PianoRollTimeMap) or the drag (the view).
// Tests: TabForge.Avalonia.Tests.PianoRollSnapTests.
public static class PianoRollSnap
{
    /// <summary>
    /// The beat a position at <paramref name="beat"/> snaps to. <paramref name="pixelsPerBeat"/> is the zoom; <paramref name="items"/> are the
    /// other notes' starts and ends (the dragged notes left out); <paramref name="playhead"/> is the playhead's beat, or null.
    /// </summary>
    public static double Snap(PianoRollTimeMap map, SnapSettings? snap, double beat, double pixelsPerBeat, IEnumerable<double>? items, double? playhead, bool altHeld)
    {
        if (snap is not { Enabled: true } || altHeld || pixelsPerBeat <= 0) return beat;
        var best = beat;
        var bestDistance = double.PositiveInfinity;
        void Consider(double candidate, double limitPx) =>
            SnapCore.Consider(ref best, ref bestDistance, candidate, Math.Abs(candidate - beat) * pixelsPerBeat, limitPx);
        if (snap.ToItems && items is not null)
            foreach (var item in items) Consider(item, snap.DistancePx);
        if (snap.ToPlayhead && playhead is { } at) Consider(at, snap.DistancePx);
        if (snap.ToGrid) Consider(map.NearestGrid(beat, snap.Grid), snap.GridAtAnyDistance ? double.PositiveInfinity : snap.DistancePx);
        return best;
    }

    /// <summary>The grid line at or before <paramref name="beat"/> (a click in Draw or Split lands on the line left of the pointer, as DAWs do);
    /// the pointer's own beat with snapping off or Alt held.</summary>
    public static double Floor(PianoRollTimeMap map, SnapSettings? snap, double beat, bool altHeld)
    {
        if (snap is not { Enabled: true, ToGrid: true } || altHeld) return beat;
        var step = map.GridStep(snap.Grid, beat);
        var bar = map.BarAtBeat(beat);
        var inBar = Math.Floor((beat - bar.StartBeat) / step + 1e-6) * step;
        return Math.Max(0, bar.StartBeat + inBar);
    }

    /// <summary>The choices of the Length dropdown: Grid (one snap step), the note values with their triplets and dotted values, Last used.</summary>
    public static IReadOnlyList<string> Lengths { get; } = new[] { "Grid", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/4T", "1/8T", "1/16T", "1/4D", "1/8D", "1/16D", "Last used" };

    /// <summary>The length (beats) a new note gets with the Length choice <paramref name="choice"/>: Grid follows the snap, Last used repeats
    /// <paramref name="lastUsed"/> (the snap step before any note was drawn), the others are their note values.</summary>
    public static double NewLength(PianoRollTimeMap map, SnapSettings? snap, double beat, string choice, double lastUsed) => choice switch
    {
        "Grid" => NoteLength(map, snap, beat),
        "Last used" => lastUsed > 0 ? lastUsed : NoteLength(map, snap, beat),
        "1/1" => 4,
        _ => SnapCore.GridStepSlots(choice, 16) / SnapCore.SlotsPerBeat,
    };

    /// <summary>The length a new note gets: one grid step (a quarter note with snapping off).</summary>
    public static double NoteLength(PianoRollTimeMap map, SnapSettings? snap, double beat) =>
        snap is { Enabled: true } ? Math.Min(map.GridStep(snap.Grid, beat), map.BarAtBeat(beat).Beats) : 1;
}
