namespace TabForge.Presenters;

// Owns: the view-free part of snapping that the timeline clip drag and the piano roll share: the grid step of each SnapSettings grid value
//       (straight and triplet) in sixteenth slots, the nearest grid line inside one bar in any unit (pixels or beats), and the nearest-candidate
//       rule (ClipGesturePresenter.ConsiderSnap).
// Does not own: which candidates a drag offers (TimelineClipSnap for clips, PianoRollSnap for notes), the settings (SnapSettings) or any view.
// Tests: TabForge.Avalonia.Tests.PianoRollSnapTests, TimelineAudioClipTests.
public static class SnapCore
{
    /// <summary>Sixteenth slots in one quarter-note beat.</summary>
    public const double SlotsPerBeat = 4;

    /// <summary>The grid step in sixteenth slots of a bar of <paramref name="slots"/>: "Bar" is the whole bar, the others are note values (a quarter
    /// note is 4 slots), the T values their triplets (three in the time of two: 1/8T is 4/3 of a slot) and the D values dotted (1/8D is 3 slots).</summary>
    public static double GridStepSlots(string grid, int slots) => grid switch
    {
        "Bar" => slots,
        "1/2" => 8.0,
        "1/4" => 4.0,
        "1/8" => 2.0,
        "1/16" => 1.0,
        "1/32" => 0.5,
        "1/4T" => 8.0 / 3,
        "1/8T" => 4.0 / 3,
        "1/16T" => 2.0 / 3,
        "1/4D" => 6.0,
        "1/8D" => 3.0,
        "1/16D" => 1.5,
        _ => 4.0,
    };

    /// <summary>True for a triplet grid value (1/4T, 1/8T, 1/16T).</summary>
    public static bool IsTriplet(string grid) => grid.EndsWith('T');

    /// <summary>
    /// The grid line nearest to <paramref name="position"/> inside the bar that starts at <paramref name="barStart"/> and is <paramref name="barSize"/>
    /// long (any unit) and holds <paramref name="slots"/> sixteenths. The lines run from the bar start, so every third line of a triplet grid is a
    /// straight one (1/8T meets each beat, 1/4T each half note).
    /// </summary>
    public static double NearestInBar(double position, double barStart, double barSize, int slots, string grid)
    {
        if (barSize <= 0 || slots <= 0) return position;
        var step = GridStepSlots(grid, slots);
        var slot = (position - barStart) / barSize * slots;
        var snapped = Math.Round(slot / step) * step;
        return barStart + snapped / slots * barSize;
    }

    /// <summary>Keeps the nearest candidate within its limit (the one rule both snaps use).</summary>
    public static void Consider(ref double best, ref double bestDistance, double candidate, double distance, double limit) =>
        ClipGesturePresenter.ConsiderSnap(ref best, ref bestDistance, candidate, distance, limit);
}
