using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TabForge.Models;

namespace TabForge.Services;

// Owns: writing a song's volume and pan mix changes into the gpif of a clean .gp (alphaTab's writer emits none).
// Does not own: reading them back (GpifAutomationReader) and the scale (GpMixerScale).
// Tests: GpifAutomationTests.
/// <summary>
/// Guitar Pro 7/8 keeps a track's volume and pan changes as automations of the track's ChannelStrip: type DSPParam_12 (volume) and
/// DSPParam_11 (pan), each with a bar, a position in the bar (0..1) and the fader fraction, like the ChannelStrip's own Parameters.
/// alphaTab's GP7 writer writes only the starting values, so after it has written the file the changes are inserted here.
/// </summary>
internal static class GpifAutomationWriter
{
    internal const string PanType = "DSPParam_11", VolumeType = "DSPParam_12";

    /// <summary>A volume and/or pan step (volume 0..16, pan -8..8) taking effect at a place in a bar of a track.</summary>
    internal readonly record struct MixPoint(int Track, int Bar, double Position, int? Volume, int? Pan);

    /// <summary>Adds the change a cell carries (all tracks when it says so) to <paramref name="points"/>.</summary>
    internal static void Collect(List<MixPoint> points, MixChange? mix, int track, int trackCount, int bar, double position)
    {
        if (mix is not { } m || m.Volume is null && m.Pan is null) return;
        var at = Math.Clamp(position, 0, 1);
        foreach (var index in m.AllTracks ? Enumerable.Range(0, trackCount) : new[] { track })
            points.Add(new MixPoint(index, bar, at, m.Volume, m.Pan));
    }

    /// <summary>The gpif with the changes written into each track's ChannelStrip; unchanged when there are none or the text is not as expected.</summary>
    internal static byte[] Patch(byte[] gpif, IReadOnlyList<MixPoint> points)
    {
        if (points.Count == 0) return gpif;
        var bom = gpif.Length >= 3 && gpif[0] == 0xEF && gpif[1] == 0xBB && gpif[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(gpif, bom ? 3 : 0, gpif.Length - (bom ? 3 : 0));
        var trackIndex = -1;
        var patched = Regex.Replace(text, "<Track id=\"[^\"]*\">.*?</Track>", span =>
        {
            trackIndex++;
            var mine = points.Where(p => p.Track == trackIndex).ToList();
            if (mine.Count == 0) return span.Value;
            var strip = Regex.Match(span.Value, @"<ChannelStrip[^>]*>\s*<Parameters>([^<]*)</Parameters>(.*?)</ChannelStrip>", RegexOptions.Singleline);
            if (!strip.Success || strip.Groups[2].Value.Contains("<Automations>", StringComparison.Ordinal)) return span.Value;
            var parameters = strip.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parameters.Length < 13) return span.Value;
            var xml = new StringBuilder("<Automations>");
            Append(xml, PanType, mine.Where(p => p.Pan is not null).Select(p => (p.Bar, p.Position, GpMixerScale.StepToFraction(p.Pan!.Value + 8))), parameters[11]);
            Append(xml, VolumeType, mine.Where(p => p.Volume is not null).Select(p => (p.Bar, p.Position, GpMixerScale.StepToFraction(p.Volume!.Value))), parameters[12]);
            xml.Append("</Automations>");
            var at = strip.Index + strip.Length - "</ChannelStrip>".Length;
            return span.Value.Insert(at, xml.ToString());
        }, RegexOptions.Singleline);
        if (patched == text) return gpif;
        var body = new UTF8Encoding(false).GetBytes(patched);
        return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
    }

    // One parameter's automations: the starting value at bar 0 (as Guitar Pro writes it) unless a change sits there, then the changes in order.
    private static void Append(StringBuilder xml, string type, IEnumerable<(int Bar, double Position, double Value)> changes, string startValue)
    {
        var ordered = changes.GroupBy(c => (c.Bar, Math.Round(c.Position, 6))).Select(g => g.Last()).OrderBy(c => c.Bar).ThenBy(c => c.Position).ToList();
        if (ordered.Count == 0) return;
        if (ordered[0].Bar != 0 || ordered[0].Position > 0) Add(xml, type, 0, 0, startValue);
        foreach (var c in ordered) Add(xml, type, c.Bar, c.Position, c.Value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void Add(StringBuilder xml, string type, int bar, double position, string value) =>
        xml.Append("<Automation><Type>").Append(type).Append("</Type><Linear>false</Linear><Bar>").Append(bar.ToString(CultureInfo.InvariantCulture))
            .Append("</Bar><Position>").Append(position.ToString("R", CultureInfo.InvariantCulture)).Append("</Position><Visible>true</Visible><Value>")
            .Append(value).Append("</Value></Automation>");
}
