using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: which timeline rows (their tab blocks, takes and imported clips) draw in the muted look: the mixer's rule decides (muted, or not soloed while another track is soloed),
/// and the clip fill follows it. Toggling mute or solo needs only a repaint, no rebuild.
/// Does not own: the mute/solo rule itself (MixerGroups.IsAudible, Core).
/// Tests: TimelineView.IsRowDimmed, TimelineAudioClips.FillOpacityOf.
/// </summary>
public class TimelineMuteSoloDimTests
{
    private const int DimPercent = 80;

    private static (TimelineView View, SongProject Song) Make()
    {
        var song = DemoSongFactory.Create();
        foreach (var track in song.Tracks.Take(2))
            track.AudioClips.Add(new AudioClip { Name = "clip", StartSec = 0, SourceLengthSec = 2 });
        var view = new TimelineView(song, null);
        return (view, song);
    }

    private static bool ClipIsMuted(TimelineView view, int row, bool light = false)
    {
        var track = view.Song.Tracks[row];
        var opacity = TimelineAudioClips.FillOpacityOf(light, ClipLanes.Audible(track, track.AudioClips[0]), view.IsRowDimmed(row), DimPercent);
        return opacity < TimelineAudioClips.FillOpacityOf(light, true, false, DimPercent);
    }

    [AvaloniaFact]
    public void AMutedTracksTabTakeAndClipDrawInTheMutedLook()
    {
        var (view, song) = Make();
        song.Tracks[0].Mute = true;
        view.DrawBody(new NullDrawContext(), false);

        Assert.True(view.IsRowDimmed(0));
        Assert.True(ClipIsMuted(view, 0));
        Assert.True(ClipIsMuted(view, 0, light: true));
        Assert.False(view.IsRowDimmed(1));
        Assert.False(ClipIsMuted(view, 1));
    }

    [AvaloniaFact]
    public void SoloGreysEveryTrackThatIsNotSoloedAndSoloOffRestoresThem()
    {
        var (view, song) = Make();
        song.Tracks[0].Solo = true;
        view.DrawBody(new NullDrawContext(), false);

        Assert.False(view.IsRowDimmed(0));
        Assert.False(ClipIsMuted(view, 0));
        Assert.True(view.IsRowDimmed(1));
        Assert.True(ClipIsMuted(view, 1));

        song.Tracks[0].Solo = false;
        view.DrawBody(new NullDrawContext(), false);

        Assert.False(view.IsRowDimmed(0));
        Assert.False(view.IsRowDimmed(1));
        Assert.False(ClipIsMuted(view, 1));
    }

    [AvaloniaFact]
    public void SeveralSoloedTracksStayBrightAndAMutedSoloedTrackFollowsTheMixer()
    {
        var (view, song) = Make();
        song.Tracks[0].Solo = true;
        song.Tracks[1].Solo = true;
        song.Tracks[1].Mute = true;   // a track both muted and soloed plays
        view.DrawBody(new NullDrawContext(), false);

        Assert.False(view.IsRowDimmed(0));
        Assert.False(view.IsRowDimmed(1));
        Assert.Equal(!MixerGroups.IsAudible(song, song.Tracks[2]), view.IsRowDimmed(2));
        Assert.True(view.IsRowDimmed(2));
    }

    [Fact]
    public void ADimmingOfZeroLeavesATrackClipAloneButAMutedClipStaysMuted()
    {
        var playing = TimelineAudioClips.FillOpacityOf(false, true, false, 0);
        Assert.Equal(playing, TimelineAudioClips.FillOpacityOf(false, true, true, 0));
        Assert.True(TimelineAudioClips.FillOpacityOf(false, true, true, 100) < playing);
        Assert.True(TimelineAudioClips.FillOpacityOf(false, false, false, 0) < playing);
    }
}
