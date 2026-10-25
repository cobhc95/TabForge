using System.Linq;
using TabForge.Models;
using TabForge.Services;
namespace TabForge.Playback;
// Owns: the pitch-wheel curves of a note (bend, vibrato, slide in and out, trem bar, whammy) and the wheel arithmetic.
// Does not own: the per-note output that calls it (ScoreToMidiCompiler.Techniques.cs) or the bar and cell walk (ScoreToMidiCompiler.cs).
// Tests: no dedicated test class.
internal sealed partial class ScoreToMidiCompiler
{
    /// <summary>How far the note's own curve (bend, vibrato, slide in or out, trem bar, whammy) moves the pitch wheel from its pitch, at most, in semitones.</summary>
    private static double WheelPeakSemitones(TabCell cell, TabNote note, HashSet<string> t)
    {
        var peak = 0.0;
        if (note.BendPoints.Count > 0) foreach (var point in BendCurve(note)) peak = Math.Max(peak, Math.Abs(point.Semitones));
        else if (t.Contains("Bend")) peak = 0.5;
        foreach (var point in cell.WhammyPoints) peak = Math.Max(peak, Math.Min(12.0, Math.Abs(point.Value / 2.0)));
        if (t.Contains("TremBar") || t.Contains("TremBarWide")) peak = Math.Max(peak, 2);
        if (t.Contains("SlideOutUp") || t.Contains("SlideOutDown")) peak = Math.Max(peak, SlideOutSemitones);
        if (t.Contains("SlideInBelow") || t.Contains("SlideInAbove")) peak = Math.Max(peak, SlideInSemitones);
        if (t.Contains("Vibrato") || t.Contains("WideVibrato")) peak = Math.Max(peak, t.Contains("WideVibrato") ? WideVibratoSemitones : NormalVibratoSemitones);
        return peak;
    }


    /// <summary>
    /// Combines compatible note pitch effects into one pitch-wheel stream. MIDI has one wheel per
    /// channel, so independent bend/slide/vibrato streams must be summed rather than sent as
    /// competing events that overwrite each other.
    /// </summary>
    /// <param name="baseSemitones">Offset the string already sounds at (a merged note after a slide); added to every wheel value.</param>
    /// <param name="joinIndex">Index of the wheel reset that ends the tied origin's curve: this curve starts from it (moved to this note's onset) instead of adding a second event.</param>
    /// <returns>Index of the wheel reset that ends this curve, or -1 when no wheel events were written.</returns>
    private int EmitPitchExpression(TabNote note, TabCell cell, HashSet<string> techniques, int midi, int? slideTarget,
        double onset, double length, int device, int channel, int trackIndex, double baseSemitones = 0, int joinIndex = -1)
    {
        var vibrato = techniques.Contains("Vibrato") || techniques.Contains("WideVibrato");
        var genericBend = techniques.Contains("Bend") && note.BendPoints.Count == 0;
        var tremBar = techniques.Contains("TremBar") || techniques.Contains("TremBarWide");
        var whammyPoints = cell.WhammyPoints
            .Select(point => (Fraction: Math.Clamp(point.Offset <= 1.0 ? point.Offset : point.Offset / 60.0, 0, 1),
                Semitones: Math.Clamp(point.Value / 2.0, -12.0, 12.0)))
            .OrderBy(point => point.Fraction).ToArray();
        var slideIn = techniques.Contains("SlideInBelow") || techniques.Contains("SlideInAbove");
        var slideOut = techniques.Contains("SlideOutUp") || techniques.Contains("SlideOutDown");
        if (!vibrato && !genericBend && !tremBar && !slideIn && !slideOut &&
            whammyPoints.Length == 0 && note.BendPoints.Count == 0 && slideTarget is null) return -1;

        var duration = Math.Max(0, length);
        var end = onset + duration;
        var bendPoints = BendCurve(note);
        var times = CurveTimes(onset, duration, bendPoints, whammyPoints, slideTarget is not null, genericBend, tremBar, slideIn, slideOut);

        var resetIndex = -1;
        var vibratoStart = onset + Math.Min(55, duration * 0.12);
        var vibratoDepth = techniques.Contains("WideVibrato") ? WideVibratoSemitones : NormalVibratoSemitones;
        var slideInDuration = _slideInLeadMs > 0 ? _slideInLeadMs : Math.Max(1, Math.Min(120, duration * 0.25));
        // The reference slides in from ~3 frets, falls/rises ~5, progressively from 25% of the note

        foreach (var at in times)
        {
            var fraction = duration <= 0 ? 1 : Math.Clamp((at - onset) / duration, 0, 1);
            var semitones = bendPoints.Length > 0
                ? BendAt(bendPoints, fraction)
                : genericBend ? GenericBendAt(fraction) : 0;
            if (whammyPoints.Length > 0) semitones += BendAt(whammyPoints, fraction);

            // A target slide, a fretted bend and a separate slide ornament can coexist; each is
            // expressed as a delta around the note's imported sounding pitch.
            if (slideTarget is int target && fraction < 1)
                semitones += (target - midi) * Math.Clamp((fraction - SlideStart) / (SlideEnd - SlideStart), 0, 1);
            if (tremBar)
            {
                if (whammyPoints.Length == 0)
                    semitones += TremBarAt(techniques, fraction);
            }
            semitones += SlideOrnamentSemitones(techniques, at - onset, fraction, slideInDuration);
            if (vibrato && at < end) semitones += VibratoSemitones(at, end, vibratoStart, vibratoDepth);

            semitones += baseSemitones;
            // All generated curves return to the unbent sounding pitch, even when the imported
            // bend holds its peak through the written note end.
            if (Math.Abs(at - end) < 0.001) semitones = 0;
            var written = WriteCurveWheel(PitchWheelForSemitones(semitones), at, onset, joinIndex, device, channel, trackIndex);
            if (Math.Abs(at - end) < 0.001) resetIndex = written;
        }
        return resetIndex;
    }

    // Standard slide: hold the fretted pitch, then move to the target in the last part of the note.
    private const double SlideStart = 0.55, SlideEnd = 0.95;

    /// <summary>The moments a curve gets a wheel value: the ends, every vibrato sample and the corners of each bend, whammy, slide and trem bar shape.</summary>
    private static SortedSet<double> CurveTimes(double onset, double duration, (double Fraction, double Semitones)[] bendPoints, (double Fraction, double Semitones)[] whammyPoints,
        bool hasSlideTarget, bool genericBend, bool tremBar, bool slideIn, bool slideOut)
    {
        var end = onset + duration;
        var times = new SortedSet<double> { onset, end };
        for (var at = onset + VibratoSampleMs; at < end; at += VibratoSampleMs) times.Add(at);
        foreach (var point in bendPoints) times.Add(onset + duration * point.Fraction);
        foreach (var point in whammyPoints) times.Add(onset + duration * point.Fraction);
        if (hasSlideTarget) { times.Add(onset + duration * SlideStart); times.Add(onset + duration * SlideEnd); }
        if (genericBend) { times.Add(onset + duration * 0.35); times.Add(onset + duration * 0.9); }
        if (tremBar) times.Add(onset + duration * 0.5);
        if (slideIn) times.Add(onset + Math.Min(120, duration * 0.25));
        if (slideOut) { times.Add(onset + duration * SlideOutStart); times.Add(onset + duration * 0.97); }
        return times;
    }

    /// <summary>The slide in and slide out ornaments at <paramref name="sinceOnset"/> ms into the note.</summary>
    private static double SlideOrnamentSemitones(HashSet<string> techniques, double sinceOnset, double fraction, double slideInDuration)
    {
        var semitones = 0.0;
        if (techniques.Contains("SlideInBelow"))
            semitones -= SlideInSemitones * Math.Max(0, 1 - sinceOnset / slideInDuration);
        if (techniques.Contains("SlideInAbove"))
            semitones += SlideInSemitones * Math.Max(0, 1 - sinceOnset / slideInDuration);
        if (techniques.Contains("SlideOutUp"))
            semitones += SlideOutSemitones * Math.Clamp((fraction - SlideOutStart) / (1 - SlideOutStart), 0, 1);
        if (techniques.Contains("SlideOutDown"))
            semitones -= SlideOutSemitones * Math.Clamp((fraction - SlideOutStart) / (1 - SlideOutStart), 0, 1);
        return semitones;
    }

    private static double VibratoSemitones(double at, double end, double vibratoStart, double vibratoDepth)
    {
        var attack = Math.Clamp((at - vibratoStart) / VibratoFadeMs, 0, 1);
        var release = Math.Clamp((end - at) / VibratoFadeMs, 0, 1);
        return Math.Sin(2 * Math.PI * VibratoRateHz * (at - vibratoStart) / 1000.0) *
               vibratoDepth * Math.Min(attack, release);
    }

    /// <summary>Writes one wheel value of a curve (or moves the tied origin's reset onto it at the join); returns the index of the event written, -1 while chasing.</summary>
    private int WriteCurveWheel(int wheel, double at, double onset, int joinIndex, int device, int channel, int trackIndex)
    {
        var written = _chasing ? -1 : _timeline.Events.Count;
        if (at == onset && joinIndex >= 0 && joinIndex < _timeline.Events.Count && _timeline.Events[joinIndex] is { } join &&
            join.Status == (0xE0 | (channel & 0x0F)))
        {
            join.TimeMs = onset;   // a cut origin (palm mute, staccato) or a grace-shifted onset leaves its reset off the join: move it here, no blip
            join.Data1 = wheel & 0x7F;   // the bend carries on through the tie: no reset to centre between the links
            join.Data2 = (wheel >> 7) & 0x7F;
            written = joinIndex;
        }
        else AddPitchWheel(device, channel, wheel, at, trackIndex);
        return written;
    }

    /// <summary>
    /// The note's bend as (fraction of the note, semitones) points. the standard plain bend (0 at the start to the
    /// target at the end of the curve) reaches its target by the middle of the note and holds it, so it does not
    /// sound late and sluggish; curves the file draws itself (release, pre-bend, custom) keep their own points.
    /// </summary>
    internal static (double Fraction, double Semitones)[] BendCurve(TabNote note)
    {
        var points = note.BendPoints
            .Select(point => (Fraction: Math.Clamp(point.Offset <= 1.0 ? point.Offset : point.Offset / 60.0, 0, 1),
                Semitones: Math.Clamp(point.Value / 2.0, -12.0, 12.0)))
            .OrderBy(point => point.Fraction).ToArray();
        if (note.BendTypeName == "Bend" && points.Length == 2 && points[0].Fraction <= 0 && points[0].Semitones == 0 &&
            points[1].Fraction >= 0.99)
            points[1].Fraction = BendReachedAt;
        return points;
    }

    private const double SlideInSemitones = 3, SlideOutSemitones = 5;   // the reference slides in from ~3 frets, falls/rises ~5
    private const double BendReachedAt = 0.5;
    private const double SlideOutStart = 0.25;

    private static double BendAt((double Fraction, double Semitones)[] points, double fraction)
    {
        if (fraction < points[0].Fraction) return points[0].Fraction <= 0 ? points[0].Semitones : 0;
        for (var index = 1; index < points.Length; index++)
        {
            if (fraction > points[index].Fraction) continue;
            var previous = points[index - 1];
            var current = points[index];
            var span = current.Fraction - previous.Fraction;
            var position = span <= 0 ? 1 : (fraction - previous.Fraction) / span;
            return previous.Semitones + (current.Semitones - previous.Semitones) * position;
        }
        return points[^1].Semitones;
    }

    private static double GenericBendAt(double fraction) => fraction switch
    {
        < 0.35 => 0,
        < 0.9 => 0.5 * (1 - (fraction - 0.35) / 0.55),
        _ => 0
    };

    private static double TremBarAt(HashSet<string> techniques, double fraction)
    {
        if (techniques.Contains("TremBarDive")) return -2 * fraction;
        if (techniques.Contains("TremBarDip")) return -2 * (1 - Math.Abs(2 * fraction - 1));
        if (techniques.Contains("TremBarHold")) return -2 * Math.Min(1, fraction * 4);
        if (techniques.Contains("TremBarPredive") || techniques.Contains("TremBarPrediveDive"))
            return -2 * Math.Min(1, 0.5 + fraction * 2);
        var depth = techniques.Contains("TremBarWide") ? 1.0 : 0.5;
        return fraction < 0.5 ? -depth * (1 - fraction * 2) : 0;
    }

    private static int PitchWheelForSemitones(double semitones)
    {
        var unitsPerSemitone = 8192.0 / PitchBendRangeSemitones;
        return Math.Clamp(8192 + (int)Math.Round(semitones * unitsPerSemitone), 0, 16383);
    }

    private void AddPitchWheel(int device, int channel, int wheel, double atMs, int trackIndex) =>
        Add(device, channel, 0xE0, wheel & 0x7F, (wheel >> 7) & 0x7F, atMs, trackIndex);
}
