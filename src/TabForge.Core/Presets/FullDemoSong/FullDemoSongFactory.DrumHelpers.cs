using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

// Owns: the drum helpers of the full demo song: the drum lanes, Grid, Fill, Roll, the drum re-duration and the seeded drum velocities.
// Does not own: the drum part's bars (FullDemoSongFactory.Drums.cs) or the shared contract in FullDemoSongFactory.Helpers.cs.
// Tests: no dedicated test class.

internal static partial class FullDemoSongFactory
{
    // ------------------------------------------------------------------ drums (plan 3.9)

    private sealed record DrumChar(int Midi, int Centre, Lane Lane, int Jitter, bool Accent2 = false, bool Ghost = false, bool Flam = false, bool Ramp = false, bool Choke = false);

    private static readonly Dictionary<string, Dictionary<char, DrumChar>> Lanes = new(StringComparer.Ordinal)
    {
        ["K"] = new() { ['X'] = new(36, 120, Lane.Kick, 4), ['x'] = new(36, 110, Lane.Kick, 4), ['d'] = new(36, 100, Lane.KickRun, 4), ['b'] = new(36, 96, Lane.Kick, 4), ['p'] = new(36, 49, Lane.Kick, 4) },
        ["S"] = new()
        {
            ['X'] = new(38, 127, Lane.Snare, 4, Accent2: true), ['x'] = new(38, 122, Lane.Snare, 4), ['f'] = new(38, 122, Lane.Snare, 4, Flam: true),
            ['g'] = new(38, 45, Lane.Ghost, 8, Ghost: true), ['b'] = new(38, 104, Lane.Snare, 4),
        },
        ["SS"] = new() { ['x'] = new(37, 80, Lane.SideStick, 6) },
        ["CL"] = new() { ['x'] = new(39, 96, Lane.Clap, 4) },
        ["H"] = new() { ['X'] = new(42, 90, Lane.HiHat, 6), ['x'] = new(42, 76, Lane.HiHat, 6), ['y'] = new(42, 66, Lane.HiHat, 6), ['o'] = new(46, 102, Lane.HiHat, 6), ['p'] = new(44, 70, Lane.HiHat, 6) },
        ["R"] = new() { ['X'] = new(51, 94, Lane.Ride, 6), ['x'] = new(51, 80, Lane.Ride, 6), ['b'] = new(53, 110, Lane.Ride, 6) },
        ["C"] = new() { ['X'] = new(49, 120, Lane.Crash, 4), ['x'] = new(49, 108, Lane.Crash, 4), ['2'] = new(57, 120, Lane.Crash, 4), ['c'] = new(49, 120, Lane.Crash, 4, Choke: true) },
        ["CH"] = new() { ['X'] = new(52, 118, Lane.China, 4), ['x'] = new(52, 118, Lane.China, 4) },
        ["SP"] = new() { ['X'] = new(55, 104, Lane.Splash, 4), ['x'] = new(55, 104, Lane.Splash, 4) },
        ["T1"] = new() { ['X'] = new(50, 122, Lane.Tom, 8), ['x'] = new(50, 110, Lane.Tom, 8) },
        ["T2"] = new() { ['X'] = new(48, 122, Lane.Tom, 8), ['x'] = new(48, 110, Lane.Tom, 8) },
        ["T3"] = new() { ['X'] = new(45, 122, Lane.Tom, 8), ['x'] = new(45, 110, Lane.Tom, 8) },
        ["T4"] = new() { ['X'] = new(43, 122, Lane.Tom, 8), ['x'] = new(43, 110, Lane.Tom, 8) },
    };

    /// <summary>GM note of a lane's plain hit (used for 'r' ramp hits): K 36, S 38, SS 37, CL 39, H 42, R 51, C 49, CH 52, SP 55, T1..T4 50/48/45/43.</summary>
    private static int LaneMidi(string lane) => lane switch
    {
        "K" => 36, "S" => 38, "SS" => 37, "CL" => 39, "H" => 42, "R" => 51, "C" => 49, "CH" => 52, "SP" => 55,
        "T1" => 50, "T2" => 48, "T3" => 45, "T4" => 43, _ => throw new ArgumentException($"Demo song: unknown drum lane '{lane}'."),
    };

    /// <summary>
    /// Writes drum lanes into one bar (plan 3.9 lane key). Each pattern has one character per slot and must be exactly the bar's slot
    /// count long (16 for 4/4, 14 for 7/8, 12 for 3/4, 8 for 2/4), else it throws naming the bar and lane. '.' = no hit.
    /// <para>Lanes and characters (centre velocity before humanisation): K 36 (X 120, x 110, d 100 double-bass, b 96, p 49);
    /// S 38 (X 127 + cell.Accent 2, x 122, f flam = a 0.5-slot grace 38 before the beat (55..70) + main 122, g 45 ghost, b 104);
    /// SS 37 (x 80); CL 39 (x 96); H (X 90, x 76, y 66 closed 42; o 102 open 46; p 70 pedal 44); R (X 94, x 80 bow 51; b 110 bell 53);
    /// C (X 120, x 108 crash 49; 2 120 crash 57; c 120 choke: the cell is cut to a 16th, rests after); CH 52 (X/x 118); SP 55 (x/X 104);
    /// T1..T4 50/48/45/43 (X 122, x 110). 'r' in ANY lane is a fill-ramp hit: all 'r' hits of this call, in time order (lanes together),
    /// ramp 90 → 120 (±3 jitter).</para>
    /// <para>Velocities go through <see cref="Hv"/> with <paramref name="sec"/> (metric accents on H, R and 'd' kicks; alternation on runs
    /// of adjacent H hits and 'd' kicks). Hits at the same slot share one cell (merged with hits already in the bar; an existing note of the
    /// same GM number is replaced). Afterwards every non-roll cell lasts until the next onset in the bar (unrepresentable gaps are completed
    /// with rests), so Grid, Fill and Roll can be combined on one bar in any order. Voice 1 only.</para>
    /// </summary>
    internal static void Grid(TrackModel drums, int bar, Section sec, params (string lane, string pattern)[] lanes)
    {
        var m = drums.Measures[bar - 1];
        EnsureSlots(m);
        var n = SlotsOf(m);
        var hits = new List<(int Slot, int Order, string Lane, char Ch, int Run)>();
        for (var li = 0; li < lanes.Length; li++)
        {
            var (lane, pattern) = lanes[li];
            if (!Lanes.ContainsKey(lane)) throw new ArgumentException($"Demo song: drums bar {bar}: unknown lane '{lane}'.");
            if (pattern.Length != n)
                throw new InvalidOperationException($"Demo song: drums bar {bar} lane {lane}: pattern '{pattern}' has {pattern.Length} slots, the bar has {n}.");
            for (var i = 0; i < n; i++)
            {
                var ch = pattern[i];
                if (ch == '.') continue;
                if (ch != 'r' && !Lanes[lane].ContainsKey(ch)) throw new ArgumentException($"Demo song: drums bar {bar} lane {lane}: unknown hit '{ch}'.");
                var run = -1;
                if ((lane == "H" && ch != 'r') || (lane == "K" && ch == 'd'))
                {
                    var start = i;
                    while (start > 0 && pattern[start - 1] != '.' && (lane == "H" || pattern[start - 1] == 'd')) start--;
                    var end = i;
                    while (end < n - 1 && pattern[end + 1] != '.' && (lane == "H" || pattern[end + 1] == 'd')) end++;
                    if (end > start) run = i - start;
                }
                hits.Add((i, li, lane, ch, run));
            }
        }
        var ramp = hits.Where(h => h.Ch == 'r').OrderBy(h => h.Slot).ThenBy(h => h.Order).ToList();
        foreach (var h in hits.OrderBy(h => h.Slot).ThenBy(h => h.Order))
        {
            if (h.Ch == 'r')
            {
                var k = ramp.IndexOf(h);
                var centre = ramp.Count == 1 ? 120 : (int)Math.Round(90 + 30.0 * k / (ramp.Count - 1));
                var midi = LaneMidi(h.Lane);
                AddDrumHit(drums, m, bar, h.Slot, midi, Hv(drums.Name, bar, h.Slot, midi, centre, 3, sec, Lane.FillRamp), ghost: false, flam: false, sec);
                continue;
            }
            var dc = Lanes[h.Lane][h.Ch];
            var vel = Hv(drums.Name, bar, h.Slot, dc.Midi, dc.Centre, dc.Jitter, sec, dc.Lane, h.Run);
            var cell = AddDrumHit(drums, m, bar, h.Slot, dc.Midi, vel, dc.Ghost, dc.Flam, sec);
            if (dc.Accent2) cell.Accent = 2;
            if (dc.Choke) Choked.AddOrUpdate(cell, Mark);
        }
        Reduration(m);
    }

    /// <summary>Adds (or replaces) one drum note at an onset; a flam also gets its 0.5-slot grace 38.</summary>
    private static TabCell AddDrumHit(TrackModel drums, MeasureModel m, int bar, double slot, int midi, int vel, bool ghost, bool flam, Section sec)
    {
        var cell = Place(m, slot, 0, out var existed);
        if (!existed) SetValue(cell, 16, 0, 0, 0);
        cell.IsRest = false;
        AutoRest.Remove(cell);   // a hit on a rest the re-duration added: the cell is a real beat now (else Reduration drops it)
        cell.Notes.RemoveAll(x => !x.IsGraceNote && x.MidiValue == midi);
        cell.Notes.Add(Drum(midi, vel, ghost));
        if (flam && !cell.Notes.Any(x => x.IsGraceNote))
            Grace(cell, drums, 0, 38, before: true, "GraceBefore", 0.5, Hv(drums.Name, bar, slot, 1038, 62, 8, sec, Lane.Grace));
        return cell;
    }

    /// <summary>
    /// Writes a fill (plan 3.9 "Fill kinds") into a 4/4 bar, replacing what was there in the fill's range:
    /// P4 = S 16ths on slots 12–15 (the kick and snare on 12–15 are removed; hats/cymbals stay);
    /// P8 = slots 8–15 cleared, then S S T1 T1 T2 T2 T4 T4 (16ths);
    /// F16 = the whole bar: S 0–3, T1 4–7, T2 8–9, T3 10–11, T4 12–14, T4 'X' on 15, K on 0/4/8/12;
    /// Roll39 = the whole bar: S 16ths 0–7, 6 × 16th-triplets on beat 3, 8 × 32nds on beat 4, K on 0/4/8/12, ramp 90 → 127;
    /// F6 = slots 8–15 cleared, then 12 × 16(6:4): S T1 S T2 S T3 T1 T2 T3 T4 T4 T4;
    /// FTrip8 = the whole bar: 12 × 8th-triplets T1×3 T2×3 T3×3 T4×3 (last 'X' 122), K on every beat.
    /// Every fill hit ramps 90 → 120 in time order (Roll39 to 127) with ±3 jitter; kicks are 'x' (110) through Hv; the section comes from
    /// <see cref="SectionOf"/>. <paramref name="flam"/> makes the first snare of the fill a flam (P8 slot 8, F16 slot 0; ignored by the others).
    /// Combine with Grid in either order (a later Grid merges its hits into the fill's cells).
    /// </summary>
    internal static void Fill(TrackModel drums, int bar, FillKind kind, bool flam = false)
    {
        var m = drums.Measures[bar - 1];
        EnsureSlots(m);
        if (SlotsOf(m) != 16) throw new InvalidOperationException($"Demo song: drums bar {bar}: fill {kind} needs a 4/4 bar.");
        var sec = SectionOf(bar);
        // (slot, midi, den, tupN, tupD, rampHit, fixedVel)
        var hits = new List<(double Slot, int Midi, int Den, int TupN, int TupD, bool Ramp, int Vel)>();
        void Seq(double start, int den, int tn, int td, params int[] midis)
        {
            var dur = 16.0 / den * (tn > 0 ? td / (double)tn : 1);
            for (var k = 0; k < midis.Length; k++) hits.Add((start + k * dur, midis[k], den, tn, td, true, 0));
        }
        void Kicks(params int[] slots) { foreach (var s in slots) hits.Add((s, 36, 16, 0, 0, false, 110)); }
        const int S = 38, T1 = 50, T2 = 48, T3 = 45, T4 = 43;
        var rampTop = 120;
        switch (kind)
        {
            case FillKind.P4:
                Clear(m, 12, 16, x => x.MidiValue is 35 or 36 or 38);
                Seq(12, 16, 0, 0, S, S, S, S);
                break;
            case FillKind.P8:
                Clear(m, 8, 16, null);
                Seq(8, 16, 0, 0, S, S, T1, T1, T2, T2, T4, T4);
                break;
            case FillKind.F16:
                Clear(m, 0, 16, null);
                Seq(0, 16, 0, 0, S, S, S, S, T1, T1, T1, T1, T2, T2, T3, T3, T4, T4, T4);
                hits.Add((15, T4, 16, 0, 0, false, 122));
                Kicks(0, 4, 8, 12);
                break;
            case FillKind.Roll39:
                Clear(m, 0, 16, null);
                Seq(0, 16, 0, 0, S, S, S, S, S, S, S, S);
                Seq(8, 16, 3, 2, S, S, S, S, S, S);
                Seq(12, 32, 0, 0, S, S, S, S, S, S, S, S);
                Kicks(0, 4, 8, 12);
                rampTop = 127;
                break;
            case FillKind.F6:
                Clear(m, 8, 16, null);
                Seq(8, 16, 6, 4, S, T1, S, T2, S, T3, T1, T2, T3, T4, T4, T4);
                break;
            case FillKind.FTrip8:
                Clear(m, 0, 16, null);
                Seq(0, 8, 3, 2, T1, T1, T1, T2, T2, T2, T3, T3, T3, T4, T4, T4);
                hits[^1] = hits[^1] with { Ramp = false, Vel = 122 };
                Kicks(0, 4, 8, 12);
                break;
        }
        var ramp = hits.Where(h => h.Ramp).OrderBy(h => h.Slot).ToList();
        var flamDone = !flam || kind is not (FillKind.P8 or FillKind.F16);
        foreach (var h in hits.OrderBy(h => h.Slot))
        {
            int vel;
            if (h.Ramp)
            {
                var k = ramp.IndexOf(h);
                var centre = ramp.Count == 1 ? rampTop : (int)Math.Round(90 + (rampTop - 90.0) * k / (ramp.Count - 1));
                vel = Hv(drums.Name, bar, h.Slot, h.Midi, centre, 3, sec, Lane.FillRamp);
            }
            else vel = Hv(drums.Name, bar, h.Slot, h.Midi, h.Vel, h.Midi == 36 ? 4 : 8, sec, h.Midi == 36 ? Lane.Kick : Lane.Tom);
            var isFlam = !flamDone && h.Midi == S;
            if (isFlam) flamDone = true;
            var cell = AddDrumHit(drums, m, bar, h.Slot, h.Midi, vel, ghost: false, flam: isFlam, sec);
            if (h.TupN > 0 || h.Den > 16) { SetValue(cell, h.Den, 0, h.TupN, h.TupD); FixedValue.AddOrUpdate(cell, Mark); }
        }
        Reduration(m);
    }

    /// <summary>
    /// A roll of <paramref name="count"/> hits of GM note <paramref name="midi"/> from <paramref name="startSlot"/>, each written as
    /// 1/<paramref name="den"/> in a <paramref name="tupN"/>:<paramref name="tupD"/> tuplet (0:0 = none): e.g. (8, 12, 6, 4, 16, 42, 60, 90)
    /// = 6 × 16(6:4) hats on beat 3 ramping 60 → 90; (15, 4, 0, 0, 64, 42, ...) = four 64ths on 15, 15.25, 15.5, 15.75.
    /// Velocity ramps linearly <paramref name="v0"/> → <paramref name="v1"/> ("as written": no metric accent or section contour) with ±3 jitter
    /// from <see cref="Hv"/>, plus the ±3 alternation for hi-hat rolls (42/44/46). Hits merge into cells already at those onsets (the cell takes
    /// the roll's written value); roll cells keep their value when Grid/Fill re-time the bar. Change a hit afterwards through the cell
    /// (e.g. the last 64th to an open hat).
    /// </summary>
    internal static void Roll(TrackModel drums, int bar, double startSlot, int count, int tupN, int tupD, int den, int midi, int v0, int v1)
    {
        var m = drums.Measures[bar - 1];
        EnsureSlots(m);
        var sec = SectionOf(bar);
        var dur = 16.0 / den * (tupN > 0 && tupD > 0 ? tupD / (double)tupN : 1);
        var end = Snap(startSlot + count * dur);
        if (end > SlotsOf(m) + 1e-6) throw new InvalidOperationException($"Demo song: drums bar {bar}: the roll ends at slot {end:0.###}, past the bar ({SlotsOf(m)}).");
        var hat = midi is 42 or 44 or 46;
        for (var k = 0; k < count; k++)
        {
            var slot = Snap(startSlot + k * dur);
            var centre = count == 1 ? v1 : (int)Math.Round(v0 + (v1 - v0) * (double)k / (count - 1));
            var vel = Hv(drums.Name, bar, slot, midi, centre, 3, sec, Lane.Roll, hat ? k : -1);
            var cell = AddDrumHit(drums, m, bar, slot, midi, vel, ghost: false, flam: false, sec);
            SetValue(cell, den, 0, tupN, tupD);
            FixedValue.AddOrUpdate(cell, Mark);
        }
        Reduration(m);
    }

    /// <summary>A drum note: StringIndex = GuitarProImporter.DrumLine(midi), Fret = MidiValue = midi (plan B35).</summary>
    internal static TabNote Drum(int midi, int vel, bool ghost = false) =>
        new() { StringIndex = GuitarProImporter.DrumLine(midi), Fret = midi, MidiValue = midi, Velocity = Math.Clamp(vel, 1, MidiLimits.Max), Ghost = ghost };

    /// <summary>Removes notes (all, or those matching <paramref name="which"/>) from beats starting in [from, to); emptied beats are dropped.</summary>
    private static void Clear(MeasureModel m, double from, double to, Func<TabNote, bool>? which)
    {
        var cells = m.Cells;
        var beats = Beats(cells);
        var keep = new List<(double, TabCell)>();
        foreach (var (index, onset) in beats)
        {
            var c = cells[index];
            if (onset >= from - 1e-9 && onset < to - 1e-9)
            {
                var removed = c.Notes.RemoveAll(x => !x.IsGraceNote && (which is null || which(x)));
                if (removed > 0 && c.Notes.All(x => x.IsGraceNote)) c.Notes.Clear();
                if (c.Notes.Count == 0 && !c.HasAnnotation && c.Mix is null) continue;
            }
            keep.Add((onset, c));
        }
        Relayout(cells, keep);
    }

    private static readonly IReadOnlyList<(double Slots, int Den, int Dots)> NoteValues = new (double Slots, int Den, int Dots)[]
    {
        (16, 1, 0), (14, 2, 2), (12, 2, 1), (8, 2, 0), (7, 4, 2), (6, 4, 1), (4, 4, 0), (3.5, 8, 2), (3, 8, 1), (2, 8, 0),
        (1.75, 16, 2), (1.5, 16, 1), (1, 16, 0), (0.75, 32, 1), (0.5, 32, 0), (0.375, 64, 1), (0.25, 64, 0),
    };

    /// <summary>Drum bar (voice 1): each non-roll hit lasts until the next onset (a choke: at most a 16th); gaps are completed with rests.</summary>
    private static void Reduration(MeasureModel m)
    {
        var cells = m.Cells;
        var n = SlotsOf(m);
        var beats = Beats(cells).Where(b => !AutoRest.TryGetValue(cells[b.Index], out _)).Select(b => (b.Onset, Cell: cells[b.Index])).ToList();
        if (beats.Count == 0) { Relayout(cells, beats); return; }
        var order = new List<(double Onset, TabCell Cell)>();
        void Rests(double from, double to)
        {
            while (to - from > 0.2)
            {
                var v = NoteValues.First(x => x.Slots <= to - from + 1e-9);
                var r = new TabCell { IsRest = true };
                SetValue(r, v.Den, v.Dots, 0, 0);
                AutoRest.AddOrUpdate(r, Mark);
                order.Add((Snap(from), r));
                from += v.Slots;
            }
        }
        Rests(0, beats[0].Onset);
        for (var k = 0; k < beats.Count; k++)
        {
            var (onset, cell) = beats[k];
            var next = k + 1 < beats.Count ? beats[k + 1].Onset : n;
            order.Add((onset, cell));
            double end;
            if (FixedValue.TryGetValue(cell, out _) || cell.Tuplet.Numerator > 0) end = onset + MusicTime.CellSlots(cell);
            else
            {
                var gap = next - onset;
                if (Choked.TryGetValue(cell, out _)) gap = Math.Min(gap, 1);
                var v = NoteValues.FirstOrDefault(x => x.Slots <= gap + 1e-9);
                if (v.Den == 0) v = NoteValues[^1];
                SetValue(cell, v.Den, v.Dots, 0, 0);
                end = onset + v.Slots;
            }
            Rests(end, next);
        }
        Relayout(cells, order.OrderBy(o => o.Onset).ToList());
    }
}
