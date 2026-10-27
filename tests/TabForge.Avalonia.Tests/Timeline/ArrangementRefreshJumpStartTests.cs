using System;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: an arrangement refresh and an internal restart keep the performance (pass) they were playing when the start
/// follows the full order (jumps back to earlier bars, second passes of repeats).
/// Does not own: the start-order rules (PlaybackStartOrderTests).
/// Tests: this class. Bars here are 0-based, as in the timelines.
/// </summary>
public class ArrangementRefreshJumpStartTests
{
    private static readonly SongProject Demo = FullDemoSongFactory.Create();

    private static int[] Order(SongProject song) => PlaybackOrder.Build(song, new PlaybackOptions()).ToArray();

    private static int[] BarsOf(ScoreTimeline timeline) => timeline.Bars.Select(bar => bar.Bar).ToArray();

    private static int[] Identity(SongProject song) => Enumerable.Range(0, song.Tracks.Max(t => t.Measures.Count)).ToArray();

    private static ArrangementRefreshCompiler.Plan Refresh(SongProject song, PlaybackOptions options, int activeIndex, int[]? map = null)
    {
        var current = MidiTimelineBuilder.Build(song, options);
        return ArrangementRefreshCompiler.CompileFuture(current, song, options, map ?? Identity(song), current.Bars[activeIndex], null);
    }

    private static void AssertSameAsFresh(SongProject song, PlaybackOptions options, ArrangementRefreshCompiler.Plan plan) =>
        Assert.Equal(BarsOf(MidiTimelineBuilder.Build(song, options)), BarsOf(plan.Timeline));

    [Fact]
    public void ActiveOnTheSegnoAfterTheDsKeepsTheJumpOnce()
    {
        var options = new PlaybackOptions { StartBar = 116 };   // bar 117
        var plan = Refresh(Demo, options, 1);                   // bar 36 after the D.S.
        AssertSameAsFresh(Demo, options, plan);
        var bars = BarsOf(plan.Timeline);
        Assert.Equal(new[] { 116, 35, 36, 37, 38, 117 }, bars.Take(6).ToArray());
        Assert.Single(bars, bar => bar == 35);
        Assert.Single(bars, bar => bar == 116);
    }

    [Fact]
    public void ActiveOnBar117TheFutureStartsWithTheSegno()
    {
        var options = new PlaybackOptions { StartBar = 116 };
        var plan = Refresh(Demo, options, 0);
        AssertSameAsFresh(Demo, options, plan);
        Assert.Equal(new[] { 116, 35, 36 }, BarsOf(plan.Timeline).Take(3).ToArray());
    }

    [Fact]
    public void ActiveOnTheSecondPassOfBar20ContinuesWithoutReplayingTheRepeat()
    {
        var options = new PlaybackOptions { StartBar = 19 };   // bar 20
        var plan = Refresh(Demo, options, 4);                  // 20,21,22,19,[20]
        AssertSameAsFresh(Demo, options, plan);
        Assert.Equal(new[] { 19, 20, 21, 18, 19, 20, 22, 23 }, BarsOf(plan.Timeline).Take(8).ToArray());
    }

    [Fact]
    public void AStartAtTheSongStartIsUnchanged()
    {
        var options = new PlaybackOptions { StartBar = 0 };
        var plan = Refresh(Demo, options, 10);
        AssertSameAsFresh(Demo, options, plan);
        Assert.Equal(Order(Demo), BarsOf(plan.Timeline));
    }

    [Fact]
    public void ADeletedBarBeforeTheStartDoesNotThrowAndTheFutureRemains()
    {
        var options = new PlaybackOptions { StartBar = 116 };
        var map = Identity(Demo);
        map[10] = -1;
        var plan = Refresh(Demo, options, 0, map);
        Assert.True(plan.Timeline.Bars.Count > 2);
    }

    [Fact]
    public void AStartOccurrenceOnARepeatBarContinuesTheSecondPass()
    {
        var options = new PlaybackOptions { StartBar = 18, StartOccurrence = 2 };   // second performance of bar 19
        var plan = Refresh(Demo, options, 1);                                       // 19,[20]
        AssertSameAsFresh(Demo, options, plan);
        Assert.Equal(new[] { 18, 19, 20, 22, 23 }, BarsOf(plan.Timeline).Take(5).ToArray());
    }

    [Fact]
    public void RestartOccurrenceOfBar37AfterTheDsIsTheSecondPerformance()
    {
        var order = Order(Demo);
        var timeline = order.Skip(Array.IndexOf(order, 116)).ToArray();   // started at bar 117
        Assert.Equal(2, ArrangementRefreshCompiler.ActiveOccurrence(order, timeline, 2, Array.IndexOf(order, 116)));
    }

    [Fact]
    public void RestartOccurrenceOnPassTwoOfBar20IsTheSecondPerformance()
    {
        var order = Order(Demo);
        var start = Array.IndexOf(order, 19);
        var timeline = order.Skip(start).ToArray();
        Assert.Equal(2, ArrangementRefreshCompiler.ActiveOccurrence(order, timeline, 4, start));
    }

    [Fact]
    public void RestartOccurrenceOnTheFirstPerformanceIsOneAndALinearSongIsAlwaysOne()
    {
        var order = Order(Demo);
        var start = Array.IndexOf(order, 36);
        Assert.Equal(1, ArrangementRefreshCompiler.ActiveOccurrence(order, order.Skip(start).ToArray(), 0, start));

        var linear = new SongProject { Tempo = 120 };
        linear.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(6) });
        var linearOrder = Order(linear);
        for (var at = 0; at < 6; at++)
            Assert.Equal(1, ArrangementRefreshCompiler.ActiveOccurrence(linearOrder, linearOrder.Skip(at).ToArray(), 0, at));
    }

    [Fact]
    public void AnUnmatchedTimelineFallsBackToNoOccurrence()
    {
        var order = Order(Demo);
        Assert.Null(ArrangementRefreshCompiler.ActiveOccurrence(order, new[] { 144, 3, 99 }, 2, 0));
        Assert.Null(ArrangementRefreshCompiler.ActiveOccurrence(order, new[] { 0 }, 5, 0));
    }

    [Fact]
    public void TheLiveTimelineGivesTheOccurrenceOfTheBarUnderThePlayhead()
    {
        var options = new PlaybackOptions { StartBar = 116 };
        var timeline = MidiTimelineBuilder.Build(Demo, options);
        var at = timeline.Bars[2].StartMs + 1;   // bar 37 after the D.S.
        Assert.Equal(36, timeline.Bars[2].Bar);
        Assert.Equal(2, ArrangementRefreshCompiler.ActiveOccurrence(timeline, Demo, options, at));
    }

    [Fact]
    public void ATransportSpeedChangeOnTheSecondPerformanceKeepsThePass()
    {
        var engine = new PassEngine(Demo);
        using var transport = new PlaybackTransport(engine);
        transport.Seek(Demo, 116, 0);   // bar 117
        transport.Play(Demo);
        engine.Now.Bar = 36;            // bar 37 after the D.S.
        engine.Now.ElapsedMs = engine.Timeline!.Bars[2].StartMs + 1;

        transport.SetSpeed(Demo, 0.5);

        Assert.Equal(36, engine.LastOptions!.StartBar);
        Assert.Equal(2, engine.LastOptions.StartOccurrence);
        Assert.Equal(0.5, engine.LastOptions.Speed);
    }

    [Fact]
    public void ATransportSpeedChangeOnTheFirstPerformanceStaysABarStart()
    {
        var engine = new PassEngine(Demo);
        using var transport = new PlaybackTransport(engine);
        transport.Seek(Demo, 36, 0);    // bar 37, first performance
        transport.Play(Demo);
        engine.Now.Bar = 37;
        engine.Now.ElapsedMs = engine.Timeline!.Bars[1].StartMs + 1;

        transport.SetSpeed(Demo, 0.5);

        Assert.Equal(37, engine.LastOptions!.StartBar);
        Assert.Null(engine.LastOptions.StartOccurrence);
    }

    /// <summary>A playing engine that compiles the real timeline for the options it is started with and reports the live pass through the shared helper.</summary>
    private sealed class PassEngine(SongProject song) : IPlaybackEngine
    {
        public bool IsPlaying { get; private set; }
        public bool IsPaused => false;
        public PlaybackOptions? LastOptions { get; private set; }
        public ScoreTimeline? Timeline { get; private set; }
        public PlaybackPosition Now { get; } = new();

        public event Action<ScoreTimeline>? TimelineChanged { add { } remove { } }
        public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

        public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
        {
            IsPlaying = true;
            LastOptions = options;
            Timeline = MidiTimelineBuilder.Build(song, options);
        }

        public int? LiveOccurrence(SongProject project) =>
            Timeline is null || LastOptions is null ? null : ArrangementRefreshCompiler.ActiveOccurrence(Timeline, project, LastOptions, Now.ElapsedMs) is > 1 and var pass ? pass : null;

        public void Stop() => IsPlaying = false;
        public void Pause() { }
        public void Resume() { }
        public void SetLoop(bool loop) { }
        public void Seek(SongProject project, int bar, int cell) { }
        public PlaybackPosition Playhead() => new() { Bar = Now.Bar, Cell = Now.Cell, ElapsedMs = Now.ElapsedMs };
        public PlaybackPosition AudiblePlayhead(PlaybackPosition frame) => frame;
        public void Dispose() { }
    }
}
