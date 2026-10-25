using TabForge.Models;

namespace TabForge.Playback;

// Owns: the order in which a score's bars are performed (repeats, alternate endings, jumps and directions).
// Does not own: MIDI events (ScoreToMidiCompiler) and the playhead (PlayheadMapper).
// Tests: TestPlaybackOrderSpec, PlaybackStartOrderTests.
// Long on purpose: the repeat and jump rules are one ordered walk over the bars, and its stages share that walk's state.

/// <summary>
/// Turns a score's bar structure into the order in which the bars are performed, following the repeat,
/// alternate-ending and direction rules that Guitar Pro files define.
/// </summary>
/// <remarks>
/// The order is built in three stages:
/// <list type="number">
/// <item>Repeats and alternate endings are expanded, walking the bars from the first one.</item>
/// <item>Directions (D.C., D.S., D.S.S., To Coda, Coda, Fine) are applied to that expanded order.</item>
/// <item>The start bar cuts the performed order at its first performance at or after the start bar.</item>
/// </list>
/// Only the first track's bars carry structure; bars beyond the end of the first track are plain bars.
/// Every stage has a visit limit, so contradictory structures still give a finite order.
/// </remarks>
public static partial class PlaybackOrder
{
    /// <summary>The largest number of passes a repeat close can have.</summary>
    public const int MaxRepeats = 99;

    /// <summary>Endings can name passes 1 to 8 only.</summary>
    private const int EndingPassCount = 8;

    /// <summary>
    /// Bar indices (source bars, from 0) in performance order. The same bar appears once per time it is
    /// played; the list is empty only for a score without bars or a start bar at or past the song's end.
    /// </summary>
    /// <param name="project">The song. Structure is read from its first track.</param>
    /// <param name="opt">Uses <see cref="PlaybackOptions.RepeatExpansion"/>, <see cref="PlaybackOptions.StartBar"/> and <see cref="PlaybackOptions.StartOccurrence"/>.</param>
    public static List<int> Build(SongProject project, PlaybackOptions opt)
    {
        opt ??= new PlaybackOptions();
        var score = BarStructure.Read(project);
        if (score.BarCount == 0) return new List<int>();

        var expanded = opt.RepeatExpansion ? ExpandRepeats(score) : Enumerable.Range(0, score.BarCount).ToList();
        var performed = ApplyDirections(score, expanded);
        return ApplyStartBar(performed, opt.StartBar, score.BarCount, opt.StartOccurrence);
    }

    /// <summary>The alternate-ending mask of every source bar (its own or its bracket's; bit n = pass n+1, 0 = plays on every pass), as the order itself reads it.</summary>
    public static int[] EndingMasks(SongProject project)
    {
        var score = BarStructure.Read(project);
        var masks = new int[score.BarCount];
        for (var bar = 0; bar < masks.Length; bar++) masks[bar] = score.Ending(bar);
        return masks;
    }

    /// <summary>
    /// The loop's inclusive source-bar range: both loop bars clamped to the song, in ascending order.
    /// A song without bars gives (0, 0).
    /// </summary>
    public static (int startBar, int endBar) LoopRange(SongProject project, PlaybackOptions opt)
    {
        opt ??= new PlaybackOptions();
        var last = Math.Max(0, BarStructure.CountBars(project) - 1);
        var a = Math.Clamp(opt.LoopStartBar, 0, last);
        var b = Math.Clamp(opt.LoopEndBar, 0, last);
        return a <= b ? (a, b) : (b, a);
    }

    /// <summary>
    /// The loop's start and end in milliseconds inside a compiled timeline. The loop starts at the selected
    /// grid cell of the first performance of its first bar and ends after the selected cell (or the whole bar)
    /// of the last performance of its last bar. Not available when either bar is missing from the timeline or
    /// the span is empty.
    /// </summary>
    public static (double startMs, double endMs, bool available) LoopBounds(ScoreTimeline timeline, SongProject project, PlaybackOptions opt)
    {
        opt ??= new PlaybackOptions();
        var (startBar, endBar) = LoopRange(project, opt);

        ScoreBar? first = null;
        ScoreBar? last = null;
        foreach (var bar in timeline.Bars)
        {
            if (bar.Bar == startBar && first is null) first = bar;
            if (bar.Bar == endBar) last = bar;
        }
        if (first is not { } from || last is not { } to) return (0, 0, false);

        var fromSlots = Math.Max(1, from.Slots);
        var startCell = Math.Clamp(opt.LoopStartCell, 0, fromSlots - 1);
        var start = from.StartMs + (from.EndMs - from.StartMs) * startCell / fromSlots;

        var toSlots = Math.Max(1, to.Slots);
        var endCell = opt.LoopEndCell == -1 ? toSlots : Math.Clamp(opt.LoopEndCell + 1, 1, toSlots);
        var end = to.StartMs + (to.EndMs - to.StartMs) * endCell / toSlots;

        return end > start ? (start, end, true) : (0, 0, false);
    }

    /// <summary>
    /// A bar's direction names: the comma-separated text split, trimmed, empty pieces dropped, and each
    /// stored spelling normalised (for example <c>JumpDaCoda</c> becomes <c>ToCoda</c>, <c>TargetSegno</c>
    /// becomes <c>Segno</c>). Names are compared case-insensitively afterwards.
    /// </summary>
    internal static string[] DirectionTokens(string? directions)
    {
        if (string.IsNullOrWhiteSpace(directions)) return Array.Empty<string>();
        return directions.Split(',')
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length > 0)
            .Select(NormaliseDirection)
            .ToArray();
    }

    private static string NormaliseDirection(string piece) => piece switch
    {
        "JumpDaCoda" => "ToCoda",
        "JumpDaDoubleCoda" => "ToDoubleCoda",
        "JumpDaCapoAlFine" => "DaCapo",
        "JumpDalSegnoAlFine" => "DalSegno",
        "JumpDaCapoAlDoubleCoda" => "DaCapoAlCoda",
        "JumpDalSegnoAlDoubleCoda" => "DalSegnoAlCoda",
        "JumpDalSegnoSegno" or "JumpDalSegnoSegnoAlFine" => "DalSegnoSegno",
        "JumpDalSegnoSegnoAlCoda" or "JumpDalSegnoSegnoAlDoubleCoda" => "DalSegnoSegnoAlCoda",
        _ when piece.Length > 6 && piece.StartsWith("Target", StringComparison.Ordinal) => piece[6..],
        _ when piece.Length > 4 && piece.StartsWith("Jump", StringComparison.Ordinal) => piece[4..],
        _ => piece,
    };

    // ------------------------------------------------------------------------------------------------
    // Stage 1: repeats and alternate endings
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Walks the bars from the first one and expands repeats and alternate endings.
    /// <list type="bullet">
    /// <item>The song start acts as an open until the first open bar is played. The most recently played
    /// open is the return point; the pass count restarts only when an open bar is reached for the first time.</item>
    /// <item>A close with N passes sends playback back N-1 times, then lets it through. The count belongs to
    /// the close; a close that is not played (its ending does not cover the pass) still counts. A close that has an
    /// ending of its own is the exception: it sends playback back every time it plays, and once the passes its ending names are
    /// used up a skipped close lets playback through, so the endings decide how many passes there are.</item>
    /// <item>A close under an ending whose jumps are used up hands over to a further close of the same repeat
    /// (one that follows before the next open bar). Otherwise the repeat is finished: pass count and jump counts
    /// are cleared, and a later close without an open of its own returns to the bar after this close.</item>
    /// <item>An ending bar plays on the passes it names. After a repeat has finished, ending bars play once
    /// until the next open bar. On pass 9 and later, ending bars are skipped.</item>
    /// <item>An ending bar that also opens a repeat, directly after a close (or on the first bar), is the
    /// previous repeat's ending; within its own repeat it plays on every pass.</item>
    /// </list>
    /// </summary>
    private static List<int> ExpandRepeats(BarStructure score)
    {
        var n = score.BarCount;
        var order = new List<int>();
        var jumpsTaken = new int[n];
        var returnPoint = 0;
        var returnPointIsOpenBar = false;
        var pass = 1;
        var furthest = -1;
        var repeatJustFinished = false;
        var visitLimit = (long)n * 101 + 1024;
        long visits = 0;

        var bar = 0;
        while (bar < n && visits++ < visitLimit)
        {
            var firstArrival = bar > furthest;
            if (firstArrival) furthest = bar;

            var plays = PlaysOnPass(score, bar, pass, repeatJustFinished, returnPointIsOpenBar && returnPoint == bar);
            if (plays)
            {
                order.Add(bar);
                if (score.Open(bar))
                {
                    if (firstArrival)
                    {
                        pass = 1;
                        Array.Clear(jumpsTaken);
                    }
                    returnPoint = bar;
                    returnPointIsOpenBar = true;
                    repeatJustFinished = false;
                }
            }

            if (score.Close(bar))
            {
                // A close that carries an ending follows the ending, not the count (TuxGuitar, black_widow_2.gp3): it sends playback
                // back whenever it plays, and once the passes of its ending are used up it lets playback through to the ending that follows.
                var countLeft = jumpsTaken[bar] < score.Passes(bar) - 1;
                var sendsBack = score.Ending(bar) == 0 ? countLeft : (plays || (pass < HighestPass(score.Ending(bar)) && countLeft)) && pass < score.TotalPasses(bar);
                if (sendsBack)
                {
                    jumpsTaken[bar]++;
                    pass++;
                    repeatJustFinished = false;
                    bar = returnPoint;
                    continue;
                }

                var handsOver = score.Ending(bar) != 0 && score.CloseFollowsBeforeNextOpen(bar);
                if (!handsOver)
                {
                    Array.Clear(jumpsTaken);
                    pass = 1;
                    returnPoint = bar + 1;
                    returnPointIsOpenBar = false;
                    repeatJustFinished = true;
                }
            }
            bar++;
        }
        return order;
    }

    private static int HighestPass(int mask)
    {
        var highest = 0;
        for (var pass = 1; pass <= EndingPassCount; pass++)
            if ((mask & (1 << (pass - 1))) != 0) highest = pass;
        return highest;
    }

    private static bool PlaysOnPass(BarStructure score, int bar, int pass, bool repeatJustFinished, bool insideOwnRepeat)
    {
        var ending = score.Ending(bar);
        if (ending == 0) return true;
        if (insideOwnRepeat && score.LeadingEnding(bar)) return true;
        if (repeatJustFinished) return true;
        if (pass > EndingPassCount) return false;
        return (ending & (1 << (pass - 1))) != 0;
    }

    // ------------------------------------------------------------------------------------------------
    // Stage 3: start bar
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The suffix of the performed order from the start position: the first performance of the start bar, or, when
    /// that bar is never performed, the first performance of a later written bar. When neither exists, the start bar
    /// alone is played if it is inside the song. A start occurrence above 1 instead begins at that performance of the
    /// start bar (the pass-aware seek); 1 or null uses the written-order start.
    /// </summary>
    private static List<int> ApplyStartBar(List<int> performed, int startBar, int barCount, int? startOccurrence)
    {
        var index = StartIndex(performed, startBar, startOccurrence);
        if (index < performed.Count) return performed.GetRange(index, performed.Count - index);
        return startBar > 0 && startBar < barCount ? new List<int> { startBar } : new List<int>();
    }

    /// <summary>
    /// Where the start order begins in <paramref name="performed"/>: 0 for a start at or before the song start, that
    /// performance for a pass-aware start occurrence above 1, the first performance of the start bar, otherwise the
    /// first performance of a later written bar. <paramref name="performed"/>.Count when none is performed (the start
    /// bar alone is played when it is inside the song).
    /// </summary>
    internal static int StartIndex(IReadOnlyList<int> performed, int startBar, int? startOccurrence)
    {
        var bar = Math.Max(0, startBar);
        if (startOccurrence is > 1)
        {
            var occurrence = startOccurrence.Value;
            var seen = 0;
            for (var i = 0; i < performed.Count; i++)
                if (performed[i] == bar && ++seen == occurrence) return i;
            // No such performance (a pass outside the song): use the written-order start below.
        }
        if (startBar <= 0) return 0;
        for (var i = 0; i < performed.Count; i++)
            if (performed[i] == bar) return i;
        for (var i = 0; i < performed.Count; i++)
            if (performed[i] > bar) return i;
        return performed.Count;
    }

    /// <summary>
    /// The index in the full <paramref name="order"/> where a compiled timeline's bars begin. The timeline's bars are a
    /// contiguous run of the order; its first bars are matched, and among equal matches the one nearest
    /// <paramref name="preferredIndex"/> wins (a repeated passage can appear more than once). -1 when it does not match.
    /// </summary>
    internal static int TimelineStartIndex(IReadOnlyList<int> order, IReadOnlyList<int> timelineBars, int preferredIndex)
    {
        if (timelineBars.Count == 0 || timelineBars.Count > order.Count) return -1;
        var depth = Math.Min(64, timelineBars.Count);
        var lastStart = order.Count - depth;
        var preferred = Math.Clamp(preferredIndex, 0, Math.Max(0, lastStart));
        if (MatchesAt(order, timelineBars, preferred, depth)) return preferred;
        var best = -1;
        for (var start = 0; start <= lastStart; start++)
        {
            if (!MatchesAt(order, timelineBars, start, depth)) continue;
            if (best < 0 || Math.Abs(start - preferred) < Math.Abs(best - preferred)) best = start;
        }
        return best;
    }

    private static bool MatchesAt(IReadOnlyList<int> order, IReadOnlyList<int> timelineBars, int start, int depth)
    {
        for (var i = 0; i < depth; i++)
            if (order[start + i] != timelineBars[i]) return false;
        return true;
    }

    // ------------------------------------------------------------------------------------------------
    // Bar structure read from the first track
    // ------------------------------------------------------------------------------------------------

}
