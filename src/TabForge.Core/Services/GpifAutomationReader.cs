using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using TabForge.Models;

namespace TabForge.Services;

// Owns: reading a .gp's per-track volume and pan changes (ChannelStrip automations) into the song's mix changes.
// Does not own: writing them (GpifAutomationWriter), the scale (GpMixerScale) and the Guitar Pro 3-5 mix tables (GuitarProBeatReader).
// Tests: GpifAutomationTests.
/// <summary>The reverse of <see cref="GpifAutomationWriter"/>: each automation after the starting value becomes a mix change on the track's cell at that place.</summary>
internal static class GpifAutomationReader
{
    private const int MaxAutomationsPerTrack = 4096;

    /// <summary>Applies the file's volume and pan changes to the imported tracks; a file that is not a .gp zip or has none is left alone.</summary>
    internal static void Apply(byte[] file, SongProject project)
    {
        if (file.Length < 4 || file[0] != (byte)'P' || file[1] != (byte)'K') return;
        try
        {
            var part = GuitarProExporter.ReadZip(file).FirstOrDefault(p => p.Name.Equals(GuitarProExporter.ScoreEntry, StringComparison.OrdinalIgnoreCase));
            if (part.Data is not { Length: > 0 }) return;
            var bom = part.Data.Length >= 3 && part.Data[0] == 0xEF && part.Data[1] == 0xBB && part.Data[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(part.Data, bom ? 3 : 0, part.Data.Length - (bom ? 3 : 0));
            using var reader = System.Xml.XmlReader.Create(new StringReader(text), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            var tracks = XDocument.Load(reader).Root?.Element("Tracks")?.Elements("Track").ToList() ?? new();
            var applied = new List<List<Applied>>();
            for (var t = 0; t < tracks.Count && t < project.Tracks.Count; t++)
                applied.Add(ApplyTrack(tracks[t].Element("RSE")?.Element("ChannelStrip"), project, project.Tracks[t]));
            if (tracks.Count == project.Tracks.Count) RestoreAllTracks(applied);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or FormatException or IOException)
        {
            Trace.Error(Trace.Import, "gpif mix automations: " + ex.Message);   // the changes are lost, the rest of the import stands
        }
    }

    /// <summary>A change applied to a cell: where the file put it, which value and the cell that took it.</summary>
    private readonly record struct Applied(int Bar, double Position, bool IsPan, int Step, TabCell Cell);

    // The file's Linear flag is not read: the model's MixChange.TransitionBeats is a length in beats that the flag (a ramp to the next automation) does not give,
    // and the writer always writes Linear false, so a TabForge round trip loses nothing.
    private static List<Applied> ApplyTrack(XElement? strip, SongProject project, TrackModel track)
    {
        var applied = new List<Applied>();
        if (strip is null) return applied;
        var start = ((string?)strip.Element("Parameters"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var automation in strip.Element("Automations")?.Elements("Automation").Take(MaxAutomationsPerTrack) ?? Enumerable.Empty<XElement>())
        {
            var type = (string?)automation.Element("Type");
            if (type is not (GpifAutomationWriter.PanType or GpifAutomationWriter.VolumeType)) continue;
            if (!int.TryParse((string?)automation.Element("Bar"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bar) || bar < 0 || bar >= track.Measures.Count) continue;
            if (!double.TryParse((string?)automation.Element("Position"), NumberStyles.Float, CultureInfo.InvariantCulture, out var position)
                || !double.TryParse((string?)automation.Element("Value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(position) || !double.IsFinite(value)) continue;
            var step = GpMixerScale.FractionToStep(value);
            var isPan = type == GpifAutomationWriter.PanType;
            // the starting value (bar 0, position 0, as the track's own setting) is the track's volume and pan already
            var initial = start is { Length: > 12 } && double.TryParse(start[isPan ? 11 : 12], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? GpMixerScale.FractionToStep(f) : -1;   // same scale and rounding as the value above
            if (bar == 0 && position <= 0 && step == initial) continue;
            var cell = CellAt(track.Measures[bar], position, MusicTime.BarSlots(project, bar));
            if (cell is null) continue;
            cell.Mix ??= new MixChange();
            if (isPan) cell.Mix.Pan = step - 8; else cell.Mix.Volume = step;
            applied.Add(new Applied(bar, Math.Round(position, 6), isPan, step, cell));
        }
        return applied;
    }

    // The writer fans an all-tracks change out to every track; when every track carries the same changes at the same place, they are one all-tracks change again (kept on the first track).
    private static void RestoreAllTracks(List<List<Applied>> tracks)
    {
        if (tracks.Count < 2) return;
        foreach (var group in tracks[0].GroupBy(a => (a.Bar, a.Position)))
        {
            var wanted = group.Select(a => (a.IsPan, a.Step)).OrderBy(x => x).ToList();
            var same = tracks.Skip(1).Select(t => t.Where(a => (a.Bar, a.Position) == group.Key).ToList()).ToList();
            if (same.Any(list => !list.Select(a => (a.IsPan, a.Step)).OrderBy(x => x).SequenceEqual(wanted))) continue;
            foreach (var a in group) a.Cell.Mix!.AllTracks = true;
            foreach (var a in same.SelectMany(list => list))
            {
                if (a.IsPan) a.Cell.Mix!.Pan = null; else a.Cell.Mix!.Volume = null;
                if (a.Cell.Mix!.IsEmpty) a.Cell.Mix = null;
            }
        }
    }

    // The last cell that starts at or before the place (the first one when the place is before them all), so a change is never moved later than it was stored.
    // A cell's start is worked out as the exporter does (its own rhythmic position, else its grid index or where the previous written cell ended), empty grid cells holding no beat.
    private static TabCell? CellAt(MeasureModel measure, double position, int barSlots)
    {
        var target = Math.Clamp(position, 0, 1) * barSlots + 0.01;
        var cursor = 0.0;
        TabCell? first = null, last = null;
        for (var i = 0; i < measure.Cells.Count; i++)
        {
            var cell = measure.Cells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest) continue;
            var start = cell.RhythmicPosition ?? Math.Max(i, cursor);
            first ??= cell;
            if (start > target) break;
            cursor = start + MusicTime.CellSlots(cell);
            last = cell;
        }
        return last ?? first;
    }
}
