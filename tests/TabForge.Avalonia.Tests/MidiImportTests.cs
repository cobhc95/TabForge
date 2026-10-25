using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: MIDI import (.mid / .midi): the strict reader, bars from the tempo and time signature maps, quantising, ties across bar lines, drum channel, a TabForge export read back.
// Does not own: the file association list (FileTypes) and the Guitar Pro conversion.
public sealed class MidiImportTests : IDisposable
{
    private const int Div = 480;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-mid-{Guid.NewGuid():N}");

    public MidiImportTests() { Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>One track chunk: (tick, bytes) events written in tick order with delta times.</summary>
    private static byte[] Chunk(IEnumerable<(int Tick, byte[] Bytes)> events)
    {
        var body = new List<byte>();
        var last = 0;
        foreach (var (tick, bytes) in events.OrderBy(e => e.Tick))
        {
            Vlq(body, tick - last); last = tick;
            body.AddRange(bytes);
        }
        body.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
        return Ascii("MTrk").Concat(new byte[] { 0, 0, (byte)(body.Count >> 8), (byte)body.Count }).Concat(body).ToArray();
    }

    private static void Vlq(List<byte> o, int v)
    {
        var parts = new Stack<byte>();
        parts.Push((byte)(v & 0x7F));
        for (v >>= 7; v > 0; v >>= 7) parts.Push((byte)(v & 0x7F | 0x80));
        o.AddRange(parts);
    }

    private static byte[] File1(int format, params byte[][] chunks) =>
        Ascii("MThd").Concat(new byte[] { 0, 0, 0, 6, 0, (byte)format, 0, (byte)chunks.Length, Div >> 8, Div & 0xFF }).Concat(chunks.SelectMany(c => c)).ToArray();

    private static (int, byte[]) Tempo(int tick, int bpm) { var mpq = 60_000_000 / bpm; return (tick, new byte[] { 0xFF, 0x51, 3, (byte)(mpq >> 16), (byte)(mpq >> 8), (byte)mpq }); }
    private static (int, byte[]) Sig(int tick, int n, int d) => (tick, new byte[] { 0xFF, 0x58, 4, (byte)n, (byte)Math.Log2(d), 24, 8 });
    private static (int, byte[]) On(int tick, int pitch, int ch = 0) => (tick, new byte[] { (byte)(0x90 | ch), (byte)pitch, 90 });
    private static (int, byte[]) Off(int tick, int pitch, int ch = 0) => (tick, new byte[] { (byte)(0x80 | ch), (byte)pitch, 0 });
    private static IEnumerable<(int, byte[])> Note(int tick, int length, int pitch, int ch = 0) { yield return On(tick, pitch, ch); yield return Off(tick + length, pitch, ch); }

    private SongProject Load(byte[] file, string name = "t.mid")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, file);
        return GuitarProImporter.Import(path);
    }

    private static List<TabCell> Beats(IEnumerable<TabCell> cells) => cells.Where(c => c.Notes.Count > 0 || c.IsRest).ToList();

    [Fact]
    public void ScaleInQuartersMakesTwoBars()
    {
        var notes = new[] { 60, 62, 64, 65, 67, 69, 71, 72 };
        var song = Load(File1(1, Chunk(new[] { Tempo(0, 120), Sig(0, 4, 4) }), Chunk(notes.SelectMany((p, i) => Note(i * Div, Div, p)))));

        Assert.Equal(120, song.Tempo);
        var track = Assert.Single(song.Tracks);
        Assert.Equal(2, track.Measures.Count);
        foreach (var bar in track.Measures) Assert.Equal(4, Beats(bar.Cells).Count);
        Assert.Equal(notes, track.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => n.MidiValue).ToArray());
        Assert.All(track.Measures.SelectMany(m => m.Cells).Where(c => c.Notes.Count > 0), c => Assert.Equal(4, c.DurationDenominator));
    }

    [Fact]
    public void TempoChangeAtBarThreeIsOnTheMasterBar()
    {
        var events = Enumerable.Range(0, 12).SelectMany(i => Note(i * Div, Div, 60));
        var song = Load(File1(1, Chunk(new[] { Tempo(0, 100), Tempo(8 * Div, 140) }), Chunk(events)));

        Assert.Equal(100, song.Tempo);
        var bars = song.Tracks[0].Measures;
        Assert.Equal(3, bars.Count);
        Assert.Equal(140, bars[2].TempoChange);
        Assert.Null(bars[1].TempoChange);
    }

    [Fact]
    public void EighthTripletsBecomeThreeToTwoTuplets()
    {
        var events = Enumerable.Range(0, 3).SelectMany(i => Note(i * 160, 160, 64)).Concat(Note(Div, Div * 3, 67));
        var song = Load(File1(1, Chunk(new[] { Tempo(0, 120) }), Chunk(events)));

        var first = song.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).Take(3).ToList();
        Assert.Equal(3, first.Count);
        Assert.All(first, c => Assert.Equal((3, 2), c.Tuplet));
        Assert.All(first, c => Assert.Equal(8, c.DurationDenominator));
    }

    [Fact]
    public void WholeNoteAcrossTheBarLineIsTied()
    {
        var song = Load(File1(1, Chunk(new[] { Tempo(0, 120) }), Chunk(Note(2 * Div, 4 * Div, 60).Concat(Note(6 * Div, 2 * Div, 62)))));

        var bars = song.Tracks[0].Measures;
        Assert.Equal(2, bars.Count);
        var firstBar = bars[0].Cells.Where(c => c.Notes.Count > 0).Single();
        Assert.Equal(2, firstBar.DurationDenominator);
        Assert.Contains("Tie", firstBar.Notes[0].Techniques);
        var tied = bars[1].Cells.Where(c => c.Notes.Count > 0).First();
        Assert.True(tied.Notes[0].Tied);
        Assert.Equal(60, tied.Notes[0].MidiValue);
    }

    [Fact]
    public void ChannelTenIsADrumTrack()
    {
        var drums = Enumerable.Range(0, 4).SelectMany(i => Note(i * Div, 60, i % 2 == 0 ? 36 : 38, 9));
        var song = Load(File1(1, Chunk(new[] { Tempo(0, 120) }), Chunk(Note(0, Div, 60).Concat(drums))));

        Assert.Equal(2, song.Tracks.Count);
        var kit = song.Tracks.Single(t => t.Kind == TrackKind.Drums);
        Assert.Equal(9, kit.MidiChannel);
        var hits = kit.Measures[0].Cells.SelectMany(c => c.Notes).ToList();
        Assert.Equal(new[] { 36, 38, 36, 38 }, hits.Select(n => n.MidiValue).ToArray());
        Assert.Equal(GuitarProImporter.DrumLine(36), hits[0].StringIndex);
    }

    [Fact]
    public void OverlappingLineGoesToVoiceTwo()
    {
        var events = Note(0, 4 * Div, 48).Concat(Note(Div, Div, 64)).Concat(Note(2 * Div, Div, 66));
        var song = Load(File1(1, Chunk(new[] { Tempo(0, 120) }), Chunk(events)));

        var bar = song.Tracks[0].Measures[0];
        Assert.Single(bar.Cells, c => c.Notes.Count > 0);
        Assert.Equal(2, bar.Voice2Cells.Count(c => c.Notes.Count > 0));
    }

    [Fact]
    public void TimeSignatureChangeSetsTheBarLength()
    {
        var events = Enumerable.Range(0, 7).SelectMany(i => Note(i * Div, Div, 60));
        var song = Load(File1(1, Chunk(new[] { Sig(0, 4, 4), Sig(4 * Div, 3, 4) }), Chunk(events)));

        Assert.Equal(2, song.Tracks[0].Measures.Count);
        Assert.Equal((3, 4), (song.Tracks[0].Measures[1].TimeSigNum, song.Tracks[0].Measures[1].TimeSigDenom));
        Assert.Equal(3, Beats(song.Tracks[0].Measures[1].Cells).Count);
    }

    [Fact]
    public void FormatZeroReadsRunningStatus()
    {
        // Two notes in one track; the second note-on and the second note-off use running status.
        var body = new List<byte>();
        body.AddRange(new byte[] { 0, 0x90, 60, 90, 0, 62, 90, 0x83, 0x60, 0x80, 60, 0, 0, 62, 0 });
        body.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
        var chunk = Ascii("MTrk").Concat(new byte[] { 0, 0, 0, (byte)body.Count }).Concat(body).ToArray();
        var song = Load(File1(0, chunk));

        Assert.Single(song.Tracks);
        Assert.Equal(2, song.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes).Count());
    }

    [Fact]
    public void DamagedFilesAreRefused()
    {
        var good = File1(1, Chunk(new[] { Tempo(0, 120) }), Chunk(Note(0, Div, 60)));
        Assert.Throws<InvalidDataException>(() => Load(good[..(good.Length - 6)], "cut.mid"));
        Assert.Throws<InvalidDataException>(() => Load(File1(2, Chunk(Note(0, Div, 60))), "f2.mid"));
        Assert.Throws<InvalidDataException>(() => Load(new byte[] { 1, 2, 3, 4, 5 }, "junk.midi"));
        var empty = Assert.Throws<InvalidDataException>(() => Load(File1(1, Chunk(new[] { Tempo(0, 120) })), "empty.mid"));
        Assert.Contains("no tracks", empty.Message);
    }

    [Fact]
    public void ExtensionsAreOpenable()
    {
        foreach (var e in new[] { ".mid", ".MIDI" }) Assert.True(FileTypes.IsOpenable(e));
        Assert.DoesNotContain(".mid", FileTypes.AllOpenable);
        Assert.Contains(".midi", GuitarProImporter.SupportedExtensions);
    }

    [Fact]
    public void OwnExportReadsBack()
    {
        var song = new SongProject { Title = "RT", Tempo = 96 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiProgram = 29, Measures = TabForge.Presets.TemplateFactory.Measures(4) };
        for (var bar = 0; bar < 4; bar++)
            foreach (var cell in new[] { 0, 4, 8, 12 })
            {
                var c = track.Measures[bar].Cells[cell];
                c.DurationDenominator = 4;
                var fret = (bar + cell / 4) % 5;
                c.Notes.Add(new TabNote { StringIndex = 2, Fret = fret, MidiValue = track.PitchOf(2, fret) });
            }
        song.Tracks.Add(track);
        var path = Path.Combine(_folder, "own.mid");
        MidiExportService.Export(song, path);

        var back = GuitarProImporter.Import(path);

        Assert.Equal(96, back.Tempo);
        var bt = Assert.Single(back.Tracks);
        Assert.Equal(4, bt.Measures.Count);
        Assert.Equal(track.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => n.MidiValue),
            bt.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => n.MidiValue));
    }

    [Fact]
    public void DemoSongExportReadsBack()
    {
        var song = FullDemoSongFactory.Create();
        var path = Path.Combine(_folder, "demo.mid");
        MidiExportService.Export(song, path);

        var back = GuitarProImporter.Import(path);

        Assert.True(back.Tracks.Count >= song.Tracks.Count(t => !t.IsAudio));
        Assert.True(back.Tracks[0].Measures.Count >= song.Tracks[0].Measures.Count / 2);
        Assert.Equal(song.Tempo, back.Tempo);
        Assert.NotEmpty(back.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes));
    }
}
