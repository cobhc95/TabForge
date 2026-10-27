using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the pure clip edits behind the timeline clip commands: move by seconds, the beat length, lane up/down, and placing a copy on the wanted
/// lane or the first free one, including the lane rows they ensure.
/// Does not own: the command table, the undo step, the selection or the clipboard (those stay in MainWindowClipKeys and Core).
/// Tests: this class.
/// </summary>
public class ClipEditServiceTests
{
    [Fact]
    public void MoveClampsAtTheSongStartAndReportsNoMove()
    {
        var clip = new AudioClip { StartSec = 0.2 };

        Assert.True(ClipEditService.Move(clip, -1));
        Assert.Equal(0, clip.StartSec);
        Assert.False(ClipEditService.Move(clip, -1));
        Assert.Equal(0, clip.StartSec);
    }

    [Fact]
    public void BeatSecondsIsTheTempoBeatButNeverFasterThanTheMinimumTempo()
    {
        Assert.Equal(0.5, ClipEditService.BeatSeconds(120), 9);
        Assert.Equal(60.0 / ClipEditService.MinimumTempo, ClipEditService.BeatSeconds(1), 9);
    }

    [Fact]
    public void LaneUpAtTheTopLaneReportsNoChange()
    {
        var clip = new AudioClip { Lane = 0 };

        Assert.False(ClipEditService.LaneUp(new TrackModel(), clip));
        Assert.Equal(0, clip.Lane);
    }

    [Fact]
    public void LaneDownMovesTheClipAndEnsuresTheLane()
    {
        var track = AudioTrack();
        var clip = new AudioClip { Lane = 0, StartSec = 0, SourceLengthSec = 1 };
        track.AudioClips.Add(clip);

        Assert.True(ClipEditService.LaneDown(track, clip));
        Assert.Equal(1, clip.Lane);
        Assert.True(track.Lanes.Count >= 2, $"lanes {track.Lanes.Count}");
    }

    [Fact]
    public void PlaceCopyTakesTheFirstFreeLaneWhenTheWantedOneOverlapsAndClampsTheStart()
    {
        var track = AudioTrack();
        track.AudioClips.Add(new AudioClip { Lane = 0, StartSec = 0, SourceLengthSec = 2 });
        var copy = new AudioClip { StartSec = 0, SourceLengthSec = 1 };

        ClipEditService.PlaceCopy(track, copy, startSec: -3, lane: 0);

        Assert.Equal(0, copy.StartSec);
        Assert.Equal(1, copy.Lane);
        Assert.Contains(copy, track.AudioClips);
        Assert.True(track.Lanes.Count >= 2, $"lanes {track.Lanes.Count}");
    }

    private static TrackModel AudioTrack()
    {
        var project = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(track);
        return track;
    }
}
