using TabForge.Models;

namespace TabForge.Services;

// Owns: "fill incomplete bars with rests": the gaps of a plain bar become rests, a deleted beat becomes a rest of the same length,
//     an inserted beat consumes following rests and never drops a note.
// Does not own: when it runs (the editor's edit pipeline, only for bars an edit touched; opening a file never calls it) or the setting.
// Tests: TestBarFill.
public static class BarFill
{
    // Rest sizes in 16th slots, largest first, with the slot alignment each size sits on (a dotted rest sits on its plain value's grid).
    private static readonly (int Size, int Denominator, int Dots, int Align)[] Sizes =
    {
        (16, 1, 0, 16), (12, 2, 1, 8), (8, 2, 0, 8), (6, 4, 1, 4), (4, 4, 0, 4), (3, 8, 1, 2), (2, 8, 0, 2), (1, 16, 0, 1)
    };

    /// <summary>An edit that, when it changed something, then fills the bars of <paramref name="range"/> (measure, cell, measure, cell) of one track.</summary>
    public static Func<bool> Wrap(SongProject project, int trackIndex, (int m1, int c1, int m2, int c2) range, Func<bool> edit)
        => () =>
        {
            var changed = edit();
            if (changed) FillBars(project, trackIndex, Math.Min(range.m1, range.m2), Math.Max(range.m1, range.m2));
            return changed;
        };

    /// <summary>Fills the gaps of bars <paramref name="firstBar"/>..<paramref name="lastBar"/> of one track. Returns whether anything changed.</summary>
    public static bool FillBars(SongProject project, int trackIndex, int firstBar, int lastBar)
    {
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count) return false;
        var track = project.Tracks[trackIndex];
        var changed = false;
        for (var bar = Math.Max(0, firstBar); bar <= lastBar && bar < track.Measures.Count; bar++)
        {
            var measure = track.Measures[bar];
            if (measure.FreeTime || measure.Anacrusis || measure.SimileOneBar || measure.SimileTwoBar) continue;
            var slots = MusicTime.BarSlots(project, bar);
            changed |= FillCells(measure.Cells, slots, always: true);
            if (measure.Voice2Cells.Count > 0) changed |= FillCells(measure.Voice2Cells, slots, always: false);
        }
        return changed;
    }

    /// <summary>Whether a note of the given value fits inside <paramref name="rest"/> (so the typed note shortens the rest instead of keeping its length).</summary>
    public static bool RestTakes(TabCell rest, int denominator, int dots)
    {
        if (rest.Tuplet.Item1 > 0 || rest.RhythmicPosition is not null) return false;
        var probe = new TabCell { DurationDenominator = denominator, Dots = dots };
        return MusicTime.CellSlotsRounded(probe) <= MusicTime.CellSlotsRounded(rest);
    }

    private static bool IsBeat(TabCell cell) => cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation;

    /// <summary>A bar the fill does not touch: free-timed positions or tuplets make its gaps ambiguous.</summary>
    public static bool IsPlain(IReadOnlyList<TabCell> cells)
        => cells.All(cell => cell.RhythmicPosition is null && cell.Tuplet.Item1 <= 0);

    /// <summary>Puts rests into every gap of one voice. <paramref name="always"/>: an empty voice becomes one whole-bar rest (voice 1); otherwise an empty voice stays empty.</summary>
    public static bool FillCells(List<TabCell> cells, int slots, bool always)
    {
        if (!IsPlain(cells)) return false;
        if (!always && !cells.Any(IsBeat)) return false;
        var changed = false;
        while (cells.Count < slots) { cells.Add(new TabCell()); changed = true; }
        var consumed = 0;
        for (var i = 0; i < slots; i++)
        {
            var cell = cells[i];
            if (IsBeat(cell))
            {
                consumed = Math.Max(consumed, i) + MusicTime.CellSlotsRounded(cell);
                continue;
            }
            if (i < consumed) continue;                       // inside the span of an earlier beat
            var end = i + 1;
            while (end < slots && !IsBeat(cells[end])) end++;   // the gap runs to the next beat
            PlaceRests(cells, i, end);
            changed = true;
            consumed = end;
            i = end - 1;
        }
        return changed;
    }

    /// <summary>The fewest standard rests over slots <paramref name="from"/>..<paramref name="to"/>, each on its natural beat boundary.</summary>
    private static void PlaceRests(List<TabCell> cells, int from, int to)
    {
        for (var at = from; at < to;)
        {
            var size = Sizes.First(s => s.Size <= to - at && at % s.Align == 0 && (s.Dots == 0 || s.Size == to - at));
            var rest = cells[at];
            rest.IsRest = true; rest.DurationDenominator = size.Denominator; rest.Dots = size.Dots;
            rest.IsTriplet = false; rest.TupletNumerator = 0; rest.TupletDenominator = 0;
            at += size.Size;
        }
    }

    /// <summary>A voice with no notes and no beat text becomes a clean grid of <paramref name="slots"/> beats: one whole-bar rest (<paramref name="wholeRest"/>) or empty. Tuplets, off-grid positions and extra beats go too. Returns the result's first and last cell; null when the voice still holds notes.</summary>
    public static (int First, int Last)? ResetEmptyVoice(List<TabCell> cells, int slots, bool wholeRest)
    {
        if (slots <= 0 || cells.Any(cell => cell.Notes.Count > 0 || cell.HasAnnotation)) return null;
        cells.Clear();
        for (var i = 0; i < slots; i++) cells.Add(new TabCell());
        if (wholeRest) FillCells(cells, slots, always: true);
        return (0, wholeRest ? 0 : slots - 1);
    }

    /// <summary>
    /// Re-normalises the rests an edit touched: every run of free time (not covered by a note) that holds a touched cell, or lies right before or
    /// after one, becomes the fewest standard rests, so the bar is exactly full. Notes are never moved or dropped. Returns the first and last cell
    /// of the runs it rewrote (null: nothing to do, e.g. a bar with tuplets or free-position beats). <paramref name="wholeRuns"/> false: only the span the touched
    /// cells cover is rewritten, the rest of each run stays as it is.
    /// </summary>
    public static (int First, int Last)? MergeRestRuns(List<TabCell> cells, int slots, IReadOnlySet<TabCell> touched, bool wholeRuns = true)
    {
        if (!IsPlain(cells)) return null;
        while (cells.Count < slots) cells.Add(new TabCell());
        var covered = new bool[slots];
        var probes = new List<int>();
        var consumed = 0;
        for (var i = 0; i < slots; i++)
        {
            var cell = cells[i];
            if (!IsBeat(cell)) continue;
            var start = Math.Max(consumed, i);
            var span = MusicTime.CellSlotsRounded(cell);
            if (cell.Notes.Count > 0 || cell.HasAnnotation)   // only notes hold time; rests are re-laid, so an edited rest never pushes a note
            {
                consumed = start + span;
                for (var s = start; s < Math.Min(slots, start + span); s++) covered[s] = true;
            }
            if (touched.Contains(cell)) { probes.Add(i); probes.Add(start - 1); probes.Add(start + span); }
        }
        for (var i = 0; i < slots; i++)
        {
            if (touched.Contains(cells[i])) probes.Add(i);
            if (covered[i] && cells[i].IsRest && cells[i].Notes.Count == 0 && !cells[i].HasAnnotation) cells[i].IsRest = false;   // a rest hidden under a note
        }
        int lo = slots, hi = 0;
        for (var i = 0; i < slots; i++)
            if (touched.Contains(cells[i])) { lo = Math.Min(lo, i); hi = Math.Max(hi, i + (IsBeat(cells[i]) ? MusicTime.CellSlotsRounded(cells[i]) : 1)); }
        int? first = null, last = null;
        for (var a = 0; a < slots;)
        {
            if (covered[a]) { a++; continue; }
            var b = a;
            while (b < slots && !covered[b]) b++;
            var from = wholeRuns ? a : Math.Max(a, lo);
            var to = wholeRuns ? b : Math.Min(b, hi);
            if (from < to && (!wholeRuns || probes.Any(p => p >= a && p < b)))
            {
                for (var i = from; i < to; i++) { cells[i].IsRest = false; cells[i].Notes.Clear(); }
                PlaceRests(cells, from, to);
                first = Math.Min(first ?? from, from); last = Math.Max(last ?? from, to - 1);
            }
            a = b;
        }
        return first is null ? null : (first.Value, last!.Value);
    }

    /// <summary>
    /// A duration change on a rest-only selection covering cells <paramref name="a"/>..<paramref name="b"/> of one bar: the time span from the first
    /// selected rest to the end of the last one is refilled with rests of <paramref name="denominator"/>; a remainder that is not a multiple is completed
    /// with the fewest rests. Returns the first and last cell of the new rests; null when this does not apply (no rest, tuplets, a value over the
    /// 16th grid, or a value longer than the span).
    /// </summary>
    public static (int First, int Last)? RefillRestSpan(List<TabCell> cells, int slots, int a, int b, int denominator)
    {
        if (denominator > 16 || !IsPlain(cells)) return null;
        while (cells.Count < slots) cells.Add(new TabCell());
        a = Math.Max(0, a); b = Math.Min(b, slots - 1);
        int from = -1, lastRest = -1;
        for (var i = a; i <= b; i++)
        {
            if (cells[i].Notes.Count > 0 || cells[i].HasAnnotation) return null;
            if (!cells[i].IsRest) continue;
            if (from < 0) from = i;
            lastRest = i;
        }
        if (from < 0) return null;
        var end = Math.Min(slots, Math.Max(b + 1, lastRest + MusicTime.CellSlotsRounded(cells[lastRest])));
        var size = MusicTime.CellSlotsRounded(new TabCell { DurationDenominator = denominator });
        if (size < 1 || size > end - from) return null;
        for (var i = from; i < end; i++) { cells[i].IsRest = false; cells[i].Notes.Clear(); }
        var pos = from; var last = from;
        for (; pos + size <= end; pos += size)
        {
            var rest = cells[pos];
            rest.IsRest = true; rest.DurationDenominator = denominator; rest.Dots = 0;
            rest.IsTriplet = false; rest.TupletNumerator = 0; rest.TupletDenominator = 0;
            last = pos;
        }
        if (pos < end) { PlaceRests(cells, pos, end); last = pos; for (var i = pos; i < end; i++) if (cells[i].IsRest) last = i; }
        return (from, last);
    }

    /// <summary>
    /// A duration change on rests: the touched rests keep the value they were given and are laid one after another from the first of them; only the
    /// time that frees up (shrinking) or the rests that are overrun (growing, until a note or the bar end) is refilled with the fewest rests. Returns the
    /// first and last cell of the resized rests; null when this does not apply (a note is touched, or the bar has tuplets or free positions).
    /// </summary>
    public static (int First, int Last)? ResizeRests(List<TabCell> cells, int slots, IReadOnlySet<TabCell> touched)
    {
        if (!IsPlain(cells)) return null;
        while (cells.Count < slots) cells.Add(new TabCell());
        var at = new List<int>();
        for (var i = 0; i < slots; i++)
        {
            if (!touched.Contains(cells[i])) continue;
            if (cells[i].Notes.Count > 0 || cells[i].HasAnnotation) return null;
            if (cells[i].IsRest) at.Add(i);
        }
        if (at.Count == 0) return null;
        var lo = at[0];
        var values = at.Select(i => (cells[i].DurationDenominator, cells[i].Dots)).ToList();
        var sizes = at.Select(i => MusicTime.CellSlotsRounded(cells[i])).ToList();
        int NextBeat(int after) { for (var i = after + 1; i < slots; i++) if (IsBeat(cells[i])) return i; return slots; }
        var end = NextBeat(at[^1]);
        var total = sizes.Sum();
        while (lo + total > end && end < slots && cells[end].IsRest && cells[end].Notes.Count == 0 && !cells[end].HasAnnotation)
            end = NextBeat(end);                                   // growing overruns the following rests
        for (var i = lo; i < end; i++) cells[i].IsRest = false;
        var pos = lo; var last = lo;
        for (var k = 0; k < sizes.Count && pos + sizes[k] <= end; k++)
        {
            var rest = cells[pos];
            rest.IsRest = true; rest.DurationDenominator = values[k].DurationDenominator; rest.Dots = values[k].Dots;
            rest.IsTriplet = false; rest.TupletNumerator = 0; rest.TupletDenominator = 0;
            last = pos; pos += sizes[k];
        }
        if (pos < end) PlaceRests(cells, pos, end);
        return (lo, last);
    }

    /// <summary>
    /// Insert beat with the fill on: <paramref name="beat"/> goes in at <paramref name="at"/> and everything after it moves right by the beat's length.
    /// The room comes from the rests and gaps at the end of the bar; a note is never dropped (a bar that no longer fits stays longer than its
    /// time signature and shows red).
    /// </summary>
    public static void InsertBeat(List<TabCell> cells, int at, int slots, TabCell beat)
    {
        while (cells.Count < slots) cells.Add(new TabCell());
        at = Math.Clamp(at, 0, Math.Max(0, slots - 1));
        var size = MusicTime.CellSlotsRounded(beat);
        beat.IsRest = beat.Notes.Count == 0;
        if (IsPlain(cells))
        {
            cells.Insert(at, beat);
            for (var i = 1; i < size; i++) cells.Insert(at + i, new TabCell());
        }
        else
        {
            for (var i = at; i < cells.Count; i++)
                if (cells[i].RhythmicPosition is { } position) cells[i].RhythmicPosition = position + size;
            cells.Insert(at, beat);
        }
        // Take the room back from the end: rests and gaps past the bar go; a rest that now runs past the end becomes a gap for the refill.
        while (cells.Count > slots && (!IsBeat(cells[^1]) || (cells[^1].IsRest && cells[^1].Notes.Count == 0))) cells.RemoveAt(cells.Count - 1);
        for (var i = 0; i < Math.Min(cells.Count, slots); i++)
            if (cells[i].IsRest && cells[i].Notes.Count == 0 && cells[i].RhythmicPosition is null && i + MusicTime.CellSlotsRounded(cells[i]) > slots && !ReferenceEquals(cells[i], beat))
                cells[i].IsRest = false;
    }
}
