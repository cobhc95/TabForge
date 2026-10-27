using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using TabForge.Models;

namespace TabForge.Services;

// Owns: uncompressed MusicXML export of a song.
// Does not own: the other export formats.
// Tests: MusicXmlRoundTripTests.
// Split: the part, measure and note encoders live in MusicXmlExportService.Parts/Measures/Notes.cs.
// This file keeps the statics, the export entry points, the Write dispatcher and the drum and pitch helpers.
/// <summary>
/// Exports a song as uncompressed MusicXML 4.0 (score-partwise, .musicxml): one part per track with a
/// notation staff and, for fretted tracks, a tab staff carrying the string and fret of every note.
/// Durations, dots, tuplets, ties, rests, grace notes, chords, key / time / tempo, repeats, endings,
/// section marks, lyrics, dynamics (through the shared <see cref="Dynamics"/> table) and the common
/// techniques (hammer-on / pull-off, slides, bends, harmonics, palm mute, accents, staccato) are written.
/// Beat onsets follow the one rule in <see cref="MusicTime"/>, like the .gp export.
/// </summary>
public static partial class MusicXmlExportService
{
    /// <summary>Divisions per quarter note: divisible by 3, 4, 5 and 16, so tuplets and 64th notes stay exact.</summary>
    private const int Divisions = 240;
    private const double DivisionsPerSlot = Divisions / (double)MusicTime.SlotsPerQuarter;

    private static readonly string[] SharpSteps = { "C", "C", "D", "D", "E", "F", "F", "G", "G", "A", "A", "B" };
    private static readonly int[] SharpAlters = { 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0 };
    private static readonly string[] FlatSteps = { "C", "D", "D", "E", "E", "F", "G", "G", "A", "A", "B", "B" };
    private static readonly int[] FlatAlters = { 0, -1, 0, -1, 0, 0, -1, 0, -1, 0, -1, 0 };

    public static void Export(SongProject project, string path)
    {
        path = FilePathPolicy.OutputFile(path, "MusicXML export", ".musicxml", ".xml");
        var bytes = ToBytes(project);
        FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes));
    }

    /// <summary>The complete .musicxml file (UTF-8, no byte-order mark); nothing is written to disk.</summary>
    public static byte[] ToBytes(SongProject project)
    {
        AudioTrackExport.RequireNotation(project, "MusicXML");
        using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false), CloseOutput = false };
        using (var xml = XmlWriter.Create(buffer, settings))
        {
            Write(project, xml);
            xml.Flush();
        }
        return buffer.ToArray();
    }

    private static void Write(SongProject project, XmlWriter xml)
    {
        xml.WriteStartDocument();
        xml.WriteDocType("score-partwise", "-//Recordare//DTD MusicXML 3.1 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", null);
        xml.WriteStartElement("score-partwise");
        xml.WriteAttributeString("version", "3.1"); // 3.1 (no 4.0-only elements) is what older readers such expect

        xml.WriteStartElement("work");
        xml.WriteElementString("work-title", project.Title ?? "");
        xml.WriteEndElement();
        xml.WriteStartElement("identification");
        if (!string.IsNullOrWhiteSpace(project.Artist))
        {
            xml.WriteStartElement("creator");
            xml.WriteAttributeString("type", "composer");
            xml.WriteString(project.Artist);
            xml.WriteEndElement();
        }
        if (!string.IsNullOrWhiteSpace(project.Copyright)) xml.WriteElementString("rights", project.Copyright);
        xml.WriteStartElement("encoding");
        xml.WriteElementString("software", "TabForge");
        xml.WriteEndElement();
        WriteTabForgeFields(project, xml);
        xml.WriteEndElement();

        xml.WriteStartElement("part-list");
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var track = project.Tracks[t];
            if (track.IsAudio) continue;
            xml.WriteStartElement("score-part");
            xml.WriteAttributeString("id", PartId(t));
            xml.WriteStartElement("part-name");
            xml.WriteAttributeString("color", track.ColorHex);   // the track colour: readers that ignore it lose nothing, TabForge reads it back
            xml.WriteString(string.IsNullOrWhiteSpace(track.Name) ? "Track " + (t + 1) : track.Name);
            xml.WriteEndElement();
            if (track.IsDrumTrack)
            {
                // One instrument per drum sound used, with its General MIDI number writes a drum part.
                var sounds = track.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes).Select(DrumMidi).Distinct().OrderBy(v => v).ToList();
                if (sounds.Count == 0) sounds.Add(36);   // a drum part without a hit still has its instrument (the mixer values live there)
                foreach (var sound in sounds)
                {
                    xml.WriteStartElement("score-instrument");
                    xml.WriteAttributeString("id", PartId(t) + "-I" + Num(sound));
                    xml.WriteElementString("instrument-name", DrumName(sound));
                    xml.WriteEndElement();
                }
                foreach (var sound in sounds)
                {
                    xml.WriteStartElement("midi-instrument");
                    xml.WriteAttributeString("id", PartId(t) + "-I" + Num(sound));
                    xml.WriteElementString("midi-channel", "10");
                    xml.WriteElementString("midi-unpitched", Num(sound + 1)); // 1-based, like midi-program
                    WriteMixer(xml, track);
                    xml.WriteEndElement();
                }
            }
            else
            {
                xml.WriteStartElement("score-instrument");
                xml.WriteAttributeString("id", PartId(t) + "-I1");
                xml.WriteElementString("instrument-name", string.IsNullOrWhiteSpace(track.Name) ? "Track " + (t + 1) : track.Name);
                xml.WriteEndElement();
                xml.WriteStartElement("midi-instrument");
                xml.WriteAttributeString("id", PartId(t) + "-I1");
                xml.WriteElementString("midi-channel", Num(Math.Clamp(track.MidiChannel, 0, 15) + 1));
                xml.WriteElementString("midi-program", Num(Math.Clamp(track.MidiProgram, 0, MidiLimits.Max) + 1));
                WriteMixer(xml, track);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();

        var markers = new Dictionary<int, MarkerModel>();
        foreach (var marker in project.Markers) markers[marker.MeasureIndex] = marker;
        for (var t = 0; t < project.Tracks.Count; t++) if (!project.Tracks[t].IsAudio) WritePart(project, project.Tracks[t], t, markers, xml);

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    /// <summary>The mixer of a track in the units MusicXML has: volume 0..100 percent, pan -90..90 degrees (the mixer's 0..127 and 64 = centre); TabForge converts back exactly.</summary>
    private static void WriteMixer(XmlWriter xml, TrackModel track)
    {
        xml.WriteElementString("volume", MusicXmlExtras.VolumeText(track.Volume));
        xml.WriteElementString("pan", MusicXmlExtras.PanText(track.Pan));
    }

    /// <summary>MusicXML has no field for a track's mute, solo and sends, nor a section's own colour: they go in the score's miscellaneous fields, which every reader may ignore.</summary>
    private static void WriteTabForgeFields(SongProject project, XmlWriter xml)
    {
        xml.WriteStartElement("miscellaneous");
        foreach (var (key, value) in MusicXmlExtras.MetaFields(project))
            if (!string.IsNullOrEmpty(value))
            {
                xml.WriteStartElement("miscellaneous-field");
                xml.WriteAttributeString("name", MusicXmlExtras.MetaPrefix + key);
                xml.WriteString(value);
                xml.WriteEndElement();
            }
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var track = project.Tracks[t];
            if (track.IsAudio) continue;
            xml.WriteStartElement("miscellaneous-field");
            xml.WriteAttributeString("name", MusicXmlExtras.TrackField(PartId(t)));
            xml.WriteString(MusicXmlExtras.TrackFieldValue(track));
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static string PartId(int index) => "P" + (index + 1).ToString(CultureInfo.InvariantCulture);
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool HasTab(TrackModel track) => !track.IsDrumTrack && track.StringTunings.Count > 0 && track.Kind is TrackKind.Guitar or TrackKind.Bass;

    private static int DrumMidi(TabNote note) => Math.Clamp(note.MidiValue > 0 ? note.MidiValue : note.Fret, 0, MidiLimits.Max);

    private static readonly string[] DrumNames =
    {
        "Acoustic Bass Drum", "Kick", "Side Stick", "Snare", "Hand Clap", "Electric Snare", "Low Floor Tom", "Hi-Hat (closed)", "High Floor Tom",
        "Pedal Hi-Hat", "Low Tom", "Hi-Hat (open)", "Low-Mid Tom", "High-Mid Tom", "Crash Cymbal 1", "High Tom", "Ride Cymbal 1", "Chinese Cymbal",
        "Ride Bell", "Tambourine", "Splash Cymbal", "Cowbell", "Crash Cymbal 2", "Vibraslap", "Ride Cymbal 2", "High Bongo", "Low Bongo", "Mute High Conga",
        "Open High Conga", "Low Conga", "High Timbale", "Low Timbale", "High Agogo", "Low Agogo", "Cabasa", "Maracas", "Short Whistle", "Long Whistle",
        "Short Guiro", "Long Guiro", "Claves", "High Wood Block", "Low Wood Block", "Mute Cuica", "Open Cuica", "Mute Triangle", "Open Triangle",
    };

    private static string DrumName(int midi) => midi is >= 35 and <= 81 ? DrumNames[midi - 35] : "Drum " + Num(midi);

    /// <summary>Staff position (the reference's own percussion staff) and notehead of a General MIDI drum sound.</summary>
    private static (string Step, int Octave, string? Notehead) DrumDisplay(int midi)
    {
        var (step, octave) = midi switch
        {
            35 => ("E", 4), 36 => ("F", 4), 37 or 38 or 39 or 54 => ("C", 5), 41 or 45 => ("A", 4), 42 or 46 or 57 => ("G", 5), 43 => ("G", 4),
            44 => ("D", 4), 47 => ("B", 4), 48 or 59 => ("D", 5), 49 or 55 => ("A", 5), 50 => ("E", 5), 51 or 53 or 56 => ("F", 5), 52 => ("B", 5),
            40 => ("C", 5), _ => ("C", 5),
        };
        var head = midi switch
        {
            42 or 44 or 49 or 51 or 52 or 55 or 57 or 59 => "x", 46 => "circle-x", 53 => "diamond", 56 => "triangle", 37 => "x", _ => null,
        };
        return (step, octave, head);
    }

    private static string TypeName(int denominator) => denominator switch
    {
        1 => "whole", 2 => "half", 4 => "quarter", 8 => "eighth", 16 => "16th", 32 => "32nd", _ => "64th",
    };

    private static (string Step, int Alter, int Octave) Pitch(int midi, bool flats)
    {
        midi = Math.Clamp(midi, 0, MidiLimits.Max);
        var pc = midi % 12;
        var octave = midi / 12 - 1;
        return flats ? (FlatSteps[pc], FlatAlters[pc], octave) : (SharpSteps[pc], SharpAlters[pc], octave);
    }
}
