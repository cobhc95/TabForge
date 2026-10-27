using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the TabForge MusicXML round trip: a song exported by MusicXmlExportService and read back by the importer keeps everything MusicXML can carry
//   (strings, frets, pitches, rhythm, ties, rests, chords, grace notes, techniques, text, dynamics, repeats, sections, signatures, tempo, colours, mixer), and invents nothing.
// Does not own: the Guitar Pro conversion itself (GuitarProImportParityTests) or the file-type refusals (MusicXmlImportTests).
// Known losses, by design (MusicXmlRoundTripDump leaves them out): the song-level time signature (the first bar signature is read back; every bar own signature is compared),
//   the kit tuning of a drum track, the string and fret of a keys track (pitch-only: the pitch is compared), the instrument name (derived from the program),
//   leading / trailing blanks of comments, a rest-only bar (one whole-bar rest reads back as an empty bar), tempo changes that repeat the tempo already playing.
public sealed class MusicXmlRoundTripTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-mxrt-{Guid.NewGuid():N}");

    public MusicXmlRoundTripTests() { Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private SongProject RoundTrip(SongProject song, string name = "rt.musicxml")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, MusicXmlExportService.ToBytes(song));
        return GuitarProImporter.Import(path);
    }

    private SongProject ReadText(string name, string xml)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, xml, new UTF8Encoding(false));
        return GuitarProImporter.Import(path);
    }

    private static void AssertSame(SongProject expected, SongProject actual)
    {
        var a = MusicXmlRoundTripDump.Lines(expected); var b = MusicXmlRoundTripDump.Lines(actual);
        var first = Enumerable.Range(0, Math.Max(a.Count, b.Count)).Where(i => i >= a.Count || i >= b.Count || a[i] != b[i]).Take(5)
            .Select(i => $"line {i}:\n  was  {(i < a.Count ? a[i] : "(none)")}\n  read {(i < b.Count ? b[i] : "(none)")}").ToList();
        Assert.True(first.Count == 0, string.Join("\n", first));
    }

    private static TrackModel Guitar(string name, int bars = 4) =>
        new() { Name = name, Kind = TrackKind.Guitar, StringTunings = new() { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars) };

    private static TabNote Note(TrackModel track, int stringIndex, int fret, params string[] techniques)
    {
        var note = new TabNote { StringIndex = stringIndex, Fret = fret, MidiValue = track.PitchOf(stringIndex, fret), Velocity = 100 };
        foreach (var technique in techniques) note.Techniques.Add(technique);
        return note;
    }

    private static TabCell Beat(TrackModel track, int bar, int cell, int denominator = 4)
    {
        var c = track.Measures[bar].Cells[cell];
        c.DurationDenominator = denominator;
        return c;
    }

    /// <summary>A guitar part with a palm-muted chord, a dead note, a rest, a tie across the barline, a grace note, a ghost note, an accent and text: every shape that once came back as an open high-E note.</summary>
    private static SongProject SmallSong()
    {
        var song = new SongProject { Title = "Small", Artist = "Me", Tempo = 100 };
        var t = Guitar("Lead");
        foreach (var n in new[] { Note(t, 1, 3, "PalmMute"), Note(t, 2, 2, "PalmMute"), Note(t, 3, 0, "PalmMute") }) Beat(t, 0, 0).Notes.Add(n);
        var dead = Note(t, 5, 0); dead.Dead = true; Beat(t, 0, 4).Notes.Add(dead);
        Beat(t, 0, 8).IsRest = true;
        Beat(t, 0, 12).Notes.Add(Note(t, 2, 5, "Tie"));
        var destination = Note(t, 2, 5); destination.Tied = true; Beat(t, 1, 0).Notes.Add(destination);
        var grace = Note(t, 1, 5, "GraceBefore"); grace.IsGraceNote = true; grace.GraceBeforeBeat = true; grace.GraceDurationSlots = 0.5;
        var four = Beat(t, 1, 4); four.Notes.Add(grace); four.Notes.Add(Note(t, 1, 7));
        var ghost = Note(t, 4, 2); ghost.Ghost = true; Beat(t, 1, 8).Notes.Add(ghost);
        var accent = Beat(t, 1, 12); accent.Accent = 1; accent.Notes.Add(Note(t, 0, 12, "Vibrato"));
        var last = Beat(t, 3, 0); last.Text = "riff"; last.ChordName = "Am"; last.Lyrics = "la"; last.Notes.Add(Note(t, 0, 0));
        song.Tracks.Add(t);
        return song;
    }

    [Fact]
    public void DemoSongRoundTripsEverythingMusicXmlCanCarry()
    {
        var song = FullDemoSongFactory.Create();
        AssertSame(song, RoundTrip(song, "demo.musicxml"));
    }

    [Fact]
    public void SmallSongKeepsPalmMuteDeadTiesRestsChordsAndGraceNotesWithoutNewNotes()
    {
        var song = SmallSong();
        var back = RoundTrip(song);

        AssertSame(song, back);
        var cells = back.Tracks[0].Measures.SelectMany(m => m.Cells).ToList();
        Assert.Equal(song.Tracks[0].Measures.SelectMany(m => m.Cells).Sum(c => c.Notes.Count), cells.Sum(c => c.Notes.Count));
        Assert.Single(back.Tracks);   // the notation staff of the part is not a second track
        // nothing sits on the open high-E string that the song did not put there
        Assert.Equal(
            song.Tracks[0].Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Count(n => n.StringIndex == 0),
            cells.SelectMany(c => c.Notes).Count(n => n.StringIndex == 0));
    }

    [Fact]
    public void PalmMutedDeadAndTiedNotesAreTheSameNotesAfterTheTrip()
    {
        var back = RoundTrip(SmallSong()).Tracks[0].Measures;

        Assert.Equal(3, back[0].Cells.First(c => c.Notes.Count == 3).Notes.Count(n => n.Techniques.Contains("PalmMute")));
        Assert.Single(back[0].Cells.SelectMany(c => c.Notes), n => n.Dead && n.StringIndex == 5);
        var destination = Assert.Single(back[1].Cells.SelectMany(c => c.Notes), n => n.Tied);
        Assert.Equal((2, 5), (destination.StringIndex, destination.Fret));
        Assert.Contains(back[0].Cells.SelectMany(c => c.Notes), n => n.Techniques.Contains("Tie"));
        Assert.Contains(back[1].Cells, c => c.Notes.Any(n => n.IsGraceNote) && c.Notes.Any(n => !n.IsGraceNote && n.Fret == 7));
        Assert.Single(back[0].Cells, c => c.IsRest && c.Notes.Count == 0);
    }

    [Fact]
    public void TextChordNameLyricsAndLoudnessOfATabPartSurvive()
    {
        var song = SmallSong();
        var loud = Note(song.Tracks[0], 1, 1);
        loud.Velocity = 83;   // between two dynamic markings: only the exact data keeps it
        song.Tracks[0].Measures[2].Cells[0].DurationDenominator = 4;
        song.Tracks[0].Measures[2].Cells[0].Notes.Add(loud);

        var back = RoundTrip(song);

        var last = back.Tracks[0].Measures[3].Cells.First(c => c.Notes.Count > 0);
        Assert.Equal(("riff", "Am", "la"), (last.Text, last.ChordName, last.Lyrics));
        Assert.Equal(83, back.Tracks[0].Measures[2].Cells.First(c => c.Notes.Count > 0).Notes[0].Velocity);
    }

    [Fact]
    public void TrackColoursMixerMuteSoloAndSendsSurvive()
    {
        var song = SmallSong();
        var second = Guitar("Rhythm", 4);
        Beat(second, 0, 0).Notes.Add(Note(second, 3, 2));
        song.Tracks.Add(second);
        var (one, two) = (song.Tracks[0], song.Tracks[1]);
        one.ColorHex = "#12AB34"; one.Volume = 37; one.Pan = 101; one.Mute = true; one.Chorus = 9; one.Reverb = 55;
        two.ColorHex = "#FF8800"; two.Volume = 127; two.Pan = 0; two.Solo = true; two.ColorFollowsInstrument = true;

        var back = RoundTrip(song);

        Assert.Equal(("#12AB34", 37, 101, true, false, 9, 55), (back.Tracks[0].ColorHex, back.Tracks[0].Volume, back.Tracks[0].Pan, back.Tracks[0].Mute, back.Tracks[0].Solo, back.Tracks[0].Chorus, back.Tracks[0].Reverb));
        Assert.Equal(("#FF8800", 127, 0, false, true, true), (back.Tracks[1].ColorHex, back.Tracks[1].Volume, back.Tracks[1].Pan, back.Tracks[1].Mute, back.Tracks[1].Solo, back.Tracks[1].ColorFollowsInstrument));
    }

    [Fact]
    public void MixerValuesAreWrittenInMusicXmlUnits()
    {
        var song = SmallSong();
        song.Tracks[0].Volume = 64; song.Tracks[0].Pan = 64; song.Tracks[0].ColorHex = "#112233";
        var xml = Encoding.UTF8.GetString(MusicXmlExportService.ToBytes(song));

        Assert.Contains("<part-name color=\"#112233\">Lead</part-name>", xml);
        Assert.Contains("<volume>50.3937</volume>", xml);   // 64 of 127 as a percentage
        Assert.Contains("<pan>0</pan>", xml);               // centre
    }

    [Fact]
    public void EveryMixerValueSurvivesTheMusicXmlUnits()
    {
        for (var value = 0; value <= 127; value++)
        {
            Assert.Equal(value, MusicXmlExtras.VolumeOf(double.Parse(MusicXmlExtras.VolumeText(value), System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(value, MusicXmlExtras.PanOf(double.Parse(MusicXmlExtras.PanText(value), System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<score-partwise version=\"3.1\">";

    [Fact]
    public void AFileWithoutColoursOrMixerGetsTheInstrumentGroupColourAndTheDefaultMixer()
    {
        const string tab = "<attributes><divisions>1</divisions><key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>TAB</sign><line>5</line></clef>"
            + "<staff-details><staff-lines>4</staff-lines><staff-tuning line=\"1\"><tuning-step>E</tuning-step><tuning-octave>1</tuning-octave></staff-tuning><staff-tuning line=\"2\"><tuning-step>A</tuning-step><tuning-octave>1</tuning-octave></staff-tuning>"
            + "<staff-tuning line=\"3\"><tuning-step>D</tuning-step><tuning-octave>2</tuning-octave></staff-tuning><staff-tuning line=\"4\"><tuning-step>G</tuning-step><tuning-octave>2</tuning-octave></staff-tuning></staff-details></attributes>";
        const string note = "<note><pitch><step>A</step><octave>1</octave></pitch><duration>4</duration><type>whole</type><notations><technical><string>3</string><fret>0</fret></technical></notations></note>";
        var xml = Header + "<part-list>"
            + "<score-part id=\"P1\"><part-name>Bass</part-name><midi-instrument id=\"P1-I1\"><midi-channel>1</midi-channel><midi-program>34</midi-program></midi-instrument></score-part>"
            + "<score-part id=\"P2\"><part-name>Gtr</part-name><midi-instrument id=\"P2-I1\"><midi-channel>2</midi-channel><midi-program>30</midi-program></midi-instrument></score-part></part-list>"
            + $"<part id=\"P1\"><measure number=\"1\">{tab}{note}</measure></part>"
            + $"<part id=\"P2\"><measure number=\"1\">{tab}{note}</measure></part></score-partwise>";

        var song = ReadText("plain.musicxml", xml);

        Assert.Equal(2, song.Tracks.Count);
        Assert.All(song.Tracks, t => Assert.True(t.ColorFollowsInstrument));
        Assert.Equal(NamedColourSwatches.HexOf("Yellow"), song.Tracks[0].ColorHex);   // a bass, as a new bass track
        Assert.All(song.Tracks, t => Assert.Equal((100, 64, false, false), (t.Volume, t.Pan, t.Mute, t.Solo)));
    }

    [Fact]
    public void ANotationOnlyPitchKeepsItsPitchAndIsNotPutOnStringOneFretZero()
    {
        const string attributes = "<attributes><divisions>1</divisions><key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>";
        string Quarter(string step, int octave) => $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>1</duration><type>quarter</type></note>";
        var xml = Header + "<part-list><score-part id=\"P1\"><part-name>Piano</part-name><midi-instrument id=\"P1-I1\"><midi-channel>1</midi-channel><midi-program>1</midi-program></midi-instrument></score-part></part-list>"
            + $"<part id=\"P1\"><measure number=\"1\">{attributes}{Quarter("C", 4)}{Quarter("E", 4)}{Quarter("G", 4)}{Quarter("C", 5)}</measure></part></score-partwise>";

        var song = ReadText("piano.musicxml", xml);

        var notes = song.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes).ToList();
        Assert.Equal(new[] { 60, 64, 67, 72 }, notes.Select(n => n.MidiValue));
        Assert.DoesNotContain(notes, n => n.StringIndex == 0 && n.Fret == 0);
    }

    [Fact]
    public void JumpsDoubleBarsPickupBarsClefsAndSwingSurvive()
    {
        var song = new SongProject { Title = "Marks", Tempo = 90 };
        var t = Guitar("Lead", 6);
        for (var bar = 0; bar < 6; bar++) Beat(t, bar, 0).Notes.Add(Note(t, 2, bar));
        t.Measures[0].Anacrusis = true;
        t.Measures[1].Directions = "Segno";
        t.Measures[2].IsDoubleBar = true;
        t.Measures[3].Directions = "ToCoda";
        t.Measures[3].Clef = Clefs.Treble;
        t.Measures[3].TripletFeelKind = TripletFeels.Eighth; t.Measures[3].TripletFeel = true;
        t.Measures[4].Directions = "DalSegnoAlCoda";
        t.Measures[5].Directions = "DoubleCoda";
        t.Measures[5].Clef = Clefs.Guitar;
        song.Tracks.Add(t);

        AssertSame(song, RoundTrip(song));
    }

    [Fact]
    public void ACapoAndAnExtraStringKeepTheirTuning()
    {
        var song = new SongProject { Title = "Seven" };
        var t = Guitar("Seven");
        t.StringTunings = new() { 64, 59, 55, 50, 45, 40, 33 };
        t.Capo = 3;
        Beat(t, 0, 0).Notes.Add(Note(t, 6, 2));
        Beat(t, 0, 4).Notes.Add(Note(t, 0, 5));
        song.Tracks.Add(t);

        var back = RoundTrip(song).Tracks[0];

        Assert.Equal(("64,59,55,50,45,40,33", 3), (string.Join(",", back.StringTunings), back.Capo));
        AssertSame(song, RoundTrip(song));
    }
}
