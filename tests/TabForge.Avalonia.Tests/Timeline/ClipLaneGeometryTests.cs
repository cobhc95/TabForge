using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the lane band heights of a track: every lane is the WPF 54 px tall; an audio track with lanes has no notation band, so its row is its
/// lanes; an instrument track keeps its notation band above its lanes.
/// Does not own: the drawing of the bands (TimelineAudioClips, TimelineView) and the armed sub-row under them.
/// Tests: this class.
/// </summary>
public class ClipLaneGeometryTests
{
    [Fact]
    public void EveryLaneIsWpfsFiftyFourPixelsAndAnAudioTrackWithLanesHasNoNotationBand()
    {
        var track = new TrackModel { Kind = TrackKind.Audio };
        track.AudioClips.Add(new AudioClip { Lane = 0, StartSec = 0, SourceLengthSec = 1 });
        track.AudioClips.Add(new AudioClip { Lane = 1, StartSec = 0, SourceLengthSec = 1 });

        Assert.Equal(54, ClipLanes.AudioLaneHeight);
        Assert.Equal(108, ClipLanes.LanesHeight(track));
        Assert.Equal(0, ClipLanes.NotationBandHeight(track, 40));
    }

    [Fact]
    public void AnInstrumentTrackKeepsItsNotationBandAboveItsLanes()
    {
        var track = new TrackModel { Kind = TrackKind.Guitar };
        track.AudioClips.Add(new AudioClip { Lane = 0, StartSec = 0, SourceLengthSec = 1 });

        Assert.Equal(40, ClipLanes.NotationBandHeight(track, 40));
        Assert.Equal(54, ClipLanes.LanesHeight(track));
    }
}
