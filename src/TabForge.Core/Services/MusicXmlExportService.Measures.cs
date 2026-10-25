using System.Xml;
using TabForge.Models;

namespace TabForge.Services;

// Owns: MusicXML measure encoding: one voice of one bar on one staff, and the note-to-note link state.
public static partial class MusicXmlExportService
{
    /// <summary>
    /// Writes one voice of one bar to one staff; returns the divisions the voice advanced, which is always the
    /// bar's length: a bar with no notes is one whole-bar rest, a short bar is padded with a trailing forward,
    /// and an overfull bar is cut at the barline (a cell that starts after it is dropped, one that crosses it is shortened).
    /// </summary>
    private static int WriteVoice(XmlWriter xml, string partId, List<TabCell> cells, int barSlots, int staff, int voice, bool isTab, bool drums,
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

            if (writeDynamics && principal.Count > 0) WriteDynamicChange(xml, principal[0], staff, ref previousDynamic);
            WriteCellDirections(xml, cell, staff, voice, writeDynamics, ref octaveShift);

            foreach (var grace in graces)
                WriteNote(xml, partId, cell, grace, isChord: false, isGrace: true, duration, staff, voice, isTab, drums, project, false, null, ref tupletRun, pendingHopo, pendingSlide, cells, i);

            if (principal.Count == 0)
            {
                WriteNote(xml, partId, cell, null, isChord: false, isGrace: false, duration, staff, voice, isTab, drums, project, lyricsHere, cell.Lyrics, ref tupletRun, pendingHopo, pendingSlide, cells, i);
            }
            else
            {
                for (var n = 0; n < principal.Count; n++)
                    WriteNote(xml, partId, cell, principal[n], isChord: n > 0, isGrace: false, duration, staff, voice, isTab, drums, project, lyricsHere && n == 0, cell.Lyrics, ref tupletRun, pendingHopo, pendingSlide, cells, i);
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

    /// <summary>A dynamics direction below the staff when the loudness of the beat steps to another marking.</summary>
    private static void WriteDynamicChange(XmlWriter xml, TabNote first, int staff, ref int previousDynamic)
    {
            var dynamic = TabForge.Models.Dynamics.NearestIndex(first.Velocity);
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

    /// <summary>The text of a beat above the staff (voice 1 of the first staff), an octave shift and the chord name.</summary>
    private static void WriteCellDirections(XmlWriter xml, TabCell cell, int staff, int voice, bool writeDynamics, ref int octaveShift)
    {
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
}
