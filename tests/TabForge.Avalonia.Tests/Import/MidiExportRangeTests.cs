using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Export MIDI track-and-range checks (track filter, whole-song length, clipped and chased
//   range exports, time range equals bar range, tempo in force at the range start, empty tracks, repeats,
//   and whole-song bytes unchanged from the old export).
// Does not own: the exporter, the range helpers or the compiler.
// Tests: this class. Headless, no sleeps; files live under Path.GetTempPath() and are deleted in Dispose.
public sealed class MidiExportRangeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-midi-range-{Guid.NewGuid():N}");

    public MidiExportRangeTests() => Directory.CreateDirectory(_folder);
    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static SongProject BuildSong(int bars = 8)
    {
        var song = new SongProject { Title = "Range", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        song.Tracks.Add(new TrackModel { Name = "Gtr", Kind = TrackKind.Guitar, MidiChannel = 0, MidiProgram = 25,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars) });
        song.Tracks.Add(new TrackModel { Name = "Bass", Kind = TrackKind.Bass, MidiChannel = 1, MidiProgram = 33,
            StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(bars) });
        song.Tracks.Add(new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, Measures = TemplateFactory.Measures(bars) });
        return song;
    }

    private static void PutNote(TrackModel track, int bar, int cell, int stringIndex = 0, int fret = 0, int denom = 4)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = denom;
        beat.Notes.Add(new TabNote { StringIndex = stringIndex, Fret = fret, MidiValue = track.PitchOf(stringIndex, fret) });
    }

    private sealed record Ev(int Tick, int Status, int D1, int D2, int? MetaType, byte[]? Meta);
    private sealed record MidiFile(int Format, int Division, List<List<Ev>> Chunks);

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

    private static MidiFile ParseMidi(byte[] b)
    {
        var format = ReadBe16(b, 8);
        var trackCount = ReadBe16(b, 10);
        var division = ReadBe16(b, 12);
        var chunks = new List<List<Ev>>();
        var p = 14;
        for (var c = 0; c < trackCount; c++)
        {
            var length = ReadBe32(b, p + 4);
            var end = p + 8 + length;
            var q = p + 8;
            var tick = 0;
            var list = new List<Ev>();
            while (q < end)
            {
                tick += ReadVarLen(b, ref q);
                var s = b[q];
                if (s == 0xFF)
                {
                    var type = b[q + 1];
                    q += 2;
                    var len = ReadVarLen(b, ref q);
                    var data = b.Skip(q).Take(len).ToArray();
                    q += len;
                    list.Add(new Ev(tick, 0xFF, type, len, type, data));
                    continue;
                }
                var status = s; q++;
                var kind = status & 0xF0;
                if (kind == 0xC0 || kind == 0xD0) { list.Add(new Ev(tick, status, b[q], 0, null, null)); q++; continue; }
                var d1 = b[q++]; var d2 = b[q++];
                list.Add(new Ev(tick, status, d1, d2, null, null));
            }
            chunks.Add(list);
            p = end;
        }
        return new MidiFile(format, division, chunks);
    }

    private string Export(SongProject song, MidiExportSlice slice, string name)
    {
        var path = Path.Combine(_folder, name);
        MidiExportService.Export(song, path, slice);
        return path;
    }

    private static int MaxTick(MidiFile midi) => midi.Chunks.SelectMany(c => c.Select(e => e.Tick)).DefaultIfEmpty(0).Max();

    private static int TempoAt0(List<Ev> conductor) =>
        (conductor.First(e => e is { Status: 0xFF, D1: 0x51, Tick: 0 }).Meta![0] << 16)
        | (conductor.First(e => e is { Status: 0xFF, D1: 0x51, Tick: 0 }).Meta![1] << 8)
        | conductor.First(e => e is { Status: 0xFF, D1: 0x51, Tick: 0 }).Meta![2];

    private static (int Num, int Den) TimeSigAt0(List<Ev> conductor)
    {
        var meta = conductor.First(e => e is { Status: 0xFF, D1: 0x58, Tick: 0 }).Meta!;
        return (meta[0], 1 << meta[1]);
    }

    private static int? ChannelMessageAt0(List<Ev> track, int kind, int channel, int d1)
    {
        var e = track.FirstOrDefault(e => e.Tick == 0 && (e.Status & 0xF0) == kind && (e.Status & 0x0F) == channel
            && (kind is 0xC0 or 0xD0 || e.D1 == d1));
        return e?.D2;
    }

    private static List<(int Pitch, int Start, int End)> Notes(List<Ev> track)
    {
        var notes = new List<(int, int, int)>();
        var pending = new Dictionary<(int, int), Queue<int>>();
        foreach (var e in track)
        {
            var kind = e.Status & 0xF0;
            var channel = e.Status & 0x0F;
            if (kind == 0x90 && e.D2 > 0)
            {
                if (!pending.TryGetValue((channel, e.D1), out var queue)) pending[(channel, e.D1)] = queue = new Queue<int>();
                queue.Enqueue(e.Tick);
            }
            else if (kind == 0x80 || kind == 0x90)
            {
                if (pending.TryGetValue((channel, e.D1), out var queue) && queue.Count > 0)
                    notes.Add((e.D1, queue.Dequeue(), e.Tick));
            }
        }
        return notes;
    }

    [Fact]
    public void BassOnlyWholeSongHasOneTrackPlusTempoAndLastsSixteenSeconds()
    {
        var song = BuildSong();
        PutNote(song.Tracks[1], 0, 0);
        var (timeline, slice) = MidiExportRange.Resolve(song,
            new MidiExportSettings { TrackNames = new List<string> { "Bass" } }, null, null);
        var midi = ParseMidi(File.ReadAllBytes(Export(song, slice, "bass.mid")));

        Assert.Equal(2, midi.Chunks.Count);   // conductor plus the one ticked track
        Assert.Equal(480, midi.Division);
        Assert.Equal(500_000, TempoAt0(midi.Chunks[0]));
        Assert.Equal((4, 4), TimeSigAt0(midi.Chunks[0]));
        Assert.Equal(8 * 4 * 480, MaxTick(midi));   // 8 bars of 4 quarters: 16 s at 120 bpm
        Assert.DoesNotContain(midi.Chunks[1], e => e.Status == 0xFF && e.D1 == 0x51);
    }

    [Fact]
    public void BarRangeStartsAtTickZeroWithTempoTimeSigChaseAndClippedNote()
    {
        var song = BuildSong();
        song.Tracks[0].Measures[0].Cells[0].Mix = new MixChange { Volume = 10 };
        PutNote(song.Tracks[0], 1, 12, denom: 2);   // half note: starts in bar 2, ends inside bar 3
        PutNote(song.Tracks[0], 2, 4, fret: 2);   // a note fully inside the range, one beat in
        var settings = new MidiExportSettings { Bounds = 2, CustomFromBar = 3, CustomToBar = 4, TrackNames = new List<string> { "Gtr" } };
        var (_, slice) = MidiExportRange.Resolve(song, settings, null, null);
        var midi = ParseMidi(File.ReadAllBytes(Export(song, slice, "bars34.mid")));

        Assert.Equal(2, midi.Chunks.Count);
        Assert.Equal(500_000, TempoAt0(midi.Chunks[0]));
        Assert.Equal((4, 4), TimeSigAt0(midi.Chunks[0]));
        Assert.Equal(2 * 4 * 480, MaxTick(midi));   // two bars, offset removed
        var track = midi.Chunks[1];
        Assert.Equal(25, track.First(e => e.Tick == 0 && (e.Status & 0xF0) == 0xC0).D1);   // program chase
        Assert.NotNull(ChannelMessageAt0(track, 0xB0, 0, 7));   // volume CC chase
        var notes = Notes(track);
        Assert.Contains(notes, n => n.Start == 0 && n.End == 480);   // the crossing note: not dropped, clipped to start
        Assert.Contains(notes, n => n.Pitch == 66 && n.Start == 480 && n.End == 960);   // the inside note keeps its relative tick
    }

    [Fact]
    public void TimeRangeEqualsBarRange()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 2, 0);
        PutNote(song.Tracks[0], 3, 8);
        var (_, bars) = MidiExportRange.Resolve(song,
            new MidiExportSettings { Bounds = 2, CustomFromBar = 3, CustomToBar = 4 }, null, null);
        var (_, secs) = MidiExportRange.Resolve(song,
            new MidiExportSettings { Bounds = 3, CustomStartSec = 4, CustomEndSec = 8 }, null, null);
        var a = File.ReadAllBytes(Export(song, bars, "a.mid"));
        var b = File.ReadAllBytes(Export(song, secs, "b.mid"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void RangeStartingAtTempoChangeBeginsWithTheNewTempo()
    {
        var song = BuildSong();
        foreach (var track in song.Tracks) track.Measures[2].TempoChange = 150;
        PutNote(song.Tracks[0], 2, 0);
        var (_, slice) = MidiExportRange.Resolve(song,
            new MidiExportSettings { Bounds = 2, CustomFromBar = 3, CustomToBar = 4 }, null, null);
        var midi = ParseMidi(File.ReadAllBytes(Export(song, slice, "tempo.mid")));
        Assert.Equal((int)Math.Round(60_000_000.0 / 150), TempoAt0(midi.Chunks[0]));
    }

    [Fact]
    public void NoTracksWritesConductorOnly()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 0, 0);
        var midi = ParseMidi(File.ReadAllBytes(Export(song, new MidiExportSlice(new HashSet<int>(), 0, 2000), "empty.mid")));
        Assert.Single(midi.Chunks);
        Assert.Equal(0, midi.Format);
    }

    [Fact]
    public void WholeSongWithAllTracksMatchesTheOldExportExceptSectionMarkers()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 0, 0);
        PutNote(song.Tracks[1], 4, 8);
        var (_, slice) = MidiExportRange.Resolve(song, new MidiExportSettings(), null, null);
        var oldPath = Path.Combine(_folder, "old.mid");
        MidiExportService.Export(song, oldPath);
        var newPath = Export(song, slice, "new.mid");
        Assert.Equal(File.ReadAllBytes(oldPath), File.ReadAllBytes(newPath));
        // With sections the default export only adds the section markers.
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Chorus" });
        var (_, sectionSlice) = MidiExportRange.Resolve(song, new MidiExportSettings(), null, null);
        var offPath = Path.Combine(_folder, "off.mid");
        MidiExportService.Export(song, offPath, sectionSlice, null, writeSections: false);
        Assert.Equal(File.ReadAllBytes(oldPath), File.ReadAllBytes(offPath));
        var onMidi = ParseMidi(File.ReadAllBytes(Export(song, sectionSlice, "on.mid")));
        var sectionNames = onMidi.Chunks[0].Where(e => e is { Status: 0xFF, D1: 0x06 }).Select(e => System.Text.Encoding.UTF8.GetString(e.Meta!)).ToList();
        Assert.Equal(new[] { "Intro", "Chorus" }, sectionNames);
    }

    [Fact]
    public void AudioTracksAreNeverExported()
    {
        var song = BuildSong();
        song.Tracks.Add(new TrackModel { Name = "Vox", Kind = TrackKind.Audio, Measures = TemplateFactory.Measures(8) });
        Assert.DoesNotContain(3, MidiExportRange.TrackIndexes(song, null));
    }

    [Fact]
    public void BarRangeMapsSourceBarsToPerformedPassesLikeRender()
    {
        var song = BuildSong(4);
        song.Tracks[0].Measures[0].RepeatStart = true;
        song.Tracks[0].Measures[1].RepeatEnd = true;
        song.Tracks[1].Measures[0].RepeatStart = true;
        song.Tracks[1].Measures[1].RepeatEnd = true;
        song.Tracks[2].Measures[0].RepeatStart = true;
        song.Tracks[2].Measures[1].RepeatEnd = true;
        PutNote(song.Tracks[0], 0, 0);
        var settings = new MidiExportSettings { Bounds = 2, CustomFromBar = 1, CustomToBar = 2 };
        var (timeline, slice) = MidiExportRange.Resolve(song, settings, null, (0, 0, 1, -1));
        var expected = Rendering.RenderSpecBuilder.Bounds(timeline, Rendering.RenderBounds.CustomBars, 0, 0, 1, -1, 0, 0);
        Assert.Equal(expected.StartMs, slice.StartMs, 3);
        Assert.Equal(expected.EndMs, slice.EndMs, 3);
        var midi = ParseMidi(File.ReadAllBytes(Export(song, slice, "repeat.mid")));
        Assert.Equal(2 * 2 * 4 * 480, MaxTick(midi));   // both passes of the two bars, offset removed
    }
}
