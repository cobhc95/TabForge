using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the pure track list selection (Shift range, Ctrl toggle, targets) and the group edits (flags, level delta with clamping, remove, block move).
// Does not own: the pointer and key wiring (TrackMultiSelectTests).
// Tests: TrackSelection, TrackGroupEdits.
public sealed class TrackGroupEditsTests
{
    private static SongProject Song(int count = 6)
    {
        var song = DemoSongFactory.Create();
        while (song.Tracks.Count < count) song.Tracks.Add(NewTrackFactory.CreateTrack(song, TrackKind.Audio));
        song.Tracks.RemoveRange(count, song.Tracks.Count - count);
        for (var i = 0; i < count; i++) song.Tracks[i].Name = $"T{i}";
        return song;
    }

    private static string[] Names(SongProject song) => song.Tracks.Select(t => t.Name).ToArray();

    [Fact]
    public void RangeRunsFromTheAnchorAndToggleMovesIt()
    {
        var song = Song();
        var model = new SelectionModel();
        model.Tracks.Click(song.Tracks, 4, TrackPick.Plain);
        model.Tracks.Click(song.Tracks, 2, TrackPick.Range);
        Assert.Equal(new[] { 2, 3, 4 }, model.Tracks.Rows(song.Tracks));
        model.Tracks.Click(song.Tracks, 0, TrackPick.Toggle);
        Assert.Equal(new[] { 0, 2, 3, 4 }, model.Tracks.Rows(song.Tracks));
        model.Tracks.Click(song.Tracks, 1, TrackPick.Range);   // the anchor is now track 0
        Assert.Equal(new[] { 0, 1 }, model.Tracks.Rows(song.Tracks));
    }

    [Fact]
    public void TargetsAreTheSelectionOnlyWhenTheClickedTrackIsInIt()
    {
        var song = Song();
        var model = new SelectionModel();
        model.Tracks.Click(song.Tracks, 1, TrackPick.Plain);
        model.Tracks.Click(song.Tracks, 2, TrackPick.Range);
        Assert.Equal(new[] { 1, 2 }, model.Tracks.Targets(song.Tracks, 2));
        Assert.Equal(new[] { 4 }, model.Tracks.Targets(song.Tracks, 4));
        Assert.Empty(model.Tracks.Targets(song.Tracks, 9));
    }

    [Fact]
    public void TheSelectionFollowsTheTracksWhenTheyMove()
    {
        var song = Song();
        var model = new SelectionModel();
        model.Tracks.Click(song.Tracks, 1, TrackPick.Plain);
        model.Tracks.Click(song.Tracks, 2, TrackPick.Range);
        Assert.True(TrackGroupEdits.MoveBlock(song, new[] { 1, 2 }, 1, 4));
        Assert.Equal(new[] { 4, 5 }, model.Tracks.Rows(song.Tracks));
    }

    [Fact]
    public void LevelDeltaIsClampedPerTrack()
    {
        var song = Song();
        song.Tracks[1].Volume = 120;
        song.Tracks[2].Volume = 50;
        Assert.True(TrackGroupEdits.ShiftLevel(song, new[] { 1, 2 }, TrackLevel.Volume, 20));
        Assert.Equal(127, song.Tracks[1].Volume);
        Assert.Equal(70, song.Tracks[2].Volume);
        Assert.True(TrackGroupEdits.ShiftLevel(song, new[] { 1, 2 }, TrackLevel.Volume, -200));
        Assert.Equal(0, song.Tracks[1].Volume);
        Assert.False(TrackGroupEdits.ShiftLevel(song, new[] { 1, 2 }, TrackLevel.Volume, -5));
    }

    [Fact]
    public void FlagsAreSetOnEveryRowAndReportNoChangeWhenAlreadySet()
    {
        var song = Song();
        Assert.True(TrackGroupEdits.SetFlag(song, new[] { 0, 3 }, TrackFlag.Solo, true));
        Assert.True(song.Tracks[0].Solo && song.Tracks[3].Solo && !song.Tracks[1].Solo);
        Assert.False(TrackGroupEdits.SetFlag(song, new[] { 0, 3 }, TrackFlag.Solo, true));
    }

    [Fact]
    public void RemoveRefusesToEmptyTheSong()
    {
        var song = Song(3);
        Assert.False(TrackGroupEdits.Remove(song, new[] { 0, 1, 2 }));
        Assert.True(TrackGroupEdits.Remove(song, new[] { 0, 2 }));
        Assert.Equal(new[] { "T1" }, Names(song));
    }

    [Fact]
    public void BlockMoveKeepsRelativeOrderAndPutsThePressedTrackAtTheDrop()
    {
        var song = Song();
        Assert.True(TrackGroupEdits.MoveBlock(song, new[] { 1, 3 }, 3, 5));
        Assert.Equal(new[] { "T0", "T2", "T4", "T5", "T1", "T3" }, Names(song));
    }

    [Fact]
    public void ASingleTrackBlockMoveMatchesMoveTrack()
    {
        var a = Song();
        var b = Song();
        TrackGroupEdits.MoveBlock(a, new[] { 1 }, 1, 4);
        b.MoveTrack(1, 4);
        Assert.Equal(Names(b), Names(a));
    }
}
