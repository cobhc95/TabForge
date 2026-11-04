using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the partial-capo model rules and the .tforge round trip: the effective capo per string, the note shift,
// the mask refit after a string-count change, and the optional save field that keeps old files byte-identical.
// Does not own: the window, the fretboard or the import.
public sealed class PartialCapoTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-partial-capo-" + Guid.NewGuid().ToString("N"));

    public PartialCapoTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static TrackModel Guitar()
    {
        var track = new TrackModel { Name = "Capo guitar", StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        for (var s = 0; s < track.StringTunings.Count; s++)
            track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = s, Fret = 0, MidiValue = track.PitchOf(s, 0) });
        return track;
    }

    private static SongProject Song(TrackModel track)
    {
        var project = new SongProject { Title = "Partial capos" };
        project.Tracks.Add(track);
        return project;
    }

    [Fact]
    public void EffectiveCapoIsTheHigherOfFullAndPartialPerString()
    {
        var partials = new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } };
        // Full 2 + partial 5 on strings 1-3 (index 0-2): 5 wins there, 2 elsewhere.
        Assert.Equal(new[] { 5, 5, 5, 2, 2, 2 }, Enumerable.Range(0, 6).Select(s => PartialCapoRules.EffectiveCapo(2, partials, s)));
        // Partial only.
        Assert.Equal(new[] { 5, 5, 5, 0, 0, 0 }, Enumerable.Range(0, 6).Select(s => PartialCapoRules.EffectiveCapo(0, partials, s)));
        // Partial below the full capo is ignored.
        var below = new List<PartialCapo> { new() { Fret = 2, StringMask = 0b000111 } };
        Assert.All(Enumerable.Range(0, 6), s => Assert.Equal(4, PartialCapoRules.EffectiveCapo(4, below, s)));
        // Off and empty masks add nothing.
        Assert.All(Enumerable.Range(0, 6), s => Assert.Equal(3, PartialCapoRules.EffectiveCapo(3, new List<PartialCapo> { new() { Fret = 0, StringMask = 0b111111 }, new() { Fret = 7, StringMask = 0 } }, s)));
    }

    [Fact]
    public void StringCountChangeDropsBitsForRemovedStrings()
    {
        Assert.Equal(0b000011, PartialCapoRules.Refit(0b110011, 3));
        Assert.Equal(0, PartialCapoRules.Refit(0b111000, 3));
        Assert.Equal(0b111111, PartialCapoRules.Refit(0b111111, 6));
        // The model never keeps a partial whose remaining mask is empty.
        Assert.Null(PartialCapoRules.Normalize(new List<PartialCapo> { new() { Fret = 5, StringMask = 0b110000 } }, 3));
    }

    [Fact]
    public void SetPartialCaposMovesOnlyTheStringsItCovers()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000101 } });

        var notes = track.Measures[0].Cells[0].Notes;
        Assert.Equal(new[] { 5, 0, 5, 0, 0, 0 }, notes.Select(n => n.MidiValue - track.StringTunings[n.StringIndex]));
        Assert.Equal(5, track.EffectiveCapo(0));
        Assert.Equal(0, track.EffectiveCapo(1));
        Assert.Equal(69, notes[0].MidiValue);   // 64 + 5
        Assert.Equal(59, notes[1].MidiValue);
    }

    [Fact]
    public void ChangingTheFullCapoLeavesStringsBehindAHigherPartialAlone()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } });
        TrackSetup.SetCapo(track, 4);

        var notes = track.Measures[0].Cells[0].Notes;
        Assert.Equal(new[] { 5, 5, 5, 4, 4, 4 }, notes.Select(n => n.MidiValue - track.StringTunings[n.StringIndex]));
        Assert.Equal(4, track.Capo);
    }

    [Fact]
    public void ApplyTuningDropsTheFlagsOfRemovedStrings()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b100101 } });
        TrackSetup.ApplyTuning(track, new List<int> { 64, 59, 55 }, keepFrets: true);

        Assert.Equal(0b000101, Assert.Single(track.PartialCapos!).StringMask);
        Assert.Equal(5, track.EffectiveCapo(0));
        Assert.Equal(0, track.EffectiveCapo(5));
    }

    [Fact]
    public void ProjectWithoutPartialsOmitsTheFieldAndSavesIdenticallyAgain()
    {
        var project = Song(Guitar());
        var path = Path.Combine(_folder, "plain.tforge");
        ProjectService.Save(path, project);
        var first = File.ReadAllBytes(path);

        var json = DiskJson(project);
        Assert.DoesNotContain("PartialCapos", json);

        var reopened = ProjectService.Load(path);
        Assert.Null(reopened.Tracks[0].PartialCapos);

        var secondPath = Path.Combine(_folder, "plain2.tforge");
        ProjectService.Save(secondPath, reopened);
        Assert.Equal(first, File.ReadAllBytes(secondPath));
    }

    [Fact]
    public void ProjectRoundTripsPartialCapos()
    {
        var track = Guitar();
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 }, new() { Fret = 2, StringMask = 0b110000 } });
        var path = Path.Combine(_folder, "partials.tforge");
        ProjectService.Save(path, Song(track));

        var loaded = ProjectService.Load(path).Tracks[0];
        Assert.Equal(2, loaded.PartialCapos!.Count);
        Assert.Equal(5, loaded.PartialCapos[0].Fret);
        Assert.Equal(0b000111, loaded.PartialCapos[0].StringMask);
        Assert.Equal(2, loaded.PartialCapos[1].Fret);
        Assert.Equal(5, loaded.EffectiveCapo(0));
        Assert.Equal(2, loaded.EffectiveCapo(5));
    }

    [Fact]
    public void AFileWhoseMaskOutlivesItsStringsLoadsRefit()
    {
        var track = Guitar();
        track.PartialCapos = new List<PartialCapo> { new() { Fret = 5, StringMask = 0b110011 } };
        track.StringTunings = new List<int> { 64, 59, 55 };
        for (var i = track.Measures[0].Cells[0].Notes.Count - 1; i >= 3; i--) track.Measures[0].Cells[0].Notes.RemoveAt(i);

        var path = Path.Combine(_folder, "refit.tforge");
        ProjectService.Save(path, Song(track));

        var loaded = ProjectService.Load(path).Tracks[0];
        Assert.Equal(0b000011, Assert.Single(loaded.PartialCapos!).StringMask);
        Assert.Equal(0, loaded.EffectiveCapo(6));   // a string the track does not have never gets a partial's raise
    }

    [Fact]
    public void ScoreHeaderLineNamesTheFullAndPartialCapos()
    {
        var partials = new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } };
        Assert.Equal("Capo 2, partial 5 (1-3)", PartialCapoRules.CapoLine(2, partials));
        Assert.Equal("Partial 5 (1-3)", PartialCapoRules.CapoLine(0, partials));
        Assert.Equal("Capo 2", PartialCapoRules.CapoLine(2, null));
        Assert.Equal("", PartialCapoRules.CapoLine(0, null));
        Assert.True(PartialCapoRules.HasLine(0, partials));
        Assert.False(PartialCapoRules.HasLine(0, null));

        var many = new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 }, new() { Fret = 2, StringMask = 0b110000 } };
        Assert.Equal("Capo 2, partial 5 (1-3), partial 2 (5-6)", PartialCapoRules.CapoLine(2, many));
    }

    [Fact]
    public void StripNameShowsThePlusWhenPartialsExist()
    {
        var track = Guitar();
        Assert.Equal("Capo guitar", TabForge.Avalonia.Platform.TrackHeaderColumn.DisplayName(track));
        TrackSetup.SetCapo(track, 2);
        Assert.Equal("Capo guitar · Capo 2", TabForge.Avalonia.Platform.TrackHeaderColumn.DisplayName(track));
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } });
        Assert.Equal("Capo guitar · Capo 2 +", TabForge.Avalonia.Platform.TrackHeaderColumn.DisplayName(track));
        TrackSetup.SetCapo(track, 0);
        Assert.Equal("Capo guitar · Partial +", TabForge.Avalonia.Platform.TrackHeaderColumn.DisplayName(track));
    }

    [Fact]
    public void PasteLayoutUsesTheEffectiveCapoOfEachString()
    {
        var track = Guitar();
        var plain = InstrumentLayout.Of(track);
        TrackSetup.SetPartialCapos(track, new List<PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } });
        var partial = InstrumentLayout.Of(track);

        Assert.Equal(5, partial.CapoOf(0));
        Assert.Equal(0, partial.CapoOf(5));
        Assert.Equal(69, partial.OpenPitch(0));
        Assert.Equal(40, partial.OpenPitch(5));
        Assert.False(plain.SameLayout(partial));
    }

    // The disk JSON the save actually writes (DiskOptions), decompressed from its gzip wrapper.
    private static string DiskJson(SongProject project)
    {
        using var gzip = new GZipStream(new MemoryStream(ProjectService.PersistBytes(project)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
