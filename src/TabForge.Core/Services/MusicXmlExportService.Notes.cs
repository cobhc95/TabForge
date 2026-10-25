using System.Globalization;
using System.Xml;
using TabForge.Models;

namespace TabForge.Services;

// Owns: MusicXML note encoding: note elements, techniques, bends, harmonies and navigation marks.
public static partial class MusicXmlExportService
{
    private static void WriteNote(XmlWriter xml, string partId, TabCell cell, TabNote? note, bool isChord, bool isGrace, int duration, int staff, int voice,
        bool isTab, bool drums, SongProject project, bool lyrics, string? lyricText, ref int tupletRun,
        Dictionary<int, string> pendingHopo, HashSet<int> pendingSlide, List<TabCell> cells, int cellIndex)
    {
        xml.WriteStartElement("note");
        WriteNoteHead(xml, partId, cell, note, isChord, isGrace, duration, voice, drums, project, out var tieStop, out var tieStart);

        var (tupNum, tupDen) = cell.Tuplet;
        var inTuplet = !isGrace && tupNum > 0;
        WriteTupletAndNotehead(xml, note, drums, inTuplet, tupNum, tupDen, staff);
        var tupletType = TupletMark(inTuplet, isChord, isGrace, tupNum, ref tupletRun);

        var t = note?.Techniques;
        var links = note is not null && !drums ? LinkNote(note, cells, cellIndex, pendingHopo, pendingSlide) : null;
        var first = note is not null && !isGrace && !isChord;
        var notations = new List<Action>();
        AddTieAndTupletNotations(xml, notations, cell, isGrace, isChord, tieStop, tieStart, tupletType);
        AddArticulationNotations(xml, notations, cell, t, first, isChord);
        AddOrnamentNotations(xml, notations, cell, note, t, first, isGrace);
        if (note is not null && !drums) AddTechnicalNotations(xml, notations, note, t!, links!, first, isTab);
        if (notations.Count > 0)
        {
            xml.WriteStartElement("notations");
            foreach (var write in notations) write();
            xml.WriteEndElement();
        }
        if (lyrics && !string.IsNullOrWhiteSpace(lyricText))
        {
            xml.WriteStartElement("lyric");
            xml.WriteAttributeString("number", "1");
            xml.WriteElementString("syllabic", "single");
            xml.WriteElementString("text", lyricText.Split('\n')[0].Trim());
            xml.WriteEndElement();
        }
        if (note is not null && !drums) WriteGuitarProData(xml, cell, t!, first);
        xml.WriteEndElement();
    }

    /// <summary>The start of a note element: grace and chord marks, pitch (or rest / unpitched), duration, ties, instrument, voice, type and dots.</summary>
    private static void WriteNoteHead(XmlWriter xml, string partId, TabCell cell, TabNote? note, bool isChord, bool isGrace, int duration, int voice, bool drums, SongProject project, out bool tieStop, out bool tieStart)
    {
        if (isGrace)
        {
            xml.WriteStartElement("grace");
            if (note is { GraceBeforeBeat: true }) xml.WriteAttributeString("slash", "yes"); // a slashed (acciaccatura) grace note
            xml.WriteEndElement();
        }
        if (isChord) { xml.WriteStartElement("chord"); xml.WriteEndElement(); }
        if (note is null)
        {
            xml.WriteElementString("rest", "");
        }
        else if (drums)
        {
            var (displayStep, displayOctave, _) = DrumDisplay(DrumMidi(note));
            xml.WriteStartElement("unpitched");
            xml.WriteElementString("display-step", displayStep);
            xml.WriteElementString("display-octave", Num(displayOctave));
            xml.WriteEndElement();
        }
        else
        {
            var midi = note.MidiValue > 0 ? note.MidiValue : 40;
            var (step, alter, octave) = Pitch(midi, project.KeySignature < 0);
            xml.WriteStartElement("pitch");
            xml.WriteElementString("step", step);
            if (alter != 0) xml.WriteElementString("alter", Num(alter));
            xml.WriteElementString("octave", Num(octave));
            xml.WriteEndElement();
        }
        if (!isGrace) xml.WriteElementString("duration", Num(duration));

        tieStop = note is not null && (note.Tied || cell.IsTied);
        tieStart = note is not null && note.Techniques.Contains("Tie");
        if (tieStop) { xml.WriteStartElement("tie"); xml.WriteAttributeString("type", "stop"); xml.WriteEndElement(); }
        if (tieStart) { xml.WriteStartElement("tie"); xml.WriteAttributeString("type", "start"); xml.WriteEndElement(); }
        if (note is not null && drums)
        {
            xml.WriteStartElement("instrument");
            xml.WriteAttributeString("id", partId + "-I" + Num(DrumMidi(note)));
            xml.WriteEndElement();
        }
        xml.WriteElementString("voice", Num(voice));
        xml.WriteElementString("type", TypeName(isGrace && note is not null && note.GraceDurationSlots is > 0 and < 1 ? 32 : cell.DurationDenominator));
        if (!isGrace) for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++) xml.WriteElementString("dot", "");
    }

    /// <summary>The time modification of a tuplet note, the notehead (dead, ghost, drum) and the staff number.</summary>
    private static void WriteTupletAndNotehead(XmlWriter xml, TabNote? note, bool drums, bool inTuplet, int tupNum, int tupDen, int staff)
    {
        if (inTuplet)
        {
            xml.WriteStartElement("time-modification");
            xml.WriteElementString("actual-notes", Num(tupNum));
            xml.WriteElementString("normal-notes", Num(tupDen));
            xml.WriteEndElement();
        }
        if (note is { Dead: true }) xml.WriteElementString("notehead", "x");
        else if (note is { Ghost: true }) { xml.WriteStartElement("notehead"); xml.WriteAttributeString("parentheses", "yes"); xml.WriteString("normal"); xml.WriteEndElement(); }
        else if (note is not null && drums && DrumDisplay(DrumMidi(note)).Notehead is { } drumHead) xml.WriteElementString("notehead", drumHead);
        xml.WriteElementString("staff", Num(staff));
    }

    /// <summary>The tuplet bracket mark this note carries ("start", "stop" or none), counting the notes of the run.</summary>
    private static string? TupletMark(bool inTuplet, bool isChord, bool isGrace, int tupNum, ref int tupletRun)
    {
        string? tupletType = null;
        if (inTuplet && !isChord)
        {
            if (tupletRun == 0) tupletType = "start";
            tupletRun++;
            if (tupletRun >= tupNum) { tupletType ??= "stop"; tupletRun = 0; }
        }
        else if (!inTuplet && !isChord && !isGrace) tupletRun = 0;
        return tupletType;
    }

    private static void AddTieAndTupletNotations(XmlWriter xml, List<Action> notations, TabCell cell, bool isGrace, bool isChord, bool tieStop, bool tieStart, string? tupletType)
    {
        if (tieStop) notations.Add(() => { xml.WriteStartElement("tied"); xml.WriteAttributeString("type", "stop"); xml.WriteEndElement(); });
        if (tieStart) notations.Add(() => { xml.WriteStartElement("tied"); xml.WriteAttributeString("type", "start"); xml.WriteEndElement(); });
        if (tupletType is not null)
        {
            var kind = tupletType;
            notations.Add(() =>
            {
                xml.WriteStartElement("tuplet");
                xml.WriteAttributeString("number", "1");
                xml.WriteAttributeString("type", kind);
                xml.WriteAttributeString("bracket", "yes");
                xml.WriteAttributeString("placement", "below");
                xml.WriteEndElement();
            });
        }
        if (!isGrace && cell.Fermata && !isChord) notations.Add(() => { xml.WriteStartElement("fermata"); xml.WriteEndElement(); });
    }

    private static void AddArticulationNotations(XmlWriter xml, List<Action> notations, TabCell cell, HashSet<string>? t, bool first, bool isChord)
    {
        var articulations = new List<string>();
        if (first && cell.Accent == 1) articulations.Add("accent");
        if (first && cell.Accent >= 2) articulations.Add("strong-accent");
        if (first && cell.Staccato) articulations.Add("staccato");
        if (first && cell.Tenuto) articulations.Add("tenuto");
        if (t is not null && !isChord)
        {
            foreach (var row in TechniqueInfo.MusicXmlArticulations)
                if (t.Contains(row.Name)) articulations.Add(row.MusicXmlArticulation);
        }
        if (articulations.Count > 0)
            notations.Add(() =>
            {
                xml.WriteStartElement("articulations");
                foreach (var name in articulations) xml.WriteElementString(name, "");
                xml.WriteEndElement();
            });
    }

    private static void AddOrnamentNotations(XmlWriter xml, List<Action> notations, TabCell cell, TabNote? note, HashSet<string>? t, bool first, bool isGrace)
    {
        var slashes = cell.TremoloPickDenominator >= 8 ? Math.Clamp((int)Math.Log2(cell.TremoloPickDenominator) - 2, 1, 4)
            : note is not null && t!.Contains("TremoloPick") ? 1 : 0;
        var trill = note is not null && t!.Contains("Trill");
        if ((slashes > 0 && first) || (trill && !isGrace))
            notations.Add(() =>
            {
                xml.WriteStartElement("ornaments");
                if (slashes > 0 && first) xml.WriteElementString("tremolo", Num(slashes));
                if (trill)
                {
                    xml.WriteStartElement("trill-mark");
                    var span = note!.TrillTargetMidi > 0 ? Math.Abs(note.TrillTargetMidi - note.MidiValue) : 2;
                    xml.WriteAttributeString("trill-step", span == 1 ? "half" : "whole");
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            });
    }

    private static void AddTechnicalNotations(XmlWriter xml, List<Action> notations, TabNote note, HashSet<string> t, NoteLinks links, bool first, bool isTab)
    {
        var stringNumber = note.StringIndex + 1; // MusicXML string 1 is the highest string, like the model's index 0
        var fret = note.Fret;
        notations.Add(() =>
        {
            xml.WriteStartElement("technical");
            WriteTechniques(xml, note, links, first);
            if (isTab)
            {
                xml.WriteElementString("string", Num(stringNumber));
                xml.WriteElementString("fret", Num(fret));
            }
            xml.WriteEndElement();
        });
        if (links.Slur is { } slur)
            notations.Add(() => { xml.WriteStartElement("slur"); xml.WriteAttributeString("type", slur); xml.WriteEndElement(); });
        if (first && (t.Contains("BrushDown") || t.Contains("BrushUp") || t.Contains("ArpeggioDown") || t.Contains("ArpeggioUp")))
        {
            var direction = t.Contains("BrushDown") || t.Contains("ArpeggioDown") ? "down" : "up";
            notations.Add(() => xml.WriteProcessingInstruction("GP", $"<root><brush type=\"{direction}\"/></root>"));
        }
    }

    /// <summary>Palm mute, let ring, vibrato and whammy bar data, which Guitar Pro reads from its own processing instructions.</summary>
    private static void WriteGuitarProData(XmlWriter xml, TabCell cell, HashSet<string> t, bool first)
    {
        // Guitar Pro reads palm mute from <play><mute>palm</mute></play> and keeps let ring, vibrato and whammy in its own <?GP?> data.
        if (TechniqueNames.HasPalmMute(t))
        {
            xml.WriteStartElement("play");
            xml.WriteElementString("mute", "palm");
            xml.WriteEndElement();
        }
        if (t!.Contains("LetRing")) xml.WriteProcessingInstruction("GP", "<root><letring/></root>");
        if (t.Contains("Vibrato") || t.Contains("WideVibrato"))
            xml.WriteProcessingInstruction("GP", $"<root><vibrato type=\"{(t.Contains("WideVibrato") ? "Wide" : "Slight")}\"/></root>");
        if (first && cell.WhammyPoints.Count > 0)
        {
            var pts = cell.WhammyPoints.OrderBy(p => p.Offset).ToList();
            var middle = pts.Count > 2 ? pts.Skip(1).Take(pts.Count - 2).MaxBy(p => Math.Abs(p.Value))!.Value : (pts[0].Value + pts[^1].Value) / 2;
            string Cents(double v) => (Math.Round(v * 25)).ToString("0", CultureInfo.InvariantCulture);
            xml.WriteProcessingInstruction("GP", $"<root><WhammyBar data='{{\"destination\":\"{Cents(pts[^1].Value)}\",\"middle\":\"{Cents(middle)}\",\"origin\":\"{Cents(pts[0].Value)}\"}}'/></root>");
        }
    }

    private static void WriteTechniques(XmlWriter xml, TabNote note, NoteLinks links, bool first)
    {
        var t = note.Techniques;
        if (links.HopoStop is { } stopKind)
        {
            xml.WriteStartElement(stopKind);
            xml.WriteAttributeString("number", "1");
            xml.WriteAttributeString("type", "stop");
            xml.WriteEndElement();
        }
        if (links.HopoStart is { } startKind)
        {
            xml.WriteStartElement(startKind);
            xml.WriteAttributeString("number", "1");
            xml.WriteAttributeString("type", "start");
            xml.WriteString(startKind == "hammer-on" ? "H" : "P");
            xml.WriteEndElement();
        }
        if (links.SlideStop)
        {
            xml.WriteStartElement("slide");
            xml.WriteAttributeString("number", "4");
            xml.WriteAttributeString("type", "stop");
            xml.WriteEndElement();
        }
        if (links.SlideStart)
        {
            xml.WriteStartElement("slide");
            xml.WriteAttributeString("number", "4");
            xml.WriteAttributeString("type", "start");
            xml.WriteEndElement();
        }
        WriteBend(xml, note.BendPoints);
        // The standard harmonic encoding: natural and artificial name the base pitch, tapped the touching pitch, pinch the sounding pitch.
        if (t.Contains("PinchHarmonic") || t.Contains("ArtificialHarmonic") || t.Contains("TapHarmonic"))
        {
            xml.WriteStartElement("harmonic");
            xml.WriteElementString("artificial", "");
            xml.WriteElementString(t.Contains("TapHarmonic") ? "touching-pitch" : t.Contains("PinchHarmonic") ? "sounding-pitch" : "base-pitch", "");
            xml.WriteEndElement();
        }
        else if (t.Contains("Harmonic") || t.Contains("SemiHarmonic") || t.Contains("FeedbackHarmonic"))
        {
            xml.WriteStartElement("harmonic");
            xml.WriteElementString("natural", "");
            xml.WriteElementString("base-pitch", "");
            xml.WriteEndElement();
        }
        if (note.HarmonicFret is { } harmonicFret) xml.WriteElementString("other-technical", "harmonic fret " + harmonicFret.ToString("0.##", CultureInfo.InvariantCulture));
        if (t.Contains("Tapping")) xml.WriteElementString("tap", "");
        if (t.Contains("PickDown")) xml.WriteElementString("down-bow", "");
        if (t.Contains("PickUp")) xml.WriteElementString("up-bow", "");
        if (note.LeftHandFinger is >= 1 and <= 4 and var finger) xml.WriteElementString("fingering", Num(finger));
        if (note.RightHandFinger is >= 0 and <= 3 and var pluck) xml.WriteElementString("pluck", "pima"[pluck].ToString());
    }

    /// <summary>
    /// Bend encoding: bend-alter in semitones (the model counts quarter-tone steps, so value / 2), a pre-bend
    /// flagged with pre-bend, and a release as a second bend element ending on the released pitch.
    /// </summary>
    private static void WriteBend(XmlWriter xml, List<BendPointModel> points)
    {
        if (points.Count == 0) return;
        var ordered = points.OrderBy(p => p.Offset).ToList();
        var first = ordered[0].Value; var last = ordered[^1].Value; var peak = ordered.Max(p => p.Value);
        string Semitones(double v) => (v / 2.0).ToString("0.##", CultureInfo.InvariantCulture);
        if (first > 0)
        {
            xml.WriteStartElement("bend");
            xml.WriteElementString("bend-alter", Semitones(first));
            xml.WriteElementString("pre-bend", "");
            xml.WriteEndElement();
            if (last < first) WriteRelease(xml, Semitones(last));
        }
        else if (peak > 0)
        {
            xml.WriteStartElement("bend");
            xml.WriteElementString("bend-alter", Semitones(peak));
            xml.WriteEndElement();
            if (last < peak) WriteRelease(xml, Semitones(last));
        }
    }

    private static void WriteRelease(XmlWriter xml, string semitones)
    {
        xml.WriteStartElement("bend");
        xml.WriteElementString("bend-alter", semitones);
        xml.WriteElementString("release", "");
        xml.WriteEndElement();
    }

    private static void WriteOctaveShift(XmlWriter xml, int semitones)
    {
        xml.WriteStartElement("direction");
        xml.WriteStartElement("direction-type");
        xml.WriteStartElement("octave-shift");
        if (semitones == 0) xml.WriteAttributeString("type", "stop");
        else
        {
            // 8va / 15ma raise the sound: the sign says "down" (the written notes sit below the sounding pitch); 8vb / 15mb say "up".
            xml.WriteAttributeString("type", semitones > 0 ? "down" : "up");
            xml.WriteAttributeString("size", Math.Abs(semitones) >= 24 ? "15" : "8");
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>A chord name such as "F#m7" as a harmony: the root, and the rest as the kind's text.</summary>
    private static void WriteHarmony(XmlWriter xml, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name[0] is < 'A' or > 'G') return;
        var alter = name.Length > 1 && name[1] == '#' ? 1 : name.Length > 1 && name[1] == 'b' ? -1 : 0;
        var rest = name[(alter == 0 ? 1 : 2)..];
        xml.WriteStartElement("harmony");
        xml.WriteStartElement("root");
        xml.WriteElementString("root-step", name[0].ToString());
        xml.WriteElementString("root-alter", Num(alter));
        xml.WriteEndElement();
        xml.WriteStartElement("kind");
        xml.WriteAttributeString("text", rest);
        xml.WriteString("other");
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>Segno, coda, To Coda, Fine, D.C. and D.S. marks, with the sound attributes readers use to follow them.</summary>
    private static IEnumerable<string> PlaybackDirections(string? directions) => Playback.PlaybackOrder.DirectionTokens(directions);

    private static void WriteNavigation(XmlWriter xml, string token)
    {
        (string? Symbol, string? Words, string? Sound, string? Value) mark = token switch
        {
            "Segno" => ("segno", null, "segno", "1"),
            "SegnoSegno" => ("segno", null, "segno", "2"),
            "Coda" or "DoubleCoda" => ("coda", null, "coda", "1"),
            "ToCoda" or "ToDoubleCoda" => (null, "To Coda", "tocoda", "1"),
            "Fine" => (null, "Fine", "fine", "yes"),
            "DaCapo" => (null, "D.C. al Fine", "dacapo", "yes"),
            "DaCapoAlCoda" => (null, "D.C. al Coda", "dacapo", "yes"),
            "DalSegno" => (null, "D.S. al Fine", "dalsegno", "1"),
            "DalSegnoAlCoda" => (null, "D.S. al Coda", "dalsegno", "1"),
            "DalSegnoSegno" => (null, "D.S.S. al Fine", "dalsegno", "2"),
            "DalSegnoSegnoAlCoda" => (null, "D.S.S. al Coda", "dalsegno", "2"),
            _ => default,
        };
        if (mark.Sound is null) return;
        xml.WriteStartElement("direction");
        xml.WriteAttributeString("placement", "above");
        xml.WriteStartElement("direction-type");
        if (mark.Symbol is not null) xml.WriteElementString(mark.Symbol, "");
        else xml.WriteElementString("words", mark.Words);
        xml.WriteEndElement();
        xml.WriteStartElement("sound");
        xml.WriteAttributeString(mark.Sound, mark.Value);
        xml.WriteEndElement();
        xml.WriteEndElement();
    }
}
