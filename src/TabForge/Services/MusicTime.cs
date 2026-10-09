using System.Linq;
using TabForge.Models;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Services;

// Musical time helpers. The editor grid is 16 slots per bar in simple signatures:
// a slot = one 16th note (4/4), 12 slots for 3/4, 12 for 6/8, etc.
// Owns: bar and cell timing arithmetic in slots and the bar fill state.
// Does not own: the playback timeline and the drawing.
// Tests: TestBarSlots, TestCellSlots, TestAnalyzeBar.
public static class MusicTime
{
    public const int SlotsPerQuarter = 4;

    public static int BarSlots(int numerator, int denominator)
        => Math.Max(1, (int)Math.Round(numerator * 4.0 * SlotsPerQuarter / Math.Max(1, denominator)));

    public static int BarSlots(SongProject p, int measureIndex)
    {
        var bar = BarOf(p, measureIndex);
        var num = bar?.TimeSigNum ?? p.TimeSignatureNumerator;
        var den = bar?.TimeSigDenom ?? p.TimeSignatureDenominator;
        return BarSlots(num, den);
    }

    public static MeasureModel? BarOf(SongProject p, int measureIndex)
    {
        var t = p.MasterBarTrack;
        if (t is null || measureIndex < 0 || measureIndex >= t.Measures.Count) return null;
        return t.Measures[measureIndex];
    }

    /// <summary>Duration of a cell expressed in 16th-note slots.</summary>
    public static double CellSlots(TabCell cell)
    {
        var denominator = Math.Clamp(cell.DurationDenominator <= 0 ? 16 : cell.DurationDenominator, 1, 64);
        var slots = 16.0 / denominator;
        if (cell.Dots == 1) slots *= 1.5;
        else if (cell.Dots >= 2) slots *= 1.75;
        var (tupletNum, tupletDen) = cell.Tuplet;
        if (tupletNum > 0) slots *= tupletDen / (double)tupletNum;
        return Math.Clamp(slots, 0.25, 64);
    }

    public static int CellSlotsRounded(TabCell cell) => Math.Max(1, (int)Math.Round(CellSlots(cell)));

    /// <summary>Slots consumed by the cell at index, or 1 for an implicit rest.</summary>
    public static double ConsumeSlots(TabCell cell) =>
        cell.Notes.Count > 0 || cell.IsRest || cell.IsTied || cell.HasAnnotation ? CellSlots(cell) : 1.0;

    /// <summary>
    /// Cell indices that carry a beat (notes or an explicit rest), in order. This is the authoritative
    /// list of what the editor draws and navigates: every one of these is rendered, even when a tuplet's
    /// rounded slot falls inside the previous note's written span.
    /// </summary>
    public static List<int> BeatSlots(MeasureModel measure)
        => BeatSlots(measure.Cells);

    public static List<int> BeatSlots(IReadOnlyList<TabCell> cells)
    {
        var list = new List<int>();
        for (var i = 0; i < cells.Count; i++)
            if (cells[i].Notes.Count > 0 || cells[i].IsRest || cells[i].HasAnnotation) list.Add(i);
        return list;
    }

    /// <summary>Walks the bar and reports whether the contents fit the time signature.</summary>
    /// <summary>
    /// Bars that are overfilled/overlapping (drawn red) or partly filled. Empty bars are fine: they
    /// play as a whole-bar rest.
    /// </summary>
    public static List<BarProblem> FindBarProblems(SongProject p)
    {
        var problems = new List<BarProblem>();
        var bars = p.Tracks.Count == 0 ? 0 : p.Tracks.Max(track => track.Measures.Count);
        for (var bar = 0; bar < bars; bar++)
        {
            var state = AnalyzeBar(p, bar);
            if (state.Marked || (!state.Complete && state.Used > 0.001)) problems.Add(new BarProblem(bar, state));
        }
        return problems;
    }

    /// <summary>Tolerance of a bar's fill check: a few file ticks (1/240 slot each).</summary>
    private const double TickSlack = 0.012;

    /// <summary>
    /// The bar's fill state. Over-full or overlapping in any voice is an error; the bar is short when voice 1 of a track
    /// has beats summing to less than the time signature. Voice 2 is optional and never makes a bar short. Grace notes
    /// take no bar time. With <paramref name="only"/> the bar is judged on that track alone (the track's own view).
    /// </summary>
    public static BarState AnalyzeBar(SongProject p, int measureIndex, TrackModel? only = null)
    {
        var bar = BarOf(p, measureIndex);
        var slots = BarSlots(p, measureIndex);
        if (bar is null) return new BarState(slots, 16, false, false);
        if (bar.FreeTime) return new BarState(slots, slots, true, false);
        if (bar.SimileOneBar || bar.SimileTwoBar) return new BarState(slots, slots, true, false);

        // Used is the union of all tracks (the longest content); short is judged per track on voice 1.
        double used = 0; var shortVoice1 = false; var tooLong = false; var overlap = false; var anyBeat = false;
        foreach (var track in p.Tracks)
        {
            if (measureIndex >= track.Measures.Count || (only is not null && !ReferenceEquals(track, only))) continue;
            var m = track.Measures[measureIndex];
            var voices = m.Voice2Cells.Count == 0
                ? new[] { (IReadOnlyList<TabCell>)m.Cells }
                : new[] { (IReadOnlyList<TabCell>)m.Cells, m.Voice2Cells };
            foreach (var cells in voices)
            {
                double consumed = 0, beatsEnd = 0, cover = 0; var hasNotes = false;
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    var isBeat = cell.Notes.Count > 0 || cell.IsRest || cell.IsTied || cell.HasAnnotation;   // a tied beat holds its time
                    if (!isBeat)
                    {
                        if (consumed <= i) consumed = i + 1;
                        continue;
                    }
                    // A beat of grace notes only takes no bar time; an imported grace is merged into its main beat's cell
                    // (the cell is flagged IsGrace too), and that cell keeps the main beat's length.
                    if (cell.IsGrace && cell.Notes.Count > 0 && cell.Notes.TrueForAll(n => n.IsGraceNote)) continue;
                    var start = cell.RhythmicPosition ?? Math.Max(i, consumed);
                    var d = ConsumeSlots(cell);
                    anyBeat = true; cover += d; hasNotes |= cell.Notes.Count > 0;
                    // Imported beats sit on whole file ticks (240 per slot), so the exact tuplet lengths (a 9:8 thirty-second is
                    // 0.444 slot) drift by up to a tick against the next beat's position: that is not an overfull bar.
                    if (start + d > slots + TickSlack) { tooLong = true; consumed = Math.Max(consumed, start + d); break; }
                    // Overlap is judged against the beats only: an empty cell before a beat timed earlier than its cell (moved up after a
                    // length change, see EditCommands.RetimeFrom) holds no time of its own.
                    if (cell.Notes.Count > 0 && start < beatsEnd - TickSlack) overlap = true;
                    consumed = Math.Max(consumed, start + d);
                    beatsEnd = Math.Max(beatsEnd, start + d);
                }
                used = Math.Max(used, Math.Min(consumed, slots + 64));
                // A rest-only voice plays as silence (an imported empty bar is one quarter rest): not short.
                if (ReferenceEquals(cells, m.Cells) && hasNotes && cover < slots - TickSlack && !m.FreeTime && !m.SimileOneBar && !m.SimileTwoBar)
                    shortVoice1 = true;
            }
        }
        var complete = !tooLong && !overlap && used >= slots - TickSlack;
        var error = tooLong || overlap;
        // Content shorter than the time signature (half-empty); a bar with no beat at all and a pickup bar are not marked.
        var isShort = !error && anyBeat && !bar.Anacrusis && shortVoice1;
        return new BarState(slots, used, complete && !isShort, error, isShort);
    }

    /// <summary>
    /// The tempo in force at a bar: its own tempo change, or the last one before it, else the song tempo.
    /// A tempo change lasts until the next one (it used to apply to its own bar only, so the
    /// following bar fell back to the song's first tempo).
    /// </summary>
    public static int TempoAt(SongProject p, int measureIndex)
    {
        var track = p.MasterBarTrack;
        if (track is not null)
            for (var i = Math.Min(measureIndex, track.Measures.Count - 1); i >= 0; i--)
            {
                var m = track.Measures[i];
                // An earlier bar's last mid-bar change is still in force at this bar's start.
                if (i < measureIndex && m.MidBarTempos is { Count: > 0 } points) return Math.Clamp(points[^1].Tempo, 20, 400);
                if (m.TempoChange is { } change) return Math.Clamp(change, 20, 400);
            }
        return Math.Clamp(p.Tempo, 20, 400);
    }

    /// <summary>The tempo at the end of a bar (its last mid-bar change, else the tempo it starts with).</summary>
    public static int TempoAfter(SongProject p, int measureIndex, int startTempo) =>
        BarOf(p, measureIndex)?.MidBarTempos is { Count: > 0 } points ? Math.Clamp(points[^1].Tempo, 20, 400) : startTempo;

    /// <summary>Milliseconds from the bar start to a slot, following the bar's mid-bar tempo changes.</summary>
    public static double OffsetMs(MeasureModel? bar, double slot, int startTempo, double tempoScale = 1.0)
    {
        if (bar?.MidBarTempos is not { Count: > 0 } points) return SlotsToMsAt(slot, startTempo, tempoScale);
        double ms = 0, at = 0, tempo = startTempo;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.Slot >= slot) break;
            ms += ConstMs(Math.Max(0, point.Slot - at), tempo, tempoScale);
            at = Math.Max(at, point.Slot);
            var target = Math.Clamp(point.Tempo, 20, 400);
            var next = i + 1 < points.Count ? points[i + 1].Slot : double.MaxValue;
            var length = point.RampSlots > 0 ? Math.Min(point.RampSlots, next - at) : 0;
            if (length <= 0) { tempo = target; continue; }
            // Linear-in-beat ramp from `tempo` to `target` over RampSlots: exact integral of 60000/tempo.
            var used = Math.Min(slot, at + length) - at;
            ms += RampMs(used, tempo, target, point.RampSlots, tempoScale);
            if (at + length >= slot) return ms;
            tempo += (target - tempo) * length / point.RampSlots;
            at += length;
        }
        return ms + ConstMs(Math.Max(0, slot - at), tempo, tempoScale);
    }

    /// <summary>The tempo in force at a slot of a bar (interpolated inside a ramp), rounded to whole BPM.</summary>
    public static int TempoAtSlot(MeasureModel? bar, double slot, int startTempo)
    {
        double tempo = startTempo;
        if (bar?.MidBarTempos is { } points)
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point.Slot > slot) break;
                var target = Math.Clamp(point.Tempo, 20, 400);
                var next = i + 1 < points.Count ? points[i + 1].Slot : double.MaxValue;
                var length = point.RampSlots > 0 ? Math.Min(point.RampSlots, next - point.Slot) : 0;
                if (length > 0 && slot < point.Slot + length) { tempo += (target - tempo) * (slot - point.Slot) / point.RampSlots; break; }
                tempo = length > 0 ? tempo + (target - tempo) * length / point.RampSlots : target;
            }
        return (int)Math.Round(Math.Clamp(tempo, 20, 400));
    }

    /// <summary>How long a typed note previews: the beat's written length at the song tempo in force there (tempo changes up to the bar included).</summary>
    public static int NoteLengthMs(SongProject p, int measureIndex, int cellIndex, TabCell cell)
    {
        var tempo = p.Tempo;
        var master = p.MasterBarTrack;
        if (master is not null)
            for (var i = 0; i <= measureIndex && i < master.Measures.Count; i++)
                if (master.Measures[i].TempoChange is { } t) tempo = t;
        var at = TempoAtSlot(BarOf(p, measureIndex), cell.RhythmicPosition ?? cellIndex, tempo);
        return (int)Math.Round(ConstMs(CellSlots(cell), at, 1.0));
    }

    private static double ConstMs(double slots, double tempo, double tempoScale)
        => slots / SlotsPerQuarter * TempoMath.MsPerBeat(Math.Clamp(tempo, 20, 400)) * tempoScale;

    /// <summary>Milliseconds for the first <paramref name="x"/> slots of a ramp from t0 to t1 BPM over <paramref name="ramp"/> slots.</summary>
    public static double RampMs(double x, double t0, double t1, double ramp, double tempoScale = 1.0)
    {
        if (x <= 0) return 0;
        if (Math.Abs(t1 - t0) < 1e-9 || ramp <= 0) return ConstMs(x, t0, tempoScale);
        var tx = t0 + (t1 - t0) * x / ramp;
        return TempoMath.MsPerMinute / SlotsPerQuarter * ramp / (t1 - t0) * Math.Log(tx / t0) * tempoScale;
    }

    public static double SlotsToMs(SongProject p, int measureIndex, double slots, double tempoScale = 1.0)
        => OffsetMs(BarOf(p, measureIndex), slots, TempoAt(p, measureIndex), tempoScale);

    /// <summary>Slots to milliseconds at an explicit tempo (playback carries the running tempo itself).</summary>
    public static double SlotsToMsAt(double slots, int tempo, double tempoScale = 1.0) => ConstMs(slots, tempo, tempoScale);

    public static double BarMs(SongProject p, int measureIndex, double tempoScale = 1.0)
        => SlotsToMs(p, measureIndex, BarSlots(p, measureIndex), tempoScale);

    public static string DurationGlyph(int denominator) => denominator switch
    {
        1 => "𝅝", 2 => "𝅗𝅥", 4 => "♩", 8 => "♪", 16 => "𝅘𝅥𝅯", 32 => "𝅘𝅥𝅰", 64 => "𝅘𝅥𝅱", _ => "♪"
    };

    public static string DurationText(TabCell c)
    {
        var dots = c.Dots == 1 ? "." : c.Dots >= 2 ? ".." : "";
        var (tupletNum, _) = c.Tuplet;
        var trip = tupletNum > 0 ? tupletNum.ToString() : "";
        return $"{DurationGlyph(c.DurationDenominator)}{dots}{trip}";
    }

    public static string DurationName(int denominator) => denominator switch
    {
        1 => "whole", 2 => "half", 4 => "quarter", 8 => "eighth", 16 => "sixteenth",
        32 => "thirty-second", 64 => "sixty-fourth", _ => "note"
    };

    // ----- arrangement X-axis: one musical axis shared by every track -----

    /// <summary>Width of one bar at the given unit (4/4) bar width. Bars scale with their musical length.</summary>
    public static double BarWidth(SongProject project, int bar, double unitWidth)
        => unitWidth * Math.Clamp(BarSlots(project, bar) / 16.0, 0.25, 4.0);

    /// <summary>
    /// Exact left edge of a bar. Computed as a running sum with no per-track input, so every
    /// track line is guaranteed to share the same musical X-axis (no cumulative drift).
    /// </summary>
    public static double BarX(SongProject project, int bar, double unitWidth)
    {
        var x = 0.0;
        for (var i = 0; i < bar; i++) x += BarWidth(project, i, unitWidth);
        return x;
    }

    public static double TotalWidth(SongProject project, double unitWidth)
        => BarX(project, project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count), unitWidth);

    public static readonly int[] AllDenominators = { 1, 2, 4, 8, 16, 32, 64 };

    public static int Longer(int denominator)
    {
        var i = Array.IndexOf(AllDenominators, denominator);
        if (i <= 0) return AllDenominators[0];
        return AllDenominators[Math.Max(0, i - 1)];
    }

    public static int Shorter(int denominator)
    {
        var i = Array.IndexOf(AllDenominators, denominator);
        if (i < 0) return 8;
        return AllDenominators[Math.Min(AllDenominators.Length - 1, i + 1)];
    }
}

public readonly record struct BarState(int Slots, double Used, bool Complete, bool Error, bool Short = false)
{
    /// <summary>Drawn in the red bar style: overfull/overlapping, or content shorter than the time signature (an empty bar is not).</summary>
    public bool Marked => Error || Short;
}

/// <summary>A bar whose rhythm does not add up to its time signature (see <see cref="MusicTime.FindBarProblems"/>).</summary>
public readonly record struct BarProblem(int BarIndex, BarState State)
{
    /// <summary>"Bar 12: 4.5 of 4 beats (too long)" – beats counted in quarter notes.</summary>
    public string Describe()
    {
        static string Beats(double slots) => (slots / MusicTime.SlotsPerQuarter).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        var kind = State.Error ? "too long or overlapping" : "incomplete";
        return $"Bar {BarIndex + 1}: {Beats(State.Used)} of {Beats(State.Slots)} beats ({kind})";
    }
}
