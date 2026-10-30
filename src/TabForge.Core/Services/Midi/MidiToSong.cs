using System.IO;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge.Services;

// Owns: turning a parsed MIDI file into a song: master bars (tempo, time and key signatures), one track per MIDI track or channel, notes on the bar grid.
// Does not own: reading the file (SmfReader) and picking frets (MidiClipToTab).
// Tests: MidiImportTests.
/// <summary>
/// MIDI import. Notes are quantised per beat to straight sixteenths, eighth-note triplets or sixteenth-note triplets (whichever fits the
/// onsets of that beat best), notes that run past the bar line continue as tied beats, and a line that overlaps the one before it goes to voice 2.
/// </summary>
internal static partial class MidiToSong
{
    private sealed class Bar { public long Start; public long Length; public int Num; public int Den; public int Slots; }

    private sealed class Ev
    {
        public double Slot;
        public double End;
        public List<TabNote> Notes = new();
        public List<long> EndTicks = new();
        public bool Tied;
        public int Voice;
    }

    private sealed class Lost { public int Notes; public int Controllers; }

    private static readonly IReadOnlyList<(double Slots, int Den, int Dots, int Tuplet)> Straight = new (double Slots, int Den, int Dots, int Tuplet)[]
        { (16, 1, 0, 0), (12, 2, 1, 0), (8, 2, 0, 0), (6, 4, 1, 0), (4, 4, 0, 0), (3, 8, 1, 0), (2, 8, 0, 0), (1, 16, 0, 0) };
    private static readonly IReadOnlyList<(double Slots, int Den, int Dots, int Tuplet)> Triplet = new (double Slots, int Den, int Dots, int Tuplet)[]
        { (8 / 3.0, 4, 0, 3), (4 / 3.0, 8, 0, 3), (2 / 3.0, 16, 0, 3) };

    public static SongProject Convert(SmfFile smf, string path, ImportContext context)
    {
        var division = smf.Division;
        var endTick = smf.Tracks.SelectMany(t => t.Notes).Select(n => Math.Max(n.End, n.Start + 1)).DefaultIfEmpty(0).Max();
        if (endTick == 0) throw new InvalidDataException("The score file loaded, but contained no tracks.");
        var bars = MasterBars(smf, endTick);

        var project = new SongProject
        {
            Title = smf.Tracks.Count > 0 && smf.Tracks[0].Name.Length > 0 && (smf.Format == 0 || smf.Tracks[0].Notes.Count == 0) ? smf.Tracks[0].Name : Path.GetFileNameWithoutExtension(path),
            ImportedFrom = path,
            TimeSignatureNumerator = bars[0].Num,
            TimeSignatureDenominator = bars[0].Den,
            Tracks = new List<TrackModel>(),
        };
        if (smf.Keys.Count > 0 && smf.Keys[0].Tick <= 0) { project.KeySignature = Math.Clamp(smf.Keys[0].Fifths, -7, 7); project.KeySignatureMinor = smf.Keys[0].Minor; }
        project.Tempo = smf.Tempos.Where(t => t.Tick <= 0).Select(t => Bpm(t.Mpq)).DefaultIfEmpty(120).Last();

        var groups = Groups(smf);
        if (groups.Count == 0) throw new InvalidDataException("The score file loaded, but contained no tracks.");
        if (groups.Count > InputLimits.MaxTracks) throw new InvalidDataException("The score file contains too many tracks.");
        var budget = new ImportBudget(context);
        var lost = new Lost();
        var channel = -1;
        foreach (var (name, drums, notes, source, partChannel) in groups)
        {
            budget.AddMeasures(bars.Count);
            project.Tracks.Add(BuildTrack(name, drums, notes, bars, division, budget, lost, ref channel, source, partChannel, smf, context));
        }
        ApplyMasterBars(project, smf, bars);
        MidiSectionImport.ApplySections(project, smf, bars.Select(b => (b.Start, b.Length)).ToList());
        if (lost.Notes > 0)
            context.DamageNotice = $"{lost.Notes:N0} MIDI note{(lost.Notes == 1 ? "" : "s")} could not be written: more than two overlapping lines in a track, or no string can play them.";
        context.MidiNotice = MidiImportNotice.Text(context.MidiMode, project.Tracks.Count, project.Markers.Count, lost.Controllers);
        ProjectValidator.Validate(project);
        project.IsDirty = false;
        return project;
    }

    private static int Bpm(int mpq) => Math.Clamp((int)Math.Round(60_000_000.0 / mpq), InputLimits.MinTempo, InputLimits.MaxTempo);

    /// <summary>Bars from tick 0 until the last note ends; a time signature that starts inside a bar applies from the next bar.</summary>
    private static List<Bar> MasterBars(SmfFile smf, long endTick)
    {
        var bars = new List<Bar>();
        var signatures = smf.TimeSignatures;
        var index = -1; int num = 4, den = 4;
        long start = 0;
        while (true)
        {
            while (index + 1 < signatures.Count && signatures[index + 1].Tick <= start) { index++; num = signatures[index].Numerator; den = signatures[index].Denominator; }
            var length = Math.Max(1, (long)Math.Round(smf.Division * 4.0 * num / den));
            bars.Add(new Bar { Start = start, Length = length, Num = num, Den = den, Slots = MusicTime.BarSlots(num, den) });
            start += length;
            if (start >= endTick) break;
            if (bars.Count >= InputLimits.MaxMeasuresPerTrack) throw new InvalidDataException("The score file contains too many measures.");
        }
        return bars;
    }

    /// <summary>One track per MIDI track (format 1) or per channel (format 0); channel 10 is always a drum track of its own.</summary>
    internal static List<(string Name, bool Drums, List<SmfNote> Notes, SmfTrack Source, int? PartChannel)> Groups(SmfFile smf)
    {
        var list = new List<(string, bool, List<SmfNote>, SmfTrack, int?)>();
        foreach (var track in smf.Tracks)
        {
            if (track.Notes.Count == 0) continue;
            var parts = smf.Format == 0
                ? track.Notes.GroupBy(n => n.Channel).OrderBy(g => g.Key).Select(g => (Notes: g.ToList(), Channel: (int?)g.Key)).ToList()
                : new[] { (Notes: track.Notes.Where(n => n.Channel != 9).ToList(), Channel: (int?)null), (Notes: track.Notes.Where(n => n.Channel == 9).ToList(), Channel: (int?)9) }
                    .Where(p => p.Notes.Count > 0).ToList();
            foreach (var (notes, partChannel) in parts)
            {
                var drums = notes[0].Channel == 9;
                var named = parts.Count == 1 && track.Name.Length > 0;
                list.Add((named ? track.Name : drums ? "Drums" : GeneralMidi.NameOf(notes[0].Program), drums, notes, track, partChannel));
            }
        }
        return list;
    }

    private static TrackModel BuildTrack(string name, bool drums, List<SmfNote> notes, List<Bar> bars, int division, ImportBudget budget, Lost lost, ref int channel,
        SmfTrack source, int? partChannel, SmfFile smf, ImportContext context)
    {
        notes = MidiImportExtras.WithoutKeyswitches(notes, context);
        var (program, hasProgram) = drums ? (0, false) : FirstProgram(source, partChannel, notes);
        var track = new TrackModel { Name = name, Measures = TemplateFactory.Measures(bars.Count) };
        if (drums)
        {
            track.Kind = TrackKind.Drums; track.MidiChannel = 9; track.MidiProgram = 0; track.InstrumentName = GeneralMidi.DrumKit;
            track.StringTunings = Enumerable.Range(0, DrumMaps.LineCount(track.DrumMapPreset)).Select(i => 60 - i).ToList();
        }
        else
        {
            channel = GuitarProTrackConverter.NextMelodicChannel(channel);
            track.MidiChannel = channel; track.MidiProgram = program; track.InstrumentName = GeneralMidi.NameOf(program);
            SetInstrument(track, program, hasProgram, name, notes.Min(n => n.Pitch), notes.Max(n => n.Pitch));
        }

        if (context.MidiMode == MidiImportMode.Midi)
        {
            // A MIDI track: its notes, controllers and bends live in one MIDI clip and the written bars stay empty (never both).
            MidiImportExtras.AddMidiClip(track, source, smf, partChannel, drums, notes);
            return track;
        }
        var pointer = 0;
        var carry = new List<Ev>();
        for (var b = 0; b < bars.Count; b++)
        {
            budget.Context.Check();
            var bar = bars[b];
            var raw = new List<SmfNote>();
            while (pointer < notes.Count && notes[pointer].Start < bar.Start + bar.Length) raw.Add(notes[pointer++]);
            if (raw.Count == 0 && carry.Count == 0) continue;
            carry = BuildBar(track, track.Measures[b], bar, raw, carry, division, drums, budget, lost);
        }
        GuitarProImporter.LinkTieOrigins(track);
        lost.Controllers += MidiImportExtras.CountControllers(source, partChannel, drums);   // a tab track has no lane for them: dropped, and said so
        return track;
    }

    /// <summary>First program change anywhere on the track's channel(s); name fallback only with no program.</summary>
    internal static (int Program, bool HasProgram) FirstProgram(SmfTrack source, int? partChannel, List<SmfNote> notes)
    {
        foreach (var (_, channel, program) in source.ProgramChanges)
            if (partChannel is int only ? channel == only : channel != 9) return (program, true);
        return notes.Count > 0 ? (notes[0].Program, source.ProgramChanges.Count > 0) : (0, false);
    }

    /// <summary>GM 24-31 guitar, 32-39 bass regardless of range; no-program tracks follow the track-name keywords.</summary>
    internal static void SetInstrument(TrackModel track, int program, bool hasProgram, string name, int lowest, int highest)
    {
        if (hasProgram && program is >= 24 and <= 31) { Guitar(track); return; }
        if (hasProgram && program is >= 32 and <= 39) { Bass(track, lowest); return; }
        if (!hasProgram)
        {
            var lower = (name ?? "").ToLowerInvariant();
            if (lower.Contains("bass")) { Bass(track, lowest); return; }
            if (lower.Contains("piano") || lower.Contains("key")) { Keys(track); return; }
            Guitar(track); return;
        }
        if (program is >= 32 and <= 39) Bass(track, lowest);
        else if (program < 24) Keys(track);
        else Guitar(track);
        if (lowest >= track.StringTunings.Min() && highest <= track.StringTunings.Max() + track.NumberOfFrets) return;
        Keys(track);
    }
    private static void Guitar(TrackModel t) { t.Kind = TrackKind.Guitar; t.StringTunings = new() { 64, 59, 55, 50, 45, 40 }; t.NumberOfFrets = 24; }
    private static void Bass(TrackModel t, int lowest) { t.Kind = TrackKind.Bass; t.StringTunings = lowest < 28 ? new() { 43, 38, 33, 28, 23 } : new() { 43, 38, 33, 28 }; t.NumberOfFrets = 24; }
    private static void Keys(TrackModel t) { t.Kind = TrackKind.Keys; t.StringTunings = new() { 96, 84, 72, 60, 48, 36, 24 }; t.NumberOfFrets = 36; }

    /// <summary>The beat grids (straight sixteenths, eighth triplets, sixteenth triplets) as the distance between positions in slots.</summary>
    private static double Step(int kind) => kind == 0 ? 1.0 : kind == 1 ? 4 / 3.0 : 2 / 3.0;

    private static double Tidy(double x) => Math.Abs(x - Math.Round(x)) < 1e-9 ? Math.Round(x) : x;

    private static double GridError(List<double> points, double step) => points.Sum(p => Math.Abs(p - Math.Round(p / step) * step));

    /// <summary>The grid whose positions the given onsets (slots from the beat start) sit closest to; straight wins unless a triplet grid is clearly better.</summary>
    private static int ChooseGrid(List<double> points)
    {
        if (points.Count == 0) return 0;
        var best = 0;
        var bestError = GridError(points, Step(0));
        var margin = 0.1 * points.Count;
        foreach (var kind in new[] { 1, 2 })
        {
            var error = GridError(points, Step(kind));
            if (error < bestError - margin) { best = kind; bestError = error; }
        }
        return best;
    }

    /// <summary>Writes one bar of one track from its raw notes (and the notes carried in over the bar line); returns what runs on into the next bar.</summary>
    private static List<Ev> BuildBar(TrackModel track, MeasureModel measure, Bar bar, List<SmfNote> raw, List<Ev> carryIn, int division, bool drums, ImportBudget budget, Lost lost)
    {
        var q = new Quant(bar, division, raw, carryIn);
        var events = new List<Ev>();
        var voiceEnd = new double[2];
        AddCarriedIn(q, carryIn, events, voiceEnd);
        AddBarNotes(q, track, raw, drums, lost, events, voiceEnd);
        EmitBarVoices(q, measure, events, drums, budget);
        return drums ? new List<Ev>() : CarryOut(q, events);
    }

    /// <summary>The quantising of one bar: the beat grid chosen from where the notes start (or stop), and the snapping of ticks to it.</summary>
    private sealed class Quant
    {
        public readonly int Slots;
        public readonly int BeatCount;
        public readonly int[] Grid;
        private readonly long _start;
        private readonly int _division;

        public Quant(Bar bar, int division, List<SmfNote> raw, List<Ev> carryIn)
        {
            _start = bar.Start; _division = division; Slots = bar.Slots; BeatCount = (Slots + 3) / 4;
            // The grid of every beat from where its notes start (or, with none starting there, where they stop).
            var onsets = Enumerable.Range(0, BeatCount).Select(_ => new List<double>()).ToArray();
            var ends = Enumerable.Range(0, BeatCount).Select(_ => new List<double>()).ToArray();
            foreach (var n in raw) { var s = SlotOf(n.Start); onsets[BeatOf(s)].Add(s - BeatOf(s) * 4); }
            foreach (var tick in raw.Select(n => n.End).Concat(carryIn.SelectMany(c => c.EndTicks)))
            {
                var s = SlotOf(tick);
                if (s > 0.01 && s < Slots - 0.01) ends[BeatOf(s)].Add(s - BeatOf(s) * 4);
            }
            Grid = Enumerable.Range(0, BeatCount).Select(i => ChooseGrid(onsets[i].Count > 0 ? onsets[i] : ends[i])).ToArray();
        }

        public double SlotOf(long tick) => (tick - _start) * 4.0 / _division;
        public int BeatOf(double s) => Math.Clamp((int)Math.Floor(s / 4 + 1e-9), 0, BeatCount - 1);

        public double Snap(double s)
        {
            var beat = BeatOf(s);
            var step = Step(Grid[beat]);
            return Tidy(beat * 4 + Math.Round((s - beat * 4) / step) * step);
        }

        public double QuantEnd(long tick)
        {
            var s = SlotOf(tick);
            return s >= Slots - 0.4 ? Slots : Snap(s);
        }
    }

    private static void AddCarriedIn(Quant q, List<Ev> carryIn, List<Ev> events, double[] voiceEnd)
    {
        foreach (var c in carryIn)
        {
            var ev = new Ev { Slot = 0, Tied = true, Voice = c.Voice, Notes = c.Notes, EndTicks = c.EndTicks };
            ev.End = Math.Max(ev.EndTicks.Max(q.QuantEnd), Step(q.Grid[0]));
            voiceEnd[ev.Voice] = Math.Max(voiceEnd[ev.Voice], ev.End);
            events.Add(ev);
        }
    }

    /// <summary>Groups the notes of the bar by snapped position and turns each group into one event in voice 1 or 2 (a note with no room in either voice is lost).</summary>
    private static void AddBarNotes(Quant q, TrackModel track, List<SmfNote> raw, bool drums, Lost lost, List<Ev> events, double[] voiceEnd)
    {
        var groups = new SortedDictionary<long, (double Slot, List<SmfNote> Notes)>();
        foreach (var n in raw)
        {
            var x = q.Snap(q.SlotOf(n.Start));
            if (x >= q.Slots - 1e-6) x = Tidy(q.Slots - Step(q.Grid[q.BeatCount - 1]));
            var key = (long)Math.Round(x * 1000);
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = (x, new List<SmfNote>());
            group.Notes.Add(n);
        }
        foreach (var (slot, notes) in groups.Values)
        {
            var ev = new Ev { Slot = slot };
            var used = new HashSet<int>();
            var seen = new HashSet<int>();
            foreach (var n in notes.OrderByDescending(x => x.Pitch))
            {
                if (!seen.Add(n.Pitch)) continue;
                var note = drums ? (n.Pitch is >= 27 and <= 87 ? MidiClipToTab.DrumNote(n.Pitch) : null) : MidiClipToTab.StringNote(track, n.Pitch, used);
                if (note is null) { lost.Notes++; continue; }
                note.Velocity = Math.Clamp(n.Velocity, 1, MidiLimits.Max);
                TechniqueMarker.Apply(note, n.Marker);
                used.Add(note.StringIndex);
                ev.Notes.Add(note);
                ev.EndTicks.Add(n.End);
            }
            if (ev.Notes.Count == 0) continue;
            var unit = Step(q.Grid[q.BeatOf(slot)]);
            ev.End = drums ? Math.Min(q.Slots, slot + unit) : Math.Max(Math.Min(q.Slots, slot + unit), ev.EndTicks.Max(q.QuantEnd));
            var voice = drums || voiceEnd[0] <= slot + 1.5 ? 0 : voiceEnd[1] <= slot + 1.5 ? 1 : -1;
            if (voice < 0) { lost.Notes += ev.Notes.Count; continue; }
            ev.Voice = voice;
            voiceEnd[voice] = ev.End;
            events.Add(ev);
        }
    }

    private static void EmitBarVoices(Quant q, MeasureModel measure, List<Ev> events, bool drums, ImportBudget budget)
    {
        var measureNotes = 0;
        for (var voice = 0; voice < 2; voice++)
        {
            var sequence = events.Where(e => e.Voice == voice).OrderBy(e => e.Slot).ToList();
            if (sequence.Count == 0) continue;
            var cells = EmitVoice(sequence, voice, q.Slots, q.Grid, drums);
            while (cells.Count < q.Slots) cells.Add(new TabCell());
            budget.AddCells(cells.Count);
            budget.AddNotes(cells.Sum(c => c.Notes.Count), ref measureNotes);
            if (voice == 0) measure.Cells = cells; else measure.Voice2Cells = cells;
        }
    }

    /// <summary>What still sounds at the bar end continues in the next bar, in the same voice, on the same strings.</summary>
    private static List<Ev> CarryOut(Quant q, List<Ev> events)
    {
        var carryOut = new List<Ev>();
        for (var voice = 0; voice < 2; voice++)
        {
            var last = events.Where(e => e.Voice == voice).OrderBy(e => e.Slot).LastOrDefault();
            if (last is null) continue;
            var onward = new Ev { Voice = voice, Tied = true };
            for (var i = 0; i < last.Notes.Count; i++)
            {
                if (q.SlotOf(last.EndTicks[i]) <= q.Slots + 0.4) continue;
                var note = last.Notes[i].Clone();
                note.Tied = true;
                onward.Notes.Add(note);
                onward.EndTicks.Add(last.EndTicks[i]);
            }
            if (onward.Notes.Count > 0) carryOut.Add(onward);
        }
        return carryOut;
    }

    /// <summary>
    /// The beat cells of one voice of a bar: each event lasts to the next one (a short gap after it is absorbed, a longer one becomes a rest),
    /// long spans split into plain values with ties. Cell indexes and RhythmicPosition follow the editor rule: the index is the onset slot unless a
    /// tuplet or an earlier long beat pushed it.
    /// </summary>
    private static List<TabCell> EmitVoice(List<Ev> sequence, int voice, int slots, int[] grid, bool drums)
    {
        var cells = new List<TabCell>();
        var lastEnd = 0.0;
        void Add(TabCell cell, double slot)
        {
            var index = Math.Max((int)Math.Floor(slot + 1e-9), cells.Count);
            while (cells.Count < index) cells.Add(new TabCell());
            cell.RhythmicPosition = Math.Abs(slot - Math.Round(slot)) < 1e-9 && index == (int)Math.Round(slot) && lastEnd <= slot + 1e-9 ? null : Math.Round(slot, 6);
            cells.Add(cell);
            lastEnd = Math.Max(lastEnd, slot + MusicTime.CellSlots(cell));
        }
        void Span(double from, double to, Ev? ev)
        {
            var first = true;
            foreach (var (position, option) in Pieces(from, to - from, grid))
            {
                var cell = new TabCell
                {
                    DurationDenominator = option.Den, Dots = option.Dots,
                    TupletNumerator = option.Tuplet, TupletDenominator = option.Tuplet > 0 ? 2 : 0, IsTriplet = option.Tuplet == 3,
                };
                if (ev is null) cell.IsRest = true;
                else
                    foreach (var note in ev.Notes)
                    {
                        var copy = note.Clone();
                        copy.Tied = !first || note.Tied;
                        cell.Notes.Add(copy);
                    }
                Add(cell, position);
                first = false;
            }
        }

        var cursor = 0.0;
        for (var i = 0; i < sequence.Count; i++)
        {
            var ev = sequence[i];
            var next = i + 1 < sequence.Count ? sequence[i + 1].Slot : slots;
            if (ev.Slot > cursor + 0.3 && (voice == 0 || cursor > 0)) Span(cursor, ev.Slot, null);
            var end = Math.Min(ev.End, next);
            if (drums) end = Math.Min(next, ev.Slot + 4);
            else if (next - end <= Math.Max(1.0, 0.34 * (next - ev.Slot))) end = next;
            if (end < ev.Slot + 0.6) end = Math.Min(next, ev.Slot + 1);
            Span(ev.Slot, end, ev);
            cursor = end;
        }
        if (voice == 0 && cursor < slots - 0.3) Span(cursor, slots, null);
        return cells;
    }

    /// <summary>Plain note values (dots, 3:2 tuplets) that add up to a span, each chosen by the grid of the beat it starts in.</summary>
    private static List<(double Position, (double Slots, int Den, int Dots, int Tuplet) Option)> Pieces(double from, double length, int[] grid)
    {
        var result = new List<(double, (double, int, int, int))>();
        var p = from;
        var remaining = length;
        while (remaining > 0.2 && result.Count < 64)
        {
            var beat = Math.Clamp((int)Math.Floor(p / 4 + 1e-9), 0, grid.Length - 1);
            var offset = p - beat * 4;
            (double Slots, int Den, int Dots, int Tuplet)? pick = null;
            if (grid[beat] == 0)
            {
                foreach (var o in Straight) if (o.Slots <= remaining + 0.05) { pick = o; break; }
            }
            else
            {
                var room = Math.Min(remaining, 4 - offset);
                if (offset < 0.05)
                    foreach (var o in Straight) if (o.Slots >= 4 && o.Slots <= remaining + 0.05) { pick = o; break; }
                if (pick is null)
                    foreach (var o in Triplet) if (o.Slots <= room + 0.05 && (grid[beat] == 2 || o.Den != 16)) { pick = o; break; }
            }
            pick ??= remaining >= 0.6 ? Triplet[2] : null;
            if (pick is not { } chosen) break;
            result.Add((Math.Round(p, 6), chosen));
            p = Tidy(p + chosen.Slots);
            remaining -= chosen.Slots;
        }
        return result;
    }

    /// <summary>Tempo, time signature and key changes live on the bars of every track.</summary>
    private static void ApplyMasterBars(SongProject project, SmfFile smf, List<Bar> bars)
    {
        var change = new int?[bars.Count];
        var inside = new List<TempoPoint>?[bars.Count];
        var running = project.Tempo;
        var index = 0;
        foreach (var (tick, mpq) in smf.Tempos)
        {
            if (tick <= 0) continue;
            while (index + 1 < bars.Count && bars[index + 1].Start <= tick) index++;
            if (tick >= bars[index].Start + bars[index].Length) continue;
            var tempo = Bpm(mpq);
            if (tempo == running) continue;
            running = tempo;
            var slot = (tick - bars[index].Start) * 4.0 / smf.Division;
            if (slot < 0.25) change[index] = tempo;
            else if ((inside[index] ??= new()).Count < 64) inside[index]!.Add(new TempoPoint(Math.Round(slot, 3), tempo));
        }
        foreach (var track in project.Tracks)
            for (var b = 0; b < bars.Count && b < track.Measures.Count; b++)
            {
                var measure = track.Measures[b];
                measure.TempoChange = change[b];
                measure.MidBarTempos = inside[b]?.ToList();
                if (bars[b].Num != project.TimeSignatureNumerator || bars[b].Den != project.TimeSignatureDenominator)
                {
                    measure.TimeSigNum = bars[b].Num;
                    measure.TimeSigDenom = bars[b].Den;
                }
                var key = smf.Keys.LastOrDefault(k => k.Tick <= bars[b].Start);
                var (fifths, minor) = key == default ? (0, false) : (Math.Clamp(key.Fifths, -7, 7), key.Minor);
                if (fifths != project.KeySignature || minor != project.KeySignatureMinor)
                {
                    measure.KeySignature = fifths;
                    measure.KeySignatureMinor = minor;
                }
            }
    }
}
