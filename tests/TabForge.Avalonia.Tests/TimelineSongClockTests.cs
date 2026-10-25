using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that a playing timeline's milliseconds map to the song's performed seconds (repeats applied), so a take
/// recorded mid-song lands where the song was when heard instead of near the song start.
/// Tests: TimelineSongClock.
/// </summary>
public class TimelineSongClockTests
{
    private static (TabForge.Models.SongProject Song, ScoreTimeline Full, ScoreTimeline Mid) Setup()
    {
        var song = DemoSongFactory.Create();
        var opt = new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true };
        var full = MidiTimelineBuilder.Build(song, opt);
        var midOpt = new PlaybackOptions { StartBar = 8, RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true };
        return (song, full, MidiTimelineBuilder.Build(song, midOpt));
    }

    [Fact]
    public void ATimelineSecondMidSongMapsToTheSongBarBeingPlayed()
    {
        var (song, _, mid) = Setup();
        Assert.Equal(8, mid.Bars[0].Bar);
        var ms = mid.Bars[2].StartMs + 50;
        var songSec = TimelineSongClock.SongSecAt(song, mid, ms);
        // Without the conversion the take would land ~3.5 s in (bar 2); on the song clock it is at bar 10.
        var (bar, _) = AudioClipTimelineRules.BarAtSeconds(song, songSec);
        Assert.Equal(mid.Bars[2].Bar, bar);
        Assert.Equal(10, bar);
    }

    [Fact]
    public void ASongStartTimelineMapsToItself()
    {
        var (song, full, _) = Setup();
        var ms = full.Bars[3].StartMs + 50;
        Assert.Equal(ms / 1000.0, TimelineSongClock.SongSecAt(song, full, ms), 6);
    }

    [Fact]
    public void NoTimelinePassesTheValueThrough()
    {
        var (song, _, _) = Setup();
        Assert.Equal(2.5, TimelineSongClock.SongSecAt(song, null, 2500), 6);
        Assert.True(double.IsNaN(TimelineSongClock.SongSecAt(song, null, double.NaN)));
    }

    [Fact]
    public void ALoopPreferencePicksTheLoopedPass()
    {
        // Two identical passes: without a preference the first pass wins, with a loop start the pass nearest to it wins.
        var performed = new[]
        {
            new PerformedBar(0, 0, 1000), new PerformedBar(1, 1000, 2000),
            new PerformedBar(0, 2000, 3000), new PerformedBar(1, 3000, 4000),
        };
        var timeline = new ScoreTimeline();
        timeline.Bars.Add(new ScoreBar(0, 0, 1000, 8, 120));
        timeline.Bars.Add(new ScoreBar(1, 1000, 2000, 8, 120));
        Assert.Equal(0.5, TimelineSongClock.SongSecAt(performed, timeline, 500), 6);
        Assert.Equal(2.5, TimelineSongClock.SongSecAt(performed, timeline, 500, preferSongMs: 2.5), 6);
    }
}
