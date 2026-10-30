using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: writing a piano-roll edit into a tab track touching only the edited notes. Removed notes leave their beats (a beat left with no note
//       becomes a rest of the same length, so nothing after it moves); each added note goes into a beat that starts and lasts exactly as it does,
//       or the beats it overlaps are split at its start and end (plain and dotted values down to a 32nd, tied on, each beat's own marks staying on
//       its first piece) and the bar's beats are laid out again with every untouched beat at its old place. A moved note takes its own marks along
//       (bends, slides, palm mute and the rest) when its string and fret allow. Tuplet beats are never split: a note edge inside one moves to its edge.
// Does not own: reading the tab (PianoRollTabSource), the string rule (MidiClipToTab.StringNote) or the undo step (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollTabWriterTests.
internal static class PianoRollTabWriter
{
    private const double Epsilon = 1e-6;
    private const double Grain = 0.5;   // the finest written step: a 32nd note, in sixteenth slots

    // Plain and dotted values in sixteenth slots, longest first.
    private static readonly IReadOnlyList<(double Slots, int Denominator, int Dots)> Values = new (double, int, int)[]
        { (16, 1, 0), (12, 2, 1), (8, 2, 0), (6, 4, 1), (4, 4, 0), (3, 8, 1), (2, 8, 0), (1.5, 16, 1), (1, 16, 0), (0.5, 32, 0) };

    // A beat as it stood before the edit changed it: its place and length are kept, so a beat shortened as the first piece of a split still
    // owns the rest of its old span.
    private sealed class Beat
    {
        public Beat(double onset, TabCell cell)
        {
            Onset = onset;
            Cell = cell;
            End = onset + MusicTime.HeldSlots(cell);
            Timed = MusicTime.ConsumesTime(cell);
            Held = cell.Notes.ToList();
        }

        /// <summary>The notes the beat held before the edit (the pieces of a split tie these on, not the added note).</summary>
        public List<TabNote> Held { get; }

        public double Onset { get; }
        public TabCell Cell { get; }
        public double End { get; }
        public bool Timed { get; }
    }

    private static (long, long, int, int, bool, int) Key(PianoRollNote n) =>
        ((long)Math.Round(n.Start * 960), (long)Math.Round(n.Length * 960), n.Pitch, n.Velocity, n.Muted, n.Voice);

    /// <summary>Applies the difference between the written notes and <paramref name="after"/>; false when there is none.</summary>
    public static bool Apply(SongProject song, TrackModel track, PianoRollTimeMap map, List<PianoRollTabSource.Entry> entries, IReadOnlyList<PianoRollNote> after)
    {
        var left = after.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var removed = new List<PianoRollTabSource.Entry>();
        foreach (var entry in entries)
            if (left.TryGetValue(Key(entry.Note), out var count) && count > 0) left[Key(entry.Note)] = count - 1;
            else removed.Add(entry);
        var kept = entries.Select(e => e.Note).GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var added = new List<PianoRollNote>();
        foreach (var note in after)
            if (kept.TryGetValue(Key(note), out var count) && count > 0) kept[Key(note)] = count - 1;
            else added.Add(note);
        if (removed.Count == 0 && added.Count == 0) return false;
        var carried = Pair(removed, added);
        var marks = SnapshotBeats(removed);
        foreach (var entry in removed) Remove(entry);
        ClearOrigins(removed);
        for (var i = 0; i < added.Count; i++)
        {
            carried.TryGetValue(i, out var from);
            var (tremolo, whammy) = from is not null && marks.TryGetValue(from, out var mark)
                ? (mark.Tremolo, mark.Whammy)
                : (0, null);
            Insert(song, track, map, added[i], from, tremolo, whammy);
        }
        return true;
    }

    /// <summary>Which removed note each added note came from (the nearest in time, then pitch), so a moved note keeps its marks.</summary>
    private static Dictionary<int, TabNote> Pair(List<PianoRollTabSource.Entry> removed, List<PianoRollNote> added)
    {
        var pairs = new Dictionary<int, TabNote>();
        var free = removed.ToList();
        foreach (var i in Enumerable.Range(0, added.Count).OrderBy(i => added[i].Start))
        {
            if (free.Count == 0) break;
            var best = free.OrderBy(e => Math.Abs(e.Note.Start - added[i].Start) + Math.Abs(e.Note.Length - added[i].Length) / 2 + Math.Abs(e.Note.Pitch - added[i].Pitch) / 12.0).First();
            free.Remove(best);
            pairs[i] = best.Parts[0].Note;
        }
        return pairs;
    }

    /// <summary>Takes a note (and the tied notes continuing it) out of its beats; a beat left empty becomes a rest of the same length.</summary>
    private static void Remove(PianoRollTabSource.Entry entry)
    {
        foreach (var (cell, note) in entry.Parts)
        {
            cell.Notes.Remove(note);
            if (cell.Notes.Count == 0) cell.IsRest = true;
        }
    }

    private static bool NeedsTremolo(TabNote note) => note.Techniques.Contains("TremoloPick");

    private static bool NeedsWhammy(TabNote note) =>
        note.Techniques.Any(t => t.Contains("TremBar", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<TabNote, (int Tremolo, List<BendPointModel> Whammy)> SnapshotBeats(List<PianoRollTabSource.Entry> removed)
    {
        var marks = new Dictionary<TabNote, (int, List<BendPointModel>)>();
        foreach (var entry in removed)
        {
            if (entry.Parts.Count == 0) continue;
            var (cell, note) = entry.Parts[0];
            if (cell.TremoloPickDenominator == 0 && cell.WhammyPoints.Count == 0) continue;
            marks.TryAdd(note, (cell.TremoloPickDenominator, cell.WhammyPoints.Select(p => p.Clone()).ToList()));
        }
        return marks;
    }

    private static void ClearOrigins(List<PianoRollTabSource.Entry> removed)
    {
        foreach (var cell in removed.Where(e => e.Parts.Count > 0).Select(e => e.Parts[0].Cell).Distinct())
        {
            if (!cell.Notes.Any(NeedsTremolo)) cell.TremoloPickDenominator = 0;
            if (!cell.Notes.Any(NeedsWhammy)) cell.WhammyPoints.Clear();
        }
    }

    private static void CarryBeats(TabCell cell, int tremolo, List<BendPointModel>? whammy)
    {
        if (tremolo != 0 && cell.TremoloPickDenominator == 0) cell.TremoloPickDenominator = tremolo;
        if (whammy is { Count: > 0 } && cell.WhammyPoints.Count == 0)
            cell.WhammyPoints = whammy.Select(p => p.Clone()).ToList();
    }

    /// <summary>Writes one added note: a piece in each bar it covers, the first carrying the note (and its marks), the others tied on.</summary>
    private static void Insert(SongProject song, TrackModel track, PianoRollTimeMap map, PianoRollNote note, TabNote? carried, int carriedTremolo, List<BendPointModel>? carriedWhammy)
    {
        var first = map.IndexAtBeat(note.Start);
        var last = map.IndexAtBeat(Math.Max(note.Start, note.End - Epsilon));
        if (first < 0) return;
        TabNote? head = null;
        for (var index = first; index <= last; index++)
        {
            var bar = map.Bars[index];
            if (bar.Bar < 0 || bar.Bar >= track.Measures.Count) continue;
            var from = (Math.Max(note.Start, bar.StartBeat) - bar.StartBeat) * SnapCore.SlotsPerBeat;
            var to = (Math.Min(note.End, bar.EndBeat) - bar.StartBeat) * SnapCore.SlotsPerBeat;
            head = InsertInBar(song, track, bar.Bar, note, from, to, index > first, carried, head, carriedTremolo, carriedWhammy);
        }
    }

    /// <summary>One bar's piece of an added note; returns the written head (the note later pieces tie on from).</summary>
    private static TabNote? InsertInBar(SongProject song, TrackModel track, int bar, PianoRollNote note, double from, double to, bool tiedIn, TabNote? carried, TabNote? head, int carriedTremolo, List<BendPointModel>? carriedWhammy)
    {
        var measure = track.Measures[bar];
        var slots = Math.Max(1, MusicTime.BarSlots(song, bar));
        var cells = measure.CellsForVoice(note.Voice, create: true);
        var s = Math.Clamp(Math.Round(from / Grain) * Grain, 0, slots - Grain);
        var e = Math.Clamp(Math.Round(to / Grain) * Grain, s + Grain, slots);
        var beats = MusicTime.BeatOnsets(cells).Select(b => new Beat(b.Start, b.Cell)).ToList();
        var timed = beats.Where(b => b.Timed).ToList();
        foreach (var b in timed.Where(b => b.Cell.Tuplet.Numerator > 0))
        {
            if (s > b.Onset + Epsilon && s < b.End - Epsilon) s = b.Onset;
            if (e > b.Onset + Epsilon && e < b.End - Epsilon) e = b.End;
        }
        TabNote Piece(TabCell cell, bool atStart)
        {
            if (atStart && !tiedIn) return head = Make(track, cell, note, carried, false);
            return Continue(track, cell, head, note);
        }
        var exact = timed.FirstOrDefault(b => Math.Abs(b.Onset - s) < Epsilon && Math.Abs(b.End - e) < Epsilon);
        if (exact is not null)
        {
            var isAttack = !tiedIn;
            Add(exact.Cell, Piece(exact.Cell, true));
            if (isAttack) CarryBeats(exact.Cell, carriedTremolo, carriedWhammy);
            return head;
        }
        var overlap = timed.Where(b => b.Onset < e - Epsilon && b.End > s + Epsilon).ToList();
        var ws = overlap.Count == 0 ? s : Math.Min(s, overlap.Min(b => b.Onset));
        var we = overlap.Count == 0 ? e : Math.Max(e, overlap.Max(b => b.End));
        var bounds = new SortedSet<double> { ws, we, s, e };
        foreach (var b in overlap) { bounds.Add(b.Onset); bounds.Add(b.End); }
        var points = bounds.ToList();
        var pieces = new List<Beat>();
        var reused = new HashSet<TabCell>();
        for (var k = 0; k + 1 < points.Count; k++)
        {
            var (a, b) = (points[k], points[k + 1]);
            if (b - a < Epsilon) continue;
            var owner = overlap.FirstOrDefault(o => o.Onset <= a + Epsilon && o.End > a + Epsilon);
            var inNew = a >= s - Epsilon && b <= e + Epsilon;
            if (owner is null && !inNew) continue;
            var lengths = owner is not null && owner.Cell.Tuplet.Numerator > 0 ? new List<(double, int, int)> { (b - a, 0, 0) } : Decompose(b - a);
            var at = a;
            foreach (var (length, denominator, dots) in lengths)
            {
                var cell = PieceCell(track, owner, at, reused, denominator, dots);
                if (inNew)
                {
                    var atStart = Math.Abs(at - s) < Epsilon;
                    Add(cell, Piece(cell, atStart));
                    if (atStart && !tiedIn) CarryBeats(cell, carriedTremolo, carriedWhammy);
                }
                pieces.Add(new Beat(at, cell));
                at += length;
            }
        }
        var layout = beats.Where(b => !overlap.Contains(b)).Concat(pieces).OrderBy(b => b.Onset).ThenBy(b => b.Timed ? 1 : 0).ToList();
        var written = Serialize(layout, slots);
        if (note.Voice <= 0) measure.Cells = written;
        else measure.Voice2Cells = written;
        return head;
    }

    /// <summary>The beat of one piece of a split: the old beat itself for its first piece (its marks stay), else a new beat holding the old notes tied on
    /// (or a rest where the old beat was a rest).</summary>
    private static TabCell PieceCell(TrackModel track, Beat? owner, double at, HashSet<TabCell> reused, int denominator, int dots)
    {
        if (owner is not null && !reused.Contains(owner.Cell) && Math.Abs(at - owner.Onset) < Epsilon)
        {
            reused.Add(owner.Cell);
            SetValue(owner.Cell, denominator, dots);
            return owner.Cell;
        }
        var cell = new TabCell();
        SetValue(cell, denominator, dots);
        if (owner is null) return cell;
        if (owner.Held.Count == 0) cell.IsRest = true;
        foreach (var held in owner.Held) cell.Notes.Add(Continue(track, cell, held, null));
        return cell;
    }

    private static void Add(TabCell cell, TabNote note)
    {
        cell.Notes.Add(note);
        cell.IsRest = false;
    }

    private static void SetValue(TabCell cell, int denominator, int dots)
    {
        if (denominator <= 0) return;
        cell.DurationDenominator = denominator;
        cell.Dots = dots;
        cell.TupletNumerator = 0;
        cell.TupletDenominator = 0;
        cell.IsTriplet = false;
    }

    /// <summary>Plain and dotted values that add up to <paramref name="slots"/> (in 32nds at the finest).</summary>
    private static List<(double Slots, int Denominator, int Dots)> Decompose(double slots)
    {
        var list = new List<(double, int, int)>();
        var remaining = slots;
        while (remaining > Epsilon && list.Count < 64)
        {
            var value = Values.FirstOrDefault(v => v.Slots <= remaining + Epsilon);
            if (value.Slots <= 0) break;
            list.Add(value);
            remaining -= value.Slots;
        }
        return list;
    }

    /// <summary>The written head of an added note in <paramref name="cell"/>: the note it came from (its marks kept) or a new one, on a free string.</summary>
    private static TabNote Make(TrackModel track, TabCell cell, PianoRollNote note, TabNote? carried, bool tied)
    {
        var written = carried?.Clone() ?? new TabNote();
        written.IsGraceNote = false;
        if (PianoRollTabSource.Fingering(track, cell, carried, note.Pitch) is var (s, fret, midi)) (written.StringIndex, written.Fret, written.MidiValue) = (s, fret, midi);
        else written.MidiValue = note.Pitch;   // no free string can play it: it keeps its old string so the note is not lost
        written.Velocity = note.Velocity;
        written.Silent = note.Muted;
        written.Tied = tied;
        return written;
    }

    /// <summary>A tied continuation of <paramref name="held"/> in another beat: same string and pitch, without the marks that belong to the attack.</summary>
    private static TabNote Continue(TrackModel track, TabCell cell, TabNote? held, PianoRollNote? note)
    {
        if (held is null && note is { } fresh) return Make(track, cell, fresh, null, true);
        var copy = held!.Clone();
        copy.Tied = true;
        copy.BendPoints.Clear();
        copy.Techniques.Clear();
        copy.SlideTargetMidi = 0;
        copy.TrillTargetMidi = 0;
        if (cell.Notes.Any(other => other.StringIndex == copy.StringIndex) && PianoRollTabSource.Fingering(track, cell, null, copy.MidiValue) is var (s, fret, midi))
            (copy.StringIndex, copy.Fret, copy.MidiValue) = (s, fret, midi);
        return copy;
    }

    /// <summary>Lays a bar's beats out again so each starts where it should: at its own slot when it can (padded with empty cells), else with a
    /// written position (MusicTime.OnsetSlot reads it).</summary>
    private static List<TabCell> Serialize(List<Beat> beats, int slots)
    {
        var cells = new List<TabCell>();
        var consumed = 0.0;
        foreach (var beat in beats)
        {
            var index = Math.Max((int)Math.Floor(beat.Onset + 1e-9), cells.Count);
            if (index > cells.Count && consumed <= index - 1) consumed = index;
            while (cells.Count < index) cells.Add(new TabCell());
            var natural = Math.Max(index, consumed);
            beat.Cell.RhythmicPosition = Math.Abs(natural - beat.Onset) < 1e-9 ? null : Math.Round(beat.Onset, 6);
            cells.Add(beat.Cell);
            if (beat.Timed) consumed = Math.Max(consumed, beat.Onset + MusicTime.CellSlots(beat.Cell));   // a grace or text-only beat takes no time
        }
        while (cells.Count < slots) cells.Add(new TabCell());
        return cells;
    }
}
