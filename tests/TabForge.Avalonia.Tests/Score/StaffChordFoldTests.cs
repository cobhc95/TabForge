using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the layout-only fold of identical same-onset cells into one staff beat (one stem) and the beam tolerance at that onset.
// Does not own: note entry, the model or the drawing (ScoreEditCommands / StaffNotationModels).
// Tests: this file (headless, no Avalonia window).
public class StaffChordFoldTests
{
    private static TabNote Note(TrackModel track, int stringIndex, int fret) =>
        new() { StringIndex = stringIndex, Fret = fret, MidiValue = track.NoteMidi(stringIndex, fret), Velocity = 100 };

    private static StaffNotationMeasureLayout Build(params TabCell[] cells)
    {
        var track = new TrackModel { Kind = TrackKind.Guitar };
        var measure = new MeasureModel { Number = 1 };
        measure.Cells = cells.ToList();
        while (measure.Cells.Count < 16) measure.Cells.Add(new TabCell());
        track.Measures.Add(measure);
        return StaffNotationLayoutBuilder.CreateLayout(track, measure, 0, 16, 0, 100, 24, 4, 4, 0);
    }

    /// <summary>Two cells at the same onset with the same written shape: one beat, one stem, both cells mapping back to it.</summary>
    [Fact]
    public void IdenticalSameOnsetCellsFoldIntoOneBeatWithOneStem()
    {
        var track = new TrackModel { Kind = TrackKind.Guitar };
        var first = new TabCell { DurationDenominator = 8, RhythmicPosition = 0 };
        first.Notes.Add(Note(track, 0, 0));
        var second = new TabCell { DurationDenominator = 8, RhythmicPosition = 0 };
        second.Notes.Add(Note(track, 2, 2));

        var layout = Build(first, second);

        var beat = Assert.Single(layout.Beats);
        Assert.Equal(2, beat.Notes.Count);
        Assert.True(beat.HasStem, "the folded chord has no stem");
        Assert.Same(beat, layout.BeatForCell(0));
        Assert.Same(beat, layout.BeatForCell(1));
        Assert.Equal(new List<int> { 0, 1 }, beat.CellIndices);
    }

    /// <summary>Same onset but a different duration: the cells stay separate beats (no data is lost by folding).</summary>
    [Fact]
    public void SameOnsetCellsWithDifferentDurationsStaySeparate()
    {
        var track = new TrackModel { Kind = TrackKind.Guitar };
        var first = new TabCell { DurationDenominator = 4, RhythmicPosition = 0 };
        first.Notes.Add(Note(track, 0, 0));
        var second = new TabCell { DurationDenominator = 8, RhythmicPosition = 0 };
        second.Notes.Add(Note(track, 2, 2));

        var layout = Build(first, second);

        Assert.Equal(2, layout.Beats.Count);
        Assert.NotSame(layout.BeatForCell(0), layout.BeatForCell(1));
        Assert.Single(layout.BeatForCell(0)!.Notes);
        Assert.Single(layout.BeatForCell(1)!.Notes);
    }

    /// <summary>Two eighths whose second onset touches the first's end within the position epsilon beam together.</summary>
    [Fact]
    public void EighthNotesTouchingWithinPositionEpsilonBeamTogether()
    {
        var track = new TrackModel { Kind = TrackKind.Guitar };
        var first = new TabCell { DurationDenominator = 8, RhythmicPosition = 0 };
        first.Notes.Add(Note(track, 0, 0));
        var second = new TabCell { DurationDenominator = 8, RhythmicPosition = 2 + StaffNotationGeometry.PositionEpsilon / 2 };
        second.Notes.Add(Note(track, 0, 2));

        var layout = Build(first, second);

        Assert.Equal(2, layout.Beats.Count);
        var group = Assert.Single(layout.BeamGroups);
        Assert.Equal(2, group.Beats.Count);
        Assert.All(layout.Beats, beat => Assert.Equal(group.Index, beat.BeamGroupIndex));
    }
}
