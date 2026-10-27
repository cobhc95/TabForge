using System.Collections.Generic;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the engine position anchors a session sends: play, pause, resume, stop, seek and drift
// report the song transport, so the engine's audio clips start, follow jumps and stop with the song. Nothing anchors while the start
// is still compiling.
// Does not own: the transport (PlaybackTransport) or the engine client (the sessions host wires ReportEnginePosition to it).
// Tests: this file.
public class EnginePositionAnchorTests
{
    [Fact]
    public void PlayAnchorsPauseSilencesResumeReanchorsAndStopHoldsTheSongSecond()
    {
        var project = DemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));

        session.PlayOrPause();
        Assert.Empty(anchors);   // the start still compiles: the first real frame anchors it

        // A start from bar 1 reports the performed second of that bar (the song clock), not the timeline's own clock that begins at 0 there.
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { StartBar = 1, RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        var first = Presenters.AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        Assert.True(first > 0.5);
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = 0, Cell = 0, BarFraction = 0, ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();
        Assert.Single(anchors);
        Assert.Equal(first, anchors[0].Sec, 3);
        first = anchors[0].Sec;

        session.PumpFrame();
        Assert.Single(anchors);   // no change, no drift: nothing resends

        engine.Report(new PlaybackPosition { Bar = 1, Cell = 0, BarFraction = 0, ElapsedMs = timeline.PlayFromMs + 5000 });
        session.PumpFrame();
        var later = anchors[1].Sec;
        Assert.True(later > first + 3);
        Assert.Equal(new[] { (true, first), (true, later) }, anchors);

        session.PlayOrPause();   // pause
        Assert.Equal(new[] { (true, first), (true, later), (false, later) }, anchors);

        session.PlayOrPause();   // resume
        Assert.Equal(new[] { (true, first), (true, later), (false, later), (true, later) }, anchors);

        session.Stop();
        Assert.Equal(new[] { (true, first), (true, later), (false, later), (true, later), (false, later) }, anchors);
    }

    [Fact]
    public void SeekWhilePlayingAnchorsTheJumpAtOnce()
    {
        var project = DemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));

        session.PlayOrPause();
        engine.RaiseTimelineChanged(new ScoreTimeline { PlayFromMs = 0 });
        engine.Report(new PlaybackPosition { Bar = 0, Cell = 0, BarFraction = 0, ElapsedMs = 0 });
        session.PumpFrame();
        Assert.Single(anchors);

        session.Seek(1, 0);

        var expected = Presenters.AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        Assert.Equal(2, anchors.Count);
        Assert.True(anchors[1].Playing);
        Assert.Equal(expected, anchors[1].Sec, 6);
        Assert.Equal((1, 0), engine.LastSeek);
    }

    [Fact]
    public void TheAnchorCarriesTheTransportSpeed()
    {
        var project = DemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        var speeds = new List<double>();
        session.ReportEnginePosition = (_, _, speed) => speeds.Add(speed);
        session.SetSpeed(0.5);

        session.PlayOrPause();
        engine.RaiseTimelineChanged(new ScoreTimeline { PlayFromMs = 0 });
        engine.Report(new PlaybackPosition { Bar = 0, Cell = 0, BarFraction = 0, ElapsedMs = 0 });
        session.PumpFrame();

        Assert.Equal(new[] { 0.5 }, speeds);
    }

    [Fact]
    public void NothingAnchorsWithoutAReporter()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new SongProject());

        session.PlayOrPause();
        engine.RaiseTimelineChanged(new ScoreTimeline { PlayFromMs = 250 });
        session.PumpFrame();
        session.Stop();   // no reporter wired: must not throw
    }
}
