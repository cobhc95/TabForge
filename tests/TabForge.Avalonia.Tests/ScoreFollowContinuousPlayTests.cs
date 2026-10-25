using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score follow over a song played bar by bar through the session and its clock, in a ScrollViewer, with the default settings applied
///   by the settings applier (the main window's wiring); the line stays on the sounding bar and the score scrolls to it in both view modes.
/// Does not own: the follow maths (FollowSettingsTests) or a held moment (ScoreEngravingAndFollowTests).
/// Tests: this class.
/// </summary>
public class ScoreFollowContinuousPlayTests
{
    private const int LastBar = 39;   // bar 40

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayingBarByBarToBar40ScrollsTheScoreAndKeepsTheLineOnTheSoundingBar(bool horizontal)
    {
        var project = FullDemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = project });
        new ScoreSettingsApplier(score).Apply(new AppSettings());
        score.Probe.HorizontalScroll = horizontal;
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 600, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var window = new Window { Content = viewer, Width = 900, Height = 600 };
        window.Show();
        score.Refresh();
        Dispatcher.UIThread.RunJobs();
        score.BindPlayback(session);

        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        engine.RaiseTimelineChanged(timeline);
        for (var bar = 0; bar <= LastBar; bar++)
        {
            var span = timeline.Bars[bar];
            engine.Report(new PlaybackPosition { Bar = bar, ElapsedMs = span.StartMs + (span.EndMs - span.StartMs) / 2 });
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        for (var settle = 0; settle < 60; settle++) { session.PumpFrame(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }

        var line = score.Probe.Play.PlayheadLine(score.Probe)!.Value;
        Assert.Equal(session.Playhead.Bar, score.Probe.Play.Measure);   // the score marks the bar the clock plays (the source bar of the performed bar, through repeats)
        Assert.True(viewer.Offset.X > 0 || viewer.Offset.Y > 0, "The score did not scroll.");
        Assert.InRange(line.X * score.Zoom, viewer.Offset.X, viewer.Offset.X + viewer.Viewport.Width);
        Assert.InRange(line.Top * score.Zoom, viewer.Offset.Y, viewer.Offset.Y + viewer.Viewport.Height);
        Assert.Equal(line.X * score.Zoom, score.PlayheadOverlay.LineX, 3);
        window.Close();
        session.Dispose();
    }

    private const int SoakBars = 120;
    private const double SoakStepMs = 250;           // song time per frame
    private const int SoakFrameSleepMs = 4;          // wall time per frame, above the follow frame gate
    private const int SettleFrames = 40;             // frames allowed to catch up after the first scroll and after a manual scroll

    [AvaloniaTheory]
    [InlineData(false, FollowModes.Jump)]
    [InlineData(false, FollowModes.Smooth)]
    [InlineData(true, FollowModes.Jump)]
    [InlineData(true, FollowModes.Smooth)]
    public void ThePlayingSystemStaysInViewOnEveryFrameIncludingAfterAManualScroll(bool horizontal, string mode)
    {
        var project = FullDemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = project });
        var settings = new AppSettings();
        settings.Follow.Mode = mode;
        new ScoreSettingsApplier(score).Apply(settings);
        score.Probe.HorizontalScroll = horizontal;
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 600, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var window = new Window { Content = viewer, Width = 900, Height = 600 };
        window.Show();
        score.Refresh();
        Dispatcher.UIThread.RunJobs();
        score.BindPlayback(session);
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        engine.RaiseTimelineChanged(timeline);

        var endMs = timeline.Bars[System.Math.Min(SoakBars, timeline.Bars.Count) - 1].EndMs;
        var violations = 0;
        var settleUntil = -1;
        var frame = 0;
        for (var ms = 0.0; ms < endMs; ms += SoakStepMs, frame++)
        {
            engine.Report(new PlaybackPosition { Bar = 0, ElapsedMs = ms });
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(SoakFrameSleepMs);
            if (ms > endMs / 2 && settleUntil < 0)
            {
                viewer.Offset = new global::Avalonia.Vector(0, 0);   // the reader scrolls away by hand, then playback keeps going
                settleUntil = frame + SettleFrames;
            }
            if (frame < SettleFrames || frame < settleUntil || score.Probe.Play.PlayheadLine(score.Probe) is not { } line) continue;
            var x = score.Bounds.X + viewer.Offset.X + line.X * score.Zoom;
            var top = line.Top * score.Zoom;
            var inside = top >= viewer.Offset.Y - 1 && top < viewer.Offset.Y + viewer.Viewport.Height && x >= viewer.Offset.X - 1 && x < viewer.Offset.X + viewer.Viewport.Width;
            if (!inside) violations++;
        }

        Assert.Equal(0, violations);
        window.Close();
        session.Dispose();
    }
}
