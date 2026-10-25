using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: volume and pan changes in a clean .gp (GpifAutomationWriter / GpifAutomationReader) and the zip staying valid after the patch.
// Does not own: the exporter's other content.
// Env: TABFORGE_SAMPLES_DIR (opt-in sample round trip).
public sealed class GpifAutomationTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-gpifauto-{Guid.NewGuid():N}");

    public GpifAutomationTests(ITestOutputHelper output) { _out = output; Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private static SongProject Song(int bars)
    {
        var song = new SongProject { Title = "T", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        song.Tracks.Add(new TrackModel
        {
            Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars),
        });
        foreach (var m in song.Tracks[0].Measures)   // four quarter notes a bar
            m.Cells = Enumerable.Range(0, 4).Select(_ => new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 } } }).ToList();
        return song;
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>Every entry opens in a stock zip reader and its bytes match the CRC-32 the archive stores.</summary>
    private static void AssertValidZip(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        var stored = GuitarProExporter.ReadZip(file).ToDictionary(p => p.Name, p => p.Data);
        Assert.Contains("Content/score.gpif", stored.Keys);
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            using var s = entry.Open(); using var ms = new MemoryStream(); s.CopyTo(ms);
            Assert.Equal(entry.Crc32, Crc(ms.ToArray()));
            Assert.Equal(stored[entry.FullName].Length, ms.Length);
        }
    }

    [Fact]
    public void CleanExport_KeepsVolumeAndPanChanges_AtBarAndMidBar()
    {
        var song = Song(6);
        var t = song.Tracks[0];
        t.Measures[2].Cells[0].Mix = new MixChange { Volume = 5, Pan = -4 };
        t.Measures[4].Cells[2].Mix = new MixChange { Volume = 12 };   // half way into the bar
        var bytes = GuitarProExporter.ToBytes(song, embedProject: false);
        AssertValidZip(bytes);
        var path = Path.Combine(_folder, "r.gp");
        File.WriteAllBytes(path, bytes);
        var back = GuitarProImporter.Import(path).Tracks[0];
        Assert.Equal(5, back.Measures[2].Cells[0].Mix?.Volume);
        Assert.Equal(-4, back.Measures[2].Cells[0].Mix?.Pan);
        Assert.Equal(12, back.Measures[4].Cells[8].Mix?.Volume);   // a reopened bar is a 16th-note grid: the third quarter note is slot 8
        Assert.Null(back.Measures[4].Cells[8].Mix?.Pan);
        Assert.Null(back.Measures[1].Cells[0].Mix?.Volume);
        Assert.Null(back.Measures[3].Cells[0].Mix?.Volume);
    }

    [Fact]
    public void CleanExport_AllTracksChangeReachesEveryTrack()
    {
        var song = Song(3);
        song.Tracks.Add(new TrackModel { Name = "Bass", Kind = TrackKind.Bass, MidiChannel = 1, StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(3) });
        song.Tracks[0].Measures[1].Cells[0].Mix = new MixChange { Volume = 3, AllTracks = true };
        var path = Path.Combine(_folder, "r.gp");
        File.WriteAllBytes(path, GuitarProExporter.ToBytes(song, embedProject: false));
        var back = GuitarProImporter.Import(path);
        var first = back.Tracks[0].Measures[1].Cells[0].Mix;
        Assert.Equal(3, first?.Volume);
        Assert.True(first?.AllTracks);   // the same change on every track is one all-tracks change again
        Assert.Null(first?.Pan);
        Assert.DoesNotContain(back.Tracks[1].Measures.SelectMany(m => m.Cells), c => c.Mix is not null);
        Assert.DoesNotContain(back.Tracks[0].Measures[0].Cells, c => c.Mix is not null);
    }

    [Fact]
    public void CleanExport_DifferentChangesOnTwoTracks_StayPerTrack()
    {
        var song = Song(3);
        song.Tracks.Add(new TrackModel { Name = "Bass", Kind = TrackKind.Bass, MidiChannel = 1, StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(3) });
        foreach (var m in song.Tracks[1].Measures) m.Cells = new List<TabCell> { new() { IsRest = true, DurationDenominator = 1 } };
        song.Tracks[0].Measures[1].Cells[0].Mix = new MixChange { Volume = 3 };
        song.Tracks[1].Measures[1].Cells[0].Mix = new MixChange { Volume = 9 };
        var path = Path.Combine(_folder, "r.gp");
        File.WriteAllBytes(path, GuitarProExporter.ToBytes(song, embedProject: false));
        var back = GuitarProImporter.Import(path);
        Assert.Equal(3, back.Tracks[0].Measures[1].Cells[0].Mix?.Volume);
        Assert.False(back.Tracks[0].Measures[1].Cells[0].Mix?.AllTracks);
        Assert.Equal(9, back.Tracks[1].Measures[1].Cells[0].Mix?.Volume);
    }

    [Fact]
    public void CleanExport_ChangeEqualToTheStartingValueAtBarZero_IsNotAMixChange()
    {
        var song = Song(2);
        song.Tracks[0].Measures[0].Cells[0].Mix = new MixChange { Volume = GpMixerScale.MixerToStep(song.Tracks[0].Volume) };   // the track's own starting step
        song.Tracks[0].Measures[1].Cells[0].Mix = new MixChange { Volume = 7 };
        var path = Path.Combine(_folder, "r.gp");
        File.WriteAllBytes(path, GuitarProExporter.ToBytes(song, embedProject: false));
        var back = GuitarProImporter.Import(path).Tracks[0];
        Assert.DoesNotContain(back.Measures[0].Cells, c => c.Mix is not null);   // bar 0 at the starting value is the track's setting, not a change
        Assert.Equal(7, back.Measures[1].Cells[0].Mix?.Volume);   // exact value
        Assert.Null(back.Measures[1].Cells[0].Mix?.Pan);
    }

    [Theory]
    [InlineData(4, 1)]    // 0.5 of a step rounds away from zero, the same in the exporter, writer and reader
    [InlineData(12, 2)]
    [InlineData(127, 16)]
    [InlineData(0, 0)]
    public void MixerScale_RoundsTheSameEverywhere(int mixer, int step)
    {
        Assert.Equal(step, GpMixerScale.MixerToStep(mixer));
        Assert.Equal(step, GpMixerScale.FractionToStep(GpMixerScale.ToFraction(mixer)));
    }

    [Fact]
    public void MixerScale_ClampsBeforeTheCast()
    {
        Assert.Equal(127, GpMixerScale.FromFraction(double.PositiveInfinity));
        Assert.Equal(0, GpMixerScale.FromFraction(double.NegativeInfinity));
        Assert.Equal(0, GpMixerScale.FromFraction(double.NaN));
        Assert.Equal(16, GpMixerScale.FractionToStep(1e300));
    }

    [Fact]
    public void CleanExport_WithoutMixChanges_WritesNoAutomations()
    {
        var bytes = GuitarProExporter.ToBytes(Song(2), embedProject: false);
        var gpif = System.Text.Encoding.UTF8.GetString(GuitarProExporter.ReadZip(bytes).First(p => p.Name == "Content/score.gpif").Data);
        Assert.DoesNotContain("DSPParam", gpif);
        AssertValidZip(bytes);
    }

    // An all-tracks change is one event (track -1): the GPIF writer copies it to every track and the reader keeps it on the first.
    private static List<(int Track, int Bar, string Kind, int Value)> Events(SongProject song) =>
        song.Tracks.SelectMany((t, ti) => t.Measures.SelectMany((m, bi) => m.Cells.Where(c => c.Mix is not null).SelectMany(c => new[]
            { ("vol", c.Mix!.Volume), ("pan", c.Mix.Pan) }.Where(x => x.Item2 is not null).Select(x => (c.Mix.AllTracks ? -1 : ti, bi, x.Item1, x.Item2!.Value))))).ToList();

    [Fact]
    public void CleanExport_OfSamplesWithMixChanges_KeepsThem()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        var tested = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.gp*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (tested >= 5) break;
            SongProject a;
            try { a = GuitarProImporter.Import(file); } catch (InvalidDataException) { continue; }
            var wanted = Events(a).Where(e => e.Bar > 0).Distinct().ToList();
            if (wanted.Count == 0) continue;
            var bytes = GuitarProExporter.ToBytes(a, embedProject: false);
            AssertValidZip(bytes);
            var path = Path.Combine(_folder, "s.gp");
            File.WriteAllBytes(path, bytes);
            var kept = Events(GuitarProImporter.Import(path)).ToHashSet();
            var lost = wanted.Where(e => !kept.Contains(e)).ToList();
            _out.WriteLine($"{Path.GetFileName(file)}: {wanted.Count} mix events, {lost.Count} lost {string.Join(" ", lost.Take(4))}");
            Assert.True(lost.Count <= wanted.Count / 10, $"{file}: {lost.Count} of {wanted.Count} mix events lost");
            tested++;
        }
        _out.WriteLine($"{tested} sample files with mix events");
    }
}
