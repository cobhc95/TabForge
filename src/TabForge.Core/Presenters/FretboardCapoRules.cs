using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the capo rules of the fretboard: the clamped fret range, the full and partial grab hit tests, the band a partial
// covers, the fret a drag lands on, and the drag session that previews without writing and commits once.
// Does not own: the capo pitch (TrackModel.PitchOf), the undo step (the strip commits through the score editor) or the drawing.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public static class FretboardCapoRules
{
    /// <summary>Capo fret range on the fretboard: 0 (off) to 24.</summary>
    public const int MinFret = 0, MaxFret = 24;

    /// <summary>How far from the capo's centre a press still grabs it, in drawing DIPs.</summary>
    public const double GrabRadius = 12;

    /// <summary>The capo fret kept in 0..24.</summary>
    public static int Clamp(int fret) => Math.Clamp(fret, MinFret, MaxFret);

    /// <summary>True when the point (drawing units) grabs the capo fitted at <paramref name="capo"/>: near its
    /// centre and within the board's vertical span. A capo of 0 has no image and cannot be grabbed.</summary>
    public static bool HitTest(in FretboardGeometryRules.Layout layout, double x, double y, int capo)
    {
        if (capo < layout.FirstFret || capo > layout.LastFret) return false;
        var centre = FretboardGeometryRules.FretX(layout, capo);
        return Math.Abs(x - centre) <= GrabRadius
            && y >= layout.Board.Y - GrabRadius && y <= layout.Board.Bottom + GrabRadius;
    }

    /// <summary>True when the point grabs the partial capo at <paramref name="fret"/> covering <paramref name="mask"/>: near its
    /// centre and on one of the strings it covers (not the bare board between its edge and the next string).</summary>
    public static bool HitTestPartial(in FretboardGeometryRules.Layout layout, double x, double y, int fret, int mask)
    {
        if (!HitTest(layout, x, y, fret)) return false;
        for (var s = 0; s < layout.Strings; s++)
            if (PartialCapoRules.Covers(fret, mask, s) && Math.Abs(y - FretboardGeometryRules.StringY(layout, s)) <= layout.StringGap * 0.6) return true;
        return false;
    }

    /// <summary>The top and bottom of the band a partial capo covers, one string gap wide around the outer covered strings; null
    /// when the mask covers no shown string.</summary>
    public static (double Top, double Bottom)? PartialBand(in FretboardGeometryRules.Layout layout, int mask)
    {
        var first = -1;
        var last = -1;
        for (var s = 0; s < layout.Strings; s++)
        {
            if (s >= 31 || (mask & (1 << s)) == 0) continue;
            if (first < 0) first = s;
            last = s;
        }
        if (first < 0) return null;
        var half = layout.StringGap / 2;
        return (FretboardGeometryRules.StringY(layout, first) - half, FretboardGeometryRules.StringY(layout, last) + half);
    }

    /// <summary>The fret a capo drag at <paramref name="x"/> lands on: left of the nut is 0 (off), else the
    /// nearest shown fret centre, clamped to 0..24.</summary>
    public static int FretAtX(in FretboardGeometryRules.Layout layout, double x)
    {
        // FretX is already mirrored on a left-handed board, so the pointer is compared as it is. Past the nut is no capo.
        if (layout.LeftHanded ? x > layout.Board.Right : x < layout.Board.X) return MinFret;
        var nearest = layout.FirstFret;
        var nearestDistance = double.PositiveInfinity;
        for (var f = layout.FirstFret; f <= layout.LastFret; f++)
        {
            var distance = Math.Abs(x - FretboardGeometryRules.FretX(layout, f));
            if (distance >= nearestDistance) continue;
            nearest = f;
            nearestDistance = distance;
        }
        return Clamp(nearest);
    }
}

/// <summary>A capo drag: the pointer moves preview the fret with no song write; the release commits once, so one
/// drag is exactly one undo step and one engine sync. No static state: the view owns one per drag.</summary>
public sealed class FretboardCapoDrag
{
    private readonly Action<int> _preview;
    private readonly Func<int, bool> _commit;
    private bool _ended;

    /// <param name="preview">Runs on every fret change while dragging (the display only; no song write).</param>
    /// <param name="commit">Runs at most once, on release, when the fret changed; its result is the edit's.</param>
    public FretboardCapoDrag(int startFret, Action<int> preview, Func<int, bool> commit)
    {
        StartFret = FretboardCapoRules.Clamp(startFret);
        CurrentFret = StartFret;
        _preview = preview;
        _commit = commit;
    }

    /// <summary>The capo fret when the drag started.</summary>
    public int StartFret { get; }

    /// <summary>The fret the drag shows now.</summary>
    public int CurrentFret { get; private set; }

    /// <summary>How many moves changed the shown fret (display only; never a song write).</summary>
    public int PreviewCount { get; private set; }

    /// <summary>How many commits ran: 0 or 1 per drag.</summary>
    public int CommitCount { get; private set; }

    /// <summary>The drag's result: whether the release wrote the song.</summary>
    public bool Committed { get; private set; }

    /// <summary>Shows <paramref name="fret"/> (clamped) when it differs; never writes the song.</summary>
    public void Move(int fret)
    {
        if (_ended) return;
        fret = FretboardCapoRules.Clamp(fret);
        if (fret == CurrentFret) return;
        CurrentFret = fret;
        PreviewCount++;
        _preview(fret);
    }

    /// <summary>Ends the drag: commits once when the fret changed, else writes nothing. Returns the commit's result.</summary>
    public bool End()
    {
        if (_ended) return Committed;
        _ended = true;
        if (CurrentFret == StartFret) return false;
        CommitCount++;
        Committed = _commit(CurrentFret);
        return Committed;
    }
}
