using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

// Owns: the note helpers of the full demo song: the cell and beat placement (Put, Beats, Fret, Harm, Bend, Whammy, Grace, Hopo, Key and Keys)
//   and the seeded humanising of velocities (Hv).
// Does not own: the song's content, which the owner files FullDemoSongFactory.*.cs write, or the shared contract in FullDemoSongFactory.Helpers.cs.
// Tests: no dedicated test class.

internal static partial class FullDemoSongFactory
{
    // ------------------------------------------------------------------ cells and notes

    /// <summary>Cells handed out by Put that the caller may not have filled yet: they count as beats while building.</summary>
    private static readonly ConditionalWeakTable<TabCell, object> Claimed = new();
    /// <summary>Drum cells whose written value is fixed (Roll and tuplet hits): the drum re-duration keeps them.</summary>
    private static readonly ConditionalWeakTable<TabCell, object> FixedValue = new();
    /// <summary>Rests the drum re-duration added (removed and recomputed on the next pass).</summary>
    private static readonly ConditionalWeakTable<TabCell, object> AutoRest = new();
    /// <summary>Drum cells cut to a 16th ('c' choke).</summary>
    private static readonly ConditionalWeakTable<TabCell, object> Choked = new();
    /// <summary>HOPO origins already paired by Hopo(): finalise does not link them again.</summary>
    private static readonly ConditionalWeakTable<TabNote, object> PairedOrigin = new();
    private static readonly object Mark = new();
    // Generator-only variant, deliberately not MusicTime.IsBeat: it also counts cells the generator has Claimed (reserved but still empty) so they are not reused.
    private static bool IsBeat(TabCell c) => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation || Claimed.TryGetValue(c, out _);

    /// <summary>Beat cells of a voice with their onsets (the MusicTime rule: RhythmicPosition ?? max(index, consumed)).</summary>
    internal static List<(int Index, double Onset)> Beats(List<TabCell> cells)
    {
        var list = new List<(int, double)>();
        double consumed = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (!IsBeat(c)) { if (consumed <= i) consumed = i + 1; continue; }
            var start = c.RhythmicPosition ?? Math.Max(i, consumed);
            list.Add((i, start));
            consumed = Math.Max(consumed, start + MusicTime.CellSlots(c));
        }
        return list;
    }

    private static double Snap(double x) => Math.Abs(x - Math.Round(x)) < 1e-7 ? Math.Round(x) : Math.Round(x, 9);

    private static bool IsWhole(double x) => Math.Abs(x - Math.Round(x)) < 1e-9;

    /// <summary>
    /// The cell of a new beat starting at <paramref name="slot"/> in voice <paramref name="voice"/> (0 or 1; voice 2 is created on
    /// demand), with its written value set (DurationDenominator, Dots, tuplet ratio; 3:2 also sets IsTriplet).
    /// <para>Cells stay in time order: a beat after the last one takes the cell at the slot index (or the next free index);
    /// RhythmicPosition is set when the slot is fractional or differs from the cell index (the same rule as SelfTestGpFixture.Put).
    /// A beat placed before existing beats re-lays the bar out (every beat keeps its onset). The bar grows when a tuplet run needs
    /// more cells than it has.</para>
    /// <para>When a beat already starts at <paramref name="slot"/> in that voice, that cell is returned and its value overwritten.</para>
    /// <para>The returned cell is empty: add notes, or set IsRest (unfilled cells become rests at finalise).</para>
    /// </summary>
    internal static TabCell Put(MeasureModel m, double slot, int den, int dots = 0, int tupN = 0, int tupD = 0, int voice = 0)
    {
        var cell = Place(m, slot, voice, out _);
        SetValue(cell, den, dots, tupN, tupD);
        return cell;
    }

    private static void SetValue(TabCell c, int den, int dots, int tupN, int tupD)
    {
        c.DurationDenominator = den; c.Dots = dots;
        c.TupletNumerator = tupN > 0 && tupD > 0 ? tupN : 0;
        c.TupletDenominator = tupN > 0 && tupD > 0 ? tupD : 0;
        c.IsTriplet = tupN == 3 && tupD == 2;
    }

    /// <summary>Finds or creates the beat cell at an onset (see <see cref="Put"/>); <paramref name="existed"/> tells which.</summary>
    private static TabCell Place(MeasureModel m, double slot, int voice, out bool existed)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        slot = Snap(slot);
        var cells = voice <= 0 ? m.Cells : m.CellsForVoice(1, create: true);
        var beats = Beats(cells);
        foreach (var (index, onset) in beats)
            if (Math.Abs(onset - slot) < 1e-6) { existed = true; return cells[index]; }
        existed = false;
        var fresh = new TabCell();
        Claimed.AddOrUpdate(fresh, Mark);
        if (beats.Count == 0 || beats[^1].Onset < slot)
        {
            var idx = Math.Max((int)Math.Floor(slot + 1e-9), beats.Count == 0 ? 0 : beats[^1].Index + 1);
            var lastEnd = beats.Count == 0 ? 0 : beats[^1].Onset + MusicTime.CellSlots(cells[beats[^1].Index]);
            while (cells.Count <= idx) cells.Add(new TabCell());
            // an unused cell may carry data the caller set by index (a mix or beam override): keep that object
            var target = cells[idx];
            if (!IsBeat(target) && target.Notes.Count == 0) { Claimed.AddOrUpdate(target, Mark); fresh = target; }
            else cells[idx] = fresh;
            fresh.RhythmicPosition = IsWhole(slot) && idx == (int)Math.Round(slot) && lastEnd <= slot + 1e-9 ? null : slot;
            return fresh;
        }
        var order = beats.Select(b => (b.Onset, Cell: cells[b.Index])).ToList();
        order.Add((slot, fresh));
        Relayout(cells, order.OrderBy(o => o.Onset).ToList());
        return fresh;
    }

    /// <summary>
    /// Rebuilds a voice's cell list from beats in time order: each beat at max(previous index + 1, floor(onset)). RhythmicPosition
    /// stays null only when the index is the onset and the previous beat has ended by then (else the onset rule would move it).
    /// </summary>
    private static void Relayout(List<TabCell> cells, List<(double Onset, TabCell Cell)> order)
    {
        var count = cells.Count;
        cells.Clear();
        var last = -1;
        var prevEnd = 0.0;
        foreach (var (onset, cell) in order)
        {
            var idx = Math.Max(last + 1, (int)Math.Floor(onset + 1e-9));
            while (cells.Count < idx) cells.Add(new TabCell());
            cells.Add(cell);
            cell.RhythmicPosition = IsWhole(onset) && idx == (int)Math.Round(onset) && prevEnd <= onset + 1e-9 ? null : Snap(onset);
            prevEnd = Math.Max(prevEnd, onset + MusicTime.CellSlots(cell));
            last = idx;
        }
        while (cells.Count < count) cells.Add(new TabCell());
    }

    /// <summary>A fretted note: string <paramref name="s"/> (1 = top), fret relative to the capo, MidiValue = t.PitchOf, the given velocity and technique names.</summary>
    internal static TabNote Fret(TrackModel t, int s, int fret, int vel, params string[] tech)
    {
        if (s < 1 || s > t.StringTunings.Count)
            throw new ArgumentOutOfRangeException(nameof(s), $"Demo song: track '{t.Name}' has {t.StringTunings.Count} strings, s{s} does not exist.");
        if (fret < 0 || fret > 127) throw new ArgumentOutOfRangeException(nameof(fret), $"Demo song: track '{t.Name}': fret {fret}.");
        var n = new TabNote { StringIndex = s - 1, Fret = fret, MidiValue = t.PitchOf(s - 1, fret), Velocity = Math.Clamp(vel, 1, MidiLimits.Max) };
        foreach (var x in tech) if (!string.IsNullOrEmpty(x)) n.Techniques.Add(x);
        return n;
    }

    /// <summary>
    /// A harmonic on string <paramref name="s"/> (1 = top) at <paramref name="fret"/>: kind is Natural, Artificial, Pinch, Tap, Semi or Feedback.
    /// HarmonicFret = <paramref name="harmonicFret"/>; MidiValue = GuitarProImporter.HarmonicMidi(kind, open string + capo, fret, harmonicFret),
    /// i.e. Natural sounds the string's harmonic at the written fret; Artificial/Pinch sound fret + the node read as an interval
    /// (5/7/19/24 as the fifth/double-octave nodes); Tap = node + 12 above the open string; Semi/Feedback = fret + ArtificialInterval(node).
    /// Adds the matching technique name (Harmonic, ArtificialHarmonic, PinchHarmonic, TapHarmonic, SemiHarmonic, FeedbackHarmonic).
    /// </summary>
    internal static TabNote Harm(TrackModel t, int s, int fret, string kind, double harmonicFret, int vel)
    {
        var tech = kind switch
        {
            "Natural" => "Harmonic", "Artificial" => "ArtificialHarmonic", "Pinch" => "PinchHarmonic",
            "Tap" => "TapHarmonic", "Semi" => "SemiHarmonic", "Feedback" => "FeedbackHarmonic",
            _ => throw new ArgumentException($"Demo song: unknown harmonic kind '{kind}'.", nameof(kind)),
        };
        var n = Fret(t, s, fret, vel, tech);
        n.HarmonicFret = harmonicFret;
        n.MidiValue = GuitarProImporter.HarmonicMidi(kind, t.StringTunings[s - 1] + Math.Max(0, t.Capo), fret, harmonicFret);
        return n;
    }

    /// <summary>
    /// Bend on a note: adds "Bend", sets BendTypeName = <paramref name="type"/> (Bend, Release, BendRelease, Prebend, PrebendBend,
    /// PrebendRelease, Hold, Custom; BendStyleName stays default) and BendPoints from (offset 0..60, value in quarter-tones; 4 = one tone).
    /// </summary>
    internal static void Bend(TabNote n, string type, params (int off, int val)[] pts)
    {
        n.Techniques.Add("Bend");
        n.BendTypeName = type;
        n.BendPoints = pts.Select(p => new BendPointModel { Offset = p.off, Value = p.val }).ToList();
    }

    /// <summary>
    /// Whammy: adds the tag <paramref name="subtype"/> to the note (TremBar, TremBarWide, TremBarDive, TremBarDip, TremBarHold,
    /// TremBarPredive, TremBarPrediveDive or TremBarCustom) and sets the BEAT curve cell.WhammyPoints (offset 0..60, quarter-tones;
    /// -24 = -12 semitones). The curve belongs to the whole cell, so use one whammy per cell.
    /// </summary>
    internal static void Whammy(TabCell c, TabNote n, string subtype, params (int off, int val)[] pts)
    {
        if (!subtype.StartsWith("TremBar", StringComparison.Ordinal)) throw new ArgumentException($"Demo song: '{subtype}' is not a whammy sub-type.", nameof(subtype));
        n.Techniques.Add(subtype);
        c.WhammyPoints = pts.Select(p => new BendPointModel { Offset = p.off, Value = p.val }).ToList();
    }

    /// <summary>
    /// Adds a grace note INTO cell <paramref name="c"/> (first in the cell, before the principal notes): string s (1 = top) and fret,
    /// IsGraceNote, GraceBeforeBeat = <paramref name="before"/>, GraceDurationSlots = <paramref name="slots"/>, the technique
    /// <paramref name="tag"/> ("GraceBefore", "GraceOnBeat" or "GraceBend"; add a slide or Bend() on the returned note yourself via
    /// c.Notes[0]). On a drum track <paramref name="s"/> is ignored and <paramref name="fret"/> is the GM note (a flam: 38).
    /// </summary>
    internal static void Grace(TabCell c, TrackModel t, int s, int fret, bool before, string tag, double slots = 0.5, int vel = 62)
    {
        var n = t.Kind == TrackKind.Drums ? Drum(fret, vel) : Fret(t, s, fret, vel);
        n.IsGraceNote = true;
        n.GraceBeforeBeat = before;
        n.GraceDurationSlots = slots;
        if (!string.IsNullOrEmpty(tag)) n.Techniques.Add(tag);
        c.Notes.Insert(0, n);
    }

    /// <summary>A hammer-on/pull-off pair (the importer's convention the renderer draws a slur for): both get "HOPO", the origin
    /// "HOPOOrigin" and the destination "HOPODestination". Use it for pairs on different strings; the riff flag <c>h</c> links same-string pairs.</summary>
    internal static void Hopo(TabNote origin, TabNote dest)
    {
        origin.Techniques.Add("HOPO"); origin.Techniques.Add("HOPOOrigin");
        dest.Techniques.Add("HOPO"); dest.Techniques.Add("HOPODestination");
        PairedOrigin.AddOrUpdate(origin, Mark);
    }

    /// <summary>A keys note of sounding pitch <paramref name="midi"/>: string = the highest octave "string" at or below the pitch
    /// (the importer's PlacePitch rule on {96,84,72,60,48,36,24}), fret = the semitones above it. Throws below the lowest string.</summary>
    internal static TabNote Key(TrackModel keys, int midi, int vel)
    {
        for (var s = 0; s < keys.StringTunings.Count; s++)
            if (midi >= keys.StringTunings[s])
                return new TabNote { StringIndex = s, Fret = midi - keys.StringTunings[s], MidiValue = midi, Velocity = Math.Clamp(vel, 1, MidiLimits.Max) };
        throw new ArgumentOutOfRangeException(nameof(midi), $"Demo song: track '{keys.Name}': pitch {midi} is below its lowest string.");
    }

    /// <summary>
    /// Chord-aware keys placement. TabForge itself has no chord rule (the importer's PlacePitch puts every note on the highest octave
    /// "string" at or below its pitch, so a chord inside one octave lands on ONE string and its numbers overprint). This gives each of
    /// the simultaneous pitches its own string: highest pitch first, each takes its natural string (as <see cref="Key"/>) or, when that
    /// or a higher string is already used, the next free lower string (fret = pitch - tuning, so MidiValue is unchanged and the
    /// top-to-bottom order of the rows follows the pitch). <paramref name="midis"/> may be in any order; the result is in the same order.
    /// Returns (StringIndex, Fret) per pitch. Throws when the chord runs out of strings.
    /// </summary>
    internal static (int StringIndex, int Fret)[] Keys(TrackModel keys, IReadOnlyList<int> midis)
    {
        var result = new (int, int)[midis.Count];
        var last = -1;
        foreach (var i in Enumerable.Range(0, midis.Count).OrderByDescending(i => midis[i]).ThenBy(i => i))
        {
            var s = 0;
            while (s < keys.StringTunings.Count && midis[i] < keys.StringTunings[s]) s++;
            if (s >= keys.StringTunings.Count)
                throw new ArgumentOutOfRangeException(nameof(midis), $"Demo song: track '{keys.Name}': pitch {midis[i]} is below its lowest string.");
            s = Math.Max(s, last + 1);
            if (s >= keys.StringTunings.Count)
                throw new InvalidOperationException($"Demo song: track '{keys.Name}': the chord [{string.Join(' ', midis)}] has more notes than free strings.");
            result[i] = (s, midis[i] - keys.StringTunings[s]);
            last = s;
        }
        return result;
    }

    /// <summary>
    /// Finalise step for keys tracks: every beat onset of a bar (both voices together) gets its notes spread over distinct strings with
    /// <see cref="Keys"/>; pitches (MidiValue) never change and single notes keep the <see cref="Key"/> placement.
    /// </summary>
    private static void SpreadKeys(TrackModel t)
    {
        if (t.Kind != TrackKind.Keys) return;
        static void Place(TrackModel t, List<TabNote> notes)
        {
            var placed = Keys(t, notes.Select(n => n.MidiValue).ToList());
            for (var i = 0; i < notes.Count; i++) (notes[i].StringIndex, notes[i].Fret) = placed[i];
        }
        foreach (var m in t.Measures)
        {
            var groups = new SortedDictionary<long, List<List<TabNote>>>();   // onset -> the note lists of each voice's cell
            foreach (var cells in new[] { m.Cells, m.Voice2Cells })
                foreach (var (index, onset) in Beats(cells))
                {
                    var notes = cells[index].Notes.Where(x => !x.IsGraceNote).ToList();
                    if (notes.Count == 0) continue;
                    var key = (long)Math.Round(onset * 1_000_000);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<List<TabNote>>();
                    list.Add(notes);
                }
            foreach (var voices in groups.Values)
            {
                var all = voices.SelectMany(v => v).ToList();
                if (all.Count < 2) continue;
                // Both hands at once when the 7 octave strings have room (a pitch can only use strings at or below it); else each hand alone.
                try { Place(t, all); }
                catch (InvalidOperationException) { foreach (var v in voices) if (v.Count > 1) Place(t, v); } // Not logged: demo song factory: fallback placement, no failure path
            }
        }
    }

    // ------------------------------------------------------------------ humanisation (plan 5.2)

    /// <summary>
    /// Deterministic "human" velocity: centre + metric + alternation + section contour + jitter, clamped.
    /// <list type="bullet">
    /// <item>metric (HiHat, Ride, KickRun only, on whole slots): slot 0 +6, slot 8 +3, odd slots −8;</item>
    /// <item>alternation when <paramref name="indexInRun"/> ≥ 0 (continuous 16ths, hat rolls): +3 on even, −3 on odd indices;</item>
    /// <item>contour (drum lanes except Ghost, Grace, FillRamp and Roll): IntroA −25; Pre +3 rising to +5 by the section's last bar;
    /// Chorus +2; Post +4; Interlude −20; Bridge +2; Breakdown: Kick/KickRun/China +6, HiHat −6; Solo2 +3; FinalChorus +4; Outro +3; else 0;</item>
    /// <item>jitter: a uniform integer in [−<paramref name="jitter"/>, +<paramref name="jitter"/>] from SplitMix64(FNV-1a 32 of
    /// "AshenMeridian|track|bar|round(slot×1000)|midiOrFret"): the same inputs always give the same value;</item>
    /// <item>clamp: Ghost 1..60, Grace 55..70, everything else 1..127.</item>
    /// </list>
    /// Chug and Pitched lanes get only the jitter. <paramref name="bar"/> is 1-based.
    /// </summary>
    internal static int Hv(string track, int bar, double slot, int midiOrFret, int centre, int jitter,
                           Section sec, Lane lane, int indexInRun = -1)
    {
        var v = centre;
        if ((lane is Lane.HiHat or Lane.Ride or Lane.KickRun) && IsWhole(slot))
        {
            var s = (int)Math.Round(slot);
            v += s == 0 ? 6 : s == 8 ? 3 : s % 2 == 1 ? -8 : 0;
        }
        if (indexInRun >= 0) v += indexInRun % 2 == 0 ? 3 : -3;
        if (lane is not (Lane.Ghost or Lane.Grace or Lane.FillRamp or Lane.Roll or Lane.Chug or Lane.Pitched))
            v += Contour(sec, lane, bar);
        if (jitter > 0)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"AshenMeridian|{track}|{bar}|{(int)Math.Round(slot * 1000)}|{midiOrFret}");
            var z = SplitMix64(Fnv1a32(key));
            v += (int)(z % (ulong)(2 * jitter + 1)) - jitter;
        }
        return lane switch
        {
            Lane.Ghost => Math.Clamp(v, 1, 60),
            Lane.Grace => Math.Clamp(v, 55, 70),
            _ => Math.Clamp(v, 1, MidiLimits.Max),
        };
    }

    private static int Contour(Section sec, Lane lane, int bar) => sec switch
    {
        Section.IntroA => -25,
        Section.Pre => 3 + (int)Math.Round(2.0 * (bar - (bar >= 63 ? 63 : 32)) / 7),
        Section.Chorus => 2,
        Section.Post => 4,
        Section.Interlude => -20,
        Section.Bridge => 2,
        Section.Breakdown => lane is Lane.Kick or Lane.KickRun or Lane.China ? 6 : lane == Lane.HiHat ? -6 : 0,
        Section.Solo2 => 3,
        Section.FinalChorus => 4,
        Section.Outro => 3,
        _ => 0,
    };

    private static uint Fnv1a32(string s)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619u; }
        return h;
    }

    private static ulong SplitMix64(ulong seed)
    {
        var z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
