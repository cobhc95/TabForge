using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TabForge.Models;

namespace TabForge.Services;

// Owns: what a MusicXML file carries beyond the notes alphaTab reads: track colour (part-name color), mixer volume and pan (midi-instrument),
//   mute / solo / sends (TabForge's miscellaneous fields) and section colours (rehearsal color), and the unit conversions both directions share.
// Does not own: the score itself (alphaTab through GuitarProImporter) and the writing (MusicXmlExportService).
// Tests: MusicXmlRoundTripTests.
internal static class MusicXmlExtras
{
    private static readonly Regex HexColour = new("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    // ---- units shared with the exporter ----
    internal static string VolumeText(int volume) => (Math.Clamp(volume, 0, MidiLimits.Max) * 100.0 / MidiLimits.Max).ToString("0.####", CultureInfo.InvariantCulture);
    internal static string PanText(int pan) => ((Math.Clamp(pan, 0, MidiLimits.Max) - 64) * 90.0 / 64.0).ToString("0.####", CultureInfo.InvariantCulture);
    internal static int VolumeOf(double percent) => (int)Math.Clamp(Math.Round(percent * MidiLimits.Max / 100.0), 0, MidiLimits.Max);
    internal static int PanOf(double degrees) => (int)Math.Clamp(Math.Round(degrees * 64.0 / 90.0 + 64.0), 0, MidiLimits.Max);
    internal const string MetaPrefix = "tabforge-meta-";
    /// <summary>The song details MusicXML has no field for (or alphaTab does not read, or trims), as name / value pairs; the title and artist also travel in the standard elements.</summary>
    internal static IEnumerable<(string Key, string? Value)> MetaFields(SongProject p)
    {
        yield return ("title", p.Title); yield return ("artist", p.Artist); yield return ("subtitle", p.Subtitle); yield return ("album", p.Album); yield return ("music", p.MusicAuthor); yield return ("words", p.LyricsAuthor);
        yield return ("copyright", p.Copyright); yield return ("tab", p.TabAuthor); yield return ("instructions", p.Instructions); yield return ("notice", p.Notice);
        yield return ("lyrics", p.Lyrics);
    }

    private static void ApplyMeta(SongProject p, Dictionary<string, string> fields)
    {
        // alphaTab reads the spaces of a title as no-break spaces
        p.Title = p.Title.Replace('\u00A0', ' '); p.Artist = p.Artist.Replace('\u00A0', ' ');
        string Get(string key, string current) => fields.TryGetValue(MetaPrefix + key, out var v) ? v : current.Replace('\u00A0', ' ');
        p.Title = Get("title", p.Title); p.Artist = Get("artist", p.Artist);
        p.Subtitle = Get("subtitle", p.Subtitle); p.Album = Get("album", p.Album); p.MusicAuthor = Get("music", p.MusicAuthor); p.LyricsAuthor = Get("words", p.LyricsAuthor);
        p.Copyright = Get("copyright", p.Copyright); p.TabAuthor = Get("tab", p.TabAuthor); p.Instructions = Get("instructions", p.Instructions);
        p.Notice = Get("notice", p.Notice); p.Lyrics = Get("lyrics", p.Lyrics);
    }

    internal static string TrackField(string partId) => "tabforge-track-" + partId;
    internal static string TrackFieldValue(TrackModel t) =>
        $"mute={(t.Mute ? 1 : 0)};solo={(t.Solo ? 1 : 0)};chorus={t.Chorus};reverb={t.Reverb};follow={(t.ColorFollowsInstrument ? 1 : 0)}";

    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

    /// <summary>Applies the colour, mixer, mute / solo and section colours of the file to the converted project; a file without them gets the colour of its instrument group, as a new track does.</summary>
    internal static void Apply(byte[] xml, SongProject project)
    {
        XDocument doc;
        try { using var reader = XmlReader.Create(new MemoryStream(xml, writable: false), MusicXmlInput.Settings); doc = XDocument.Load(reader); }
        catch (XmlException) { return; }   // alphaTab already read it; a damaged extra is only a missing colour
        var root = doc.Root;
        if (root is null) return;
        var fields = root.Element("identification")?.Element("miscellaneous")?.Elements("miscellaneous-field")
            .Where(f => f.Attribute("name") is not null).GroupBy(f => f.Attribute("name")!.Value).ToDictionary(g => g.Key, g => g.First().Value) ?? new();
        ApplyMeta(project, fields);
        var parts = root.Element("part-list")?.Elements("score-part").ToList() ?? new List<XElement>();
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var track = project.Tracks[i];
            track.Name = track.Name.Replace('\u00A0', ' ');   // alphaTab reads the spaces of a part name as no-break spaces
            var part = i < parts.Count && parts.Count == project.Tracks.Count ? parts[i] : null;
            var colour = part?.Element("part-name")?.Attribute("color")?.Value;
            var instrument = part?.Elements("midi-instrument").FirstOrDefault();
            var volume = Number(instrument?.Element("volume")?.Value);
            var pan = Number(instrument?.Element("pan")?.Value);
            track.Volume = volume is { } v ? VolumeOf(v) : 100;   // alphaTab's own default (119) is not the mixer's
            track.Pan = pan is { } p ? PanOf(p) : 64;
            var own = part?.Attribute("id")?.Value is { } id && fields.TryGetValue(TrackField(id), out var text) ? Parse(text) : null;
            if (own is not null)
            {
                track.Mute = own.GetValueOrDefault("mute") == 1; track.Solo = own.GetValueOrDefault("solo") == 1;
                track.Chorus = Math.Clamp(own.GetValueOrDefault("chorus"), 0, MidiLimits.Max); track.Reverb = Math.Clamp(own.GetValueOrDefault("reverb", 24), 0, MidiLimits.Max);
            }
            if (colour is not null && HexColour.IsMatch(colour))
            {
                track.ColorHex = colour;
                track.ColorFollowsInstrument = own?.GetValueOrDefault("follow") == 1;
            }
            else { track.ColorFollowsInstrument = true; TrackColouring.FollowInstrument(track); }
        }
        ApplySectionColours(root, project);
        ApplyBars(root, project);
        ApplyNavigation(root, project);
        MusicXmlSidecar.Apply(root, project);
    }

    /// <summary>A double bar (light-light), a pickup bar (implicit="yes"), tempo changes inside the bar, the triplet feel and the octave of a treble clef: alphaTab keeps neither, so the guitar clef (treble sounding an octave lower) came back as a plain treble clef.</summary>
    private static void ApplyBars(XElement root, SongProject project)
    {
        var parts = root.Elements("part").ToList();
        for (var p = 0; p < parts.Count && p < project.Tracks.Count; p++)
        {
            var track = project.Tracks[p];
            string? clef = null;
            var bar = -1;
            foreach (var measure in parts[p].Elements("measure"))
            {
                bar++;
                if (bar >= track.Measures.Count) break;
                if (p == 0 && measure.Elements("barline").Elements("bar-style").Any(b => b.Value == "light-light"))
                    foreach (var other in project.NotationTracks.Where(t => bar < t.Measures.Count)) other.Measures[bar].IsDoubleBar = true;
                if (p == 0 && measure.Attribute("implicit")?.Value == "yes")
                    foreach (var other in project.NotationTracks.Where(t => bar < t.Measures.Count)) other.Measures[bar].Anacrusis = true;
                if (measure.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(n => n.Target == MusicXmlSidecar.BarTarget) is { } barPi)
                    MusicXmlSidecar.ApplyBar(track.Measures[bar], barPi.Data);
                foreach (var found in measure.Elements("attributes").Elements("clef").Where(c => c.Attribute("number") is null || c.Attribute("number")!.Value == "1"))
                    if (found.Element("sign")?.Value == "G" && (found.Element("line")?.Value ?? "2") == "2")
                        clef = found.Element("clef-octave-change")?.Value == "-1" ? Clefs.Guitar : Clefs.Treble;
                if (clef is not null && !track.IsDrumTrack) track.Measures[bar].Clef = clef;
            }
        }
    }

    /// <summary>alphaTab reads a MusicXML segno and coda sign but not the jumps (D.C., D.S., To Coda) or Fine, nor which coda it is: the sound marks of the first part add them to the bar's directions.</summary>
    private static void ApplyNavigation(XElement root, SongProject project)
    {
        var first = root.Elements("part").FirstOrDefault();
        if (first is null) return;
        var bar = -1;
        foreach (var measure in first.Elements("measure"))
        {
            bar++;
            var found = new List<string>();
            var words = new List<string>();
            foreach (var sound in measure.Descendants("sound"))
            {
                words.AddRange(sound.Parent?.Descendants("words").Select(w => w.Value.Trim()) ?? Enumerable.Empty<string>());
                var al = sound.Parent?.Descendants("words").Any(w => w.Value.Contains("Coda", StringComparison.OrdinalIgnoreCase)) == true ? "AlCoda" : "";
                if (sound.Attribute("tocoda") is { } toCoda) found.Add(toCoda.Value == "2" ? "ToDoubleCoda" : "ToCoda");
                if (sound.Attribute("coda") is { Value: "2" }) found.Add("DoubleCoda");
                if (sound.Attribute("fine") is not null) found.Add("Fine");
                if (sound.Attribute("dacapo") is not null) found.Add("DaCapo" + al);
                if (sound.Attribute("dalsegno") is { } dal) found.Add((dal.Value == "2" ? "DalSegnoSegno" : "DalSegno") + al);
            }
            if (found.Count == 0 || bar >= project.NotationTracks.Select(t => t.Measures.Count).DefaultIfEmpty(0).Max()) continue;
            foreach (var track in project.NotationTracks.Where(t => bar < t.Measures.Count))
            {
                var tokens = Playback.PlaybackOrder.DirectionTokens(track.Measures[bar].Directions).ToList();
                if (found.Contains("DoubleCoda")) tokens.Remove("Coda");
                foreach (var token in found.Where(f => !tokens.Contains(f))) tokens.Add(token);
                track.Measures[bar].Directions = string.Join(",", tokens);
                // the words of the mark ("To Coda", "D.S. al Coda") are the mark, not a comment on the beat the reader hung them on
                foreach (var cell in track.Measures[bar].Cells.Concat(track.Measures[bar].Voice2Cells).Where(c => c.Text is not null && words.Contains(c.Text.Trim()))) cell.Text = null;
            }
        }
    }

    private static Dictionary<string, int>? Parse(string text)
    {
        var result = new Dictionary<string, int>();
        foreach (var pair in text.Split(';'))
        {
            var kv = pair.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) result[kv[0]] = n;
        }
        return result;
    }

    /// <summary>A rehearsal mark written with a color is a section colour the user picked: it sets the section at that bar.</summary>
    private static void ApplySectionColours(XElement root, SongProject project)
    {
        var first = root.Elements("part").FirstOrDefault();
        if (first is null) return;
        var bar = -1;
        foreach (var measure in first.Elements("measure"))
        {
            bar++;
            var colour = measure.Descendants("rehearsal").Select(r => r.Attribute("color")?.Value).FirstOrDefault(c => c is not null && HexColour.IsMatch(c));
            if (colour is null) continue;
            foreach (var marker in project.Markers.Where(m => m.MeasureIndex == bar)) { marker.ColorHex = colour; marker.ColorIsExplicit = true; }
        }
    }
}
