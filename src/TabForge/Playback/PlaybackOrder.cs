using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Turns a score's bar structure into the order in which the bars are performed, following the repeat,
/// alternate-ending and direction rules that Guitar Pro files define.
/// </summary>
/// <remarks>
/// The order is built in three stages:
/// <list type="number">
/// <item>Repeats and alternate endings are expanded, walking the bars from the first one.</item>
/// <item>Directions (D.C., D.S., D.S.S., To Coda, Coda, Fine) are applied to that expanded order.</item>
/// <item>The start bar is applied as a filter.</item>
/// </list>
/// Only the first track's bars carry structure; bars beyond the end of the first track are plain bars.
/// Every stage has a visit limit, so contradictory structures still give a finite order.
/// </remarks>
public static class PlaybackOrder
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
    /// <param name="opt">Uses <see cref="PlaybackOptions.RepeatExpansion"/> and <see cref="PlaybackOptions.StartBar"/>.</param>
    public static List<int> Build(SongProject project, PlaybackOptions opt)
    {
        opt ??= new PlaybackOptions();
        var score = BarStructure.Read(project);
        if (score.BarCount == 0) return new List<int>();

        var expanded = opt.RepeatExpansion ? ExpandRepeats(score) : Enumerable.Range(0, score.BarCount).ToList();
        var performed = ApplyDirections(score, expanded);
        return ApplyStartBar(performed, opt.StartBar, score.BarCount);
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
    /// the close; a close that is not played (its ending does not cover the pass) still counts.</item>
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
                if (jumpsTaken[bar] < score.Passes(bar) - 1)
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
    // Stage 2: directions
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Copies the expanded order until one D.C./D.S. is taken, then plays straight through from its target.
    /// <list type="bullet">
    /// <item>Before the jump, Fine and To Coda are ignored. A jump bar is played in full, then the jump happens;
    /// inside a repeat it is acted on at its last performance, so the repeat plays out first.</item>
    /// <item>D.C. returns to the first performed bar, D.S. to the first segno, D.S.S. to the first double segno
    /// (or the first segno when there is none). A jump without a target is ignored.</item>
    /// <item>After the jump, repeats are not taken again and only the last ending of each repeat is played.
    /// Fine ends playback after its bar. After an "al Coda" jump, the first To Coda bar leads to the nearest
    /// coda after it (or the first one in the song); after an "al Fine" jump, To Coda is ignored.</item>
    /// </list>
    /// </summary>
    private static List<int> ApplyDirections(BarStructure score, List<int> expanded)
    {
        var output = new List<int>(expanded.Count);
        if (expanded.Count == 0) return output;

        var lastPerformance = new Dictionary<int, int>();
        for (var i = 0; i < expanded.Count; i++) lastPerformance[expanded[i]] = i;

        var beforeJumpLimit = Math.Max(256, 64L * expanded.Count);
        for (var i = 0; i < expanded.Count && output.Count < beforeJumpLimit; i++)
        {
            var bar = expanded[i];
            output.Add(bar);
            if (lastPerformance[bar] != i) continue;

            foreach (var jump in score.Jumps(bar))
            {
                var target = FindJumpTarget(score, expanded, jump.Kind);
                if (target < 0) continue;
                PlayAfterJump(score, output, target, jump.AlCoda);
                return output;
            }
        }
        return output;
    }

    private static int FindJumpTarget(BarStructure score, List<int> expanded, JumpKind kind)
    {
        switch (kind)
        {
            case JumpKind.DaCapo:
                return expanded[0];
            case JumpKind.DalSegno:
                return FirstPerformedWith(score, expanded, Mark.Segno);
            case JumpKind.DalSegnoSegno:
                var doubleSegno = FirstPerformedWith(score, expanded, Mark.SegnoSegno);
                return doubleSegno >= 0 ? doubleSegno : FirstPerformedWith(score, expanded, Mark.Segno);
            default:
                return -1;
        }
    }

    private static int FirstPerformedWith(BarStructure score, List<int> expanded, Mark mark)
    {
        foreach (var bar in expanded)
            if (score.Has(bar, mark)) return bar;
        return -1;
    }

    private static void PlayAfterJump(BarStructure score, List<int> output, int target, bool alCoda)
    {
        var n = score.BarCount;
        var limit = 4L * n + 16;
        long played = 0;
        var codaTaken = !alCoda;
        var bar = target;

        // Every step moves forward except the single coda jump, so this always ends.
        while (bar < n && played < limit)
        {
            if (score.DroppedAfterJump(bar)) { bar++; continue; }

            output.Add(bar);
            played++;
            if (score.Has(bar, Mark.Fine)) return;

            if (!codaTaken && score.Has(bar, Mark.ToCoda))
            {
                var coda = score.NearestCoda(bar);
                if (coda >= 0)
                {
                    codaTaken = true;
                    bar = coda;
                    continue;
                }
            }
            bar++;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Stage 3: start bar
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps only performances of bars at or after the start bar. When nothing is left, the start bar alone is
    /// played if it is inside the song.
    /// </summary>
    private static List<int> ApplyStartBar(List<int> performed, int startBar, int barCount)
    {
        if (startBar <= 0) return performed;
        var kept = performed.Where(bar => bar >= startBar).ToList();
        if (kept.Count == 0 && startBar < barCount) kept.Add(startBar);
        return kept;
    }

    // ------------------------------------------------------------------------------------------------
    // Bar structure read from the first track
    // ------------------------------------------------------------------------------------------------

    private enum JumpKind { DaCapo, DalSegno, DalSegnoSegno }

    private readonly record struct Jump(JumpKind Kind, bool AlCoda);

    [Flags]
    private enum Mark
    {
        None = 0,
        Segno = 1,
        SegnoSegno = 2,
        Coda = 4,
        Fine = 8,
        ToCoda = 16,
    }

    /// <summary>
    /// The structural facts of every bar: opens, closes and their pass counts, endings (with each ending
    /// bracket's reach), destinations and jumps. Bars past the first track are plain.
    /// </summary>
    private sealed class BarStructure
    {
        private static readonly Jump[] NoJumps = Array.Empty<Jump>();

        private readonly bool[] _open;
        private readonly bool[] _close;
        private readonly int[] _passes;
        private readonly int[] _ownEnding;
        private readonly int[] _ending;
        private readonly int[] _bracketEnd;
        private readonly bool[] _leadingEnding;
        private readonly bool[] _droppedAfterJump;
        private readonly Mark[] _marks;
        private readonly Jump[][] _jumps;

        public int BarCount { get; }

        private BarStructure(int barCount)
        {
            BarCount = barCount;
            _open = new bool[barCount];
            _close = new bool[barCount];
            _passes = new int[barCount];
            _ownEnding = new int[barCount];
            _ending = new int[barCount];
            _bracketEnd = new int[barCount];
            _leadingEnding = new bool[barCount];
            _droppedAfterJump = new bool[barCount];
            _marks = new Mark[barCount];
            _jumps = new Jump[barCount][];
        }

        public static int CountBars(SongProject project) =>
            project?.Tracks is { Count: > 0 } tracks ? tracks.Max(t => t?.Measures?.Count ?? 0) : 0;

        public static BarStructure Read(SongProject project)
        {
            var score = new BarStructure(CountBars(project));
            var measures = project?.Tracks is { Count: > 0 } tracks ? tracks[0]?.Measures : null;
            var structured = Math.Min(measures?.Count ?? 0, score.BarCount);

            for (var bar = 0; bar < score.BarCount; bar++)
            {
                score._bracketEnd[bar] = bar;
                score._jumps[bar] = NoJumps;
                if (bar >= structured || measures![bar] is not { } m) continue;

                score._open[bar] = m.RepeatStart;
                score._close[bar] = m.RepeatEnd;
                score._passes[bar] = Math.Clamp(m.RepeatCount, 2, MaxRepeats);
                score._ownEnding[bar] = m.EndingPasses & 0xFF;
                ReadDirections(score, bar, m.Directions);
            }

            score.MarkLeadingEndings();
            score.SpanEndingBrackets();
            score.MarkEndingsDroppedAfterJump();
            return score;
        }

        private static void ReadDirections(BarStructure score, int bar, string? directions)
        {
            List<Jump>? jumps = null;
            foreach (var name in DirectionTokens(directions))
            {
                if (Is(name, "Segno")) score._marks[bar] |= Mark.Segno;
                else if (Is(name, "SegnoSegno")) score._marks[bar] |= Mark.SegnoSegno;
                else if (Is(name, "Coda") || Is(name, "DoubleCoda")) score._marks[bar] |= Mark.Coda;
                else if (Is(name, "Fine")) score._marks[bar] |= Mark.Fine;
                else if (Is(name, "ToCoda") || Is(name, "ToDoubleCoda")) score._marks[bar] |= Mark.ToCoda;
                else if (Is(name, "DaCapo")) (jumps ??= new()).Add(new Jump(JumpKind.DaCapo, false));
                else if (Is(name, "DaCapoAlCoda")) (jumps ??= new()).Add(new Jump(JumpKind.DaCapo, true));
                else if (Is(name, "DalSegno")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegno, false));
                else if (Is(name, "DalSegnoAlCoda")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegno, true));
                else if (Is(name, "DalSegnoSegno")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegnoSegno, false));
                else if (Is(name, "DalSegnoSegnoAlCoda")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegnoSegno, true));
            }
            if (jumps is not null) score._jumps[bar] = jumps.ToArray();
        }

        private static bool Is(string name, string expected) => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// An ending bar that also opens a repeat, placed on the first bar or directly after a close, is the
        /// ending of the repeat before it.
        /// </summary>
        private void MarkLeadingEndings()
        {
            for (var bar = 0; bar < BarCount; bar++)
                _leadingEnding[bar] = _ownEnding[bar] != 0 && _open[bar] && (bar == 0 || _close[bar - 1]);
        }

        /// <summary>
        /// From an ending bar, looking forward: when a close comes before any other ending bar or open bar, the
        /// bracket covers every bar through that close, and the unmarked bars inside take its passes. Otherwise
        /// the bracket is the marked bar alone. A leading ending never reaches into its own repeat.
        /// </summary>
        private void SpanEndingBrackets()
        {
            for (var bar = 0; bar < BarCount; bar++)
            {
                if (_ownEnding[bar] == 0) continue;
                _ending[bar] = _ownEnding[bar];
                if (_leadingEnding[bar] || _close[bar]) continue;

                for (var next = bar + 1; next < BarCount; next++)
                {
                    if (_open[next] || _ownEnding[next] != 0) break;
                    if (!_close[next]) continue;
                    for (var inside = bar + 1; inside <= next; inside++) _ending[inside] = _ownEnding[bar];
                    _bracketEnd[bar] = next;
                    break;
                }
            }
        }

        /// <summary>
        /// After a D.C./D.S., only a repeat's last ending plays: an ending bracket is left out when a later
        /// ending of the same repeat (the endings that follow it directly) names a higher pass, even when that
        /// later ending's bar also opens the next repeat.
        /// </summary>
        private void MarkEndingsDroppedAfterJump()
        {
            for (var bar = 0; bar < BarCount; bar++)
            {
                if (_ownEnding[bar] == 0) continue;
                var highest = HighestPass(_ownEnding[bar]);
                var dropped = false;
                for (var next = _bracketEnd[bar] + 1; next < BarCount && _ownEnding[next] != 0; next = _bracketEnd[next] + 1)
                {
                    if (HighestPass(_ownEnding[next]) > highest) { dropped = true; break; }
                    if (_open[next]) break;
                }
                if (!dropped) continue;
                for (var inside = bar; inside <= _bracketEnd[bar]; inside++) _droppedAfterJump[inside] = true;
            }
        }

        private static int HighestPass(int mask)
        {
            var highest = 0;
            for (var pass = 1; pass <= EndingPassCount; pass++)
                if ((mask & (1 << (pass - 1))) != 0) highest = pass;
            return highest;
        }

        public bool Open(int bar) => _open[bar];
        public bool Close(int bar) => _close[bar];
        public int Passes(int bar) => _passes[bar];
        /// <summary>Passes the bar plays on (its own ending or its bracket's), bit n = pass n+1; 0 = every pass.</summary>
        public int Ending(int bar) => _ending[bar];
        public bool LeadingEnding(int bar) => _leadingEnding[bar];
        public bool DroppedAfterJump(int bar) => _droppedAfterJump[bar];
        public bool Has(int bar, Mark mark) => (_marks[bar] & mark) != 0;
        public IReadOnlyList<Jump> Jumps(int bar) => _jumps[bar];

        /// <summary>True when another close follows this one before the next open bar.</summary>
        public bool CloseFollowsBeforeNextOpen(int bar)
        {
            for (var next = bar + 1; next < BarCount; next++)
            {
                if (_open[next]) return false;
                if (_close[next]) return true;
            }
            return false;
        }

        /// <summary>The first coda (or double coda) after the bar; when there is none, the first one in the song; -1 when there is none at all.</summary>
        public int NearestCoda(int bar)
        {
            for (var next = bar + 1; next < BarCount; next++)
                if (Has(next, Mark.Coda)) return next;
            for (var from = 0; from <= bar && from < BarCount; from++)
                if (Has(from, Mark.Coda)) return from;
            return -1;
        }
    }
}
