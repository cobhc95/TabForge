using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the musical-content round trip of the Guitar Pro and MIDI exporters (a small built song survives export+import).
// Does not own: the exporters/importers themselves or the export dialogs.
// Tests: this class. Headless, no sleeps; files live under Path.GetTempPath() and are deleted in Dispose.
public sealed class SongExportContentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-export-content-{Guid.NewGuid():N}");

    public SongExportContentTests() { Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static SongProject BuildSong()
    {
        var song = new SongProject
        {
            Title = "ExportContent",
            Tempo = 120,
            TimeSignatureNumerator = 4, // SongProject.cs:43-44
            TimeSignatureDenominator = 4,
        };
        var lead = new TrackModel
        {
            Name = "Lead",
            Kind = TrackKind.Guitar,
            MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
            Measures = TemplateFactory.Measures(4), // TemplateFactory.cs:117-119
        };
        var bass = new TrackModel
        {
            Name = "Bass",
            Kind = TrackKind.Bass,
            MidiChannel = 1,
            StringTunings = new List<int> { 43, 38, 33, 28 },
            Measures = TemplateFactory.Measures(4),
        };
        song.Tracks.Add(lead);
        song.Tracks.Add(bass);

        // Bar 1: quarter notes.
        PutNote(lead, 0, 0, 0, 0);
        PutNote(lead, 0, 4, 0, 3);
        PutNote(lead, 0, 8, 1, 2);
        PutNote(lead, 0, 12, 2, 0);
        PutNote(bass, 0, 0, 0, 0);
        PutNote(bass, 0, 8, 0, 3);

        // Bar 2: a tie (the second note sustains the first) and a rest on both tracks.
        PutNote(lead, 1, 0, 0, 5);
        PutNote(lead, 1, 4, 0, 5).Tied = true; // TabNote.Tied: tie destination (SongProject.cs:431)
        Rest(lead, 1, 8);
        PutNote(lead, 1, 12, 0, 7);
        PutNote(bass, 1, 0, 0, 5);
        PutNote(bass, 1, 4, 0, 5);
        Rest(bass, 1, 8);

        // Bar 3 carries the tempo change.
        lead.Measures[2].TempoChange = 140; // MeasureModel.TempoChange (SongProject.cs:236)
        PutNote(lead, 2, 0, 0, 3);
        PutNote(lead, 2, 4, 0, 5);
        PutNote(bass, 2, 0, 0, 3);

        // Bar 4.
        PutNote(lead, 3, 0, 1, 0);
        PutNote(bass, 3, 0, 0, 0);
        return song;
    }

    private static TabNote PutNote(TrackModel track, int bar, int cell, int stringIndex, int fret, int denom = 4)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = denom;
        var note = new TabNote
        {
            StringIndex = stringIndex,
            Fret = fret,
            MidiValue = track.PitchOf(stringIndex, fret), // SongProject.cs:151-153
        };
        beat.Notes.Add(note);
        return note;
    }

    private static void Rest(TrackModel track, int bar, int cell, int denom = 4)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = denom;
        beat.IsRest = true; // TabCell.IsRest (SongProject.cs:343)
    }

    private static int Mpq(int bpm) => (int)Math.Round(60_000_000.0 / bpm);

    [Fact]
    public void Gp_TrackCountTuningAndTimeSignatureSurviveRoundTrip()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.gp");
        GuitarProExporter.Save(song, path); // GuitarProExporter.cs:30
        // GuitarProImporter.cs:76; an embedded TabForge project loads back losslessly (:104-112).
        var imported = GuitarProImporter.Import(path);

        Assert.Equal(song.Tracks.Count, imported.Tracks.Count);
        for (var t = 0; t < song.Tracks.Count; t++)
            Assert.Equal(song.Tracks[t].StringTunings, imported.Tracks[t].StringTunings);
        Assert.Equal(4, imported.TimeSignatureNumerator);
        Assert.Equal(4, imported.TimeSignatureDenominator);
    }

    [Fact]
    public void Gp_TempoAndChangeBarSurviveRoundTrip()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.gp");
        GuitarProExporter.Save(song, path);
        var imported = GuitarProImporter.Import(path);

        Assert.Equal(120, imported.Tempo);
        Assert.Equal(140, imported.Tracks[0].Measures[2].TempoChange);
        Assert.Equal(120, MusicTime.TempoAt(imported, 0)); // MusicTime.cs:156-168
        Assert.Equal(140, MusicTime.TempoAt(imported, 2));
    }

    [Fact]
    public void Gp_NoteStringsFretsPositionsAndDurationsSurviveRoundTrip()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.gp");
        GuitarProExporter.Save(song, path);
        var imported = GuitarProImporter.Import(path);

        for (var t = 0; t < song.Tracks.Count; t++)
            for (var b = 0; b < song.Tracks[t].Measures.Count; b++)
                for (var c = 0; c < song.Tracks[t].Measures[b].Cells.Count; c++)
                {
                    var expected = song.Tracks[t].Measures[b].Cells[c];
                    var actual = imported.Tracks[t].Measures[b].Cells[c];
                    Assert.Equal(expected.DurationDenominator, actual.DurationDenominator);
                    Assert.Equal(expected.Dots, actual.Dots);
                    Assert.Equal(expected.IsRest, actual.IsRest);
                    Assert.Equal(expected.RhythmicPosition, actual.RhythmicPosition);
                    Assert.Equal(expected.Notes.Count, actual.Notes.Count);
                    for (var n = 0; n < expected.Notes.Count; n++)
                    {
                        Assert.Equal(expected.Notes[n].StringIndex, actual.Notes[n].StringIndex);
                        Assert.Equal(expected.Notes[n].Fret, actual.Notes[n].Fret);
                        Assert.Equal(expected.Notes[n].MidiValue, actual.Notes[n].MidiValue);
                    }
                }
    }

    [Fact]
    public void Gp_TieAndRestSurviveRoundTrip()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.gp");
        GuitarProExporter.Save(song, path);
        var imported = GuitarProImporter.Import(path);

        Assert.True(imported.Tracks[0].Measures[1].Cells[4].Notes.Single().Tied);
        Assert.True(imported.Tracks[0].Measures[1].Cells[8].IsRest);
        Assert.True(imported.Tracks[1].Measures[1].Cells[8].IsRest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gp_EveryZipEntryHasACorrectCrcAndAlphaTabReimportsIt(bool embedProject)
    {
        var bytes = GuitarProExporter.ToBytes(BuildSong(), embedProject);
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read))
            foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                using var stream = entry.Open();
                using var sink = new MemoryStream();
                stream.CopyTo(sink); // a bad CRC-32 throws InvalidDataException here
                Assert.Equal(entry.Length, sink.Length);
            }
        var settings = new AlphaTab.Settings();
        var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(bytes.Select(b => (byte)b).ToArray(), settings);
        Assert.Equal(2, score.Tracks.Count);
    }

    [Fact]
    public void Gp_ExporterDropsARestOnlySecondVoice()
    {
        var song = BuildSong();
        var lead = song.Tracks[0];
        lead.Measures[0].Voice2Cells = new List<TabCell> { new() { IsRest = true, DurationDenominator = 4 } };
        var low = new TabCell { DurationDenominator = 1 };
        low.Notes.Add(new TabNote { StringIndex = 5, Fret = 0, MidiValue = lead.PitchOf(5, 0) });
        lead.Measures[1].Voice2Cells = new List<TabCell> { low };

        var path = Path.Combine(_folder, "voice2.gp");
        GuitarProExporter.Save(song, path, embedProject: false);
        var imported = GuitarProImporter.Import(path);

        Assert.DoesNotContain(imported.Tracks[0].Measures[0].Voice2Cells, c => c.IsRest && c.DurationDenominator == 4);
        Assert.Contains(imported.Tracks[0].Measures[1].Voice2Cells, c => c.Notes.Count > 0);
        Assert.All(imported.Tracks[0].Measures.Take(2), m => Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(m, 4, 4)));
    }

    private sealed record MidiNote(int Pitch, int StartTick, int EndTick, int Channel);
    private sealed record MidiFileData(int Division, List<(int Tick, int Mpq)> Tempos, List<(int Tick, int Num, int Den)> TimeSigs, List<MidiNote> Notes, int TrackChunks);

    private static int ReadBe16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
    private static int ReadBe32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

    private static int ReadVarLen(byte[] b, ref int p)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var x = b[p++];
            value = (value << 7) | (x & 0x7F);
            if ((x & 0x80) == 0) return value;
        }
        return value;
    }

    // Minimal SMF reader for the exporter's own output shape (MThd header: MidiExportService.cs:38-43,
    // MTrk chunk framing: :257-266, tempo/time-signature meta: :287-300, note events: :100-113).
    private static MidiFileData ParseMidi(byte[] b)
    {
        Assert.True(b.Length > 14 && b[0] == (byte)'M' && b[1] == (byte)'T' && b[2] == (byte)'h' && b[3] == (byte)'d');
        var trackCount = ReadBe16(b, 10);
        var division = ReadBe16(b, 12);
        var tempos = new List<(int, int)>();
        var sigs = new List<(int, int, int)>();
        var notes = new List<MidiNote>();
        var pending = new Dictionary<(int, int), Queue<int>>();
        var p = 14;
        for (var chunk = 0; chunk < trackCount; chunk++)
        {
            var length = ReadBe32(b, p + 4);
            var end = p + 8 + length;
            var q = p + 8;
            var tick = 0;
            var status = -1;
            while (q < end)
            {
                tick += ReadVarLen(b, ref q);
                var s = b[q];
                if (s == 0xFF)
                {
                    q++;
                    var type = b[q++];
                    var len = ReadVarLen(b, ref q);
                    if (type == 0x51 && len == 3) tempos.Add((tick, (b[q] << 16) | (b[q + 1] << 8) | b[q + 2]));
                    if (type == 0x58 && len >= 2) sigs.Add((tick, b[q], 1 << b[q + 1]));
                    q += len;
                    status = -1;
                    continue;
                }
                if (s == 0xF0 || s == 0xF7)
                {
                    q++;
                    var len = ReadVarLen(b, ref q);
                    q += len;
                    status = -1;
                    continue;
                }
                byte first, second;
                if (s >= 0x80) { status = s; q++; first = b[q++]; }
                else { first = s; }
                var kind = status & 0xF0;
                var channel = status & 0x0F;
                if (kind == 0xC0 || kind == 0xD0) continue;
                second = b[q++];
                var key = (channel, (int)first);
                if (kind == 0x90 && second > 0)
                {
                    if (!pending.TryGetValue(key, out var queue)) pending[key] = queue = new Queue<int>();
                    queue.Enqueue(tick);
                }
                else if (kind == 0x80 || kind == 0x90)
                {
                    if (pending.TryGetValue(key, out var queue) && queue.Count > 0)
                        notes.Add(new MidiNote(first, queue.Dequeue(), tick, channel));
                }
            }
            p = end;
        }
        return new MidiFileData(division, tempos, sigs, notes, trackCount);
    }

    [Fact]
    public void Midi_DivisionIs480AndQuarterAt120BpmIsHalfSecond()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.mid");
        MidiExportService.Export(song, path); // MidiExportService.cs:22-23
        var midi = ParseMidi(File.ReadAllBytes(path));

        Assert.Equal(480, midi.Division); // MidiExportService.cs:20
        var first = midi.Tempos.OrderBy(t => t.Tick).First();
        Assert.Equal(0, first.Tick);
        Assert.Equal(Mpq(120), first.Mpq);
        Assert.Equal(0.5, first.Mpq / 1_000_000.0, 9);
    }

    [Fact]
    public void Midi_TimeSignatureTempoChangeAndTrackCountSurvive()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));

        Assert.Contains(midi.TimeSigs, s => s.Tick == 0 && s.Num == 4 && s.Den == 4);
        // not verified: the exact tick merge of tempo steps inside TickMap.BuildTempoMap; the change-bar
        // event value and its bar-start position (MidiExportService.cs:148 TickOfBar, :165-187) are asserted.
        var change = Assert.Single(midi.Tempos, t => t.Mpq == Mpq(140));
        Assert.Equal(8 * midi.Division, change.Tick);
        Assert.Equal(1 + song.Tracks.Count(t => !t.IsAudio), midi.TrackChunks); // MidiExportService.cs:32-34
    }

    [Fact]
    public void Midi_PitchesStartsAndDurationsSurvive()
    {
        var song = BuildSong();
        var path = Path.Combine(_folder, "song.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));

        var expected = new HashSet<int>(song.Tracks
            .SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes).Select(n => n.MidiValue));
        Assert.NotEmpty(midi.Notes);
        foreach (var pitch in expected)
            Assert.Contains(midi.Notes, n => n.Pitch == pitch);
        Assert.All(midi.Notes, n => Assert.True(n.EndTick > n.StartTick));
        Assert.Equal(0, midi.Notes.Min(n => n.StartTick));
        // not verified: the exact tick merge of tied notes in the playback compiler; a tie must at
        // least sustain (sound for two quarters here), never shorten.
        var tiedPitch = song.Tracks[0].Measures[1].Cells[0].Notes[0].MidiValue;
        var sounding = midi.Notes.Where(n => n.Pitch == tiedPitch).Sum(n => n.EndTick - n.StartTick);
        Assert.True(sounding >= 2 * midi.Division);
        // Both tracks rest over bar 2, cells 8..12: no note may start inside that span.
        Assert.DoesNotContain(midi.Notes, n => n.StartTick >= 6 * midi.Division && n.StartTick < 7 * midi.Division);
    }
}
