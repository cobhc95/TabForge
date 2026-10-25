using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the master lane's per-bar activity cache. It matches the Core rules for the song it was built from, and a reload rebuilds it.
/// Does not own: the lane's drawing (MasterLaneStrip.Draw) or the rules themselves (TimelineActivityRules).
/// Tests: TimelineView.MasterLaneActivity, MasterLaneStrip.Activity.
/// </summary>
public class MasterLaneActivityCacheTests
{
    [AvaloniaFact]
    public void CachedActivityMatchesTheRulesAndRebuildsOnReload()
    {
        var song = DemoSongFactory.Create();
        var timeline = new TimelineView(song);
        timeline.MasterLaneShown = true;

        var cached = timeline.MasterLaneActivity;
        Assert.Equal(song.Tracks.Count, cached.Length);
        for (var t = 0; t < song.Tracks.Count; t++)
        {
            var track = song.Tracks[t];
            Assert.Equal(track.Measures.Count, cached[t].Length);
            for (var bar = 0; bar < track.Measures.Count; bar++)
            {
                var measure = track.Measures[bar];
                var expected = measure.SimileOneBar || measure.SimileTwoBar || TimelineActivityRules.Build(measure, track.Kind == TrackKind.Drums).Notes > 0;
                Assert.Equal(expected, cached[t][bar]);
            }
        }

        Assert.True(cached[0][1], "Bar 2 of the demo's first track should play before the edit.");
        song.Tracks[0].Measures[1].Cells.Clear();
        timeline.Reload(song);

        Assert.NotSame(cached, timeline.MasterLaneActivity);
        Assert.False(timeline.MasterLaneActivity[0][1]);
    }
}
