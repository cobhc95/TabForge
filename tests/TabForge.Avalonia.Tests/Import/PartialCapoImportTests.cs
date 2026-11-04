using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the partial-capo import boundary. The .gp5 format has no partial-capo field, and the .gp (gpif) notes
// published with the import stack mark partial capos as not supported and name no element, so a full-capo file
// imports with the full capo and NO invented partials.
// Does not own: the model rules (PartialCapoTests) or the exporter.
public sealed class PartialCapoImportTests
{
    public PartialCapoImportTests() => AlphaTabWarmUp.EnsureReady();

    private static SongProject Song()
    {
        var track = new TrackModel
        {
            Name = "Full capo", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(2), Capo = 2,
        };
        foreach (var measure in track.Measures)
            measure.Cells = Enumerable.Range(0, 4)
                .Select(_ => new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 0, Fret = 3, MidiValue = track.PitchOf(0, 3) } } })
                .ToList();
        var song = new SongProject { Title = "Full capo import" };
        song.Tracks.Add(track);
        return song;
    }

    [Fact]
    public void GpFullCapoImportsWithoutInventingPartials()
    {
        var file = GuitarProExporter.ToBytes(Song(), embedProject: false);
        var imported = GuitarProImporter.ImportBytes(file, "full-capo.gp", new ImportContext());
        var track = imported.Tracks[0];

        Assert.Equal(2, track.Capo);
        Assert.Null(track.PartialCapos);
        Assert.Equal(3, track.Measures[0].Cells[0].Notes[0].Fret);
        Assert.Equal(69, track.Measures[0].Cells[0].Notes[0].MidiValue);
    }

    [Fact]
    public void Gp5FullCapoImportsWithoutInventingPartials()
    {
        var file = LegacyGpExport.ToBytes(Song(), LegacyGpFormat.Gp5);
        var imported = GuitarProImporter.ImportBytes(file, "full-capo.gp5", new ImportContext());
        var track = imported.Tracks[0];

        Assert.Equal(2, track.Capo);
        Assert.Null(track.PartialCapos);
        Assert.Equal(3, track.Measures[0].Cells[0].Notes[0].Fret);
        Assert.Equal(69, track.Measures[0].Cells[0].Notes[0].MidiValue);
    }
}
