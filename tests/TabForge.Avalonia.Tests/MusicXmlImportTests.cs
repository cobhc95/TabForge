using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: MusicXML import (.musicxml / .xml / .mxl) through the Guitar Pro conversion: a TabForge export read back, a tab-staff part, a percussion part, a container, a refused .xml.
// Does not own: the converter itself (GuitarProImportParityTests).
public sealed class MusicXmlImportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-mxi-{Guid.NewGuid():N}");

    public MusicXmlImportTests() { Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private string Write(string name, string text) { var p = Path.Combine(_folder, name); File.WriteAllText(p, text, new UTF8Encoding(false)); return p; }

    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE score-partwise PUBLIC \"-//Recordare//DTD MusicXML 3.1 Partwise//EN\" \"http://www.musicxml.org/dtds/partwise.dtd\">\n<score-partwise version=\"3.1\">";

    private static string Measures(string attributes, string notes, int count = 2) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"<measure number=\"{i}\">{(i == 1 ? attributes : "")}{notes}</measure>"));

    private static string Quarter(string pitch, string extra = "") =>
        $"<note><pitch><step>{pitch[0]}</step><octave>{pitch[1]}</octave></pitch><duration>1</duration>{extra}<type>quarter</type></note>";

    [Fact]
    public void RoundTripOfTabForgeOwnExport()
    {
        var song = new SongProject { Title = "RT", Tempo = 96, TimeSignatureNumerator = 3, TimeSignatureDenominator = 4 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, StringTunings = new() { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(3) };
        for (var bar = 0; bar < 3; bar++)
            foreach (var cell in new[] { 0, 4, 8 })
            {
                var c = track.Measures[bar].Cells[cell];
                c.DurationDenominator = 4;
                var tied = bar == 1 && cell == 0;
                var fret = tied ? 1 : bar + 1;   // a tie keeps its origin pitch
                var note = new TabNote { StringIndex = 1, Fret = fret, MidiValue = track.PitchOf(1, fret), Tied = tied };
                if (bar == 0 && cell == 8) note.Techniques.Add("Tie");
                c.Notes.Add(note);
            }
        song.Tracks.Add(track);
        var path = Path.Combine(_folder, "rt.musicxml");
        File.WriteAllBytes(path, MusicXmlExportService.ToBytes(song));

        var back = GuitarProImporter.Import(path);

        Assert.Equal(3, back.Tracks[0].Measures.Count);
        Assert.Equal(96, back.Tempo);
        Assert.Equal((3, 4), (back.TimeSignatureNumerator, back.TimeSignatureDenominator));
        Assert.Equal(9, back.Tracks[0].Measures.SelectMany(m => m.Cells).Sum(c => c.Notes.Count));
        Assert.Contains(back.Tracks[0].Measures[1].Cells.SelectMany(c => c.Notes), n => n.Tied);
    }

    [Fact]
    public void TabStaffKeepsStringsFretsAndTuning()
    {
        var attributes = "<attributes><divisions>1</divisions><key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>TAB</sign><line>5</line></clef>"
            + "<staff-details><staff-lines>6</staff-lines>"
            + string.Concat(new[] { ("E", 2), ("A", 2), ("D", 3), ("G", 3), ("B", 3), ("E", 4) }.Select((t, i) => $"<staff-tuning line=\"{i + 1}\"><tuning-step>{t.Item1}</tuning-step><tuning-octave>{t.Item2}</tuning-octave></staff-tuning>"))
            + "</staff-details></attributes>";
        var note = "<note><pitch><step>E</step><octave>3</octave></pitch><duration>4</duration><type>whole</type><notations><technical><string>5</string><fret>7</fret></technical></notations></note>";
        var path = Write("tab.musicxml", Header + "<part-list><score-part id=\"P1\"><part-name>Gtr</part-name></score-part></part-list><part id=\"P1\">" + Measures(attributes, note) + "</part></score-partwise>");

        var song = GuitarProImporter.Import(path);

        var track = Assert.Single(song.Tracks);
        Assert.Equal(6, track.StringTunings.Count);
        var n = track.Measures[0].Cells.SelectMany(c => c.Notes).Single();
        Assert.Equal(7, n.Fret);
        Assert.Equal(track.PitchOf(n.StringIndex, 7), n.MidiValue);
    }

    [Fact]
    public void PitchedPartWithoutTabGetsFrets()
    {
        var attributes = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>";
        var path = Write("piano.xml", Header + "<part-list><score-part id=\"P1\"><part-name>Pno</part-name></score-part></part-list><part id=\"P1\">"
            + Measures(attributes, Quarter("E4") + Quarter("G4") + Quarter("C4", "<tie type=\"start\"/>") + Quarter("C4", "<tie type=\"stop\"/>")) + "</part></score-partwise>");

        var song = GuitarProImporter.Import(path);

        var notes = song.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes).ToList();
        Assert.True(notes.Count >= 3);
        Assert.All(notes, n => Assert.InRange(n.Fret, 0, 30));
    }

    [Fact]
    public void PercussionPartBecomesDrumTrack()
    {
        var attributes = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>percussion</sign></clef></attributes>";
        var hit = "<note><unpitched><display-step>C</display-step><display-octave>5</display-octave></unpitched><duration>1</duration><instrument id=\"P1-I36\"/><type>quarter</type></note>";
        var path = Write("drums.musicxml", Header + "<part-list><score-part id=\"P1\"><part-name>Drums</part-name><score-instrument id=\"P1-I36\"><instrument-name>Kick</instrument-name></score-instrument>"
            + "<midi-instrument id=\"P1-I36\"><midi-channel>10</midi-channel><midi-unpitched>36</midi-unpitched></midi-instrument></score-part></part-list><part id=\"P1\">"
            + Measures(attributes, string.Concat(Enumerable.Repeat(hit, 4))) + "</part></score-partwise>");

        var song = GuitarProImporter.Import(path);

        Assert.Equal(TrackKind.Drums, song.Tracks[0].Kind);
        Assert.NotEmpty(song.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes));
    }

    [Fact]
    public void CompressedContainerOpens()
    {
        var attributes = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>";
        var xml = Header + "<part-list><score-part id=\"P1\"><part-name>A</part-name></score-part></part-list><part id=\"P1\">" + Measures(attributes, Quarter("A4") + Quarter("A4") + Quarter("A4") + Quarter("A4"), 4) + "</part></score-partwise>";
        var path = Path.Combine(_folder, "c.mxl");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "META-INF/container.xml", "<?xml version=\"1.0\"?><container><rootfiles><rootfile full-path=\"score/main.xml\"/></rootfiles></container>");
            Add(zip, "score/main.xml", xml);
        }

        Assert.Equal(4, GuitarProImporter.Import(path).Tracks[0].Measures.Count);
    }

    [Fact]
    public void XmlThatIsNotAScoreIsRefused()
    {
        var path = Write("config.xml", "<?xml version=\"1.0\"?><configuration><setting name=\"a\"/></configuration>");
        var ex = Assert.Throws<InvalidDataException>(() => GuitarProImporter.Import(path));
        Assert.Contains("unsupported", ex.Message);
        Assert.Throws<InvalidDataException>(() => GuitarProImporter.Import(Write("junk.xml", "not xml at all")));
    }

    [Fact]
    public void ExtensionsAreOpenable()
    {
        foreach (var e in new[] { ".musicxml", ".mxl", ".XML" }) Assert.True(FileTypes.IsOpenable(e));
        Assert.DoesNotContain(".xml", FileTypes.AllOpenable);   // never an Explorer association
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
