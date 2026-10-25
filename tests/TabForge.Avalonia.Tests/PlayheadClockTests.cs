using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the playhead clock checks: a 10 Hz engine drawn at 120 Hz rises without running more than one frame ahead of it or back, a seek snaps at once, the timeline
///   playhead moves without redrawing the body, and the score shows its green marker (line, top dot, lit notes) only while playing.
/// Does not own: the screen-refresh callback (AnimationFrameSource needs a real window) or the real engine.
/// Tests: this class.
/// </summary>
public class PlayheadClockTests
{
    private const int FramesPerSecond = 120;
    private const int EngineReportEveryFrames = 12;   // 10 Hz at 120 Hz

    private static ScoreTimeline FourBarTimeline()
    {
        var timeline = new ScoreTimeline { TotalMs = 8000 };
        for (var bar = 0; bar < 4; bar++) timeline.Bars.Add(new ScoreBar(bar, bar * 2000, (bar + 1) * 2000, 8, 120));
        return timeline;
    }

    private static (AvaloniaPlaybackSessionTests.FakeEngine Engine, PlaybackTransport Transport) PlayingTransport()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var transport = new PlaybackTransport(engine);
        transport.Play(new SongProject());
        engine.RaiseTimelineChanged(FourBarTimeline());
        return (engine, transport);
    }

    /// <summary>One second of 120 Hz frames over an engine that reports every 100 ms; calls <paramref name="onFrame"/> with the clock's position after each frame.</summary>
    private static void RunOneSecond(AvaloniaPlaybackSessionTests.FakeEngine engine, PlaybackTransport transport, PlayheadClock clock, double[] wall, Action<PlaybackPosition> onFrame)
    {
        const double startMs = 500;
        for (var frame = 0; frame < FramesPerSecond; frame++)
        {
            wall[0] = frame * 1000.0 / FramesPerSecond;
            if (frame % EngineReportEveryFrames == 0) engine.Report(new PlaybackPosition { Bar = 0, ElapsedMs = startMs + wall[0] });
            transport.ApplyFrame();
            clock.Update(transport);
            onFrame(clock.Position);
        }
    }

    [Fact]
    public void ATenHertzEngineIsFollowedWithoutRunningAheadOrBack()
    {
        var (engine, transport) = PlayingTransport();
        var wall = new double[1];
        var clock = new PlayheadClock(() => wall[0]);
        var seen = new List<(double Drawn, double Engine)>();
        RunOneSecond(engine, transport, clock, wall, p => seen.Add((p.ElapsedMs, transport.PlayheadMs)));

        Assert.True(seen.Select(s => s.Drawn).Distinct().Count() >= 20, $"only {seen.Select(s => s.Drawn).Distinct().Count()} distinct positions");
        for (var i = 1; i < seen.Count; i++) Assert.True(seen[i].Drawn >= seen[i - 1].Drawn, $"went back at frame {i}");
        for (var i = 0; i < seen.Count; i++) Assert.True(seen[i].Drawn <= seen[i].Engine + PlayheadClock.MaxLeadMs + 1e-9, $"ran ahead of the engine at frame {i}");
    }

    [Fact]
    public void ASeekOrLoopWrapSnapsToTheEngineAtTheNextFrame()
    {
        var (engine, transport) = PlayingTransport();
        var wall = new double[1];
        var clock = new PlayheadClock(() => wall[0]);
        RunOneSecond(engine, transport, clock, wall, _ => { });

        engine.Report(new PlaybackPosition { Bar = 0, ElapsedMs = 100 });
        wall[0] += 8;
        transport.ApplyFrame();
        clock.Update(transport);

        Assert.Equal(100, clock.Position.ElapsedMs, 3);
    }

    [AvaloniaFact]
    public void TheTimelinePlayheadMovesEveryFrameWithoutRedrawingTheBody()
    {
        var view = new TimelineView(FullDemoSongFactory.Create());
        var window = new Window { Width = 1300, Height = 400, Content = view };
        window.Show();
        view.SetPlayhead(0, 0);
        window.CaptureRenderedFrame();
        var bodyBefore = view.BodyRenderCount;
        var layerBefore = view.PlayheadLayer.RedrawCount;

        var (engine, transport) = PlayingTransport();
        var wall = new double[1];
        var clock = new PlayheadClock(() => wall[0]);
        var xs = new List<double>();
        RunOneSecond(engine, transport, clock, wall, p =>
        {
            view.SetPlayhead(p.Bar, p.BarFraction);
            xs.Add(view.PlayheadLayer.PlacedX);
        });
        window.CaptureRenderedFrame();

        Assert.True(xs.Distinct().Count() >= 20);
        for (var i = 1; i < xs.Count; i++) Assert.True(xs[i] >= xs[i - 1]);
        Assert.Equal(bodyBefore, view.BodyRenderCount);
        Assert.True(view.PlayheadLayer.RedrawCount - layerBefore <= 2, "the layer redrew per frame");
    }

    [AvaloniaFact]
    public void TheScoreShowsItsGreenMarkerOnlyWhilePlaying()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(0, 0.0));
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var session = sessions.For(document)!;
        var view = new ScorePreviewView();
        view.Show(document);
        view.BindPlayback(session);
        // The score follows playback only while attached, so it is shown in a window.
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Assert.True(double.IsNaN(view.PlayheadOverlay.LineX));

        session.PlayOrPause();
        session.PumpFrame();
        var overlay = view.PlayheadOverlay;
        Assert.False(double.IsNaN(overlay.LineX));
        Assert.NotNull(overlay.DotCentre);
        Assert.Equal(overlay.LineX, overlay.DotCentre!.Value.X, 3);
        var timeline = session.Transport.Timeline!;
        var note = timeline.NotesFor(0).First();
        view.Probe.Play.Set(timeline, 0, note.Bar, note.Cell, 0.3, note.OnsetMs + 1);
        Assert.Contains((note.Bar, note.Cell, note.StringIndex), view.Probe.Play.Sounding);
        overlay.Place();
        Assert.False(double.IsNaN(overlay.LineX));

        var redraws = overlay.RedrawCount;
        var before = overlay.LineX;
        view.Probe.Play.SetFraction(view.Probe.Play.Fraction + 0.1);
        overlay.Place();
        Assert.True(overlay.LineX > before);
        Assert.Equal(redraws, overlay.RedrawCount);

        session.Stop();
        Assert.True(double.IsNaN(overlay.LineX));
        Assert.Null(overlay.DotCentre);
        Assert.Empty(view.Probe.Play.Sounding);
        window.Close();
    }

    [Fact]
    public void AStartThatReportsZeroForAWhileNeverMovesTheDrawnPositionBackwards()
    {
        var (engine, transport) = PlayingTransport();
        var wall = new double[1];
        var clock = new PlayheadClock(() => wall[0]);
        var seen = new List<double>();
        for (var frame = 0; frame < FramesPerSecond; frame++)
        {
            wall[0] = frame * 1000.0 / FramesPerSecond;
            // The engine sits on its start for 150 ms, then advances at the wall rate.
            engine.Report(new PlaybackPosition { Bar = 0, ElapsedMs = System.Math.Max(0, wall[0] - 150) });
            transport.ApplyFrame();
            clock.Update(transport);
            seen.Add(clock.Position.ElapsedMs);
        }

        for (var i = 1; i < seen.Count; i++) Assert.True(seen[i] >= seen[i - 1], $"went back at frame {i}");
        Assert.True(seen.Take(15).All(ms => ms == 0), "the clock ran ahead of the engine's start");
    }
}
