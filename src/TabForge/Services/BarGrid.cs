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
    Insert
}

/// <summary>Outcome of a placement. On refusal (<see cref="Ok"/> false) the project is unchanged.</summary>
public sealed record PlacementResult(bool Ok, string? Error, int FirstBar, int LastBar, int BarsAppended, int TiesAdded,
    int DroppedBeats, int[]? BarMap = null)
{
    public static PlacementResult Refused(string error) => new(false, error, -1, -1, 0, 0, 0);
}

// Owns: the paste placement engine: absolute slot positions, replace and insert placement, bar-line splitting and ties.
// Does not own: clipboard capture (ClipboardService) and the editor's selection.
// Tests: TestBarGridPlacement, TestPasteCommands.
/// <summary>
/// Copy/paste placement engine (design C2): absolute slot positions, beat placement in Replace/Insert mode with bar-line
/// splitting and ties, tuplet-straddle refusal, bars appended at the end, and the bar-level Overwrite/Insert operations.
/// Pure model code: no UI, no undo (the caller wraps it in one transaction). Onsets follow the one MusicTime rule
/// (<c>cell.RhythmicPosition ?? max(i, cursor)</c>, durations by <see cref="MusicTime.CellSlots"/>).
/// </summary>
public static class BarGrid
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

    /// <summary>Onset of every cell of a voice grid (beats by the MusicTime rule; an empty cell sits on its own index).</summary>
    public static double[] Onsets(IReadOnlyList<TabCell> cells)
    {
        var onsets = new double[cells.Count];
        var cursor = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!IsBeat(cell)) { onsets[i] = i; continue; }
            var start = cell.RhythmicPosition is { } exact && double.IsFinite(exact) ? Math.Max(0, exact) : Math.Max(i, cursor);
            onsets[i] = start;
            cursor = Math.Max(cursor, start + MusicTime.CellSlots(cell));
        }
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

    private static readonly (int Denominator, int Dots, double Length)[] Values = MusicTime.AllDenominators
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
        if (trackIndex < 0 || trackIndex >= p.Tracks.Count) return PlacementResult.Refused("No such track.");
        if (voice is < 0 or > 1) return PlacementResult.Refused("No such voice.");
        if (run.Beats.Count == 0) return PlacementResult.Refused("Nothing to paste.");
        if (!double.IsFinite(anchor) || anchor < 0) return PlacementResult.Refused("Invalid paste position.");
        if (run.Beats.Any(b => b.Cell is null || !double.IsFinite(b.OffsetSlots) || b.OffsetSlots < 0))
            return PlacementResult.Refused("Invalid clip.");
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

        var kept = new List<Ev>();
        foreach (var e in events)
        {
            var straddles = e.Start < anchor - Eps && e.Start + e.Length > anchor + Eps;
            if (straddles && IsTuplet(e.Cell))
                return PlacementResult.Refused($"The cursor is inside a tuplet in bar {Locate(p, e.Start).Bar + 1}.");
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
        var order = events.Count;
        foreach (var beat in run.Beats.OrderBy(b => b.OffsetSlots))
        {
            var cell = beat.Cell.Clone();
            cell.RhythmicPosition = null;
            kept.Add(new Ev { Start = Snap(anchor + beat.OffsetSlots), Length = MusicTime.CellSlots(cell), Cell = cell, Pasted = true, Order = order++ });
        }
        var sorted = kept.OrderBy(e => e.Start).ThenBy(e => e.Order).ToList();

        // Ties at the edges: a pasted beat, and the first beat after the pasted span, keep a tie only when the beat just
        // before them ends on their onset with the same note (string and pitch).
        var afterSpan = sorted.FirstOrDefault(e => !e.Pasted && e.Start >= spanEnd - Eps);
        for (var i = 0; i < sorted.Count; i++)
            if (sorted[i].Pasted || ReferenceEquals(sorted[i], afterSpan))
                CheckTies(sorted[i], i > 0 ? sorted[i - 1] : before);

        // Tuplet groups never split at a bar line.
        foreach (var (start, end) in TupletGroups(sorted))
        {
            var (bar, inBar) = Locate(p, start);
            if (inBar + (end - start) > SlotsOf(p, bar) + Eps)
                return PlacementResult.Refused($"A tuplet group would cross the bar line into bar {bar + 2}.");
        }

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
        return new PlacementResult(true, null, firstBar, lastBar, appended, ties, 0);
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
                if (IsBeat(cells[i]))
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

    /// <summary>Appends <paramref name="count"/> empty bars to every track (keeping them aligned); each new bar takes its
    /// track's last bar's time signature, key and clef.</summary>
    public static void AppendBars(SongProject p, int count)
    {
        if (count <= 0) return;
        PadTracks(p);
        foreach (var track in p.Tracks)
        {
            var last = track.Measures.LastOrDefault();
            for (var i = 0; i < count; i++)
                track.Measures.Add(new MeasureModel
                {
                    TimeSigNum = last?.TimeSigNum, TimeSigDenom = last?.TimeSigDenom,
                    KeySignature = last?.KeySignature, KeySignatureMinor = last?.KeySignatureMinor,
                    Clef = last?.Clef ?? Clefs.Guitar,
                    Cells = EmptyCells(p, last?.TimeSigNum, last?.TimeSigDenom)
                });
            BarRangeEditor.Renumber(track);
        }
    }

    private static void PadTracks(SongProject p)
    {
        var total = BarRangeEditor.MaxMeasures(p);
        foreach (var track in p.Tracks)
        {
            if (track.Measures.Count >= total) continue;
            var last = track.Measures.LastOrDefault();
            while (track.Measures.Count < total)
                track.Measures.Add(new MeasureModel { Clef = last?.Clef ?? Clefs.Guitar });
            BarRangeEditor.Renumber(track);
        }
    }

    /// <summary>
    /// The bar settings a bars paste can carry: time signature, key, tempo (bar and mid-bar tempo
    /// changes), triplet feel, free time and pickup. Repeats, endings, directions and section names are form, not settings,
    /// and are never copied.
    /// </summary>
    public static void CopyBarSettings(MeasureModel from, MeasureModel to)
    {
        to.TimeSigNum = from.TimeSigNum; to.TimeSigDenom = from.TimeSigDenom;
        to.KeySignature = from.KeySignature; to.KeySignatureMinor = from.KeySignatureMinor;
        to.TempoChange = from.TempoChange; to.MidBarTempos = from.MidBarTempos?.ToList();
        to.TripletFeel = from.TripletFeel; to.TripletFeelKind = from.TripletFeelKind;
        to.FreeTime = from.FreeTime; to.Anacrusis = from.Anacrusis;
    }

    /// <summary>
    /// Overwrite: replaces both voices of bars [atBar, atBar + n) on each given track, appending bars on all tracks when the
    /// song is too short. With <paramref name="copySettings"/> the clip's bar settings are applied to that bar on every track
    /// (bars are global); otherwise the target keeps its own. Beats that do not fit the target bar are dropped and counted.
    /// </summary>
    public static PlacementResult OverwriteBars(SongProject p, int atBar, IReadOnlyList<TrackBars> tracks, bool copySettings)
    {
        var error = ValidateBars(p, atBar, tracks);
        if (error is not null) return PlacementResult.Refused(error);
        var count = tracks.Max(t => t.Bars.Count);
        var appended = Math.Max(0, atBar + count - BarRangeEditor.MaxMeasures(p));
        AppendBars(p, appended);
        PadTracks(p);
        if (copySettings)
            for (var i = 0; i < count; i++)
                if (SettingsSource(tracks, i) is { } source)
                    foreach (var track in p.Tracks) CopyBarSettings(source, track.Measures[atBar + i]);
        var dropped = 0;
        foreach (var clip in tracks)
            for (var i = 0; i < clip.Bars.Count; i++)
            {
                var target = p.Tracks[clip.TrackIndex].Measures[atBar + i];
                target.Cells = clip.Bars[i].Cells.Select(c => c.Clone()).ToList();
                target.Voice2Cells = clip.Bars[i].Voice2Cells.Select(c => c.Clone()).ToList();
                dropped += TrimToBar(p, target, atBar + i);
            }
        return new PlacementResult(true, null, atBar, atBar + count - 1, appended, 0, dropped);
    }

    /// <summary>
    /// Insert: puts the bars in before bar <paramref name="atBar"/> on ALL tracks (tracks without clip content get empty
    /// bars), so tracks stay aligned; markers and sections move as in the timeline area paste. "Insert after bar N" is
    /// <c>atBar = N + 1</c>. New bars take the clip's settings with <paramref name="copySettings"/>, otherwise the time
    /// signature, key and triplet feel in force at the insertion point. Returns the old-to-new bar map in
    /// <see cref="PlacementResult.BarMap"/>.
    /// </summary>
    public static PlacementResult InsertBars(SongProject p, int atBar, IReadOnlyList<TrackBars> tracks, bool copySettings)
    {
        var error = ValidateBars(p, 0, tracks);
        if (error is not null) return PlacementResult.Refused(error);
        PadTracks(p);
        var total = BarRangeEditor.MaxMeasures(p);
        atBar = Math.Clamp(atBar, 0, total);
        var count = tracks.Max(t => t.Bars.Count);
        if (total + count > InputLimits.MaxMeasuresPerTrack) return PlacementResult.Refused("The paste would make the song too long.");
        var clip = new List<List<MeasureModel>>();
        for (var t = 0; t < p.Tracks.Count; t++)
        {
            var track = p.Tracks[t];
            var neighbour = track.Measures.Count == 0 ? null : track.Measures[Math.Clamp(atBar - 1, 0, track.Measures.Count - 1)];
            var source = tracks.FirstOrDefault(x => x.TrackIndex == t);
            var bars = new List<MeasureModel>();
            for (var i = 0; i < count; i++)
            {
                var content = source is not null && i < source.Bars.Count ? source.Bars[i] : null;
                var bar = new MeasureModel
                {
                    Clef = neighbour?.Clef ?? Clefs.Guitar,
                    Cells = content?.Cells.Select(c => c.Clone()).ToList() ?? new MeasureModel().Cells,
                    Voice2Cells = content?.Voice2Cells.Select(c => c.Clone()).ToList() ?? new()
                };
                if (copySettings && SettingsSource(tracks, i) is { } settings) CopyBarSettings(settings, bar);
                else if (neighbour is not null)
                {
                    bar.TimeSigNum = neighbour.TimeSigNum; bar.TimeSigDenom = neighbour.TimeSigDenom;
                    bar.KeySignature = neighbour.KeySignature; bar.KeySignatureMinor = neighbour.KeySignatureMinor;
                    bar.TripletFeel = neighbour.TripletFeel; bar.TripletFeelKind = neighbour.TripletFeelKind;
                }
                if (content is null) bar.Cells = EmptyCells(p, bar.TimeSigNum, bar.TimeSigDenom);
                bars.Add(bar);
            }
            clip.Add(bars);
        }
        var map = BarRangeEditor.Insert(p, atBar, clip);
        var dropped = 0;
        foreach (var track in p.Tracks)
            for (var i = 0; i < count; i++) dropped += TrimToBar(p, track.Measures[atBar + i], atBar + i);
        return new PlacementResult(true, null, atBar, atBar + count - 1, 0, 0, dropped, map);
    }

    private static string? ValidateBars(SongProject p, int atBar, IReadOnlyList<TrackBars> tracks)
    {
        if (tracks.Count == 0 || tracks.All(t => t.Bars.Count == 0)) return "Nothing to paste.";
        if (atBar < 0) return "Invalid paste position.";
        if (tracks.Any(t => t.TrackIndex < 0 || t.TrackIndex >= p.Tracks.Count)) return "No such track.";
        if (tracks.GroupBy(t => t.TrackIndex).Any(g => g.Count() > 1)) return "A track is pasted twice.";
        if (atBar + tracks.Max(t => t.Bars.Count) > InputLimits.MaxMeasuresPerTrack) return "The paste would make the song too long.";
        return null;
    }

    private static MeasureModel? SettingsSource(IReadOnlyList<TrackBars> tracks, int index) =>
        tracks.Select(t => index < t.Bars.Count ? t.Bars[index] : null).FirstOrDefault(b => b is not null);

    /// <summary>Clears beats that do not fit the bar's length (both voices); returns how many.</summary>
    private static int TrimToBar(SongProject p, MeasureModel measure, int bar)
    {
        var slots = SlotsOf(p, bar);
        var dropped = 0;
        foreach (var cells in new[] { measure.Cells, measure.Voice2Cells })
        {
            if (cells.Count > 0) while (cells.Count < slots) cells.Add(new TabCell());
            var onsets = Onsets(cells);
            for (var i = 0; i < cells.Count; i++)
                if (IsBeat(cells[i]) && onsets[i] + MusicTime.CellSlots(cells[i]) > slots + TickSlack)
                {
                    cells[i] = new TabCell();
                    dropped++;
                }
        }
        return dropped;
    }

    private static List<TabCell> EmptyCells(SongProject p, int? numerator, int? denominator) =>
        Enumerable.Range(0, MusicTime.BarSlots(numerator ?? p.TimeSignatureNumerator, denominator ?? p.TimeSignatureDenominator))
            .Select(_ => new TabCell()).ToList();

    private static bool IsBeat(TabCell cell) => cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation;

    private static bool IsTuplet(TabCell cell) => cell.Tuplet.Numerator > 0;

    private static double Snap(double value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < Eps ? rounded : value;
    }
}
