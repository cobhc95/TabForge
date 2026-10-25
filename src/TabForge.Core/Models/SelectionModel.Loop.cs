namespace TabForge.Models;

// Owns: the loop region of one document (its bars, the score cells of a loop made from notes, and whether the Loop button repeats it), the pure loop
//   rules (a released score selection sets it unless a loop is playing; the Escape order white first, then the loop) and its undo snapshot.
// Does not own: the undo step a loop clear or ruler adjust records (LoopEdits), the transport that repeats it or its drawing (TimelineLoopMarks).
// Tests: LoopRegionTests.

/// <summary>The loop region as an undo step stores it: the bars and cells, whether a score selection made it, and whether the Loop button repeats it.</summary>
public readonly record struct LoopRegionSnapshot(bool Has, int StartBar, int EndBar, int StartCell, int EndCell, bool FromScore, bool Repeat)
{
    /// <summary>No loop region, not repeating.</summary>
    public static LoopRegionSnapshot None => new(false, -1, -1, 0, -1, false, false);
}

/// <summary>What an Escape press cleared (see <see cref="SelectionModel.EscapeNext"/>).</summary>
public enum EscapeResult { Nothing, White, Loop }

public sealed partial class SelectionModel
{
    /// <summary>True while a loop region exists (made on the ruler or by a released score selection). It stays when the white selection is cleared.</summary>
    public bool HasLoop { get; private set; }
    public int LoopStartBar { get; private set; } = -1;
    public int LoopEndBar { get; private set; } = -1;
    /// <summary>First score cell inside <see cref="LoopStartBar"/> (0 = bar start; only a loop made from notes has one).</summary>
    public int LoopStartCell { get; private set; }
    /// <summary>Last score cell inside <see cref="LoopEndBar"/>; -1 = the whole last bar.</summary>
    public int LoopEndCell { get; private set; } = -1;
    /// <summary>True when a score note selection made the region (its edges snap to beats, a ruler loop's to bars).</summary>
    public bool LoopFromScore { get; private set; }
    /// <summary>The Loop button: the transport repeats the region (or, with none, the whole song) while this is on. Toggling it never clears the region.</summary>
    public bool LoopRepeat { get; private set; }

    /// <summary>The loop region's bars, or null without one.</summary>
    public (int Start, int End)? LoopBars => HasLoop ? (LoopStartBar, LoopEndBar) : null;

    public bool LoopContains(int bar) => HasLoop && bar >= LoopStartBar && bar <= LoopEndBar;

    public LoopRegionSnapshot LoopSnapshot() => new(HasLoop, LoopStartBar, LoopEndBar, LoopStartCell, LoopEndCell, LoopFromScore, LoopRepeat);

    /// <summary>Sets the loop region to bars <paramref name="start"/>…<paramref name="end"/> (any order), keeping the Loop button as it is.</summary>
    public bool SetLoop(int start, int end, int startCell = 0, int endCell = -1, bool fromScore = false, SelectionOrigin origin = SelectionOrigin.Command)
    {
        if (end < start) { (start, end) = (end, start); (startCell, endCell) = (0, -1); }
        start = Math.Max(0, start);
        return ApplyLoop(new LoopRegionSnapshot(true, start, Math.Max(start, end), Math.Max(0, startCell), Math.Max(-1, endCell), fromScore, LoopRepeat), origin);
    }

    /// <summary>Drops the loop region; the Loop button stays as it is (playback carries straight on).</summary>
    public bool ClearLoop(SelectionOrigin origin = SelectionOrigin.Command)
    {
        if (!HasLoop) return false;
        if (Focus == FocusTarget.Loop) SetFocusQuietly(FocusTarget.None);   // announced with the loop change: one notification
        return ApplyLoop(LoopRegionSnapshot.None with { Repeat = LoopRepeat }, origin);   // 25 Oct: clearing the region never switches the Loop button
    }

    /// <summary>The Loop button: repeat on or off; the region stays.</summary>
    public bool SetLoopRepeat(bool repeat, SelectionOrigin origin = SelectionOrigin.Command) => ApplyLoop(LoopSnapshot() with { Repeat = repeat }, origin);

    /// <summary>Shows a stored loop region again (undo or redo), kept inside <paramref name="barCount"/> bars.</summary>
    public bool RestoreLoop(LoopRegionSnapshot loop, int barCount, SelectionOrigin origin = SelectionOrigin.Document)
    {
        var changed = ApplyLoop(loop, origin);
        return ClampLoopTo(barCount, origin) | changed;
    }

    /// <summary>
    /// True when a released score selection may set the loop region now: always, except while playback is actively looping (Loop on and playing,
    /// a region or the whole song/section): the playing loop stays put; the notes are still selected.
    /// </summary>
    public bool ScoreSelectionSetsLoop(bool playing) => !(playing && LoopRepeat);

    /// <summary>
    /// A score note selection was released: the loop region becomes exactly its beats and the Loop button turns on, (while a loop plays the Loop button stays as it is)
    /// (<see cref="ScoreSelectionSetsLoop"/>). False when nothing changed.
    /// </summary>
    public bool LoopScoreSelection(bool playing)
    {
        // Re-enabled 25 Oct: sets the region only; the Loop button (Repeat) is never switched on here.
        if (!HasScoreSelection) return false;
        return ApplyLoop(new LoopRegionSnapshot(true, StartBar, EndBar, StartCell, EndCell, true, LoopRepeat), SelectionOrigin.Editor);
    }

    /// <summary>
    /// Escape, Guitar Pro style: the first presses clear the white selection (<see cref="ClearNext"/>); once nothing white is left the next one clears the
    /// loop region and turns Loop off (also while it plays). The caller records a loop clear as one undo step (LoopEdits.Escape).
    /// </summary>
    public EscapeResult EscapeNext()
    {
        if (ClearNext()) return EscapeResult.White;
        return HasLoop && ClearLoop() ? EscapeResult.Loop : EscapeResult.Nothing;
    }

    private bool ApplyLoop(LoopRegionSnapshot loop, SelectionOrigin origin)
    {
        if (!loop.Has) loop = LoopRegionSnapshot.None with { Repeat = loop.Repeat };   // no region: Repeat loops the whole song
        if (loop == LoopSnapshot()) return false;
        HasLoop = loop.Has;
        LoopStartBar = loop.StartBar;
        LoopEndBar = loop.EndBar;
        LoopStartCell = loop.StartCell;
        LoopEndCell = loop.EndCell;
        LoopFromScore = loop.FromScore;
        LoopRepeat = loop.Repeat;
        Notify(origin);
        return true;
    }

    // Bars moved: the region follows its bars; one whose bars are all gone is cleared.
    private bool RemapLoop(int[] oldToNew, int barCount, SelectionOrigin origin)
    {
        if (!HasLoop) return false;
        int? first = null, last = null;
        for (var bar = LoopStartBar; bar <= LoopEndBar && bar < oldToNew.Length; bar++)
            if (oldToNew[bar] >= 0) { first = Math.Min(first ?? int.MaxValue, oldToNew[bar]); last = Math.Max(last ?? -1, oldToNew[bar]); }
        if (first is not { } start || barCount <= 0) return ApplyLoop(LoopSnapshot() with { Has = false }, origin);
        start = Math.Clamp(start, 0, barCount - 1);
        var end = Math.Clamp(last!.Value, start, barCount - 1);
        var keep = start == oldToNew.ElementAtOrDefault(LoopStartBar) && end == oldToNew.ElementAtOrDefault(LoopEndBar);
        return ApplyLoop(LoopSnapshot() with { StartBar = start, EndBar = end, StartCell = keep ? LoopStartCell : 0, EndCell = keep ? LoopEndCell : -1 }, origin);
    }

    private bool ClampLoopTo(int barCount, SelectionOrigin origin)
    {
        if (!HasLoop) return false;
        if (barCount <= 0 || LoopStartBar >= barCount) return ApplyLoop(LoopSnapshot() with { Has = false }, origin);
        var end = Math.Min(LoopEndBar, barCount - 1);
        return ApplyLoop(LoopSnapshot() with { EndBar = end, EndCell = end == LoopEndBar ? LoopEndCell : -1 }, origin);
    }
}
