using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// Exports a song as uncompressed MusicXML 4.0 (score-partwise, .musicxml): one part per track with a
/// notation staff and, for fretted tracks, a tab staff carrying the string and fret of every note.
/// Durations, dots, tuplets, ties, rests, grace notes, chords, key / time / tempo, repeats, endings,
/// section marks, lyrics, dynamics (through the shared <see cref="Dynamics"/> table) and the common
/// techniques (hammer-on / pull-off, slides, bends, harmonics, palm mute, accents, staccato) are written.
/// Beat onsets follow the one rule in <see cref="MusicTime"/>, like the .gp export.
/// </summary>
public static class MusicXmlExportService
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
        xml.WriteStartElement("encoding");
        xml.WriteElementString("software", "TabForge");
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteStartElement("part-list");
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var track = project.Tracks[t];
            xml.WriteStartElement("score-part");
            xml.WriteAttributeString("id", PartId(t));
            xml.WriteElementString("part-name", string.IsNullOrWhiteSpace(track.Name) ? "Track " + (t + 1) : track.Name);
            if (IsDrums(track))
            {
                // One instrument per drum sound used, with its General MIDI number writes a drum part.
                var sounds = track.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes).Select(DrumMidi).Distinct().OrderBy(v => v).ToList();
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
                xml.WriteElementString("midi-program", Num(Math.Clamp(track.MidiProgram, 0, 127) + 1));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();

        var markers = new Dictionary<int, string>();
        foreach (var marker in project.Markers) markers[marker.MeasureIndex] = marker.Title;
        for (var t = 0; t < project.Tracks.Count; t++) WritePart(project, project.Tracks[t], t, markers, xml);

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    [ThreadStatic] private static string? _partId;

    private static string PartId(int index) => "P" + (index + 1).ToString(CultureInfo.InvariantCulture);
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsDrums(TrackModel track) => track.Kind == TrackKind.Drums || track.MidiChannel == 9;
    private static bool HasTab(TrackModel track) => !IsDrums(track) && track.StringTunings.Count > 0 && track.Kind is TrackKind.Guitar or TrackKind.Bass;

    private static void WritePart(SongProject project, TrackModel track, int trackIndex, Dictionary<int, string> markers, XmlWriter xml)
    {
        var drums = IsDrums(track);
        var tab = HasTab(track);
        var staves = tab ? 2 : 1;
        var barCount = Math.Max(1, project.Tracks.Max(t => t.Measures.Count));
        xml.WriteStartElement("part");
        xml.WriteAttributeString("id", PartId(trackIndex));

        var key = project.KeySignature; var minor = project.KeySignatureMinor;
        var num = 0; var den = 0;
        var previousDynamic = -1;
        var tupletRun = 0;
        var octaveShift = 0;
        var previousEnding = 0;
        _partId = PartId(trackIndex);
        for (var b = 0; b < barCount; b++)
        {
            var model = b < track.Measures.Count ? track.Measures[b] : null;
            var master = project.Tracks.FirstOrDefault(t => b < t.Measures.Count)?.Measures[b];
            var slots = MusicTime.BarSlots(project, b);
            var barNum = master?.TimeSigNum ?? project.TimeSignatureNumerator;
            var barDen = master?.TimeSigDenom ?? project.TimeSignatureDenominator;
            var barKey = key; var barMinor = minor;
            if (model?.KeySignature is int ks) barKey = ks;
            if (model?.KeySignatureMinor is bool km) barMinor = km;

            xml.WriteStartElement("measure");
            xml.WriteAttributeString("number", Num(b + 1));

            var ending = model?.EndingPasses ?? 0;
            if (model is { RepeatStart: true } || (ending != 0 && ending != previousEnding))
            {
                xml.WriteStartElement("barline");
                xml.WriteAttributeString("location", "left");
                if (ending != 0 && ending != previousEnding)
                {
                    xml.WriteStartElement("ending");
                    xml.WriteAttributeString("number", EndingNumbers(ending));
                    xml.WriteAttributeString("type", "start");
                    xml.WriteEndElement();
                }
                if (model is { RepeatStart: true })
                {
                    xml.WriteStartElement("repeat");
                    xml.WriteAttributeString("direction", "forward");
                    // The pass count is also written on the forward repeat: take it from the bar that closes this repeat.
                    var closing = track.Measures.Skip(b).FirstOrDefault(m => m.RepeatEnd);
                    if (closing is not null) xml.WriteAttributeString("times", Num(Math.Max(2, closing.RepeatCount)));
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }

            var first = b == 0;
            var keyChanged = first || barKey != key || barMinor != minor;
            var timeChanged = first || barNum != num || barDen != den;
            if (first || keyChanged || timeChanged)
            {
                xml.WriteStartElement("attributes");
                if (first) xml.WriteElementString("divisions", Num(Divisions));
                if (keyChanged)
                {
                    xml.WriteStartElement("key");
                    xml.WriteElementString("fifths", Num(Math.Clamp(barKey, -7, 7)));
                    xml.WriteElementString("mode", barMinor ? "minor" : "major");
                    xml.WriteEndElement();
                }
                if (timeChanged)
                {
                    xml.WriteStartElement("time");
                    xml.WriteElementString("beats", Num(barNum));
                    xml.WriteElementString("beat-type", Num(barDen));
                    xml.WriteEndElement();
                }
                if (first)
                {
                    xml.WriteElementString("staves", Num(staves));
                    WriteClef(xml, track, drums, 1, model?.Clef);
                    if (tab) WriteTabClef(xml, 2);
                    if (tab)
                    {
                        xml.WriteStartElement("staff-details");
                        xml.WriteAttributeString("number", "2");
                        xml.WriteElementString("staff-lines", Num(track.StringTunings.Count));
                        // MusicXML numbers tuning lines from the lowest string (line 1) upward.
                        for (var line = 1; line <= track.StringTunings.Count; line++)
                        {
                            var midi = track.StringTunings[track.StringTunings.Count - line];
                            var (step, alter, octave) = Pitch(midi, false);
                            xml.WriteStartElement("staff-tuning");
                            xml.WriteAttributeString("line", Num(line));
                            xml.WriteElementString("tuning-step", step);
                            if (alter != 0) xml.WriteElementString("tuning-alter", Num(alter));
                            xml.WriteElementString("tuning-octave", Num(octave));
                            xml.WriteEndElement();
                        }
                        if (track.Capo > 0) xml.WriteElementString("capo", Num(track.Capo));
                        xml.WriteEndElement();
                    }
                }
                xml.WriteEndElement();
            }
            key = barKey; minor = barMinor; num = barNum; den = barDen;

            var tempo = b == 0 ? master?.TempoChange ?? project.Tempo : master?.TempoChange;
            if (tempo is int bpm) WriteTempo(xml, bpm);
            foreach (var token in PlaybackDirections(model?.Directions)) WriteNavigation(xml, token);
            if (markers.TryGetValue(b, out var section) && !string.IsNullOrWhiteSpace(section))
            {
                xml.WriteStartElement("direction");
                xml.WriteAttributeString("placement", "above");
                xml.WriteStartElement("direction-type");
                xml.WriteElementString("rehearsal", section);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            if (model is not null)
            {
                var voices = new List<List<TabCell>> { model.Cells };
                if (model.Voice2Cells.Any(c => c.Notes.Count > 0)) voices.Add(model.Voice2Cells);
                for (var staff = 1; staff <= staves; staff++)
                    for (var v = 0; v < voices.Count; v++)
                    {
                        var isTab = staff == 2;
                        var voiceNumber = (isTab ? 4 : 0) + v + 1;
                        // String and fret only on the tab staff (the notation staff would get circled string numbers).
                        var advanced = WriteVoice(xml, voices[v], slots, staff, voiceNumber, isTab, drums, project, lyricsHere: v == 0 && !isTab,
                            ref previousDynamic, ref tupletRun, ref octaveShift, writeDynamics: v == 0 && !isTab);
                        var isLast = staff == staves && v == voices.Count - 1;
                        if (!isLast && advanced > 0) WriteBackup(xml, advanced);
                    }
            }
            else
            {
                for (var staff = 1; staff <= staves; staff++)
                {
                    WriteWholeBarRest(xml, slots, staff == 2 ? 5 : 1, staff);
                    if (staff < staves) WriteBackup(xml, (int)Math.Round(slots * DivisionsPerSlot));
                }
            }

            if (model is { RepeatEnd: true } || EndingStops(track, b, ending))
            {
                xml.WriteStartElement("barline");
                xml.WriteAttributeString("location", "right");
                if (EndingStops(track, b, ending))
                {
                    xml.WriteStartElement("ending");
                    xml.WriteAttributeString("number", EndingNumbers(ending));
                    xml.WriteAttributeString("type", "stop");
                    xml.WriteEndElement();
                }
                if (model is { RepeatEnd: true })
                {
                    xml.WriteStartElement("repeat");
                    xml.WriteAttributeString("direction", "backward");
                    xml.WriteAttributeString("times", Num(Math.Max(2, model.RepeatCount)));
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            previousEnding = ending;
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void WriteBackup(XmlWriter xml, int divisions)
    {
        xml.WriteStartElement("backup");
        xml.WriteElementString("duration", Num(divisions));
        xml.WriteEndElement();
    }

    private static bool EndingStops(TrackModel track, int bar, int ending)
    {
        if (ending == 0) return false;
        var next = bar + 1 < track.Measures.Count ? track.Measures[bar + 1].EndingPasses : 0;
        return next != ending;
    }

    private static string EndingNumbers(int mask) =>
        string.Join(",", Enumerable.Range(0, 8).Where(n => (mask & (1 << n)) != 0).Select(n => Num(n + 1)));

    private static void WriteTempo(XmlWriter xml, int bpm)
    {
        // The measure-level <sound> below is for readers that only look for a bare sound element.
        xml.WriteStartElement("direction");
        xml.WriteAttributeString("directive", "yes"); //
        xml.WriteStartElement("direction-type");
        xml.WriteStartElement("metronome");
        xml.WriteElementString("beat-unit", "quarter");
        xml.WriteElementString("per-minute", Num(bpm));
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteStartElement("sound");
        xml.WriteAttributeString("tempo", Num(bpm));
        xml.WriteEndElement();
    }

    private static void WriteClef(XmlWriter xml, TrackModel track, bool drums, int staff, string? measureClef)
    {
        xml.WriteStartElement("clef");
        if (HasTab(track) || staff > 1) xml.WriteAttributeString("number", Num(staff));
        if (drums) { xml.WriteElementString("sign", "percussion"); xml.WriteElementString("line", "2"); }
        else
        {
            var clef = string.IsNullOrEmpty(measureClef) ? (track.Kind == TrackKind.Bass ? Clefs.Bass : Clefs.Guitar) : measureClef;
            switch (clef)
            {
                case Clefs.Bass:
                    xml.WriteElementString("sign", "F"); xml.WriteElementString("line", "4");
                    if (track.Kind == TrackKind.Bass) xml.WriteElementString("clef-octave-change", "-1");
                    break;
                case Clefs.Alto:
                    xml.WriteElementString("sign", "C"); xml.WriteElementString("line", "3");
                    break;
                case Clefs.Treble:
                    xml.WriteElementString("sign", "G"); xml.WriteElementString("line", "2");
                    break;
                default:
                    xml.WriteElementString("sign", "G"); xml.WriteElementString("line", "2");
                    xml.WriteElementString("clef-octave-change", "-1");
                    break;
            }
        }
        xml.WriteEndElement();
    }

    private static void WriteTabClef(XmlWriter xml, int staff)
    {
        xml.WriteStartElement("clef");
        xml.WriteAttributeString("number", Num(staff));
        xml.WriteElementString("sign", "TAB");
        xml.WriteElementString("line", "5");
        xml.WriteEndElement();
    }

    private static void WriteWholeBarRest(XmlWriter xml, int slots, int voice, int staff)
    {
        xml.WriteStartElement("note");
        xml.WriteStartElement("rest");
        xml.WriteAttributeString("measure", "yes");
        xml.WriteEndElement();
        xml.WriteElementString("duration", Num((int)Math.Round(slots * DivisionsPerSlot)));
        xml.WriteElementString("voice", Num(voice));
        if (slots == 16) xml.WriteElementString("type", "whole");
        xml.WriteElementString("staff", Num(staff));
        xml.WriteEndElement();
    }

    /// <summary>
    /// Writes one voice of one bar to one staff; returns the divisions the voice advanced, which is always the
    /// bar's length: a bar with no notes is one whole-bar rest, a short bar is padded with a trailing forward,
    /// and an overfull bar is cut at the barline (a cell that starts after it is dropped, one that crosses it is shortened).
    /// </summary>
    private static int WriteVoice(XmlWriter xml, List<TabCell> cells, int barSlots, int staff, int voice, bool isTab, bool drums,
        SongProject project, bool lyricsHere, ref int previousDynamic, ref int tupletRun, ref int octaveShift, bool writeDynamics)
    {
        var cursor = 0.0;
        var written = 0;
        var pendingHopo = new Dictionary<int, string>();
        var pendingSlide = new HashSet<int>();
        var barDivisions = (int)Math.Round(barSlots * DivisionsPerSlot);
        if (!cells.Any(c => c.Notes.Count > 0))
        {
            WriteWholeBarRest(xml, barSlots, voice, staff);
            return barDivisions;
        }
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var hasNotes = cell.Notes.Count > 0;
            if (!hasNotes && !cell.IsRest) continue;
            var start = cell.RhythmicPosition ?? Math.Max(i, cursor);
            if (start >= barSlots - 0.01) break;
            if (start > cursor + 0.01)
            {
                var gap = (int)Math.Round((start - cursor) * DivisionsPerSlot);
                if (gap > 0)
                {
                    xml.WriteStartElement("forward");
                    xml.WriteElementString("duration", Num(gap));
                    xml.WriteEndElement();
                    written += gap;
                }
            }
            var duration = Math.Max(1, (int)Math.Round(MusicTime.CellSlots(cell) * DivisionsPerSlot));
            if (written + duration > barDivisions) duration = Math.Max(1, barDivisions - written);
            cursor = start + MusicTime.CellSlots(cell);

            var principal = cell.Notes.Where(n => !n.IsGraceNote).ToList();
            var graces = cell.Notes.Where(n => n.IsGraceNote).ToList();
            if (principal.Count == 0 && graces.Count > 0 && !cell.IsRest) { principal = graces; graces = new List<TabNote>(); }

            if (writeDynamics && principal.Count > 0)
            {
                var dynamic = TabForge.Models.Dynamics.NearestIndex(principal[0].Velocity);
                if (dynamic != previousDynamic)
                {
                    previousDynamic = dynamic;
                    xml.WriteStartElement("direction");
                    xml.WriteAttributeString("placement", "below");
                    xml.WriteStartElement("direction-type");
                    xml.WriteStartElement("dynamics");
                    xml.WriteElementString(TabForge.Models.Dynamics.Names[dynamic], "");
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteElementString("staff", Num(staff));
                    xml.WriteEndElement();
                }
            }
            if (staff == 1 && voice == 1 && !string.IsNullOrWhiteSpace(cell.Text))
            {
                xml.WriteStartElement("direction");
                xml.WriteAttributeString("placement", "above");
                xml.WriteStartElement("direction-type");
                xml.WriteElementString("words", cell.Text);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            if (writeDynamics && cell.OctaveShiftSemitones != octaveShift)
            {
                if (octaveShift != 0) WriteOctaveShift(xml, 0);
                if (cell.OctaveShiftSemitones != 0) WriteOctaveShift(xml, cell.OctaveShiftSemitones);
                octaveShift = cell.OctaveShiftSemitones;
            }
            if (writeDynamics && !string.IsNullOrWhiteSpace(cell.ChordName)) WriteHarmony(xml, cell.ChordName!);

            foreach (var grace in graces)
                WriteNote(xml, cell, grace, isChord: false, isGrace: true, duration, staff, voice, isTab, drums, project, false, null, ref tupletRun, pendingHopo, pendingSlide, cells, i);

            if (principal.Count == 0)
            {
                WriteNote(xml, cell, null, isChord: false, isGrace: false, duration, staff, voice, isTab, drums, project, lyricsHere, cell.Lyrics, ref tupletRun, pendingHopo, pendingSlide, cells, i);
            }
            else
            {
                for (var n = 0; n < principal.Count; n++)
                    WriteNote(xml, cell, principal[n], isChord: n > 0, isGrace: false, duration, staff, voice, isTab, drums, project, lyricsHere && n == 0, cell.Lyrics, ref tupletRun, pendingHopo, pendingSlide, cells, i);
            }
            written += duration;
        }
        if (written < barDivisions)
        {
            xml.WriteStartElement("forward");
            xml.WriteElementString("duration", Num(barDivisions - written));
            xml.WriteEndElement();
        }
        return barDivisions;
    }

    private sealed class NoteLinks
    {
        public string? HopoStop, HopoStart;
        public bool SlideStart, SlideStop;
        /// <summary>The slur paired with a hammer-on / pull-off / legato slide: start, stop or continue.</summary>
        public string? Slur => (HopoStop is not null || SlideStop) && (HopoStart is not null || SlideStart) ? "continue"
            : HopoStart is not null || SlideStart ? "start" : HopoStop is not null || SlideStop ? "stop" : null;
    }

    private static NoteLinks LinkNote(TabNote note, List<TabCell> cells, int cellIndex, Dictionary<int, string> pendingHopo, HashSet<int> pendingSlide)
    {
        var t = note.Techniques;
        var stringIndex = note.StringIndex;
        var links = new NoteLinks();
        if (pendingHopo.Remove(stringIndex, out var stopKind)) links.HopoStop = stopKind;
        if (t.Contains("HOPOOrigin") || t.Contains("HOPO") && !t.Contains("HOPODestination"))
        {
            var kind = "hammer-on";
            for (var j = cellIndex + 1; j < cells.Count; j++)
            {
                var next = cells[j].Notes.FirstOrDefault(n => n.StringIndex == stringIndex && !n.IsGraceNote);
                if (next is null) continue;
                kind = next.MidiValue < note.MidiValue ? "pull-off" : "hammer-on";
                break;
            }
            pendingHopo[stringIndex] = kind;
            links.HopoStart = kind;
        }
        if (pendingSlide.Remove(stringIndex)) links.SlideStop = true;
        if (t.Contains("LegatoSlide") || t.Contains("ShiftSlide") || t.Contains("Slide"))
        {
            pendingSlide.Add(stringIndex);
            links.SlideStart = true;
        }
        return links;
    }

    private static void WriteNote(XmlWriter xml, TabCell cell, TabNote? note, bool isChord, bool isGrace, int duration, int staff, int voice,
        bool isTab, bool drums, SongProject project, bool lyrics, string? lyricText, ref int tupletRun,
        Dictionary<int, string> pendingHopo, HashSet<int> pendingSlide, List<TabCell> cells, int cellIndex)
    {
        xml.WriteStartElement("note");
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

        var tieStop = note is not null && (note.Tied || cell.IsTied);
        var tieStart = note is not null && note.Techniques.Contains("Tie");
        if (tieStop) { xml.WriteStartElement("tie"); xml.WriteAttributeString("type", "stop"); xml.WriteEndElement(); }
        if (tieStart) { xml.WriteStartElement("tie"); xml.WriteAttributeString("type", "start"); xml.WriteEndElement(); }
        if (note is not null && drums)
        {
            xml.WriteStartElement("instrument");
            xml.WriteAttributeString("id", _partId + "-I" + Num(DrumMidi(note)));
            xml.WriteEndElement();
        }
        xml.WriteElementString("voice", Num(voice));
        xml.WriteElementString("type", TypeName(isGrace && note is not null && note.GraceDurationSlots is > 0 and < 1 ? 32 : cell.DurationDenominator));
        if (!isGrace) for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++) xml.WriteElementString("dot", "");

        var (tupNum, tupDen) = cell.Tuplet;
        var inTuplet = !isGrace && tupNum > 0;
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

        string? tupletType = null;
        if (inTuplet && !isChord)
        {
            if (tupletRun == 0) tupletType = "start";
            tupletRun++;
            if (tupletRun >= tupNum) { tupletType ??= "stop"; tupletRun = 0; }
        }
        else if (!inTuplet && !isChord && !isGrace) tupletRun = 0;

        var t = note?.Techniques;
        var links = note is not null && !drums ? LinkNote(note, cells, cellIndex, pendingHopo, pendingSlide) : null;
        var first = note is not null && !isGrace && !isChord;
        var notations = new List<Action>();
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

        // Articulations: accent / staccato / tenuto, and the standard slide-in and slide-out marks (scoop, plop, doit, falloff).
        var articulations = new List<string>();
        if (first && cell.Accent == 1) articulations.Add("accent");
        if (first && cell.Accent >= 2) articulations.Add("strong-accent");
        if (first && cell.Staccato) articulations.Add("staccato");
        if (first && cell.Tenuto) articulations.Add("tenuto");
        if (t is not null && !isChord)
        {
            if (t.Contains("SlideInBelow")) articulations.Add("scoop");
            if (t.Contains("SlideInAbove")) articulations.Add("plop");
            if (t.Contains("SlideOutUp")) articulations.Add("doit");
            if (t.Contains("SlideOutDown")) articulations.Add("falloff");
        }
        if (articulations.Count > 0)
            notations.Add(() =>
            {
                xml.WriteStartElement("articulations");
                foreach (var name in articulations) xml.WriteElementString(name, "");
                xml.WriteEndElement();
            });

        // Ornaments: tremolo picking (1/8 = 1 slash, 1/16 = 2, 1/32 = 3; unset means 1/8, as in the .gp export) and trills.
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

        if (note is not null && !drums)
        {
            var stringNumber = note.StringIndex + 1; // MusicXML string 1 is the highest string, like the model's index 0
            var fret = note.Fret;
            notations.Add(() =>
            {
                xml.WriteStartElement("technical");
                WriteTechniques(xml, note, links!, first);
                if (isTab)
                {
                    xml.WriteElementString("string", Num(stringNumber));
                    xml.WriteElementString("fret", Num(fret));
                }
                xml.WriteEndElement();
            });
            if (links!.Slur is { } slur)
                notations.Add(() => { xml.WriteStartElement("slur"); xml.WriteAttributeString("type", slur); xml.WriteEndElement(); });
            if (first && (t!.Contains("BrushDown") || t.Contains("BrushUp") || t.Contains("ArpeggioDown") || t.Contains("ArpeggioUp")))
            {
                var direction = t.Contains("BrushDown") || t.Contains("ArpeggioDown") ? "down" : "up";
                notations.Add(() => xml.WriteProcessingInstruction("GP", $"<root><brush type=\"{direction}\"/></root>"));
            }
        }
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
        if (note is not null && !drums)
        {
            // Guitar Pro reads palm mute from <play><mute>palm</mute></play> and keeps let ring, vibrato and whammy in its own <?GP?> data.
            if (TechniqueNames.HasPalmMute(t!))
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
        xml.WriteEndElement();
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

    private static int DrumMidi(TabNote note) => Math.Clamp(note.MidiValue > 0 ? note.MidiValue : note.Fret, 0, 127);

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
        midi = Math.Clamp(midi, 0, 127);
        var pc = midi % 12;
        var octave = midi / 12 - 1;
        return flats ? (FlatSteps[pc], FlatAlters[pc], octave) : (SharpSteps[pc], SharpAlters[pc], octave);
    }
}
