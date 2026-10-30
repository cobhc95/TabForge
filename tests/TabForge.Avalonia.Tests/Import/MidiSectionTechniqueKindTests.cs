using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: MIDI sections (export markers, import, round trip), the TF1 sequencer-specific technique form
//   (new 0x7F plus legacy marker reads, tick off writes none) and the instrument-kind rules.
// Does not own: the exporter, the reader or the importer internals it exercises.
public sealed class MidiSectionTechniqueKindTests : IDisposable
{
    private const int Div = 480;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-midi-5a-{Guid.NewGuid():N}");

    public MidiSectionTechniqueKindTests() { Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }
    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    private static void Vlq(List<byte> o, int v)
    {
        var parts = new Stack<byte>(); parts.Push((byte)(v & 0x7F));
        for (v >>= 7; v > 0; v >>= 7) parts.Push((byte)(v & 0x7F | 0x80));
        o.AddRange(parts);
    }
    private static byte[] Chunk(IEnumerable<(int Tick, byte[] Bytes)> events)
    {
        var body = new List<byte>(); var last = 0;
        foreach (var (tick, bytes) in events.OrderBy(e => e.Tick)) { Vlq(body, tick - last); last = tick; body.AddRange(bytes); }
        body.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
        return Ascii("MTrk").Concat(new byte[] { 0, 0, (byte)(body.Count >> 8), (byte)body.Count }).Concat(body).ToArray();
    }
    private static byte[] File1(params byte[][] chunks) =>
        Ascii("MThd").Concat(new byte[] { 0, 0, 0, 6, 0, 1, 0, (byte)chunks.Length, Div >> 8, Div & 0xFF }).Concat(chunks.SelectMany(c => c)).ToArray();
    private static (int, byte[]) Tempo(int tick, int bpm) { var mpq = 60_000_000 / bpm; return (tick, new byte[] { 0xFF, 0x51, 3, (byte)(mpq >> 16), (byte)(mpq >> 8), (byte)mpq }); }
    private static (int, byte[]) Marker(int tick, string text) { var b = Encoding.UTF8.GetBytes(text); return (tick, new byte[] { 0xFF, 0x06, (byte)b.Length }.Concat(b).ToArray()); }
    private static (int, byte[]) Cue(int tick, string text) { var b = Encoding.UTF8.GetBytes(text); return (tick, new byte[] { 0xFF, 0x07, (byte)b.Length }.Concat(b).ToArray()); }
    private static (int, byte[]) TrackName(int tick, string text) { var b = Ascii(text); return (tick, new byte[] { 0xFF, 0x03, (byte)b.Length }.Concat(b).ToArray()); }
    private static (int, byte[]) Prog(int tick, int program, int ch = 0) => (tick, new byte[] { (byte)(0xC0 | ch), (byte)program });
    private static (int, byte[]) On(int tick, int pitch, int ch = 0, int vel = 90) => (tick, new byte[] { (byte)(0x90 | ch), (byte)pitch, (byte)vel });
    private static (int, byte[]) Off(int tick, int pitch, int ch = 0) => (tick, new byte[] { (byte)(0x80 | ch), (byte)pitch, 0 });
    private static IEnumerable<(int, byte[])> Note(int tick, int len, int pitch, int ch = 0) { yield return On(tick, pitch, ch); yield return Off(tick + len, pitch, ch); }

    private sealed record Meta(int Tick, int Type, byte[] Data);
    private static List<Meta> ConductorMetas(byte[] file)
    {
        var len = (file[18] << 24) | (file[19] << 16) | (file[20] << 8) | file[21];
        var q = 22; var end = 22 + len; var out_ = new List<Meta>();
        int ReadVl(ref int p) { var v = 0; for (var i = 0; i < 4; i++) { var x = file[p++]; v = (v << 7) | (x & 0x7F); if ((x & 0x80) == 0) return v; } return v; }
        var tick = 0;
        while (true)
        {
            tick += ReadVl(ref q);
            if (file[q] == 0xFF && file[q + 1] == 0x2F) break;
            if (file[q] == 0xFF) { var t = file[q + 1]; q += 2; var l = ReadVl(ref q); out_.Add(new Meta(tick, t, file.Skip(q).Take(l).ToArray())); q += l; continue; }
            var s = file[q++]; var k = s & 0xF0;
            if (k is 0xC0 or 0xD0) q++; else if (k >= 0x80) q += 2; else break;
            if (q >= end) break;
        }
        return out_;
    }
    private static List<byte[]> SeqTechniques(byte[] file)
    {
        var list = new List<byte[]>(); var p = 14;
        var count = (file[10] << 8) | file[11];
        for (var c = 0; c < count; c++)
        {
            var len = (file[p + 4] << 24) | (file[p + 5] << 16) | (file[p + 6] << 8) | file[p + 7];
            var end = p + 8 + len; var q = p + 8; var tick = 0;
            int ReadVl(ref int x) { var v = 0; for (var i = 0; i < 4; i++) { var b = file[x++]; v = (v << 7) | (b & 0x7F); if ((b & 0x80) == 0) return v; } return v; }
            while (q < end)
            {
                tick += ReadVl(ref q);
                if (file[q] == 0xFF && file[q + 1] == 0x2F) break;
                if (file[q] == 0xFF) { var t = file[q + 1]; q += 2; var l = ReadVl(ref q); if (t == 0x7F) list.Add(file.Skip(q).Take(l).ToArray()); q += l; continue; }
                var s = file[q++]; var k = s & 0xF0;
                if (k is 0xC0 or 0xD0) q++; else if (k >= 0x80) q += 2; else break;
            }
            p = end;
        }
        return list;
    }

    private static SongProject Song(int bars = 8)
    {
        var song = new SongProject { Title = "S", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        song.Tracks.Add(new TrackModel { Name = "Gtr", Kind = TrackKind.Guitar, MidiChannel = 0, MidiProgram = 29,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars) });
        return song;
    }
    private static void Put(TrackModel t, int bar, int cell, int fret = 0, int denom = 4)
    {
        var beat = t.Measures[bar].Cells[cell]; beat.DurationDenominator = denom;
        beat.Notes.Add(new TabNote { StringIndex = 0, Fret = fret, MidiValue = t.PitchOf(0, fret), Velocity = 90 });
    }
    private SongProject Load(byte[] file, string name = "t.mid")
    {
        var path = Path.Combine(_folder, name); File.WriteAllBytes(path, file);
        return GuitarProImporter.Import(path);
    }
    private string Export(SongProject song, ArticulationExportOptions? options, string name, bool sections = true)
    {
        var path = Path.Combine(_folder, name);
        var (_, slice) = MidiExportRange.Resolve(song, new Rendering.MidiExportSettings(), null, null);
        MidiExportService.Export(song, path, slice, options, writeSections: sections);
        return path;
    }

    [Fact]
    public void SectionExportWritesMarkersWithTicksAndNames()
    {
        var song = Song(); Put(song.Tracks[0], 0, 0); Put(song.Tracks[0], 5, 0);
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "  Verse  " });
        song.Markers.Add(new MarkerModel { MeasureIndex = 5, Title = "Chorus" });
        var metas = ConductorMetas(File.ReadAllBytes(Export(song, null, "sec.mid")));
        var markers = metas.Where(m => m.Type == 0x06).ToList();
        Assert.Equal(3, markers.Count);
        Assert.Equal(new[] { "Intro", "Verse", "Chorus" }, markers.Select(m => Encoding.UTF8.GetString(m.Data)).ToArray());
        Assert.Equal(new[] { 0, 2 * 4 * Div, 5 * 4 * Div }, markers.Select(m => m.Tick).ToArray());
    }

    [Fact]
    public void SectionImportFromConductorOnly()
    {
        var song = Load(File1(Chunk(new[] { Tempo(0, 120) }), Chunk(new[] { TrackName(0, "Gtr") }.Concat(Note(0, Div, 60)))));
        Assert.Empty(song.Markers);
        var withSec = Load(File1(Chunk(new[] { Tempo(0, 120), Marker(0, "Intro"), Marker(2 * 4 * Div, "Verse") }),
            Chunk(new[] { TrackName(0, "Gtr") }.Concat(Note(0, Div, 60)).Concat(Note(2 * 4 * Div, Div, 62)))), "sec.mid");
        Assert.Equal(new[] { "Intro", "Verse" }, withSec.Markers.Select(m => m.Title).ToArray());
        Assert.Equal(new[] { 0, 2 }, withSec.Markers.Select(m => m.MeasureIndex).ToArray());
        var other = Load(File1(Chunk(new[] { Tempo(0, 120) }),
            Chunk(new[] { TrackName(0, "Gtr"), Marker(0, "Intro") }.Concat(Note(0, Div, 60)))), "other.mid");
        Assert.Empty(other.Markers);
    }

    [Fact]
    public void SectionImportEdgeCases()
    {
        var mid = Load(File1(Chunk(new[] { Tempo(0, 120), Marker(100, "Mid"), Marker(2 * 4 * Div, "Verse"), Marker(4 * 4 * Div, "Verse"), Cue(6 * 4 * Div, "  "), Marker(6 * 4 * Div, "Outro") }),
            Chunk(new[] { TrackName(0, "Gtr") }.Concat(Note(0, Div, 60)).Concat(Note(6 * 4 * Div, Div, 62)))), "edge.mid");
        Assert.Equal(new[] { "Mid", "Verse", "Verse (2)", "Outro" }, mid.Markers.Select(m => m.Title).ToArray());
        Assert.Equal(0, mid.Markers[0].MeasureIndex);
        var legacy = Load(File1(Chunk(new[] { Tempo(0, 120), Marker(0, "TF1:0:60:Bend|b=0:0") }),
            Chunk(new[] { TrackName(0, "Gtr") }.Concat(Note(0, Div, 60)))), "leg.mid");
        Assert.Empty(legacy.Markers);
    }

    [Fact]
    public void SectionRoundTrip()
    {
        var song = Song(4); Put(song.Tracks[0], 0, 0); Put(song.Tracks[0], 2, 0);
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "Solo" });
        var back = GuitarProImporter.Import(Export(song, null, "rt.mid"));
        Assert.Equal(new[] { "Intro", "Solo" }, back.Markers.Select(m => m.Title).ToArray());
        Assert.Equal(new[] { 0, 2 }, back.Markers.Select(m => m.MeasureIndex).ToArray());
    }

    [Fact]
    public void TechniqueRoundTripNewFormAndLegacy()
    {
        var song = Song(2); Put(song.Tracks[0], 0, 0);
        song.Tracks[0].Measures[0].Cells[0].Notes[0].Techniques.Add("PalmMute");
        var onPath = Export(song, new ArticulationExportOptions { MapsEnabled = false, KeepTechniques = true }, "tech.mid");
        var bytes = File.ReadAllBytes(onPath);
        Assert.DoesNotContain(ConductorMetas(bytes).Where(m => m.Type == 0x06), m => Encoding.Latin1.GetString(m.Data).StartsWith("TF1:"));
        var seq = SeqTechniques(bytes);
        Assert.NotEmpty(seq);
        Assert.All(seq, d => Assert.Equal(0x7D, d[0]));
        Assert.StartsWith("TF1:", Encoding.Latin1.GetString(seq[0], 1, seq[0].Length - 1));
        var back = GuitarProImporter.Import(onPath);
        var notes = back.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes).ToList();
        Assert.Contains(notes, n => n.Techniques.Contains("PalmMute"));
        var legacyBytes = File1(Chunk(new[] { Tempo(0, 120) }),
            Chunk(new[] { TrackName(0, "Gtr"), Marker(0, "TF1:0:60:PalmMute|"), }.Concat(Note(0, Div, 60))));
        var legacy = Load(legacyBytes, "legacytech.mid");
        Assert.Contains(legacy.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes),
            n => n.Techniques.Contains("PalmMute"));
        Assert.Empty(legacy.Markers);
        var offBytes = File.ReadAllBytes(Export(song, null, "off.mid", sections: true));
        Assert.Empty(SeqTechniques(offBytes));
    }

    [Fact]
    public void InstrumentKindCases()
    {
        byte[][] Tracks(params (string Name, int? Program, int Channel, int[] Pitches)[] specs)
        {
            var chunks = new List<byte[]> { Chunk(new[] { Tempo(0, 120) }) };
            foreach (var (name, prog, ch, pitches) in specs)
            {
                var ev = new List<(int, byte[])> { TrackName(0, name) };
                if (prog is int p) ev.Add(Prog(0, p, ch));
                var t = 0;
                foreach (var pitch in pitches) { ev.AddRange(Note(t, Div, pitch, ch)); t += Div; }
                chunks.Add(Chunk(ev));
            }
            return chunks.ToArray();
        }
        var guitarProgs = new[] { 24, 25, 27, 29, 30, 31 };
        var song = Load(File1(Tracks(guitarProgs.Select(p => ($"G{p}", (int?)p, 0, new[] { 60, 64 })).ToArray())), "g.mid");
        Assert.Equal(6, song.Tracks.Count);
        Assert.All(song.Tracks, t => Assert.Equal(TrackKind.Guitar, t.Kind));
        var bass = Load(File1(Tracks(new[] { 32, 35, 39 }.Select(p => ($"B{p}", (int?)p, 0, new[] { 40, 45 })).ToArray())), "b.mid");
        Assert.All(bass.Tracks, t => Assert.Equal(TrackKind.Bass, t.Kind));
        var drums = Load(File1(Chunk(new[] { Tempo(0, 120) }), Chunk(new[] { TrackName(0, "Kit") }.Concat(Note(0, Div, 60, 9)).Concat(Note(Div, Div, 62, 9)))), "d.mid");
        Assert.Equal(TrackKind.Drums, drums.Tracks[0].Kind);
        var late = Load(File1(Chunk(new[] { Tempo(0, 120) }),
            Chunk(new[] { TrackName(0, "Late") }.Concat(Note(0, Div, 60)).Concat(new[] { Prog(Div / 2, 29) }).Concat(Note(Div, Div, 62)))), "late.mid");
        Assert.Equal(TrackKind.Guitar, late.Tracks[0].Kind);
        var wide = Load(File1(Chunk(new[] { Tempo(0, 120) }),
            Chunk(new[] { TrackName(0, "Wide"), Prog(0, 29) }.Concat(Note(0, 4 * Div, 20)).Concat(Note(4 * Div, Div, 64)))), "wide.mid");
        Assert.Equal(TrackKind.Guitar, wide.Tracks[0].Kind);
        var named = Load(File1(Tracks(new (string, int?, int, int[])[] { ("My Guitar", null, 0, new[] { 60 }), ("My Bass", null, 0, new[] { 40 }), ("Keys Part", null, 0, new[] { 60 }) })), "n.mid");
        Assert.Equal(TrackKind.Guitar, named.Tracks[0].Kind);
        Assert.Equal(TrackKind.Bass, named.Tracks[1].Kind);
        Assert.Equal(TrackKind.Keys, named.Tracks[2].Kind);
    }
}
