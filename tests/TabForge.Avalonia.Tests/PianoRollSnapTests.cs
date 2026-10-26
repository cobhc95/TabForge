using System.Linq;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's clock and snap in beats: seconds to beats through the tempo map, the straight and triplet grid lines (the shared snap
//   core the timeline uses too), the item and playhead candidates in pixels at the zoom, Alt and the magnet off.
// Does not own: the edits (PianoRollEditTests) or the timeline clip snap (TimelineAudioClipTests).
// Tests: PianoRollTimeMap, PianoRollSnap, SnapCore.
public sealed class PianoRollSnapTests
{
    private const double PixelsPerBeat = 48;

    // The blank song: one guitar track, 4 bars of 4/4 at 120 bpm (a beat is half a second).
    private static PianoRollTimeMap Map() => PianoRollTimeMap.Written(SongDocument.BlankProject());

    [Fact]
    public void TheSharedGridHasTheTripletValues()
    {
        Assert.Contains("1/4T", SnapSettings.Grids);
        Assert.Contains("1/8T", SnapSettings.Grids);
        Assert.Contains("1/16T", SnapSettings.Grids);
        Assert.Equal(8.0 / 3, SnapCore.GridStepSlots("1/4T", 16), 9);
        Assert.Equal(4.0 / 3, SnapCore.GridStepSlots("1/8T", 16), 9);
        Assert.Equal(2.0 / 3, SnapCore.GridStepSlots("1/16T", 16), 9);
        Assert.Equal(2.0, SnapCore.GridStepSlots("1/8", 16), 9);
    }

    [Fact]
    public void TheClockMapsSecondsToBeatsThroughTheTempoMap()
    {
        var map = Map();
        Assert.Equal(4, map.Bars.Count);
        Assert.Equal(16, map.EndBeat, 6);
        Assert.Equal(2, map.BeatsAt(1.0), 6);
        Assert.Equal(3, map.SecondsAt(6), 6);
        Assert.Equal(18, map.BeatsAt(9.0), 6);   // past the end the clock goes on in bars like the last
        Assert.Equal(7.25, map.BeatsAt(map.SecondsAt(7.25)), 6);
    }

    [Theory]
    [InlineData("1/8", 0.30, 0.5)]
    [InlineData("1/8T", 0.30, 1.0 / 3)]
    [InlineData("1/16T", 0.40, 1.0 / 3)]
    [InlineData("1/4T", 1.40, 4.0 / 3)]
    [InlineData("1/4", 5.40, 5.0)]
    [InlineData("Bar", 5.10, 4.0)]
    public void GridLinesAreInBeatsStraightAndTriplet(string grid, double beat, double expected) =>
        Assert.Equal(expected, Map().NearestGrid(beat, grid), 6);

    [Fact]
    public void ANearerNoteEdgeWinsOverTheGridAndAltOrTheMagnetOffDoNotSnap()
    {
        var map = Map();
        var snap = new SnapSettings { Grid = "1/4" };
        Assert.Equal(1.0, PianoRollSnap.Snap(map, snap, 1.06, PixelsPerBeat, null, null, false), 6);
        Assert.Equal(1.05, PianoRollSnap.Snap(map, snap, 1.06, PixelsPerBeat, new[] { 1.05, 3.0 }, null, false), 6);
        Assert.Equal(1.07, PianoRollSnap.Snap(map, snap, 1.06, PixelsPerBeat, null, 1.07, false), 6);   // the playhead
        Assert.Equal(1.06, PianoRollSnap.Snap(map, snap, 1.06, PixelsPerBeat, new[] { 1.05 }, null, altHeld: true), 6);
        snap.Enabled = false;
        Assert.Equal(1.06, PianoRollSnap.Snap(map, snap, 1.06, PixelsPerBeat, new[] { 1.05 }, null, false), 6);
    }

    [Fact]
    public void AClickLandsOnTheGridLineLeftOfThePointerAndANewNoteIsOneStepLong()
    {
        var map = Map();
        Assert.Equal(1.0, PianoRollSnap.Floor(map, new SnapSettings { Grid = "1/4" }, 1.9, false), 6);
        Assert.Equal(2.0 / 3, PianoRollSnap.Floor(map, new SnapSettings { Grid = "1/8T" }, 0.9, false), 6);
        Assert.Equal(0.9, PianoRollSnap.Floor(map, new SnapSettings { Grid = "1/8T" }, 0.9, true), 6);
        Assert.Equal(1.0 / 3, PianoRollSnap.NoteLength(map, new SnapSettings { Grid = "1/8T" }, 0), 6);
        Assert.Equal(4.0, PianoRollSnap.NoteLength(map, new SnapSettings { Grid = "Bar" }, 0), 6);
    }

    [Fact]
    public void ThePerformedClockPlaysRepeatsOut()
    {
        var song = SongDocument.BlankProject();
        song.Tracks[0].Measures[0].RepeatStart = true;
        song.Tracks[0].Measures[1].RepeatEnd = true;
        song.MarkTimelineChanged();
        var performed = PianoRollTimeMap.Performed(song);
        Assert.Equal(new[] { 0, 1, 0, 1, 2, 3 }, performed.Bars.Select(b => b.Bar).ToArray());
        Assert.Equal(24, performed.EndBeat, 6);
    }
}
