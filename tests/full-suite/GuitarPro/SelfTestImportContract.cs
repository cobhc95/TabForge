using System.IO;
using System.IO.Compression;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Importer property names, shared dynamics, gzip .tforge, invalid UTF-8.</summary>
public static partial class SelfTest
{
    /// <summary>Names the importer once read that alphaTab 1.8.4 does not have; they must never come back.</summary>
    private static readonly string[] RetiredImporterNames =
    {
        "Velocity", "BreakSecondaryBeamBefore", "StemDirection", "Notice", "ForceLineBreak", "PreventLineBreak", "BeamMode"
    };

    private static void TestImporterNamesAndDynamics()
    {
        // The alphaTab members the importer/exporter depend on must exist (a rename fails here, not silently).
        var model = typeof(AlphaTab.Model.Note).Assembly;
        foreach (var (type, member) in new[]
        {
            ("Note", "Dynamics"), ("Beat", "Dynamics"), ("Beat", "BeamingMode"), ("Beat", "PreviousBeat"),
            ("Beat", "InvertBeamDirection"), ("Beat", "PreferredBeamDirection"), ("Score", "Notices")
        })
            Check($"alphaTab {type}.{member} exists", model.GetType("AlphaTab.Model." + type)?.GetProperty(member) is not null);

        // Dynamics table: forte stays 95, the rest ascend, names and velocities round-trip.
        Eq("forte velocity is unchanged", 95, Dynamics.VelocityFor("f"));
        Check("dynamics table ascends ppp..fff", Dynamics.Velocities.Zip(Dynamics.Velocities.Skip(1), (a, b) => a < b).All(x => x));
        Check("every dynamic maps back to itself", Dynamics.Names.All(n => Dynamics.NearestName(Dynamics.VelocityFor(n)) == n));
        Eq("alphaTab FFF maps to fff", 127, Dynamics.VelocityForAlphaTab("FFF"));
        Eq("alphaTab MP maps to mp", Dynamics.VelocityFor("mp"), Dynamics.VelocityForAlphaTab("MP"));

        // End to end: velocities survive a clean .gp export and import as dynamics.
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-dyn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = BuildSyntheticGpSong();
            var expected = new List<int>();
            var index = 0;
            foreach (var cell in source.Tracks[0].Measures.SelectMany(m => m.Cells).Where(c => c.Notes.Count > 0))
            {
                var velocity = Dynamics.Velocities[index++ % Dynamics.Velocities.Length];
                foreach (var note in cell.Notes) note.Velocity = velocity;
                expected.Add(velocity);
            }
            var path = Path.Combine(folder, "dynamics.gp");
            GuitarProExporter.Save(source, path, embedProject: false);
            var imported = GuitarProImporter.Import(path);
            var actual = imported.Tracks[0].Measures.SelectMany(m => m.Cells).Where(c => c.Notes.Count > 0)
                .SelectMany(c => c.Notes.Select(n => n.Velocity)).ToList();
            Check("dynamics survive a clean .gp round trip (import reads Dynamics, export writes it)",
                actual.SequenceEqual(expected), $"expected {string.Join(",", expected)} got {string.Join(",", actual)}");

            var missing = GuitarProImporter.MissingPropertyNames.ToList();
            var retired = missing.Where(m => RetiredImporterNames.Contains(m[(m.IndexOf('.') + 1)..])).ToList();
            Check("importer reads none of the property names alphaTab lacks (lost-feature list)", retired.Count == 0, string.Join(", ", retired));
            Log.Add($"  info  importer optional names alphaTab lacks (second tries): {string.Join(", ", missing.OrderBy(x => x))}");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    // Features that a clean score export can drop, checked import -> export -> import.
    private static void TestCleanGpExportKeepsFeatures()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-gpm04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = BuildSyntheticGpSong();
            var cells = source.Tracks[0].Measures[0].Cells;
            // Beat 0: a multi-point (custom) bend, beat 1: pick stroke, beat 2: 8va, beat 3: a release bend.
            foreach (var (o, v) in new[] { (0.0, 0.0), (15.0, 4.0), (30.0, 0.0), (45.0, 4.0), (60.0, 2.0) })
                cells[0].Notes[0].BendPoints.Add(new BendPointModel { Offset = o, Value = v });
            cells[4].Notes[0].Techniques.Add("PickDown");
            cells[8].OctaveShiftSemitones = 12;
            foreach (var (o, v) in new[] { (0.0, 0.0), (30.0, 4.0), (60.0, 0.0) })
                cells[12].Notes[0].BendPoints.Add(new BendPointModel { Offset = o, Value = v });

            // Bar 2: a grace note ornamenting a quarter note (one cell, as the importer stores it) and a semi-harmonic.
            var bar2 = source.Tracks[0].Measures[1].Cells;
            var principalMidi = bar2[4].Notes[0].MidiValue;
            bar2[4].Notes.Add(new TabNote { StringIndex = bar2[4].Notes[0].StringIndex, Fret = bar2[4].Notes[0].Fret + 2, MidiValue = principalMidi + 2, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 0.5 });
            bar2[8].Notes[0].Techniques.Add("SemiHarmonic");
            bar2[8].Notes[0].HarmonicFret = 7;
            bar2[12].Notes[0].Techniques.Add("TapHarmonic");
            bar2[12].Notes[0].HarmonicFret = 17;
            var deadNote = source.Tracks[0].Measures[1].Cells[0].Notes[0];
            deadNote.Dead = true; deadNote.Fret = 0; deadNote.MidiValue = source.Tracks[0].StringTunings[deadNote.StringIndex];
            var bar3 = source.Tracks[0].Measures[2].Cells;
            var tremoloSpeeds = new[] { 8, 16, 32 };
            for (var k = 0; k < tremoloSpeeds.Length; k++) bar3[k * 4].TremoloPickDenominator = tremoloSpeeds[k];

            var path = Path.Combine(folder, "m04.gp");
            GuitarProExporter.Save(source, path, embedProject: false);
            var reread = GuitarProImporter.Import(path);
            var back = reread.Tracks[0].Measures[0].Cells;
            var back2 = reread.Tracks[0].Measures[1].Cells;
            var graceCell = back2.FirstOrDefault(c => c.Notes.Any(n => n.IsGraceNote));
            Check("M-04: a grace note and its principal note keep their own durations",
                graceCell is not null && graceCell.DurationDenominator == 4 && graceCell.Notes.Any(n => !n.IsGraceNote && n.MidiValue == principalMidi),
                graceCell is null ? "no grace cell" : $"den {graceCell.DurationDenominator}");
            Check("M-04: the notes after a grace note stay on their beats", back2[8].Notes.Count > 0 && back2[12].Notes.Count > 0);
            var back3 = reread.Tracks[0].Measures[2].Cells;
            Check("M-04: 1/8, 1/16 and 1/32 tremolo picking keep their speed (no off-by-one step)",
                tremoloSpeeds.Select((d, k) => back3[k * 4].TremoloPickDenominator == d).All(x => x),
                string.Join(",", tremoloSpeeds.Select((_, k) => back3[k * 4].TremoloPickDenominator)));
            var backHarmonics = back2.SelectMany(c => c.Notes).Where(n => n.HarmonicFret.HasValue).Select(n => n.HarmonicFret!.Value).OrderBy(v => v).ToList();
            Check("V-18: the harmonic fret (7 semi, 17 tapped) survives export and import", backHarmonics.SequenceEqual(new[] { 7.0, 17.0 }), string.Join(",", backHarmonics));
            var backDead = back2[0].Notes.FirstOrDefault();
            Check("M-04: a dead note keeps its pitch across export and import", backDead is { Dead: true } && backDead.MidiValue == deadNote.MidiValue, backDead is null ? "none" : $"{backDead.MidiValue} vs {deadNote.MidiValue}");
            var tieTrack = new TrackModel { StringTunings = new List<int> { 64 }, Measures = Presets.TemplateFactory.Measures(1) };
            tieTrack.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
            tieTrack.Measures[0].Cells[4].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67, Tied = true });
            GuitarProImporter.LinkTieOrigins(tieTrack);
            Check("M-04: the note before a tie destination is marked as the tie origin", tieTrack.Measures[0].Cells[0].Notes[0].Techniques.Contains("Tie") && !tieTrack.Measures[0].Cells[4].Notes[0].Techniques.Contains("Tie"));
            Check("M-04: a semi-harmonic survives", back2.Any(c => c.Notes.Any(n => n.Techniques.Contains("SemiHarmonic"))));
            Eq("M-04: a five-point whammy curve is reduced to three", 3, GuitarProExporter.SimplifyWhammy(new() { (0, 0), (15, -4), (30, -8), (45, -4), (60, 0) }).Count);
            Eq("M-04: a four-point whammy curve is left alone", 4, GuitarProExporter.SimplifyWhammy(new() { (0, 0), (20, -8), (40, -8), (60, 0) }).Count);
            Check("M-04: a multi-point bend survives a clean .gp export", back[0].Notes.Count > 0 && back[0].Notes[0].BendPoints.Count >= 2 && back[0].Notes[0].BendPoints.Max(p => p.Value) >= 4);
            Check("M-04: a bend-release survives", back[12].Notes.Count > 0 && back[12].Notes[0].BendPoints.Count >= 3);
            Check("M-04: a pick stroke survives", back[4].Notes.Count > 0 && back[4].Notes[0].Techniques.Contains("PickDown"));
            Eq("M-04: an 8va survives", 12, back[8].OctaveShiftSemitones);
            Check("M-04: an already-simple bend is left untouched",
                GuitarProExporter.SimplifyBend(new() { (0, 0), (60, 4) }).SequenceEqual(new List<(double, double)> { (0, 0), (60, 4) }));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    // File > Export > MusicXML: well-formed, and the key elements are there.
    private static void TestMusicXmlExport()
    {
        var project = BuildSyntheticGpSong();
        project.KeySignature = 2;
        var guitar = project.Tracks[0];
        guitar.Measures[0].Cells[0].Notes[0].Techniques.Add("HOPOOrigin");
        var origin = guitar.Measures[0].Cells[0].Notes[0];
        var target = guitar.Measures[0].Cells[4].Notes[0];       // same string, two frets up: a hammer-on
        target.StringIndex = origin.StringIndex; target.Fret = origin.Fret + 2; target.MidiValue = origin.MidiValue + 2;
        target.Techniques.Add("HOPODestination");
        guitar.Measures[1].Cells[0].Dots = 1;                      // a dotted quarter
        guitar.Measures[1].Cells[4].Notes.Clear();
        guitar.Measures[2].Cells[0].Notes[0].Tied = true;          // a tie destination
        guitar.Measures[3].Cells[0].DurationDenominator = 8;       // a triplet eighth
        guitar.Measures[3].Cells[0].TupletNumerator = 3;
        guitar.Measures[3].Cells[0].TupletDenominator = 2;
        var lead = guitar.Measures[3].Cells[4].Notes[0];
        guitar.Measures[3].Cells[4].Notes.Add(new TabNote { StringIndex = lead.StringIndex, Fret = lead.Fret + 2, MidiValue = lead.MidiValue + 2, IsGraceNote = true, GraceDurationSlots = 0.5 });

        var bytes = MusicXmlExportService.ToBytes(project);
        System.Xml.Linq.XDocument doc;
        try
        {
            using var reader = System.Xml.XmlReader.Create(new MemoryStream(bytes), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
            doc = System.Xml.Linq.XDocument.Load(reader);
        }
        catch (System.Xml.XmlException ex) { Check("MusicXML export is well-formed XML", false, ex.Message); return; }
        Check("MusicXML export is well-formed XML", true);
        Check("MusicXML: no byte-order mark", bytes.Length > 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB));
        var root = doc.Root!;
        Eq("MusicXML: score-partwise root", "score-partwise", root.Name.LocalName);
        var parts = root.Elements("part").ToList();
        Eq("MusicXML: one part per track", project.Tracks.Count, parts.Count);
        Eq("MusicXML: one score-part per track", project.Tracks.Count, root.Element("part-list")!.Elements("score-part").Count());
        var first = parts[0].Elements("measure").First();
        Eq("MusicXML: bars", 4, parts[0].Elements("measure").Count());
        Eq("MusicXML: key", "2", (string?)first.Element("attributes")?.Element("key")?.Element("fifths"));
        Eq("MusicXML: time", "4/4", $"{(string?)first.Element("attributes")?.Element("time")?.Element("beats")}/{(string?)first.Element("attributes")?.Element("time")?.Element("beat-type")}");
        Eq("MusicXML: tempo", "96", (string?)first.Descendants("sound").FirstOrDefault()?.Attribute("tempo"));
        Eq("MusicXML: tab staff lists every string", guitar.StringTunings.Count, first.Element("attributes")!.Element("staff-details")!.Elements("staff-tuning").Count());
        var guitarNotes = parts[0].Descendants("note").Where(n => n.Element("pitch") is not null).ToList();
        var tabNotes = guitarNotes.Where(n => n.Descendants("fret").Any()).ToList();
        var expectedNotes = guitar.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count));
        Eq("MusicXML: every guitar note is on the tab staff with a fret", expectedNotes, tabNotes.Count);
        Check("MusicXML: string and fret are only on the tab staff (no circled string numbers on the notation staff)", guitarNotes.Where(n => (string?)n.Element("staff") == "1").All(n => !n.Descendants("string").Any() && !n.Descendants("fret").Any()));
        Check("MusicXML: string 1 is the highest string", tabNotes.Any(n => (string?)n.Descendants("string").First() == "1"));
        Check("MusicXML: durations, dots, ties, tuplets, grace notes and dynamics are written",
            guitarNotes.Any(n => n.Element("dot") is not null) && guitarNotes.Any(n => n.Elements("tie").Any(t => (string?)t.Attribute("type") == "stop")) &&
            guitarNotes.Any(n => n.Element("time-modification") is not null) && parts[0].Descendants("grace").Any() &&
            parts[0].Descendants("dynamics").Any() && guitarNotes.All(n => n.Element("duration") is not null || n.Element("grace") is not null));
        var slashProject = BuildSyntheticGpSong();
        var slashCells = slashProject.Tracks[0].Measures[0].Cells;
        slashCells[0].TremoloPickDenominator = 8; slashCells[4].TremoloPickDenominator = 16; slashCells[8].TremoloPickDenominator = 32;
        using var slashReader = System.Xml.XmlReader.Create(new MemoryStream(MusicXmlExportService.ToBytes(slashProject)), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
        var slashes = System.Xml.Linq.XDocument.Load(slashReader).Root!.Element("part")!.Descendants("tremolo").Select(x => x.Value).Distinct().OrderBy(x => x).ToList();
        Check("MusicXML: tremolo slashes are 1 (1/8), 2 (1/16), 3 (1/32)", slashes.SequenceEqual(new[] { "1", "2", "3" }), string.Join(",", slashes));
        Check("MusicXML: hammer-on start and stop are paired", parts[0].Descendants("hammer-on").Select(h => (string?)h.Attribute("type")).OrderBy(x => x).SequenceEqual(new[] { "start", "start", "stop", "stop" }));
        // Voice 1 of the standard staff fills each 4/4 bar (notes + rests + forwards, chord notes excluded).
        var total = first.Elements("note").Where(n => (string?)n.Element("staff") == "1" && n.Element("chord") is null && n.Element("grace") is null)
            .Sum(n => (int)n.Element("duration")!) + first.Elements("forward").Sum(f => (int)f.Element("duration")!);
        Eq("MusicXML: notes, rests and gaps add up to the bar length", 960, total);
        Check("MusicXML: the drum-free song has no percussion clef", !doc.Descendants("sign").Any(s => s.Value == "percussion"));
    }

    // What other programs read from the file's header: 1-based MIDI numbers, the tempo, and string-tuning order.
    private static void TestMusicXmlHeaderForReaders()
    {
        var project = BuildSyntheticGpSong();
        project.Tempo = 200;
        project.Tracks[0].MidiProgram = 30; project.Tracks[0].MidiChannel = 0;   // distortion guitar, first channel (0-based inside TabForge)
        project.Tracks[1].MidiProgram = 0; project.Tracks[1].MidiChannel = 15;
        project.Tracks[0].StringTunings = new List<int> { 62, 57, 53, 48, 43, 36 }; // highest string first, drop C
        using var reader = System.Xml.XmlReader.Create(new MemoryStream(MusicXmlExportService.ToBytes(project)), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
        var root = System.Xml.Linq.XDocument.Load(reader).Root!;
        Eq("MusicXML: version 3.1 (no 4.0-only elements)", "3.1", (string?)root.Attribute("version"));
        var instruments = root.Element("part-list")!.Elements("score-part").Select(p => p.Element("midi-instrument")!).ToList();
        Eq("MusicXML: midi-program is 1-based (0-based 30 is written as 31)", "31", (string?)instruments[0].Element("midi-program"));
        Eq("MusicXML: midi-program 0 is written as 1", "1", (string?)instruments[1].Element("midi-program"));
        Eq("MusicXML: midi-channel is 1-based", "1", (string?)instruments[0].Element("midi-channel"));
        Eq("MusicXML: midi-channel 15 is written as 16", "16", (string?)instruments[1].Element("midi-channel"));
        var first = root.Element("part")!.Element("measure")!;
        Check("MusicXML: tempo 200 is a metronome mark and a bare sound in bar 1, as Guitar Pro writes it",
            (string?)first.Element("sound")?.Attribute("tempo") == "200" &&
            (string?)first.Descendants("per-minute").FirstOrDefault() == "200");
        var lines = first.Descendants("staff-tuning").ToDictionary(t => (int)t.Attribute("line")!, t => $"{(string?)t.Element("tuning-step")}{(string?)t.Element("tuning-octave")}");
        Check("MusicXML: staff-tuning line 1 is the lowest string (C2) and line 6 the highest (D4)", lines.Count == 6 && lines[1] == "C2" && lines[2] == "G2" && lines[6] == "D4",
            string.Join(",", lines.OrderBy(l => l.Key).Select(l => l.Value)));
        var firstNote = first.Elements("note").First(n => n.Element("pitch") is not null && n.Descendants("string").Any());
        Check("MusicXML: technical string 1 is the highest string, on the tab staff only",
            !first.Elements("note").Any(n => (string?)n.Element("staff") == "1" && n.Descendants("fret").Any()) && (string?)firstNote.Element("staff") == "2");
    }

    // Every bar of every staff and voice must add up to the time signature's length (empty, short and two-voice bars included).
    private static void TestMusicXmlBarsFillTheTimeSignature()
    {
        var project = BuildSyntheticGpSong();
        foreach (var track in project.Tracks)
        {
            foreach (var cell in track.Measures[1].Cells) { cell.Notes.Clear(); cell.IsRest = false; }   // empty bar
            track.Measures[2].TimeSigNum = 3;                                                             // 3/4 bar holding four quarters: overfull
            var quarterRest = track.Measures[3].Cells;                                                    // a bar that is only one quarter rest
            foreach (var cell in quarterRest) { cell.Notes.Clear(); cell.IsRest = false; }
            quarterRest[0].IsRest = true; quarterRest[0].DurationDenominator = 4;
        }
        var guitar = project.Tracks[0];
        guitar.Measures[0].Cells[12].Notes.Clear();                                                       // short bar: three quarters in 4/4
        guitar.Measures[0].Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        guitar.Measures[0].Voice2Cells[0].DurationDenominator = 2;
        guitar.Measures[0].Voice2Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 3, MidiValue = 60 });
        using var reader = System.Xml.XmlReader.Create(new MemoryStream(MusicXmlExportService.ToBytes(project)), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
        var root = System.Xml.Linq.XDocument.Load(reader).Root!;
        var problems = new List<string>();
        foreach (var part in root.Elements("part"))
        {
            var beats = 4; var beatType = 4; var bar = 0;
            foreach (var measure in part.Elements("measure"))
            {
                bar++;
                if (measure.Element("attributes")?.Element("time") is { } time) { beats = (int)time.Element("beats")!; beatType = (int)time.Element("beat-type")!; }
                var expected = Divisions * 4 * beats / beatType;
                var position = 0; var maximum = 0;
                var blocks = 0;
                foreach (var element in measure.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "note" when element.Element("chord") is null && element.Element("grace") is null:
                            position += (int)element.Element("duration")!;
                            break;
                        case "forward": position += (int)element.Element("duration")!; break;
                        case "backup":
                            blocks++;   // each staff/voice block ends where its backup starts
                            if (position != expected) problems.Add($"{(string?)part.Attribute("id")} bar {bar} block {blocks}: {position} of {expected}");
                            position -= (int)element.Element("duration")!;
                            break;
                    }
                    maximum = Math.Max(maximum, position);
                }
                if (maximum != expected) problems.Add($"{(string?)part.Attribute("id")} bar {bar}: max {maximum} of {expected}");
                if (position != expected) problems.Add($"{(string?)part.Attribute("id")} bar {bar} last block: {position} of {expected}");
            }
        }
        Check("MusicXML: every bar, staff and voice fills the time signature (empty, short, overfull, two voices)", problems.Count == 0, string.Join("; ", problems.Take(6)));
    }

    private const int Divisions = 240;

    // Technique encodings, copied from what Guitar Pro 8 writes.
    private static void TestMusicXmlGuitarPro8Encoding()
    {
        var project = BuildSyntheticGpSong();
        var guitar = project.Tracks[0];
        var cells = guitar.Measures[0].Cells;
        cells[0].Notes[0].Techniques.Add("PalmMute");
        cells[4].Notes[0].Techniques.Add("LetRing");
        cells[8].Notes[0].BendPoints.AddRange(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 30, Value = 4 }, new BendPointModel { Offset = 60, Value = 0 } });
        cells[12].Notes[0].Techniques.Add("PinchHarmonic");
        guitar.Measures[1].Cells[0].Notes[0].Techniques.Add("SlideOutUp");
        guitar.Measures[1].Cells[4].Notes[0].Techniques.Add("LegatoSlide");
        guitar.Measures[1].Cells[8].TremoloPickDenominator = 16;
        guitar.Measures[2].RepeatStart = true; guitar.Measures[3].RepeatEnd = true; guitar.Measures[3].RepeatCount = 3;
        guitar.Measures[1].Directions = "Segno";
        guitar.Measures[3].Directions = "DalSegno";
        guitar.Measures[1].Cells[12].OctaveShiftSemitones = 12;
        cells[12].ChordName = "F#m7";
        var drums = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, Measures = Presets.TemplateFactory.Measures(4) };
        drums.Measures[0].Cells[0].DurationDenominator = 4;
        drums.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 36, MidiValue = 36 });
        project.Tracks.Add(drums);
        using var reader = System.Xml.XmlReader.Create(new MemoryStream(MusicXmlExportService.ToBytes(project)), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
        var root = System.Xml.Linq.XDocument.Load(reader).Root!;
        var part = root.Elements("part").First();
        Check("GP8 encoding: palm mute is <play><mute>palm</mute></play>", part.Descendants("play").Any(p => (string?)p.Element("mute") == "palm"));
        Check("GP8 encoding: let ring is a <?GP?> letring instruction", part.DescendantNodes().OfType<System.Xml.Linq.XProcessingInstruction>().Any(pi => pi.Data.Contains("<letring/>")));
        var bends = part.Descendants("bend").ToList();
        Check("GP8 encoding: a bend with a release is a bend of 2 semitones then a release", bends.Count >= 2 && (string?)bends[0].Element("bend-alter") == "2" && bends[1].Element("release") is not null, bends.Count.ToString());
        Check("GP8 encoding: a pinch harmonic is artificial with a sounding pitch", part.Descendants("harmonic").Any(h => h.Element("artificial") is not null && h.Element("sounding-pitch") is not null));
        Check("GP8 encoding: a slide out up is a doit, a legato slide has slide start and a slur", part.Descendants("doit").Any() && part.Descendants("slide").Any(x => (string?)x.Attribute("type") == "start") && part.Descendants("slur").Any());
        Check("GP8 encoding: tremolo picking is <ornaments><tremolo>2</tremolo> for 1/16", part.Descendants("ornaments").Any(o => (string?)o.Element("tremolo") == "2" && o.Element("tremolo")!.Attribute("type") is null));
        Check("GP8 encoding: repeats carry their pass count", part.Descendants("repeat").All(r => (string?)r.Attribute("times") == "3") && part.Descendants("repeat").Count() == 2);
        Check("GP8 encoding: segno and D.S. marks are written", part.Descendants("segno").Any() && part.Descendants("sound").Any(x => x.Attribute("dalsegno") is not null));
        Check("GP8 encoding: an 8va starts an octave shift", part.Descendants("octave-shift").Any(o => (string?)o.Attribute("type") == "down" && (string?)o.Attribute("size") == "8"));
        Check("GP8 encoding: a chord name becomes a harmony with its root", part.Descendants("harmony").Any(h => (string?)h.Element("root")?.Element("root-step") == "F" && (string?)h.Element("root")?.Element("root-alter") == "1"));
        var drumPart = root.Elements("part").Last();
        var drumInstrument = root.Element("part-list")!.Elements("score-part").Last();
        Check("GP8 encoding: drum parts list their sounds with midi-unpitched and notes name their instrument",
            (string?)drumInstrument.Element("midi-instrument")?.Element("midi-unpitched") == "37" && drumPart.Descendants("note").Any(n => (string?)n.Element("instrument")?.Attribute("id") == "P2-I36" || (string?)n.Element("instrument")?.Attribute("id") == "P3-I36"));
        Check("GP8 encoding: the kick sits on its own staff line (F4), not on the snare line", drumPart.Descendants("unpitched").Any(u => (string?)u.Element("display-step") == "F" && (string?)u.Element("display-octave") == "4"));
        Check("MusicXML: .xml is accepted as an export extension", MusicXmlAcceptsXml());
    }

    private static bool MusicXmlAcceptsXml()
    {
        var file = Path.Combine(Path.GetTempPath(), "tabforge-mx-" + Guid.NewGuid().ToString("N") + ".xml");
        try { MusicXmlExportService.Export(BuildSyntheticGpSong(), file); return File.Exists(file) && new FileInfo(file).Length > 0; }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    private static void TestTforgeCompression()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-gz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = BuildSyntheticGpSong();
            var path = Path.Combine(folder, "song.tforge");
            var hash = ProjectService.Save(path, project);
            var bytes = File.ReadAllBytes(path);
            Check("a saved .tforge is gzip", bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B);
            var plain = ProjectService.GunzipBounded(bytes);
            Check("the gzip .tforge is much smaller than its JSON", bytes.Length * 4 < plain.Length, $"{bytes.Length} vs {plain.Length}");
            Check("a gzip .tforge loads back identically", ProjectService.ContentHash(ProjectService.Load(path)).SequenceEqual(hash));

            var legacy = Path.Combine(folder, "legacy.tforge");
            File.WriteAllBytes(legacy, plain);
            Check("an old uncompressed .tforge still loads", ProjectService.ContentHash(ProjectService.Load(legacy)).SequenceEqual(hash));

            // M-07: invalid UTF-8 is a malformed file, not an unexpected exception type.
            var damaged = (byte[])plain.Clone();
            damaged[2] = 0xFF;
            var damagedPath = Path.Combine(folder, "damaged.tforge");
            File.WriteAllBytes(damagedPath, damaged);
            var kind = "no exception";
            try { ProjectService.Load(damagedPath); }
            catch (InvalidDataException) { kind = "InvalidDataException"; }
            catch (Exception ex) { kind = ex.GetType().Name; }
            Eq("invalid UTF-8 in a .tforge is an InvalidDataException", "InvalidDataException", kind);

            var truncated = Path.Combine(folder, "truncated.tforge");
            File.WriteAllBytes(truncated, bytes.Take(bytes.Length / 2).ToArray());
            kind = "no exception";
            try { ProjectService.Load(truncated); }
            catch (InvalidDataException) { kind = "InvalidDataException"; }
            catch (Exception ex) { kind = ex.GetType().Name; }
            Eq("a truncated gzip .tforge is an InvalidDataException", "InvalidDataException", kind);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
