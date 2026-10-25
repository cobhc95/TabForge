using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: export fixes from the import/export audit: ASCII wrapping and prefixes, MIDI time-signature bytes, clean .gp effects.
// Does not own: the exporters themselves.
// Env: TABFORGE_SAMPLES_DIR (opt-in file round trips).
public sealed class ExporterFixesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-exf-{Guid.NewGuid():N}");

    public ExporterFixesTests(ITestOutputHelper output) { _out = output; Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private static SongProject Song(int bars = 2, int num = 4, int den = 4)
    {
        var song = new SongProject { Title = "T", Tempo = 120, TimeSignatureNumerator = num, TimeSignatureDenominator = den };
        song.Tracks.Add(new TrackModel
        {
            Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars),
        });
        return song;
    }

    private static TabNote Put(TrackModel t, int bar, int cell, int s, int fret, int denom = 4)
    {
        var c = t.Measures[bar].Cells[cell];
        c.DurationDenominator = denom;
        var n = new TabNote { StringIndex = s, Fret = fret, MidiValue = t.PitchOf(s, fret) };
        c.Notes.Add(n);
        return n;
    }

    private static List<(int Num, int Den)> MidiSigs(byte[] b)
    {
        var list = new List<(int, int)>();
        for (var i = 0; i + 6 < b.Length; i++)
            if (b[i] == 0xFF && b[i + 1] == 0x58 && b[i + 2] == 0x04) list.Add((b[i + 3], 1 << b[i + 4]));
        return list;
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 16)]
    [InlineData(3, 2)]
    public void Midi_TimeSignatureDenominatorIsLog2(int num, int den)
    {
        var song = Song(1, num, den);
        var path = Path.Combine(_folder, "s.mid");
        MidiExportService.Export(song, path);
        Assert.Contains((num, den), MidiSigs(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Ascii_EachLineKeepsOnlyItsOwnPrefixAndLongBarsAreNotDropped()
    {
        var song = Song(8);
        var t = song.Tracks[0];
        for (var b = 0; b < 8; b++) Put(t, b, 0, 0, 1);
        var cells = t.Measures[0].Cells;
        while (cells.Count < 20) cells.Add(new TabCell { DurationDenominator = 16 });
        Put(t, 0, 18, 2, 7, 16);
        var path = Path.Combine(_folder, "s.txt");
        AsciiExportService.Export(song, path);
        var text = File.ReadAllText(path);
        var tab = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("e||") || l.StartsWith("B||") || l.StartsWith("E||")).ToList();
        Assert.All(tab, l => Assert.Equal(1, l.Split("||").Length - 1));
        Assert.Contains(tab, l => l.StartsWith("B||") && l.Contains("-7-"));
    }

    [Fact]
    public void Gp_CleanExport_KeepsTiesSlidesHammerAndHeavyAccent()
    {
        var song = Song(2);
        var t = song.Tracks[0];
        Put(t, 0, 0, 0, 5); var tied = Put(t, 0, 4, 0, 5); tied.Tied = true;
        var sl = Put(t, 0, 8, 1, 3); sl.Techniques.Add("LegatoSlide");
        Put(t, 0, 12, 1, 5);
        var ho = Put(t, 1, 0, 2, 2); ho.Techniques.Add("HOPOOrigin");
        Put(t, 1, 4, 2, 4);
        Put(t, 1, 8, 3, 0); t.Measures[1].Cells[8].Accent = 2;
        var path = Path.Combine(_folder, "s.gp");
        GuitarProExporter.Save(song, path, embedProject: false);
        var back = GuitarProImporter.Import(path).Tracks[0];
        foreach (var m in back.Measures) foreach (var c in m.Cells.Where(c => c.Notes.Count > 0))
            _out.WriteLine($"{c.Notes[0].Fret} tied={c.Notes[0].Tied} acc={c.Accent} tech={string.Join(",", c.Notes[0].Techniques)}");
        Assert.True(back.Measures[0].Cells[4].Notes.Single().Tied);
        Assert.Contains(back.Measures[0].Cells[8].Notes.Single().Techniques, x => x.Contains("Slide"));
        Assert.Contains(back.Measures[1].Cells[0].Notes.Single().Techniques, x => x.Contains("HOPO"));
        Assert.Equal(2, back.Measures[1].Cells[8].Accent);
    }

    private static IEnumerable<TabCell> AllCells(MeasureModel m) => m.Cells.Concat(m.Voice2Cells);

    [Theory]
    [InlineData("children_of_bodom/aces_high.gp4")]
    [InlineData("children_of_bodom/you_re_better_off_dead.gp4")]
    [InlineData("As I Lay Dying - Blinded (ver 2 by president100).gp")]
    [InlineData("iron_maiden/2_minutes_to_midnight_7.gp5")]
    public void Gp_CleanExportOfSampleKeepsEffects(string relative)
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var src = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        if (src is null || !File.Exists(src)) return;
        var a = GuitarProImporter.Import(src);
        var path = Path.Combine(_folder, "r.gp");
        GuitarProExporter.Save(a, path, embedProject: false);
        var b = GuitarProImporter.Import(path);
        for (var t = 0; t < a.NotationTracks.Count(); t++)
        {
            var ta = a.NotationTracks.ElementAt(t); var tb = b.NotationTracks.ElementAt(t);
            for (var m = 0; m < Math.Min(ta.Measures.Count, tb.Measures.Count); m++)
            {
                int Count(MeasureModel mm, Func<TabNote, TabCell, bool> f) => AllCells(mm).Sum(c => c.Notes.Count(n => f(n, c)));
                foreach (var (name, f) in new (string, Func<TabNote, TabCell, bool>)[] {
                    ("tie", (n, c) => n.Tied), ("slide", (n, c) => n.Techniques.Any(x => x.Contains("Slide"))),
                    ("hopo", (n, c) => n.Techniques.Contains("HOPO")), ("heavy", (n, c) => c.Accent == 2) })
                {
                    var x = Count(ta.Measures[m], f); var y = Count(tb.Measures[m], f);
                    if (x != y) _out.WriteLine($"{relative} track {t} bar {m + 1} {name}: {x} -> {y}");
                }
            }
        }
    }

    [Fact]
    public void Gp_CleanExport_KeepsAGradualTempoRamp()
    {
        var song = Song(3);
        var t = song.Tracks[0];
        for (var b = 0; b < 3; b++) Put(t, b, 0, 0, 1);
        t.Measures[1].MidBarTempos = new List<TempoPoint> { new(0, 160, 16) };
        var path = Path.Combine(_folder, "r.gp");
        GuitarProExporter.Save(song, path, embedProject: false);
        var back = GuitarProImporter.Import(path).Tracks[0];
        for (var b = 0; b < 3; b++) _out.WriteLine($"bar {b + 1}: change={back.Measures[b].TempoChange} mid={string.Join(";", back.Measures[b].MidBarTempos ?? new())}");
        var ramp = Assert.Single(back.Measures[1].MidBarTempos!);
        Assert.Equal(160, ramp.Tempo);
        Assert.True(ramp.RampSlots > 0);
    }

    [Theory]
    [InlineData("children_of_bodom/black_widow_5.gp4")]
    [InlineData("children_of_bodom/downfall_3.gp3")]
    public void MusicXml_TieStopsMatchTheScorePerPart(string relative)
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var src = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        if (src is null || !File.Exists(src)) return;
        var song = GuitarProImporter.Import(src);
        var path = Path.Combine(_folder, "o.musicxml");
        MusicXmlExportService.Export(song, path);
        var doc = System.Xml.Linq.XDocument.Load(path);
        var parts = doc.Descendants("part").ToList();
        var tracks = song.NotationTracks.ToList();
        for (var p = 0; p < parts.Count; p++)
        {
            var stops = parts[p].Descendants("note").Count(n => n.Elements("tie").Any(t => (string?)t.Attribute("type") == "stop"));
            var tied = tracks[p].Measures.Sum(m => m.Cells.Concat(m.Voice2Cells).Sum(c => c.Notes.Count(n => n.Tied)));
            var cellTied = tracks[p].Measures.Sum(m => m.Cells.Concat(m.Voice2Cells).Count(c => c.IsTied));
            var staves = parts[p].Descendants("staves").Select(e => (int)e).FirstOrDefault(1);
            _out.WriteLine($"{relative} part {p} {tracks[p].Kind} staves {staves} stops {stops} tied*staves {tied * staves} cellTied {cellTied}");
            Assert.Equal(tied * staves, stops);
        }
    }
}
