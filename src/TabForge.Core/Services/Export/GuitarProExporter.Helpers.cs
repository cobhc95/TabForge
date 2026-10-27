using AlphaTab.Model;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the pure helpers of the Guitar Pro exporter (clef, transposed positions, bend/whammy simplification, durations, rests).
// Does not own: the score build (GuitarProExporter.cs).
// Tests: no dedicated test class.
public static partial class GuitarProExporter
{

    /// <summary>
    /// The Guitar Pro clef for a TabForge bar clef (a bar's clef can change anywhere in a track): F4 for bass, C3 for alto, C4 for tenor
    /// and G2 for everything else (the guitar clef is G2 with an 8vb mark). The importer reads the clef and its octave mark back
    /// (G2 + 8vb is the guitar clef, plain G2 the treble clef), so a clef is stable over any number of round trips.
    /// </summary>
    internal static (Clef Clef, Ottavia Ottava) GpClef(string? clef, bool bassTrack)
    {
        var value = (clef ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (value.Length == 0 || value == "g8" || value.StartsWith("guitar", StringComparison.Ordinal)) return (Clef.G2, Ottavia._8vb);   // TabForge's guitar clef: G2 written an octave down
        if (value.Contains("bass", StringComparison.Ordinal) || value[0] == 'f') return (Clef.F4, bassTrack && value == "f4" ? Ottavia._8vb : Ottavia.Regular);
        if (value.Contains("tenor", StringComparison.Ordinal) || value == "c4") return (Clef.C4, Ottavia.Regular);
        if (value.Contains("alto", StringComparison.Ordinal) || value[0] == 'c') return (Clef.C3, Ottavia.Regular);
        if (value == "neutral") return (Clef.Neutral, Ottavia.Regular);
        return (Clef.G2, Ottavia.Regular);
    }

    /// <summary>True when a guitar or bass track's transpose is written as a shifted tuning (every string, frets unchanged): the shifted
    /// strings must stay between C0 (12) and 127. Otherwise (keys, extreme shifts) the notes are moved on string + fret instead.</summary>
    internal static bool TuningCarriesTranspose(TrackModel source, int semitones) =>
        semitones != 0 && source.Kind is TrackKind.Guitar or TrackKind.Bass && source.MidiChannel != 9 && source.StringTunings.Count > 0
        && source.StringTunings.Min() + semitones >= 12 && source.StringTunings.Max() + semitones <= MidiLimits.Max;

    /// <summary>The string and fret that sound <paramref name="semitones"/> above the given position: the same string when the fret stays
    /// at or above 0, otherwise the string that reaches the pitch with the lowest fret (a pitch below the lowest open string stays
    /// on it at fret 0).</summary>
    internal static (int StringIndex, int Fret) TransposedPosition(TrackModel source, int stringIndex, int fret, int semitones)
    {
        if (source.StringTunings.Count == 0) return (stringIndex, Math.Max(0, fret + semitones));
        stringIndex = Math.Clamp(stringIndex, 0, source.StringTunings.Count - 1);
        var target = source.PitchOf(stringIndex, fret) + semitones;
        if (source.FretOf(stringIndex, target) >= 0) return (stringIndex, source.FretOf(stringIndex, target));
        var best = -1;
        for (var s = 0; s < source.StringTunings.Count; s++)
            if (source.FretOf(s, target) >= 0 && (best < 0 || source.FretOf(s, target) < source.FretOf(best, target))) best = s;
        return best < 0 ? (stringIndex, 0) : (best, source.FretOf(best, target));
    }

    /// <summary>
    /// Guitar Pro 7 stores a bend as origin / middle / destination only. A multi-point (GP3-5 style) bend that
    /// alphaTab classifies as Custom is dropped by its writer, so reduce it to the nearest representable
    /// shape: prebend (release), or bend (release) with the highest point kept as the middle. Bends alphaTab
    /// already understands are written unchanged.
    /// </summary>
    internal static List<(double Offset, double Value)> SimplifyBend(List<(double Offset, double Value)> points, Dictionary<string, bool>? customByCurve = null)
    {
        if (points.Count < 2) return points;
        var key = string.Join(";", points.Select(p => p.Offset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + p.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        var custom = false;
        if (customByCurve is null || !customByCurve.TryGetValue(key, out custom))
        {
            custom = AlphaTabBoundary.WithModelLock(() =>   // a Note takes its id from a static counter (see ModelLock)
            {
                var probe = new Note();
                foreach (var (offset, value) in points) probe.AddBendPoint(new BendPoint(offset, value));
                return probe.BendType == BendType.Custom;
            });
            if (customByCurve is not null) customByCurve[key] = custom;   // the caller's own memo (one preflight run), so repeated curves probe once
        }
        if (!custom) return points;
        var first = Math.Max(0, points[0].Value);
        var last = Math.Max(0, points[^1].Value);
        var peakPoint = points.MaxBy(p => p.Value);
        var peak = Math.Max(first, Math.Max(last, peakPoint.Value));
        if (first > 0)
            return last < first
                ? new() { (0, first), (60, last) }
                : new() { (0, first), (60, peak) };
        if (last < peak)
            return new() { (0, 0), (Math.Clamp(peakPoint.Offset, 10, 50), peak), (60, last) };
        return new() { (0, 0), (60, peak) };
    }

    /// <summary>
    /// alphaTab's Guitar Pro 7 writer keeps a whammy curve of up to four points (dive, dip, hold, predive...)
    /// and drops anything longer without a trace. A longer curve is reduced to its start, its deepest or
    /// highest interior point and its end, which every reader understands as a dip.
    /// </summary>
    internal static List<(double Offset, double Value)> SimplifyWhammy(List<(double Offset, double Value)> points)
    {
        if (points.Count <= 4) return points;
        // Keep the interior peak above both ends and the interior dip below both ends (in time order), so a curve that rises
        // and then dives keeps both extremes; a plain dip or rise keeps its single extreme (the deepest/highest point).
        var inner = points.Skip(1).Take(points.Count - 2).ToList();
        var high = inner.MaxBy(p => p.Value); var low = inner.MinBy(p => p.Value);
        var ends = new[] { points[0].Value, points[^1].Value };
        var extremes = new List<(double Offset, double Value)>();
        if (high.Value > ends.Max()) extremes.Add(high);
        if (low.Value < ends.Min()) extremes.Add(low);
        if (extremes.Count == 0) extremes.Add(inner.MaxBy(p => Math.Abs(p.Value)));
        var kept = extremes.OrderBy(p => p.Offset).Select(p => (Math.Clamp(p.Offset, 10, 50), p.Value)).ToList();
        if (kept.Count == 2 && kept[0].Item1 >= kept[1].Item1)
        {
            var first = Math.Min(kept[0].Item1, 49);   // both clamped to 50 would share an offset
            kept[0] = (first, kept[0].Value); kept[1] = (first + 1, kept[1].Value);
        }
        return new List<(double, double)> { points[0] }.Concat(kept).Append(points[^1]).ToList();
    }

    /// <summary>The model's finger number (0 thumb, 1 index, 2 middle, 3 ring, 4 little; null = not written) as alphaTab's Fingers value.</summary>
    internal static Fingers FingerFor(int? finger) => finger switch
    {
        0 => Fingers.Thumb, 1 => Fingers.IndexFinger, 2 => Fingers.MiddleFinger, 3 => Fingers.AnnularFinger, 4 => Fingers.LittleFinger, _ => Fingers.Unknown,
    };

    private static Duration DurationOf(int denominator) => denominator switch
    {
        1 => Duration.Whole, 2 => Duration.Half, 4 => Duration.Quarter, 8 => Duration.Eighth,
        16 => Duration.Sixteenth, 32 => Duration.ThirtySecond, _ => Duration.SixtyFourth,
    };

    private static Beat RestBeat(int denominator) => new() { Duration = DurationOf(denominator), IsEmpty = false };

    // Largest-first rests filling a gap given in sixteenth slots.
    private static IEnumerable<Beat> FillRests(double slots)
    {
        foreach (var (size, den) in new[] { (16.0, 1), (8.0, 2), (4.0, 4), (2.0, 8), (1.0, 16), (0.5, 32), (0.25, 64) })
            while (slots >= size - 0.001) { yield return RestBeat(den); slots -= size; }
    }

    private static TabForge.Models.Rgba? ColourChooserParse(string? hex) =>
        TabForge.Models.ColourHex.TryParseStrict(hex, out var c) ? c : null;
}
