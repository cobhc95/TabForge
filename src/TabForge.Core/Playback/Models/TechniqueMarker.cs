using System.Globalization;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the compact text a "Keep techniques for TabForge" export writes into a TF1 marker meta event, and reading it back onto a note.
// Does not own: writing the meta event (MidiExportService), parsing it from the file (SmfReader) or applying it during import (MidiToSong).
// Tests: MidiTechniqueMarkerTests.
/// <summary>
/// A marker's payload: the node's technique ids, then the numbers they need. Format (versioned so older files keep loading):
/// <c>TF1:&lt;channel&gt;:&lt;pitch&gt;:&lt;ids&gt;|&lt;key=value;...&gt;</c>. Ids are comma separated; the numbers are
/// <c>b</c> bend points (offset:value,...), <c>s</c> slide target, <c>h</c> harmonic fret, <c>tt</c>/<c>td</c> trill target and speed,
/// <c>vd</c>/<c>vs</c> vibrato depth and speed, <c>g</c>/<c>d</c> ghost and dead. Unknown ids, keys and versions are ignored.
/// </summary>
public static class TechniqueMarker
{
    public const string Prefix = "TF1:";
    /// <summary>The longest marker payload written or accepted; longer text is not a technique marker.</summary>
    public const int MaxPayload = 512;
    private const int MaxIds = InputLimits.MaxTechniquesPerNote;
    private const int MaxBendPoints = InputLimits.MaxCurvePoints;

    /// <summary>The payload for a note, or null when it carries no technique and no bend curve.</summary>
    public static string? Build(TabNote note)
    {
        var ids = note.Techniques.Where(IsId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var extras = new List<string>();
        if (note.BendPoints.Count > 0)
            extras.Add("b=" + string.Join(",", note.BendPoints.Take(MaxBendPoints).Select(p => Num(p.Offset) + ":" + Num(p.Value))));
        if (note.SlideTargetMidi > 0) extras.Add("s=" + note.SlideTargetMidi.ToString(CultureInfo.InvariantCulture));
        if (note.HarmonicFret is double harmonic) extras.Add("h=" + Num(harmonic));
        if (note.TrillTargetMidi > 0) extras.Add("tt=" + note.TrillTargetMidi.ToString(CultureInfo.InvariantCulture));
        if (note.TrillDurationDenominator > 0) extras.Add("td=" + note.TrillDurationDenominator.ToString(CultureInfo.InvariantCulture));
        if (note.VibratoDepth is double depth) extras.Add("vd=" + Num(depth));
        if (note.VibratoSpeed is double speed) extras.Add("vs=" + Num(speed));
        if (note.Ghost) extras.Add("g=1");
        if (note.Dead) extras.Add("d=1");
        if (ids.Count == 0 && extras.Count == 0) return null;
        var payload = string.Join(",", ids) + "|" + string.Join(";", extras);
        return payload.Length > MaxPayload ? null : payload;
    }

    /// <summary>True when a meta text may be a technique marker of a version this build reads.</summary>
    public static bool IsMarker(string? text) =>
        text is { Length: > 4 } && text.Length <= MaxPayload + 24 && text.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The payload of a full marker text, or null when it is another version or malformed.</summary>
    public static string? PayloadOf(string? text)
    {
        if (!IsMarker(text)) return null;
        var parts = text!.Split(':', 4);
        if (parts.Length < 4 || parts[0] != "TF1") return null;
        return parts[3].Length <= MaxPayload ? parts[3] : null;
    }

    /// <summary>Restores a marker payload onto a note (a full TF1 text or the payload alone). Never throws: garbage or unknown parts are ignored.</summary>
    public static void Apply(TabNote note, string? text)
    {
        var payload = text is null ? null : text.StartsWith(Prefix, StringComparison.Ordinal) ? PayloadOf(text) : text.Length <= MaxPayload ? text : null;
        if (payload is null) return;
        var bar = payload.IndexOf('|');
        var idsPart = bar < 0 ? payload : payload[..bar];
        foreach (var id in idsPart.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (IsId(id) && note.Techniques.Count < MaxIds) note.Techniques.Add(id);
        if (bar < 0) return;
        foreach (var pair in payload[(bar + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var value = pair[(eq + 1)..];
            switch (pair[..eq])
            {
                case "b": note.BendPoints = ParseBends(value); break;
                case "s" when TryInt(value, 0, 127, out var slide): note.SlideTargetMidi = slide; break;
                case "h" when TryNum(value) is double harmonic && harmonic is >= 0 and <= 127: note.HarmonicFret = harmonic; break;
                case "tt" when TryInt(value, 0, 127, out var trill): note.TrillTargetMidi = trill; break;
                case "td" when TryInt(value, 1, 64, out var duration): note.TrillDurationDenominator = duration; break;
                case "vd" when TryNum(value) is double depth && depth is >= 0 and <= 12: note.VibratoDepth = depth; break;
                case "vs" when TryNum(value) is double speed && speed is > 0 and <= 20: note.VibratoSpeed = speed; break;
                case "g": note.Ghost = value == "1"; break;
                case "d": note.Dead = value == "1"; break;
            }
        }
    }

    private static List<BendPointModel> ParseBends(string value)
    {
        var points = new List<BendPointModel>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (points.Count >= MaxBendPoints) break;
            var colon = item.IndexOf(':');
            if (colon <= 0 || TryNum(item[..colon]) is not double offset || TryNum(item[(colon + 1)..]) is not double amount) continue;
            if (offset is < 0 or > 60 || amount is < -48 or > 48) continue;
            points.Add(new BendPointModel { Offset = offset, Value = amount });
        }
        return points;
    }

    private static bool IsId(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 32 && char.IsLetter(value[0]) &&
        value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_');

    private static bool TryInt(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private static double? TryNum(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static string Num(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
