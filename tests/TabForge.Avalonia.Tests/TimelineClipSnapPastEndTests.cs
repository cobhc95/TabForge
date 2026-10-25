using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the grid of a clip drag past the song's end: it goes on in bars as wide as the last one only where the song can extend (no D.S./Fine ending), is the end itself where it cannot,
//   and nothing snaps in an empty song.
// Does not own: the other snap targets and the drag (TimelineAudioClipTests, ClipAutoBarsTests).
// Tests: this class.
public sealed class TimelineClipSnapPastEndTests
{
    private const int Bars = 8;

    private static TimelineView Open(SongProject project)
    {
        var view = new TimelineView(project) { Snap = new SnapSettings { Grid = "Bar" }, PastEnd = true };
        return view;
    }

    private static SongProject Song(int numerator = 4)
    {
        var project = SongDocument.BlankProject();
        BarGrid.AppendBars(project, Bars - BarRangeEditor.MaxMeasures(project));
        foreach (var track in project.Tracks) { track.Measures[^1].TimeSigNum = numerator; track.Measures[^1].TimeSigDenom = 4; }
        project.MarkTimelineChanged();
        return project;
    }

    private static double Snap(TimelineView view, double x) => TimelineClipSnap.SnapX(view, x, null, altHeld: false, out _);

    [AvaloniaTheory]
    [InlineData(4)]
    [InlineData(3)]
    public void PastTheEndTheGridGoesOnInBarsAsWideAsTheLastOne(int numerator)
    {
        var view = Open(Song(numerator));
        var end = view.BarX(view.BarCount);
        var width = end - view.BarX(view.BarCount - 1);

        Assert.Equal(end + 2 * width, Snap(view, end + 2.4 * width), 6);
        Assert.Equal(end + width, Snap(view, end + 1.2 * width), 6);
    }

    [AvaloniaFact]
    public void PastTheEndOfASongThatEndsInAJumpTheEndIsTheOnlyGridLine()
    {
        var project = Song();
        project.Tracks[0].Measures[3].Directions = "Fine";
        project.Tracks[0].Measures[Bars - 1].Directions = "DaCapo";
        project.MarkTimelineChanged();
        var view = Open(project);
        var end = view.BarX(view.BarCount);
        var width = end - view.BarX(view.BarCount - 1);

        Assert.False(view.ContinuesPastEnd);
        Assert.Equal(end, Snap(view, end + 2.4 * width), 6);
    }

    [AvaloniaFact]
    public void AnEmptySongSnapsNothing()
    {
        var view = Open(new SongProject());

        Assert.Equal(123.0, TimelineClipSnap.SnapX(view, 123.0, null, altHeld: false, out var distance));
        Assert.True(double.IsPositiveInfinity(distance));
    }
}
