using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AlphaTab.Model;

namespace TabForge.Services;

// Owns: how a bend curve is written to and read from a clean .gp file.
// Does not own: the notation drawing of bends and bend playback.
// Tests: TestGpFidelity.
/// <summary>
/// How a bend curve is written to a clean .gp file. The format keeps a bend as four fields (origin, a flat middle stretch between two offsets,
/// destination), but alphaTab's writer always puts the middle stretch at the midpoint (a plain bend) or at the peak (bend and release) and ignores
/// the second offset, so a quick rise that then holds came back as a slow whole-note ramp. This picks the best four-field fit for a curve of three or
/// more points, and after alphaTab has written the file the fit is written into the bend fields of exactly those notes.
/// </summary>
internal static class GuitarProBendCurve
{
    /// <summary>The seven gpif bend fields, in the file's units (value 100 = 4 bend units, offset 100 = the end of the note), and the largest
    /// distance (in bend units) between the original curve and the one these fields draw.</summary>
    internal readonly record struct Fields(double OriginOffset, double OriginValue, double Middle1Offset, double Middle2Offset, double MiddleValue,
        double DestinationOffset, double DestinationValue, double MaxError);

    /// <summary>A curve that differs from its written form by no more than this (bend units) counts as held exactly.</summary>
    internal const double Tolerance = 0.05;

    private const double ValueUnit = 25.0;
    private const double OffsetUnit = 100.0 / 60.0;
    private static readonly string[] Names = { "BendOriginOffset", "BendOriginValue", "BendMiddleOffset1", "BendMiddleOffset2", "BendMiddleValue", "BendDestinationOffset", "BendDestinationValue" };

    private static readonly ConditionalWeakTable<Note, StrongBox<Fields>> Planned = new();

    /// <summary>The curve as (offset 0..60, value) points in time order; a model offset up to 1 is a fraction of the note.</summary>
    internal static List<(double Offset, double Value)> Normalise(IEnumerable<(double Offset, double Value)> points) =>
        points.Select(p => (p.Offset <= 1.0 ? p.Offset * 60 : p.Offset, p.Value)).OrderBy(p => p.Item1).ToList();

    /// <summary>Interpolated value of a point list (zero-width steps allowed) at an offset.</summary>
    private static double At(IReadOnlyList<(double Offset, double Value)> p, double x)
    {
        if (x <= p[0].Offset) return p[0].Value;
        for (var i = 1; i < p.Count; i++)
            if (x <= p[i].Offset && p[i].Offset > p[i - 1].Offset)
                return p[i - 1].Value + (p[i].Value - p[i - 1].Value) * (x - p[i - 1].Offset) / (p[i].Offset - p[i - 1].Offset);
        return p[^1].Value;
    }

    /// <summary>
    /// The best four-field fit of a curve of three to twelve points (origin and destination are the first and last point; the middle stretch is the
    /// value and the two offsets, taken from the curve's own points, that leave the smallest largest error), or null when the curve is not one this
    /// handles (fewer points, more than twelve, repeated offsets, negative values), in which case alphaTab's own simplified form stays.
    /// </summary>
    internal static Fields? Fit(IReadOnlyList<(double Offset, double Value)> points)
    {
        if (points.Count < 3 || points.Count > 12) return null;
        for (var i = 0; i < points.Count; i++)
            if (!double.IsFinite(points[i].Offset) || !double.IsFinite(points[i].Value) || points[i].Value < 0 || points[i].Offset < 0 || points[i].Offset > 60 || i > 0 && points[i].Offset <= points[i - 1].Offset) return null;
        // A flat tail (the last points at the same value) is a hold: the file's destination is where the hold starts, and the note keeps sounding it.
        var p = points.ToList();
        while (p.Count >= 3 && p[^1].Value == p[^2].Value) p.RemoveAt(p.Count - 1);
        var (first, last) = (p[0], p[^1]);
        // alphaTab reads the middle stretch back only when its value differs from both the origin and the destination; otherwise the curve is the straight line.
        double bestError = 0, bestValue = (first.Value + last.Value) / 2, bestM1 = (first.Offset + last.Offset) / 2, bestM2 = bestM1;
        foreach (var s in p.Select(x => x.Offset)) bestError = Math.Max(bestError, Math.Abs(At(p, s) - At(new[] { first, last }, s)));
        foreach (var value in p.Select(x => x.Value).Distinct().Where(v => v != first.Value && v != last.Value))
            foreach (var m1 in p.Select(x => x.Offset).Where(x => x > first.Offset && x < last.Offset))
                foreach (var m2 in p.Select(x => x.Offset).Where(x => x >= m1 && x < last.Offset))
                {
                    var candidate = new List<(double Offset, double Value)> { first, (m1, value), (m2, value), last };
                    var error = 0.0;
                    foreach (var s in p.Select(x => x.Offset).Append(m1).Append(m2))
                        error = Math.Max(error, Math.Abs(At(p, s) - At(candidate, s)));
                    if (error < bestError - 1e-9) { bestError = error; bestValue = value; bestM1 = m1; bestM2 = m2; }
                }
        return new Fields(first.Offset * OffsetUnit, first.Value * ValueUnit, bestM1 * OffsetUnit, bestM2 * OffsetUnit, bestValue * ValueUnit,
            last.Offset * OffsetUnit, last.Value * ValueUnit, bestError);
    }

    /// <summary>True when the clean .gp file holds the curve as drawn (up to <see cref="Tolerance"/>): two points always, a longer curve when a four-field fit matches it.</summary>
    internal static bool HeldExactly(IReadOnlyList<(double Offset, double Value)> normalised) =>
        normalised.Count <= 2 || Fit(normalised) is { } fit && fit.MaxError <= Tolerance;

    /// <summary>Remembers the fit for a note of the score being written.</summary>
    internal static void Plan(Note note, Fields fields) => Planned.AddOrUpdate(note, new StrongBox<Fields>(fields));

    /// <summary>
    /// Writes the planned fits into the bend fields of the matching notes of the score.gpif alphaTab produced. A note is found by its place
    /// (bar, track, voice, beat, note) and only changed when its fret and its origin and destination values agree with the plan; anything else, or
    /// any failure, leaves the file exactly as alphaTab wrote it.
    /// </summary>
    internal static byte[] Patch(byte[] gpif, Score score)
    {
        try { return PatchCore(gpif, score); }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException or FormatException or IndexOutOfRangeException or NullReferenceException) { return gpif; }
    }

    private static byte[] PatchCore(byte[] gpif, Score score)
    {
        var bom = gpif.Length >= 3 && gpif[0] == 0xEF && gpif[1] == 0xBB && gpif[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(gpif, bom ? 3 : 0, gpif.Length - (bom ? 3 : 0));
        var root = XDocument.Parse(text).Root;
        if (root is null) return gpif;
        Dictionary<string, XElement> Index(string list, string item) =>
            root.Element(list)?.Elements(item).Where(e => e.Attribute("id") is not null).GroupBy(e => (string)e.Attribute("id")!).ToDictionary(g => g.Key, g => g.First()) ?? new();
        var bars = Index("Bars", "Bar"); var voices = Index("Voices", "Voice"); var beats = Index("Beats", "Beat"); var notes = Index("Notes", "Note");
        static string[] Ids(XElement? e, string child) => ((string?)e?.Element(child))?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        static double? Prop(XElement note, string name) =>
            note.Element("Properties")?.Elements("Property").FirstOrDefault(x => (string?)x.Attribute("name") == name)?.Element("Float") is { } f
            && double.TryParse(f.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        var changes = new Dictionary<string, Fields>();
        var masterBars = root.Element("MasterBars")?.Elements("MasterBar").ToList() ?? new();
        for (var bar = 0; bar < masterBars.Count; bar++)
        {
            var barIds = Ids(masterBars[bar], "Bars");
            for (var track = 0; track < barIds.Length && track < score.Tracks.Count; track++)
            {
                if (!bars.TryGetValue(barIds[track], out var barElement) || score.Tracks[track].Staves.Count == 0) continue;
                var modelBars = score.Tracks[track].Staves[0].Bars;
                if (bar >= modelBars.Count) continue;
                var voiceIds = Ids(barElement, "Voices");
                for (var v = 0; v < voiceIds.Length && v < modelBars[bar].Voices.Count; v++)
                {
                    if (!voices.TryGetValue(voiceIds[v], out var voiceElement)) continue;
                    var beatIds = Ids(voiceElement, "Beats");
                    for (var b = 0; b < beatIds.Length && b < modelBars[bar].Voices[v].Beats.Count; b++)
                    {
                        if (!beats.TryGetValue(beatIds[b], out var beatElement)) continue;
                        var noteIds = Ids(beatElement, "Notes");
                        for (var n = 0; n < noteIds.Length && n < modelBars[bar].Voices[v].Beats[b].Notes.Count; n++)
                        {
                            if (changes.ContainsKey(noteIds[n]) || !notes.TryGetValue(noteIds[n], out var noteElement)) continue;
                            var model = modelBars[bar].Voices[v].Beats[b].Notes[n];
                            if (!Planned.TryGetValue(model, out var plan)) continue;
                            var fret = noteElement.Element("Properties")?.Elements("Property").FirstOrDefault(x => (string?)x.Attribute("name") == "Fret")?.Element("Fret")?.Value;
                            var origin = Prop(noteElement, "BendOriginValue"); var destination = Prop(noteElement, "BendDestinationValue");
                            if (fret != model.Fret.ToString(CultureInfo.InvariantCulture) || origin is null || destination is null
                                || Math.Abs(origin.Value - plan.Value.OriginValue) > 0.01 || Math.Abs(destination.Value - plan.Value.DestinationValue) > 0.01) continue;
                            changes[noteIds[n]] = plan.Value;
                        }
                    }
                }
            }
        }
        if (changes.Count == 0) return gpif;

        // One pass over the score's notes (a long song with many shaped bends must not rescan and copy the whole text per note).
        var properties = Names.Select(name => new Regex("(<Property name=\"" + name + "\">\\s*<Float>)[^<]*(</Float>)")).ToArray();
        text = Regex.Replace(text, "<Note id=\"([^\"]*)\">.*?</Note>", span =>
        {
            if (!changes.TryGetValue(span.Groups[1].Value, out var f)) return span.Value;
            var values = new[] { f.OriginOffset, f.OriginValue, f.Middle1Offset, f.Middle2Offset, f.MiddleValue, f.DestinationOffset, f.DestinationValue };
            var patched = span.Value;
            for (var i = 0; i < properties.Length; i++)
            {
                if (!properties[i].IsMatch(patched)) return span.Value;   // a field is missing: keep the note exactly as written
                var number = values[i].ToString("R", CultureInfo.InvariantCulture);
                patched = properties[i].Replace(patched, m => m.Groups[1].Value + number + m.Groups[2].Value, 1);
            }
            return patched;
        }, RegexOptions.Singleline);
        var body = new UTF8Encoding(false).GetBytes(text);
        if (!bom) return body;
        return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();
    }
}
