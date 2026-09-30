using System.Linq;
using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Turns the score's bar structure into the linear order of bars that are actually performed.
/// Handles repeat open/close, repeat counts and alternate endings (the reference semantics, adapted
/// from TuxGuitar's MidiRepeatController). The bar order is derived once and reused by the MIDI
/// compiler and by the arrangement's playhead logic.
/// </summary>
public static class PlaybackOrder
{
    /// <summary>Bar indices (source bars) in performance order.</summary>
    public static List<int> Build(SongProject project, PlaybackOptions opt)
    {
        var measureCount = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        var first = project.Tracks.FirstOrDefault();
        var order = new List<int>();
        if (measureCount == 0) return order;

        MeasureModel? Bar(int i) => first is not null && i >= 0 && i < first.Measures.Count ? first.Measures[i] : null;

        // Repeat expansion (the state machine TuxGuitar uses to match GP): a close sends
        // playback back to the most recent open until its passes are used up; an ending bracket stays in force
        // until the close and plays only on its passes; while an ending is in force at a close the repeat
        // keeps going, so one repeat can have several closes with their own endings (1st, 2nd, 3rd...).
        // A close with no open repeats from just after the previous finished repeat (or from bar 1).
        var startIndex = 0;
        var open = true;
        var pass = 0;
        var ending = 0;
        var furthest = -1;
        var lastClose = -1;
        var bracketToClose = false;
        var lastOpen = -1;
        var foreignEnding = new HashSet<int>();
        // Each close counts its own jumps: "x2" sends playback back once, however many passes its ending
        // covers, and a repeat with several closes (1st / 2nd / 3rd ending each with a close) uses each once.
        var closeUses = new Dictionary<int, int>();
        bool CloseFollows(int from)
        {
            for (var k = from + 1; k < measureCount; k++)
            {
                if (Bar(k)?.RepeatStart == true) return false;
                if (Bar(k)?.RepeatEnd == true) return true;
            }
            return false;
        }
        bool JumpsBack(int bar, MeasureModel? close)
        {
            var used = closeUses.GetValueOrDefault(bar);
            if (used >= Passes(close) - 1) return false;
            closeUses[bar] = used + 1;
            return true;
        }
        bool ReachesClose(int from)
        {
            for (var k = from; k < measureCount; k++)
            {
                var bar = Bar(k);
                if (bar?.RepeatEnd == true) return true;
                if (k > from && (bar?.RepeatStart == true || (bar?.EndingPasses ?? 0) != 0)) return false;
            }
            return false;
        }
        var guard = measureCount * (MaxRepeats + 2) + 1024;
        for (var i = 0; i < measureCount && guard-- > 0; i++)
        {
            var measure = Bar(i);
            if (!opt.RepeatExpansion) { order.Add(i); continue; }
            // Endings apply to the open bar too (an ending that starts on the repeat's first bar).
            {
                // An ending on the bar that opens the next repeat belongs to the repeat before it; inside its own
                // repeat that bar plays on every pass.
                var own = foreignEnding.Contains(i) && i == startIndex && open ? 0 : measure?.EndingPasses ?? 0;
                if (own != 0) { ending = own; bracketToClose = ReachesClose(i); }
                // A bracket that no repeat close ends (the last ending) covers only its own bars.
                else if (own == 0 && ending != 0 && !bracketToClose) ending = 0;
                if (open && ending != 0 && (pass >= 8 || (ending & (1 << pass)) == 0))
                {
                    if (measure?.RepeatEnd == true)
                    {
                        ending = 0;
                        // A skipped close still sends playback back while it has passes left.
                        if (JumpsBack(i, measure)) { pass++; i = startIndex - 1; }
                        else if (!CloseFollows(i))
                        {
                            // This close's passes are used up and no other close follows: the repeat is over,
                            // so the next ending (e.g. "3.") plays.
                            pass = 0; open = false; lastClose = i; closeUses.Clear();
                        }
                    }
                    continue; // an ending for other passes
                }
            }
            // A played open bar starts a repeat (checked after the endings: the open bar can carry the previous
            // repeat's last ending, e.g. "2." on the bar that opens the next repeat).
            if (measure?.RepeatStart == true)
            {
                startIndex = i;
                open = true;
                if (i > furthest)
                {
                    if ((measure.EndingPasses) != 0 && lastClose == i - 1) foreignEnding.Add(i);
                    pass = 0; ending = 0; closeUses.Clear();
                }
                lastOpen = i;
            }
            furthest = Math.Max(furthest, i);
            order.Add(i);
            if (measure?.RepeatEnd != true) continue;
            // A close with no open of its own goes back to the most recent open (or bar 1).
            if (!open) { startIndex = Math.Max(0, lastOpen); open = true; pass = 0; closeUses.Clear(); }
            if (JumpsBack(i, measure))
            {
                pass++;
                ending = 0;
                i = startIndex - 1;
                continue;
            }
            pass = 0;
            ending = 0;
            open = false;
            lastClose = i;
            closeUses.Clear();
        }

        order = ApplyNavigationDirections(first, order);
        if (opt.StartBar > 0) order = order.Where(b => b >= opt.StartBar).ToList();
        if (order.Count == 0 && opt.StartBar < measureCount)
            order.Add(Math.Clamp(opt.StartBar, 0, measureCount - 1));
        return order;
    }

    private static List<int> ApplyNavigationDirections(TrackModel? track, IReadOnlyList<int> repeatedOrder)
    {
        if (track is null || repeatedOrder.Count == 0) return repeatedOrder.ToList();
        string[] Tokens(int bar)
        {
            if (bar < 0 || bar >= track.Measures.Count) return Array.Empty<string>();
            return DirectionTokens(track.Measures[bar].Directions);
        }
        bool Has(int bar, params string[] names)
            => Tokens(bar).Any(token => names.Any(name => token.Equals(name, StringComparison.OrdinalIgnoreCase)));

        var output = new List<int>(repeatedOrder.Count);
        var position = 0;
        var jumped = false;
        var codaArmed = false;
        var tookCoda = false;
        var guard = Math.Max(256, repeatedOrder.Count * 64);
        while (position < repeatedOrder.Count && guard-- > 0)
        {
            var bar = repeatedOrder[position];
            output.Add(bar);
            if (jumped && Has(bar, "Fine")) break;

            var direction = Tokens(bar);
            if (jumped && codaArmed && !tookCoda && direction.Any(token => token.Equals("ToCoda", StringComparison.OrdinalIgnoreCase)))
            {
                var coda = FindDirectionTarget(track, repeatedOrder, "DoubleCoda", "Coda");
                if (coda >= 0) { position = coda; tookCoda = true; continue; }
            }

            var dalSegnoSegno = direction.Any(token => token.Equals("DalSegnoSegno", StringComparison.OrdinalIgnoreCase) ||
                                                       token.Equals("DalSegnoSegnoAlCoda", StringComparison.OrdinalIgnoreCase));
            var dalSegno = dalSegnoSegno || direction.Any(token => token.Equals("DalSegno", StringComparison.OrdinalIgnoreCase) ||
                                                  token.Equals("DalSegnoAlCoda", StringComparison.OrdinalIgnoreCase));
            var daCapo = direction.Any(token => token.Equals("DaCapo", StringComparison.OrdinalIgnoreCase) ||
                                                token.Equals("DaCapoAlCoda", StringComparison.OrdinalIgnoreCase));
            if (!jumped && (daCapo || dalSegno))
            {
                // D.S.S. goes to the double segno, falling back to the single one when the file marks none.
                var doubleSegno = dalSegnoSegno ? FindDirectionTarget(track, repeatedOrder, "SegnoSegno") : -1;
                var target = doubleSegno >= 0 ? doubleSegno
                    : dalSegno ? FindDirectionTarget(track, repeatedOrder, "Segno") : FindFirstBar(repeatedOrder);
                if (target >= 0)
                {
                    var alCoda = direction.Any(token => token.Equals("DalSegnoAlCoda", StringComparison.OrdinalIgnoreCase) ||
                                                        token.Equals("DalSegnoSegnoAlCoda", StringComparison.OrdinalIgnoreCase) ||
                                                        token.Equals("DaCapoAlCoda", StringComparison.OrdinalIgnoreCase));
                    // After a D.S. / D.C. playback continues straight through: no repeats, only each group's last ending.
                    PlayAfterJump(track, repeatedOrder[target], alCoda, output, Has);
                    return output;
                }
            }
            position++;
        }
        return output;
    }

    private static void PlayAfterJump(TrackModel track, int fromBar, bool alCoda, List<int> output, Func<int, string[], bool> has)
    {
        var count = track.Measures.Count;
        var skip = EarlierEndingBars(track);
        var tookCoda = false;
        var guard = count * 4 + 16;
        for (var bar = fromBar; bar < count && guard-- > 0; bar++)
        {
            if (skip[bar]) continue;
            output.Add(bar);
            if (has(bar, new[] { "Fine" })) return;
            if (alCoda && !tookCoda && has(bar, new[] { "ToCoda", "ToDoubleCoda" }))
            {
                // The Coda normally follows; an earlier one is used when there is none after this bar.
                var coda = Enumerable.Range(bar + 1, Math.Max(0, count - bar - 1)).Concat(Enumerable.Range(0, bar + 1))
                    .FirstOrDefault(k => has(k, new[] { "Coda", "DoubleCoda" }), -1);
                if (coda >= 0) { tookCoda = true; bar = coda - 1; }
            }
        }
    }

    /// <summary>Bars inside an ending bracket that a later ending of the same repeat replaces (skipped after a jump).</summary>
    private static bool[] EarlierEndingBars(TrackModel track)
    {
        var bars = track.Measures;
        var skip = new bool[bars.Count];
        static int Highest(int passes) { var h = -1; for (var n = 0; n < 8; n++) if ((passes & (1 << n)) != 0) h = n; return h; }
        var inBracket = false; var skipping = false; var closed = false;
        for (var b = 0; b < bars.Count; b++)
        {
            var own = bars[b].EndingPasses;
            if (own != 0)
            {
                var high = Highest(own);
                skipping = false;
                for (var k = b + 1; k < bars.Count && !bars[k].RepeatStart; k++)
                    if (bars[k].EndingPasses != 0 && Highest(bars[k].EndingPasses) > high) { skipping = true; break; }
                inBracket = true; closed = false;
            }
            else if (bars[b].RepeatStart || closed) inBracket = false;
            skip[b] = inBracket && skipping;
            if (bars[b].RepeatEnd) closed = true;
        }
        return skip;
    }

    private static int FindDirectionTarget(TrackModel track, IReadOnlyList<int> order, params string[] names)
    {
        for (var i = 0; i < order.Count; i++)
        {
            var bar = order[i];
            if (bar < 0 || bar >= track.Measures.Count) continue;
            var directions = DirectionTokens(track.Measures[bar].Directions);
            if (names.Any(name => directions.Any(direction => direction.Equals(name, StringComparison.OrdinalIgnoreCase)))) return i;
        }
        return -1;
    }

    /// <summary>
    /// A bar's directions in TabForge's names. Guitar Pro imports keep alphaTab's names ("TargetSegno",
    /// "JumpDalSegnoAlCoda", "JumpDaCoda"...), which playback used to ignore, so no D.S. / D.C. / Coda was followed.
    /// </summary>
    internal static string[] DirectionTokens(string? directions)
    {
        if (string.IsNullOrWhiteSpace(directions)) return Array.Empty<string>();
        return directions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(token => token switch
        {
            "JumpDaCoda" => "ToCoda",
            "JumpDaDoubleCoda" => "ToDoubleCoda",
            "JumpDaCapoAlFine" => "DaCapo",
            "JumpDalSegnoAlFine" => "DalSegno",
            "JumpDaCapoAlDoubleCoda" => "DaCapoAlCoda",
            "JumpDalSegnoAlDoubleCoda" => "DalSegnoAlCoda",
            "JumpDalSegnoSegno" or "JumpDalSegnoSegnoAlFine" => "DalSegnoSegno",
            "JumpDalSegnoSegnoAlCoda" or "JumpDalSegnoSegnoAlDoubleCoda" => "DalSegnoSegnoAlCoda",
            _ when token.StartsWith("Target", StringComparison.Ordinal) && token.Length > 6 => token[6..],
            _ when token.StartsWith("Jump", StringComparison.Ordinal) && token.Length > 4 => token[4..],
            _ => token,
        }).ToArray();
    }

    private static int FindFirstBar(IReadOnlyList<int> order)
    {
        return order.Count == 0 || order[0] < 0 ? -1 : 0;
    }

    private static int FindRepeatEnd(Func<int, MeasureModel?> bar, int start, int count)
    {
        // The open bar can close the repeat itself (a one-bar repeat); it used to be skipped.
        // A later repeat open before the close takes over (a repeat starts from the most recent open), so
        // this open is then an ordinary bar.
        for (var k = start; k < count; k++)
        {
            if (k > start && bar(k)?.RepeatStart == true) return -1;
            if (bar(k)?.RepeatEnd == true) return k;
        }
        return -1;
    }

    private static int Passes(MeasureModel? repeatEnd) => Math.Clamp(repeatEnd?.RepeatCount ?? 2, 2, MaxRepeats);

    /// <summary>A repeat may play up to 99 times (it used to be capped at 8 here).</summary>
    public const int MaxRepeats = 99;

    /// <summary>An alternate ending N sounds on the N-th pass; an unmarked bar sounds on every pass.</summary>
    /// <summary>
    /// One pass through a repeated section. An alternate-ending bracket runs from its bar to the next ending or
    /// the repeat close, so bars inside it without an ending of their own belong to it (the reference semantics).
    /// </summary>
    private static void AddSectionPass(List<int> order, Func<int, MeasureModel?> bar, int start, int end, int pass)
    {
        var bracket = 0;
        for (var b = start; b <= end; b++)
        {
            var own = bar(b)?.EndingPasses ?? 0;
            if (own != 0) bracket = own;
            var passes = own != 0 ? own : bracket;
            if (passes == 0 || pass is >= 0 and < 8 && (passes & (1 << pass)) != 0) order.Add(b);
        }
    }

    /// <summary>The ending passes in force at bar <paramref name="at"/> of a section starting at <paramref name="start"/>.</summary>
    private static MeasureModel? EndingInForce(Func<int, MeasureModel?> bar, int start, int at)
    {
        MeasureModel? bracket = null;
        for (var b = start; b <= at; b++)
            if ((bar(b)?.EndingPasses ?? 0) != 0) bracket = bar(b);
        return (bar(at)?.EndingPasses ?? 0) != 0 ? bar(at) : bracket;
    }

    private static bool PlaysOnPass(MeasureModel? bar, int pass)
    {
        // An ending can cover several passes ("1.2.3."); it used to play on its first only.
        var passes = bar?.EndingPasses ?? 0;
        return passes == 0 || pass is >= 0 and < 8 && (passes & (1 << pass)) != 0;
    }

    /// <summary>The inclusive source-bar range a loop covers, clamped to the score.</summary>
    public static (int startBar, int endBar) LoopRange(SongProject project, PlaybackOptions opt)
    {
        var max = Math.Max(0, (project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count)) - 1);
        var s = Math.Clamp(opt.LoopStartBar, 0, max);
        var e = Math.Clamp(opt.LoopEndBar, 0, max);
        if (s > e) (s, e) = (e, s);
        return (s, e);
    }

    /// <summary>Absolute ms bounds of the loop inside a compiled timeline (unavailable if it is empty).</summary>
    public static (double startMs, double endMs, bool available) LoopBounds(ScoreTimeline timeline, SongProject project, PlaybackOptions opt)
    {
        var (startBar, endBar) = LoopRange(project, opt);
        var startBars = timeline.Bars.Where(b => b.Bar == startBar).ToList();
        var endBars = timeline.Bars.Where(b => b.Bar == endBar).ToList();
        if (startBars.Count == 0 || endBars.Count == 0) return (0, 0, false);
        var first = startBars.MinBy(b => b.StartMs);
        var last = endBars.MaxBy(b => b.EndMs);
        var startCell = Math.Clamp(opt.LoopStartCell, 0, Math.Max(0, first.Slots - 1));
        var endCell = opt.LoopEndCell < 0 ? last.Slots : Math.Clamp(opt.LoopEndCell + 1, 1, Math.Max(1, last.Slots));
        var start = first.StartMs + (first.EndMs - first.StartMs) * startCell / Math.Max(1, first.Slots);
        var end = last.StartMs + (last.EndMs - last.StartMs) * endCell / Math.Max(1, last.Slots);
        return end > start ? (start, end, true) : (0, 0, false);
    }
}
