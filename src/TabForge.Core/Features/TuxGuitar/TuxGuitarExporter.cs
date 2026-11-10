using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using TabForge.Models;

namespace TabForge.Features.TuxGuitar;

// Owns: writing a song as a TuxGuitar file (.tg: a zip holding content.xml) from the song model.
// Does not own: reading the file (TuxGuitarImporter) and embedding a native copy.
// Tests: TuxGuitarTests.
public static class TuxGuitarExporter
{
    private const long TickPerQuarter = 2882880;

    public static void Save(SongProject project, string path)
    {
        RequireNotation(project);
        var bytes = ToBytes(project);
        Services.FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes));
    }

    public static byte[] ToBytes(SongProject project)
    {
        RequireNotation(project);
        var tracks = project.Tracks.Where(t => !t.IsAudio).ToList();
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "no"), BuildRoot(project, tracks));
        byte[] xmlBytes;
        using (var ms = new MemoryStream())
        {
            doc.Save(ms, SaveOptions.DisableFormatting);
            xmlBytes = ms.ToArray();
        }
        if (xmlBytes.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("The song is too large to write.");
        var version = System.Text.Encoding.UTF8.GetBytes("TuxGuitar_file_format 2.0");
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var vEntry = archive.CreateEntry("version.txt", CompressionLevel.Optimal);
            using (var vStream = vEntry.Open()) vStream.Write(version);
            var cEntry = archive.CreateEntry("content.xml", CompressionLevel.Optimal);
            using (var cStream = cEntry.Open()) cStream.Write(xmlBytes);
        }
        return zip.ToArray();
    }

    private static XElement BuildRoot(SongProject project, List<TrackModel> tracks)
    {
        var song = new XElement("TGSong",
            new XElement("name", project.Title),
            new XElement("artist", project.Artist ?? ""),
            new XElement("album", project.Album ?? ""),
            new XElement("author", project.MusicAuthor ?? ""),
            new XElement("date", ""),
            new XElement("copyright", project.Copyright ?? ""),
            new XElement("writer", project.TabAuthor ?? ""),
            new XElement("transcriber", project.LyricsAuthor ?? ""),
            new XElement("comments", project.Instructions ?? ""));
        var channelId = new Dictionary<TrackModel, int>();
        var nextId = 0;
        foreach (var track in tracks)
        {
            nextId++;
            channelId[track] = nextId;
            song.Add(BuildChannel(nextId, track));
        }
        var barCount = tracks.Count > 0 ? tracks.Max(t => t.Measures.Count) : 0;
        for (var i = 0; i < barCount; i++) song.Add(BuildHeader(project, tracks, i));
        foreach (var track in tracks) song.Add(BuildTrack(project, track, channelId[track], barCount));
        return new XElement("TuxGuitarFile", new XElement("TGVersion", new XAttribute("major", 2), new XAttribute("minor", 1), new XAttribute("revision", 0)), song);
    }

    private static XElement BuildChannel(int id, TrackModel track)
    {
        var isDrums = track.IsDrumTrack;
        var bank = isDrums ? 128 : track.MidiBankMsb >= 0 ? track.MidiBankMsb : 0;
        var program = isDrums ? 0 : Math.Clamp(track.MidiProgram, 0, 127);
        return new XElement("TGChannel",
            new XElement("id", id),
            new XElement("bank", bank),
            new XElement("program", program),
            new XElement("volume", Math.Clamp(track.Volume, 0, 127)),
            new XElement("balance", Math.Clamp(track.Pan, 0, 127)),
            new XElement("chorus", Math.Clamp(track.Chorus, 0, 127)),
            new XElement("reverb", Math.Clamp(track.Reverb, 0, 127)),
            new XElement("phaser", 0),
            new XElement("tremolo", 0),
            new XElement("name", track.InstrumentName ?? ""),
            new XElement("TGChannelParameter", new XAttribute("key", "gm-channel-1"), new XAttribute("value", Math.Clamp(track.MidiChannel, 0, 15))),
            new XElement("TGChannelParameter", new XAttribute("key", "gm-channel-2"), new XAttribute("value", Math.Clamp(track.MidiChannel, 0, 15))));
    }

    private static XElement BuildHeader(SongProject project, List<TrackModel> tracks, int index)
    {
        var reference = tracks.Select(t => t.Measures.Count > index ? t.Measures[index] : null).FirstOrDefault(m => m is not null) ?? new MeasureModel();
        var num = reference.TimeSigNum ?? project.TimeSignatureNumerator;
        var denom = reference.TimeSigDenom ?? project.TimeSignatureDenominator;
        var tempo = HeaderTempo(project, tracks, index);
        var header = new XElement("TGMeasureHeader",
            new XElement("timeSignature", new XAttribute("denominator", denom), new XAttribute("numerator", num)),
            new XElement("tempo", tempo));
        var repeatOpen = tracks.Any(t => t.Measures.Count > index && t.Measures[index].RepeatStart);
        var repeatClose = tracks.Any(t => t.Measures.Count > index && t.Measures[index].RepeatEnd);
        var repeatCount = tracks.Where(t => t.Measures.Count > index && t.Measures[index].RepeatEnd).Select(t => t.Measures[index].RepeatCount).FirstOrDefault(2);
        var altMask = 0;
        foreach (var t in tracks)
            if (t.Measures.Count > index) altMask |= t.Measures[index].AlternateEndingMask != 0 ? t.Measures[index].AlternateEndingMask : (t.Measures[index].AlternateEnding > 0 ? 1 << (t.Measures[index].AlternateEnding - 1) : 0);
        if (repeatOpen) header.Add(new XElement("repeatOpen"));
        if (repeatClose) header.Add(new XElement("repeatClose", Math.Clamp(repeatCount, 2, 8)));
        if (altMask != 0)
        {
            var alt = new XElement("repeatAlternative");
            for (var n = 0; n < 8; n++)
                if ((altMask & (1 << n)) != 0) alt.Add(new XElement("alternative", n + 1));
            header.Add(alt);
        }
        var marker = project.Markers.FirstOrDefault(m => m.MeasureIndex == index);
        if (marker is not null)
        {
            ColourHex.TryParse(marker.ColorHex, out var rgb);
            rgb = ColourHex.TryParse(marker.ColorHex, out var rgb2) ? rgb2 : new Rgba(255, 0, 0);
            header.Add(new XElement("marker", new XAttribute("B", rgb.B), new XAttribute("G", rgb.G), new XAttribute("R", rgb.R), marker.Title ?? ""));
        }
        var feel = HeaderFeel(tracks, index);
        if (feel == TripletFeels.Eighth) header.Add(new XElement("tripletFeel", "eighth"));
        else if (feel == TripletFeels.Sixteenth) header.Add(new XElement("tripletFeel", "sixteenth"));
        return header;
    }

    private static int HeaderTempo(SongProject project, List<TrackModel> tracks, int index)
    {
        var tempo = Math.Clamp(project.Tempo, Services.InputLimits.MinTempo, Services.InputLimits.MaxTempo);
        for (var i = 0; i <= index; i++)
        {
            var change = tracks.Select(t => t.Measures.Count > i ? t.Measures[i].TempoChange : null).FirstOrDefault(v => v is not null);
            if (change is not null) tempo = Math.Clamp(change.Value, Services.InputLimits.MinTempo, Services.InputLimits.MaxTempo);
        }
        return tempo;
    }

    private static string HeaderFeel(List<TrackModel> tracks, int index)
    {
        foreach (var t in tracks)
        {
            if (t.Measures.Count <= index) continue;
            var m = t.Measures[index];
            if (m.TripletFeelKind == TripletFeels.Sixteenth) return TripletFeels.Sixteenth;
            if (m.TripletFeelKind == TripletFeels.Eighth || m.TripletFeel) return TripletFeels.Eighth;
            return TripletFeels.None;
        }
        return TripletFeels.None;
    }

    private static XElement BuildTrack(SongProject project, TrackModel track, int channelId, int barCount)
    {
        var element = new XElement("TGTrack", new XAttribute("maxFret", Math.Clamp(track.NumberOfFrets, 12, 36)),
            new XElement("name", track.Name ?? ""),
            new XElement("channelId", channelId));
        if (track.Capo > 0) element.Add(new XElement("offset", track.Capo));
        else if (track.Transpose != 0) element.Add(new XElement("offset", track.Transpose));
        ColourHex.TryParse(track.ColorHex, out var trackRgb);
        element.Add(new XElement("color", new XAttribute("B", trackRgb.B), new XAttribute("G", trackRgb.G), new XAttribute("R", trackRgb.R)));
        if (track.IsDrumTrack)
        {
            for (var i = 0; i < 6; i++) element.Add(new XElement("TGString", 0));
        }
        else
        {
            foreach (var tuning in track.StringTunings) element.Add(new XElement("TGString", Math.Clamp(tuning, 0, 127)));
        }
        element.Add(new XElement("TGLyric", new XAttribute("from", 1)));
        long start = TickPerQuarter;
        for (var i = 0; i < barCount; i++)
        {
            var measure = i < track.Measures.Count ? track.Measures[i] : null;
            element.Add(BuildMeasure(project, track, measure, i, ref start));
        }
        return element;
    }

    private static XElement BuildMeasure(SongProject project, TrackModel track, MeasureModel? measure, int index, ref long start)
    {
        var element = new XElement("TGMeasure");
        if (index == 0)
        {
            var clef = measure?.Clef ?? (track.Measures.Count > 0 ? track.Measures[0].Clef : Clefs.Guitar);
            element.Add(new XElement("clef", MapClef(clef)));
            var key = measure?.KeySignature ?? project.KeySignature;
            element.Add(new XElement("keySignature", Math.Clamp(key, -7, 7)));
        }
        if (measure is null) return element;
        var slots = new SortedSet<int>();
        for (var s = 0; s < measure.Cells.Count; s++)
            if (IsContent(measure.Cells[s])) slots.Add(s);
        for (var s = 0; s < measure.Voice2Cells.Count; s++)
            if (IsContent(measure.Voice2Cells[s])) slots.Add(s);
        foreach (var slot in slots)
        {
            var cell0 = slot < measure.Cells.Count ? measure.Cells[slot] : null;
            var cell1 = slot < measure.Voice2Cells.Count ? measure.Voice2Cells[slot] : null;
            var beat = BuildBeat(track, cell0, cell1, start);
            start += DurationTicks(beat);
            element.Add(beat);
        }
        return element;
    }

    private static long DurationTicks(XElement beat)
    {
        var voices = beat.Elements("voice").ToList();
        var longest = 0L;
        foreach (var v in voices)
        {
            if (v.Attribute("empty")?.Value == "true") continue;
            var d = v.Element("duration");
            if (d is null) continue;
            var denom = IntAttr(d, "value", 4);
            var dotted = (string?)d.Attribute("dotted");
            var dots = dotted == "dotted" ? 1 : dotted == "doubleDotted" ? 2 : 0;
            var div = d.Element("divisionType");
            var enters = div is not null && int.TryParse((string?)div.Attribute("enters"), out var en) ? en : 1;
            var times = div is not null && int.TryParse((string?)div.Attribute("times"), out var ti) ? ti : 1;
            var slots = 16.0 / denom;
            if (dots == 1) slots *= 1.5;
            else if (dots == 2) slots *= 1.75;
            if (enters != 1 || times != 1) slots *= (double)times / enters;
            var ticks = (long)Math.Round(slots * TickPerQuarter / 4.0);
            if (ticks > longest) longest = ticks;
        }
        return Math.Max(0, longest);
    }

    private static XElement BuildBeat(TrackModel track, TabCell? cell0, TabCell? cell1, long start)
    {
        var beat = new XElement("TGBeat", new XElement("preciseStart", start));
        var text = cell0?.Text ?? cell1?.Text;
        if (!string.IsNullOrWhiteSpace(text)) beat.Add(new XElement("text", text));
        var pick = PickOf(cell0) ?? PickOf(cell1);
        if (pick is not null) beat.Add(new XElement("pickStroke", pick));
        var stroke = StrokeOf(cell0) ?? StrokeOf(cell1);
        if (stroke is not null) beat.Add(new XElement("stroke", new XAttribute("direction", stroke.Value.Direction), new XAttribute("value", stroke.Value.Value)));
        beat.Add(BuildVoice(track, cell0));
        beat.Add(BuildVoice(track, cell1));
        return beat;
    }

    private static XElement BuildVoice(TrackModel track, TabCell? cell)
    {
        if (cell is null || !IsContent(cell))
            return new XElement("voice", new XAttribute("empty", "true"), new XElement("duration", new XAttribute("value", 4), new XElement("divisionType", new XAttribute("enters", 1), new XAttribute("times", 1))));
        var durationAttrs = new List<XAttribute> { new XAttribute("value", cell.DurationDenominator) };
        if (cell.Dots == 1) durationAttrs.Add(new XAttribute("dotted", "dotted"));
        else if (cell.Dots == 2) durationAttrs.Add(new XAttribute("dotted", "doubleDotted"));
        var enters = cell.TupletNumerator > 0 ? cell.TupletNumerator : 1;
        var times = cell.TupletDenominator > 0 ? cell.TupletDenominator : 1;
        var voice = new XElement("voice", new XElement("duration", durationAttrs, new XElement("divisionType", new XAttribute("enters", enters), new XAttribute("times", times))));
        if (cell.Notes.Count == 0)
        {
            voice.SetAttributeValue("empty", "false");
            return voice;
        }
        foreach (var note in cell.Notes)
        {
            if (note.IsGraceNote) continue;
            var noteEl = BuildNote(track, note, cell);
            voice.Add(noteEl);
        }
        if (voice.Elements("note").Any())
        {
            foreach (var grace in cell.Notes.Where(n => n.IsGraceNote))
            {
                var main = voice.Elements("note").FirstOrDefault();
            }
        }
        if (!voice.Elements("note").Any()) voice.SetAttributeValue("empty", "false");
        return voice;
    }

    private static XElement BuildNote(TrackModel track, TabNote note, TabCell cell)
    {
        var element = new XElement("note", new XAttribute("string", note.StringIndex + 1), new XAttribute("value", note.Fret), new XAttribute("velocity", Math.Clamp(note.Velocity, 0, 127)));
        if (note.Tied) element.SetAttributeValue("tiedNote", "true");
        var grace = cell.Notes.FirstOrDefault(n => n.IsGraceNote && n.StringIndex == note.StringIndex);
        if (grace is not null)
        {
            element.Add(new XElement("grace", new XAttribute("dead", grace.Dead ? "true" : "false"), new XAttribute("duration", 64), new XAttribute("dynamic", Math.Clamp(grace.Velocity, 0, 127)), new XAttribute("fret", grace.Fret), new XAttribute("onBeat", grace.GraceBeforeBeat ? "false" : "true"), new XAttribute("transition", grace.Techniques.Contains("Slide") ? "slide" : grace.Techniques.Contains("Bend") ? "bend" : "none")));
        }
        if (cell.Accent == 1 && note.Techniques.Contains("Accent")) element.Add(new XElement("accentuatedNote"));
        else if (cell.Accent == 2 && note.Techniques.Contains("HeavyAccent")) element.Add(new XElement("heavyAccentuatedNote"));
        else if (cell.Accent == 1) element.Add(new XElement("accentuatedNote"));
        else if (cell.Accent == 2) element.Add(new XElement("heavyAccentuatedNote"));
        if (note.Dead) element.Add(new XElement("deadNote"));
        foreach (var tech in note.Techniques)
        {
            switch (tech)
            {
                case "PalmMute": element.Add(new XElement("palmMute")); break;
                case "LetRing": element.Add(new XElement("letRing")); break;
                case "HOPO": element.Add(new XElement("hammer")); break;
                case "Slide": if (grace is null) element.Add(new XElement("slide")); break;
                case "Vibrato": element.Add(new XElement("vibrato")); break;
                case "Staccato": element.Add(new XElement("staccato")); break;
                case "Tapping": element.Add(new XElement("tapping")); break;
                case "Pop": element.Add(new XElement("popping")); break;
                case "Slap": element.Add(new XElement("slapping")); break;
                case "FadeIn": element.Add(new XElement("fadeIn")); break;
                case "Harmonic": element.Add(new XElement("harmonic", new XAttribute("data", (int)(note.HarmonicFret ?? 12)), new XAttribute("type", "N.H"))); break;
                case "ArtificialHarmonic": element.Add(new XElement("harmonic", new XAttribute("data", (int)(note.HarmonicFret ?? 0)), new XAttribute("type", "A.H"))); break;
                case "PinchHarmonic": element.Add(new XElement("harmonic", new XAttribute("data", (int)(note.HarmonicFret ?? 0)), new XAttribute("type", "P.H"))); break;
                case "TremoloPick": element.Add(new XElement("tremoloPicking", new XAttribute("duration", cell.TremoloPickDenominator != 0 ? cell.TremoloPickDenominator : 16))); break;
                case "Trill": element.Add(new XElement("trill", new XAttribute("duration", note.TrillDurationDenominator != 0 ? note.TrillDurationDenominator : 8), new XAttribute("fret", track.IsDrumTrack ? note.TrillTargetMidi : track.FretOf(note.StringIndex, note.TrillTargetMidi)))); break;
                case "Bend":
                    var bend = new XElement("bend");
                    foreach (var p in note.BendPoints) bend.Add(new XElement("point", new XAttribute("position", (int)Math.Round(p.Offset / 5.0)), new XAttribute("value", (int)Math.Round(p.Value))));
                    element.Add(bend);
                    break;
            }
        }
        return element;
    }

    private static bool IsContent(TabCell cell) => cell.Notes.Count > 0 || cell.IsRest || !string.IsNullOrWhiteSpace(cell.Text);
    private static string? PickOf(TabCell? cell)
    {
        if (cell is null) return null;
        if (cell.Notes.Any(n => n.Techniques.Contains("PickDown"))) return "down";
        if (cell.Notes.Any(n => n.Techniques.Contains("PickUp"))) return "up";
        return null;
    }
    private static (string Direction, int Value)? StrokeOf(TabCell? cell)
    {
        if (cell is null) return null;
        if (cell.Notes.Any(n => n.Techniques.Contains("BrushDown"))) return ("down", (int)Math.Round(cell.BrushStepSlots));
        if (cell.Notes.Any(n => n.Techniques.Contains("BrushUp"))) return ("up", (int)Math.Round(cell.BrushStepSlots));
        return null;
    }
    private static string MapClef(string clef) => clef switch
    {
        Clefs.Bass => "bass",
        Clefs.Alto => "alto",
        Clefs.Percussion => "treble",
        _ => "treble",
    };
    private static int IntAttr(XElement el, string name, int fallback)
    {
        var a = (string?)el.Attribute(name);
        return int.TryParse(a, out var v) ? v : fallback;
    }

    private static void RequireNotation(SongProject project)
    {
        foreach (var t in project.Tracks)
            if (!t.IsAudio) return;
        throw new InvalidOperationException("This song has only audio tracks, so there is no notation to write as a TuxGuitar file. Add a tab track, or save it as a native project file to keep the audio tracks.");
    }
}
