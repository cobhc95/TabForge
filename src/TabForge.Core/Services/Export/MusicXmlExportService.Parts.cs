using System.Xml;
using TabForge.Models;

namespace TabForge.Services;

// Owns: MusicXML part encoding: score-part elements, backup, tempo, clefs and whole-bar rests.
public static partial class MusicXmlExportService
{
    private static void WritePart(SongProject project, TrackModel track, int trackIndex, Dictionary<int, MarkerModel> markers, XmlWriter xml)
    {
        var drums = track.IsDrumTrack;
        var tab = HasTab(track);
        var staves = tab ? 2 : 1;
        var barCount = Math.Max(1, project.NotationTracks.Max(t => t.Measures.Count));
        xml.WriteStartElement("part");
        xml.WriteAttributeString("id", PartId(trackIndex));

        var key = project.KeySignature; var minor = project.KeySignatureMinor;
        var clef = track.Measures.Count > 0 && !string.IsNullOrEmpty(track.Measures[0].Clef) ? track.Measures[0].Clef : Clefs.Guitar;
        var num = 0; var den = 0;
        var previousDynamic = -1;
        var tupletRun = 0;
        var octaveShift = 0;
        var previousEnding = 0;
        var partId = PartId(trackIndex);
        for (var b = 0; b < barCount; b++)
        {
            var model = b < track.Measures.Count ? track.Measures[b] : null;
            var master = project.NotationTracks.FirstOrDefault(t => b < t.Measures.Count)?.Measures[b];
            var slots = MusicTime.BarSlots(project, b);
            var barNum = master?.TimeSigNum ?? project.TimeSignatureNumerator;
            var barDen = master?.TimeSigDenom ?? project.TimeSignatureDenominator;
            // A bar without a key of its own is in the song's key, as the score shows it (BarSignatures.KeyAt); a bar past this track's end follows the previous one.
            var barKey = model is null ? key : model.KeySignature ?? project.KeySignature;
            var barMinor = model is null ? minor : model.KeySignatureMinor ?? project.KeySignatureMinor;

            xml.WriteStartElement("measure");
            xml.WriteAttributeString("number", Num(b + 1));
            if (model is { Anacrusis: true }) xml.WriteAttributeString("implicit", "yes");   // a pickup bar

            var ending = model?.EndingPasses ?? 0;
            WriteLeftBarline(xml, track, model, b, ending, previousEnding);

            var first = b == 0;
            var keyChanged = first || barKey != key || barMinor != minor;
            var timeChanged = first || barNum != num || barDen != den;
            var barClef = string.IsNullOrEmpty(model?.Clef) ? clef : model!.Clef;
            var clefChanged = !first && !drums && barClef != clef;
            if (first || keyChanged || timeChanged || clefChanged)
                WriteAttributes(xml, track, model, first, keyChanged, timeChanged, clefChanged, barKey, barMinor, barNum, barDen, staves, tab, drums);
            key = barKey; minor = barMinor; num = barNum; den = barDen; clef = barClef;

            var tempo = b == 0 ? master?.TempoChange ?? project.Tempo : master?.TempoChange;
            if (tempo is int bpm) WriteTempo(xml, bpm);
            foreach (var token in PlaybackDirections(model?.Directions)) WriteNavigation(xml, token);
            if (markers.TryGetValue(b, out var section) && !string.IsNullOrWhiteSpace(section.Title)) WriteRehearsal(xml, section.Title, section.ColorIsExplicit ? section.ColorHex : null);

            if (model is not null && MusicXmlSidecar.BarData(model) is { } barData) xml.WriteProcessingInstruction(MusicXmlSidecar.BarTarget, barData);
            WriteBarContent(xml, partId, project, track, model, slots, staves, drums, ref previousDynamic, ref tupletRun, ref octaveShift);

            WriteRightBarline(xml, track, model, b, ending);
            previousEnding = ending;
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void WriteLeftBarline(XmlWriter xml, TrackModel track, MeasureModel? model, int b, int ending, int previousEnding)
    {
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
    }

    /// <summary>The attributes of a bar that changes something: divisions and clefs of the first bar, a new key, a new time signature, the tab staff details.</summary>
    private static void WriteAttributes(XmlWriter xml, TrackModel track, MeasureModel? model, bool first, bool keyChanged, bool timeChanged, bool clefChanged, int barKey, bool barMinor, int barNum, int barDen, int staves, bool tab, bool drums)
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
        if (first) xml.WriteElementString("staves", Num(staves));
        if (first || clefChanged) WriteClef(xml, track, drums, 1, model?.Clef);   // a clef change only on the first (notation) staff
        if (first && tab) { WriteTabClef(xml, 2); WriteTabStaffDetails(xml, track); }
        xml.WriteEndElement();
    }

    private static void WriteTabStaffDetails(XmlWriter xml, TrackModel track)
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

    private static void WriteRehearsal(XmlWriter xml, string section, string? colour)
    {
        xml.WriteStartElement("direction");
        xml.WriteAttributeString("placement", "above");
        xml.WriteStartElement("direction-type");
        xml.WriteStartElement("rehearsal");
        if (colour is not null) xml.WriteAttributeString("color", colour);   // a section colour the user picked; read back by TabForge
        xml.WriteString(section);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>The voices of a bar on each staff (with the backups between them), or whole-bar rests when the track has no such bar.</summary>
    private static void WriteBarContent(XmlWriter xml, string partId, SongProject project, TrackModel track, MeasureModel? model, int slots, int staves, bool drums, ref int previousDynamic, ref int tupletRun, ref int octaveShift)
    {
        if (model is not null)
        {
            var voices = new List<List<TabCell>> { model.Cells };
            if (model.Voice2Cells.Any(c => c.Notes.Count > 0)) voices.Add(model.Voice2Cells);
            for (var staff = 1; staff <= staves; staff++)
                for (var v = 0; v < voices.Count; v++)
                {
                    var isTab = staff == 2;
                    var voiceNumber = v + 1;   // numbered per staff: alphaTab numbers a part's voices in order of first appearance, so a tab staff on 5.. read back as a second voice
                    // String and fret only on the tab staff (the notation staff would get circled string numbers).
                    var advanced = WriteVoice(xml, partId, voices[v], slots, staff, voiceNumber, isTab, carry: isTab || staves == 1, drums, project, lyricsHere: v == 0 && !isTab,
                        ref previousDynamic, ref tupletRun, ref octaveShift, writeDynamics: v == 0 && !isTab);
                    var isLast = staff == staves && v == voices.Count - 1;
                    if (!isLast && advanced > 0) WriteBackup(xml, advanced);
                }
        }
        else
        {
            for (var staff = 1; staff <= staves; staff++)
            {
                WriteWholeBarRest(xml, slots, 1, staff);
                if (staff < staves) WriteBackup(xml, (int)Math.Round(slots * DivisionsPerSlot));
            }
        }
    }

    private static void WriteRightBarline(XmlWriter xml, TrackModel track, MeasureModel? model, int b, int ending)
    {
        if (model is { RepeatEnd: true } || EndingStops(track, b, ending) || model is { IsDoubleBar: true })
        {
            xml.WriteStartElement("barline");
            xml.WriteAttributeString("location", "right");
            if (model is { IsDoubleBar: true, RepeatEnd: false }) xml.WriteElementString("bar-style", "light-light");
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
                case "G2":   // the plain treble clef under alphaTab's own name
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
}
