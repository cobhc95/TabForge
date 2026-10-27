using System;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the per-frame playhead mapping writes into the caller's frame and allocates nothing once warm.
/// Does not own: the engine's scheduler clock (PlaybackEngine.Scheduler) or the session frame timer (AvaloniaPlaybackSessionTests).
/// Tests: this class. The engine's AudiblePlayhead fills its frame through PlayheadMapper.MapInto, so this covers its mapping step.
/// </summary>
public class AudiblePlayheadAllocationTests
{
    private static ScoreTimeline TwoBarTimeline()
    {
        var timeline = new ScoreTimeline();
        timeline.Bars.Add(new ScoreBar(0, 0, 2000, 4, 120));
        timeline.Bars.Add(new ScoreBar(1, 2000, 4000, 4, 120));
        return timeline;
    }

    [Fact]
    public void MappingIntoAFrameAllocatesNothingOnceWarm()
    {
        var timeline = TwoBarTimeline();
        var frame = new PlaybackPosition();
        for (var i = 0; i < 10; i++) PlayheadMapper.MapInto(frame, timeline, i * 10.0, 0, 0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            PlayheadMapper.MapInto(frame, timeline, 2000 + i * 1.5, 0, 0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(1, frame.Bar);
    }
}
