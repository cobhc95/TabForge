using TabForge.Models;

namespace TabForge.Services;

/// <summary>One beat of a beats clip: its onset in sixteenth slots from the clip start, and the beat itself.
/// ScoreClip "events" ({ offsetSlots, cell }) map onto this one to one.</summary>
public sealed record ClipBeat(double OffsetSlots, TabCell Cell);

/// <summary>A continuous run of beats for one voice (bar lines are not stored). <see cref="LengthSlots"/> runs to the end
/// of the last beat; a longer value leaves rests at the end of the span (ScoreClip "lengthSlots").</summary>
public sealed record BeatRun(IReadOnlyList<ClipBeat> Beats, double LengthSlots);

/// <summary>Whole bars (both voices and bar settings) for one target track (ScoreClip "bars").</summary>
public sealed record TrackBars(int TrackIndex, IReadOnlyList<MeasureModel> Bars);

public enum BeatPasteMode
{
    /// <summary>Overwrite the notes at the cursor over the clip's span.</summary>
    Replace,
    /// <summary>Push the following notes of this voice along by the clip's length.</summary>
    Insert,
    /// <summary>Overwrite the whole bar (every bar of a selection) with the clip.</summary>
    ReplaceBar
}

/// <summary>Outcome of a placement. On refusal (<see cref="Ok"/> false) the project is unchanged.</summary>
public sealed record PlacementResult(bool Ok, string? Error, int FirstBar, int LastBar, int BarsAppended, int TiesAdded,
    int DroppedBeats, int[]? BarMap = null)
{
    public static PlacementResult Refused(string error) => new(false, error, -1, -1, 0, 0, 0);
}

// Owns: the paste placement engine: absolute slot positions, replace and insert placement, bar-line splitting and ties.
// Does not own: clipboard capture (ClipboardService) and the editor's selection.
// Tests: TestPasteCommands.
/// <summary>
/// Copy/paste placement engine (design C2): absolute slot positions, beat placement in Replace/Insert mode with bar-line
/// splitting and ties, tuplet-straddle refusal, bars appended at the end, and the bar-level Overwrite/Insert operations.
/// Pure model code: no UI, no undo (the caller wraps it in one transaction). Onsets and what counts as a beat come from MusicTime
/// (<see cref="MusicTime.IsBeat"/>, <see cref="MusicTime.Onsets"/>): tie-only cells take time, grace-only beats take none.
/// </summary>
public static partial class BarGrid
{
    private const double Eps = 1e-6;
    private const double TickSlack = 0.012;

    // ----- absolute positions -----

    /// <summary>Slots of a bar; bars past the end of the song use the last bar's meter (what appended bars get).</summary>
    public static int SlotsOf(SongProject p, int bar)
    {
        var count = BarRangeEditor.MaxMeasures(p);
        if (count == 0) return MusicTime.BarSlots(p.TimeSignatureNumerator, p.TimeSignatureDenominator);
        return MusicTime.BarSlots(p, Math.Min(bar, count - 1));
    }

    /// <summary>Absolute slot of a bar's first beat (sum of the earlier bars' own lengths).</summary>
    public static double BarStart(SongProject p, int bar)
    {
        var start = 0.0;
        for (var i = 0; i < bar; i++) start += SlotsOf(p, i);
        return start;
    }

    /// <summary>The bar holding an absolute slot, and the slot within it.</summary>
    public static (int Bar, double Slot) Locate(SongProject p, double absolute)
    {
        absolute = Math.Max(0, absolute);
        var bar = 0; var start = 0.0;
        while (true)
        {
            var slots = SlotsOf(p, bar);
            if (absolute < start + slots - Eps || bar > InputLimits.MaxMeasuresPerTrack) return (bar, Snap(absolute - start));
            start += slots; bar++;
        }
    }

    /// <summary>Onset of every cell of a voice grid (<see cref="MusicTime.Onsets"/>; an empty cell sits on its own index).</summary>
    public static double[] Onsets(IReadOnlyList<TabCell> cells)
    {
        var onsets = MusicTime.Onsets(cells);
        for (var i = 0; i < cells.Count; i++)
            if (!MusicTime.IsBeat(cells[i])) onsets[i] = i;
        return onsets;
    }

    /// <summary>The paste anchor for a cursor on (bar, cell) of a voice: bar start + that cell's onset.</summary>
    public static double AnchorAt(SongProject p, int trackIndex, int voice, int bar, int cellIndex)
    {
        var start = BarStart(p, bar);
        if (trackIndex < 0 || trackIndex >= p.Tracks.Count || bar < 0 || bar >= p.Tracks[trackIndex].Measures.Count) return start;
        var cells = p.Tracks[trackIndex].Measures[bar].CellsForVoice(voice);
        if (cellIndex < 0 || cellIndex >= cells.Count) return start + Math.Max(0, cellIndex);
        return start + Onsets(cells)[cellIndex];
    }

    /// <summary>Representable values (whole .. 64th, single/double dots), longest first, whose lengths add up to
    /// <paramref name="slots"/> (a remainder below a 64th, from tuplet lengths, is left out).</summary>
    public static List<(int Denominator, int Dots)> Decompose(double slots)
    {
        var parts = new List<(int, int)>();
        var remaining = slots;
        foreach (var (denominator, dots, length) in Values)
            while (remaining >= length - Eps && parts.Count < 64) { parts.Add((denominator, dots)); remaining -= length; }
        return parts;
    }

    private static readonly IReadOnlyList<(int Denominator, int Dots, double Length)> Values = MusicTime.AllDenominators
        .SelectMany(d => new[] { 0, 1, 2 }.Select(dots => (d, dots, 16.0 / d * (dots == 0 ? 1 : dots == 1 ? 1.5 : 1.75))))
        .OrderByDescending(v => v.Item3).ToArray();

    // ----- beat placement -----

    private sealed class Ev
    {
        public double Start;
        public double Length;
        public TabCell Cell = null!;
        public bool Pasted;
        /// <summary>The event continues a note from before (Insert's split tail): its first piece is tied.</summary>
        public bool TiedHead;
        public int Order;
    }

    /// <summary>
    /// Places a beat run on one voice of a track at an absolute slot (<see cref="AnchorAt"/>). Replace clears the span
    /// [anchor, anchor + length) (a beat ringing into it is shortened); Insert shifts everything from the anchor on by the
    /// run's length. Beats crossing a bar line are split into representable values with ties; a tuplet group that would
    /// cross a bar line refuses the whole paste (nothing changes). Bars are appended on all tracks when the result runs past
    /// the song. The pasted cells are cloned; the run is not modified.
    /// </summary>
    public static PlacementResult PlaceBeats(SongProject p, int trackIndex, int voice, double anchor, BeatRun run, BeatPasteMode mode)
    {
        if (RunProblem(p, trackIndex, voice, anchor, run) is { } problem) return PlacementResult.Refused(problem);
        anchor = Snap(anchor);
        var length = Math.Max(double.IsFinite(run.LengthSlots) ? run.LengthSlots : 0,
            run.Beats.Max(b => b.OffsetSlots + MusicTime.CellSlots(b.Cell)));
        length = Snap(length);

        var track = p.Tracks[trackIndex];
        var total = BarRangeEditor.MaxMeasures(p);
        var firstBar = Locate(p, anchor).Bar;
        var spanEnd = anchor + length;
        // Replace rewrites up to the bar holding the span end (one bar more when it ends on a bar line, for the tie check);
        // Insert rewrites the rest of the track.
        var regionEnd = mode == BeatPasteMode.Replace ? Locate(p, spanEnd).Bar : Math.Max(firstBar, total - 1);
        var events = ReadVoice(p, track, voice, firstBar, Math.Min(regionEnd, track.Measures.Count - 1));
        var before = firstBar > 0 && firstBar - 1 < track.Measures.Count
            ? ReadVoice(p, track, voice, firstBar - 1, firstBar - 1).LastOrDefault() : null;

        var kept = ClearOrShift(p, events, anchor, length, spanEnd, mode, out var straddleRefusal);
        if (straddleRefusal is not null) return PlacementResult.Refused(straddleRefusal);
        var sorted = AddPastedBeats(kept!, events.Count, run, anchor);

        // Ties at the edges: a pasted beat, and the first beat after the pasted span, keep a tie only when the beat just
        // before them ends on their onset with the same note (string and pitch).
        var afterSpan = sorted.FirstOrDefault(e => !e.Pasted && e.Start >= spanEnd - Eps);
        for (var i = 0; i < sorted.Count; i++)
            if (sorted[i].Pasted || ReferenceEquals(sorted[i], afterSpan))
                CheckTies(sorted[i], i > 0 ? sorted[i - 1] : before);

        if (TupletRefusal(p, sorted) is { } tupletRefusal) return PlacementResult.Refused(tupletRefusal);

        var contentEnd = sorted.Count == 0 ? anchor : sorted.Max(e => e.Start + e.Length);
        var lastBar = Math.Max(Math.Min(regionEnd, Math.Max(firstBar, track.Measures.Count - 1)), Locate(p, Math.Max(anchor, contentEnd - 0.001)).Bar);
        lastBar = Math.Max(lastBar, firstBar);
        var appended = Math.Max(0, lastBar + 1 - total);
        if (lastBar + 1 > InputLimits.MaxMeasuresPerTrack) return PlacementResult.Refused("The paste would make the song too long.");

        // Split into per-bar pieces before touching the project.
        var pieces = new Dictionary<int, List<(double Pos, TabCell Cell)>>();
        var ties = 0;
        foreach (var e in sorted) ties += Emit(p, e, pieces);
        foreach (var (bar, list) in pieces)
            if (list.Count > InputLimits.MaxCellsPerMeasure) return PlacementResult.Refused($"Bar {bar + 1} would hold too many beats.");

        WritePieces(p, track, voice, pieces, firstBar, lastBar, appended);
        return new PlacementResult(true, null, firstBar, lastBar, appended, ties, 0);
    }

    /// <summary>Why a beat run cannot be placed (no such track or voice, nothing to paste, a bad position or clip); null when it can.</summary>
    private static string? RunProblem(SongProject p, int trackIndex, int voice, double anchor, BeatRun run)
    {
        if (trackIndex < 0 || trackIndex >= p.Tracks.Count) return "No such track.";
        if (voice is < 0 or > 1) return "No such voice.";
        if (run.Beats.Count == 0) return "Nothing to paste.";
        if (!double.IsFinite(anchor) || anchor < 0) return "Invalid paste position.";
        if (run.Beats.Any(b => b.Cell is null || !double.IsFinite(b.OffsetSlots) || b.OffsetSlots < 0))
            return "Invalid clip.";
        return null;
    }

    /// <summary>The events that stay: Replace drops those inside the span (a beat ringing into it is shortened), Insert shifts those from the anchor on and splits the one that straddles it. Null with a reason when the anchor is inside a tuplet.</summary>
    private static List<Ev>? ClearOrShift(SongProject p, List<Ev> events, double anchor, double length, double spanEnd, BeatPasteMode mode, out string? refusal)
    {
        refusal = null;
    var kept = new List<Ev>();
    foreach (var e in events)
    {
        var straddles = e.Start < anchor - Eps && e.Start + e.Length > anchor + Eps;
        if (straddles && IsTuplet(e.Cell))
            { refusal = $"The cursor is inside a tuplet in bar {Locate(p, e.Start).Bar + 1}."; return null; }
        if (mode == BeatPasteMode.Replace)
        {
            if (e.Start >= anchor - Eps && e.Start < spanEnd - Eps) continue;
            if (straddles) e.Length = anchor - e.Start;
            kept.Add(e);
        }
        else
        {
            if (e.Start >= anchor - Eps) { e.Start = Snap(e.Start + length); kept.Add(e); continue; }
            if (straddles)
            {
                var tailLength = e.Start + e.Length - anchor;
                e.Length = anchor - e.Start;
                if (e.Cell.Notes.Count > 0 || e.Cell.IsRest)
                    kept.Add(new Ev { Start = Snap(anchor + length), Length = tailLength, Cell = e.Cell.Clone(), TiedHead = e.Cell.Notes.Count > 0, Order = e.Order });
            }
            kept.Add(e);
        }
    }
        return kept;
    }

    /// <summary>Adds the pasted beats after the kept events and sorts everything by position.</summary>
    private static List<Ev> AddPastedBeats(List<Ev> kept, int firstOrder, BeatRun run, double anchor)
    {
        var order = firstOrder;
    foreach (var beat in run.Beats.OrderBy(b => b.OffsetSlots))
    {
        var cell = beat.Cell.Clone();
        cell.RhythmicPosition = null;
        kept.Add(new Ev { Start = Snap(anchor + beat.OffsetSlots), Length = MusicTime.CellSlots(cell), Cell = cell, Pasted = true, Order = order++ });
    }
    var sorted = kept.OrderBy(e => e.Start).ThenBy(e => e.Order).ToList();
        return sorted;
    }

    /// <summary>Tuplet groups never split at a bar line: the reason when one would, else null.</summary>
    private static string? TupletRefusal(SongProject p, List<Ev> sorted)
    {
        foreach (var (start, end) in TupletGroups(sorted))
        {
            var (bar, inBar) = Locate(p, start);
            if (inBar + (end - start) > SlotsOf(p, bar) + Eps)
                return $"A tuplet group would cross the bar line into bar {bar + 2}.";
        }
            return null;
    }

    private static void WritePieces(SongProject p, TrackModel track, int voice, Dictionary<int, List<(double Pos, TabCell Cell)>> pieces, int firstBar, int lastBar, int appended)
    {
        AppendBars(p, appended);
        PadTracks(p);
        for (var bar = firstBar; bar <= lastBar; bar++)
        {
            var measure = track.Measures[bar];
            pieces.TryGetValue(bar, out var list);
            list ??= new();
            if (voice == 1 && measure.Voice2Cells.Count == 0 && list.Count == 0) continue;
            var cells = WriteBar(list, SlotsOf(p, bar));
            if (voice == 0) measure.Cells = cells; else measure.Voice2Cells = cells;
        }
    }

    private static List<Ev> ReadVoice(SongProject p, TrackModel track, int voice, int fromBar, int toBar)
    {
        var list = new List<Ev>();
        var barStart = BarStart(p, fromBar);
        for (var bar = fromBar; bar <= toBar && bar < track.Measures.Count; bar++)
        {
            var cells = track.Measures[bar].CellsForVoice(voice);
            var onsets = Onsets(cells);
            for (var i = 0; i < cells.Count; i++)
                if (MusicTime.IsBeat(cells[i]))
                    list.Add(new Ev { Start = Snap(barStart + onsets[i]), Length = MusicTime.CellSlots(cells[i]), Cell = cells[i].Clone(), Order = list.Count });
            barStart += SlotsOf(p, bar);
        }
        return list;
    }

    private static void CheckTies(Ev e, Ev? previous)
    {
        foreach (var note in e.Cell.Notes)
        {
            if (!note.Tied) continue;
            var ok = previous is not null && Math.Abs(previous.Start + previous.Length - e.Start) <= 0.01 &&
                previous.Cell.Notes.Any(n => n.StringIndex == note.StringIndex &&
                    (n.MidiValue > 0 && note.MidiValue > 0 ? n.MidiValue == note.MidiValue : n.Fret == note.Fret));
            if (!ok) note.Tied = false;
        }
    }

    /// <summary>Spans of tuplet groups: contiguous beats with the same ratio, closed when they fill the group's length
    /// (ratio denominator x the first beat's plain value, e.g. 3 triplet eighths = 4 slots).</summary>
    private static IEnumerable<(double Start, double End)> TupletGroups(List<Ev> sorted)
    {
        (int, int) ratio = (0, 0); double start = 0, end = 0, span = 0;
        var open = false;
        foreach (var e in sorted)
        {
            var tuplet = e.Cell.Tuplet;
            var continues = open && tuplet == ratio && Math.Abs(e.Start - end) <= 0.01 && end - start < span - 0.01;
            if (!continues && open) { yield return (start, end); open = false; }
            if (tuplet.Numerator <= 0) continue;
            if (!open)
            {
                open = true; ratio = tuplet; start = e.Start;
                span = tuplet.Denominator * 16.0 / Math.Clamp(e.Cell.DurationDenominator <= 0 ? 16 : e.Cell.DurationDenominator, 1, 64);
            }
            end = e.Start + e.Length;
        }
        if (open) yield return (start, end);
    }

    /// <summary>Adds an event's cell(s) to the bars it covers; returns the tied continuation pieces made.</summary>
    private static int Emit(SongProject p, Ev e, Dictionary<int, List<(double, TabCell)>> pieces)
    {
        var ties = 0;
        var asIs = !e.TiedHead && Math.Abs(e.Length - MusicTime.CellSlots(e.Cell)) < Eps;
        var start = e.Start; var remaining = e.Length; var first = true;
        while (remaining > Eps)
        {
            var (bar, inBar) = Locate(p, start);
            var len = Math.Min(remaining, SlotsOf(p, bar) - inBar);
            if (first && asIs && len >= remaining - Eps) { Add(pieces, bar, inBar, e.Cell); break; }
            var at = inBar;
            foreach (var (denominator, dots) in Decompose(len))
            {
                var head = first && !e.TiedHead;
                var cell = head ? e.Cell : Continuation(e.Cell);
                if (cell is not null)
                {
                    cell.DurationDenominator = denominator; cell.Dots = dots;
                    cell.IsTriplet = false; cell.TupletNumerator = 0; cell.TupletDenominator = 0;
                    Add(pieces, bar, Snap(at), cell);
                    if (!head && cell.Notes.Count > 0) ties++;
                }
                at += MusicTime.CellSlots(new TabCell { DurationDenominator = denominator, Dots = dots });
                first = false;
            }
            start = Snap(start + len); remaining -= len;
        }
        return ties;
    }

    private static readonly string[] AttackTechniques =
    {
        "HOPO", "Slide", "LegatoSlide", "ShiftSlide", "SlideInBelow", "SlideInAbove", "PickSlideUp", "PickSlideDown",
        "Tapping", "LeftTap", "Slap", "Pop", "Accent", "HeavyAccent", "PickDown", "PickUp", "BrushDown", "BrushUp",
        "ArpeggioDown", "ArpeggioUp", "Rasgueado", "GraceBefore", "GraceOnBeat", "GraceBend", "Dead", "DeadSlapped", "Ghost"
    };

    /// <summary>The tied continuation of a split beat: same notes marked tied, no attack marks or beat annotations.
    /// Null for an annotation-only beat (its remainder is a plain implicit rest).</summary>
    private static TabCell? Continuation(TabCell source)
    {
        if (source.Notes.Count == 0 && !source.IsRest) return null;
        var cell = source.Clone();
        cell.RhythmicPosition = null;
        cell.ChordName = null; cell.Text = null; cell.Lyrics = ""; cell.Mix = null;
        cell.Accent = 0; cell.IsGrace = false; cell.WhammyPoints = new();
        foreach (var note in cell.Notes)
        {
            note.Tied = true;
            note.IsGraceNote = false;
            note.BendPoints = new(); note.BendTypeName = ""; note.BendStyleName = "";
            note.SlideTargetMidi = 0; note.LeftHandFinger = null; note.RightHandFinger = null;
            note.Ghost = false; note.Dead = false;
            foreach (var technique in AttackTechniques) note.Techniques.Remove(technique);
        }
        return cell;
    }

    private static void Add(Dictionary<int, List<(double, TabCell)>> pieces, int bar, double pos, TabCell cell)
    {
        if (!pieces.TryGetValue(bar, out var list)) pieces[bar] = list = new();
        list.Add((pos, cell));
    }

    /// <summary>A fresh voice grid for one bar holding the given beats at their in-bar positions. A beat sits on its own
    /// slot index when the onset rule gives that slot anyway; otherwise it keeps its exact onset in RhythmicPosition.</summary>
    internal static List<TabCell> WriteBar(List<(double Pos, TabCell Cell)> beats, int barSlots)
    {
        var cells = Enumerable.Range(0, barSlots).Select(_ => new TabCell()).ToList();
        var last = -1; var cursor = 0.0;
        foreach (var (pos, cell) in beats.OrderBy(b => b.Pos))
        {
            var slot = Math.Max((int)Math.Floor(pos + Eps), last + 1);
            while (slot >= cells.Count) cells.Add(new TabCell());
            cell.RhythmicPosition = Math.Abs(slot - pos) < Eps && cursor <= pos + Eps ? null : pos;
            cells[slot] = cell;
            last = slot;
            cursor = Math.Max(cursor, pos + MusicTime.CellSlots(cell));
        }
        return cells;
    }

    // ----- bars -----

}
