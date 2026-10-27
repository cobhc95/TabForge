using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score's hand scrolling through routed wheel events: Shift+wheel moves the one-line view sideways, and a hand scroll while the
//   playhead follows holds the follow back while the Follow setting "Pause following when I scroll by hand" is on.
// Does not own: the follow maths (Core FollowScrollPolicy), the playback engine (AvaloniaPlaybackSessionTests.FakeEngine).
// Tests: ScoreScrollGestureTests (this file).
public class ScoreScrollGestureTests
{
    /// <summary>A wheel notch over the score's view, as the window raises it (Delta.Y positive scrolls up).</summary>
    private static void Wheel(ScorePreviewView score, Window window, KeyModifiers modifiers, Vector delta) =>
        score.RaiseEvent(new PointerWheelEventArgs(score, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), window,
            new Point(200, 150), 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), modifiers, delta)
        { RoutedEvent = InputElement.PointerWheelChangedEvent });

    [AvaloniaFact]
    public void ShiftWheelMovesTheOneLineScoreSideways()
    {
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 600, Height = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 600, Height = 300, Content = viewer };
        window.Show();
        score.Probe.HorizontalScroll = true;
        score.Refresh();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var before = viewer.Offset;

        Wheel(score, window, KeyModifiers.Shift, new Vector(0, -1));

        Assert.True(viewer.Offset.X > before.X, "the score moved to the right");
        Assert.Equal(before.Y, viewer.Offset.Y, 3);
        window.Close();
    }

    [AvaloniaFact]
    public void AHandScrollWhilePlayingHoldsTheFollowBack()
    {
        var project = FullDemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
        var settings = new AppSettings();
        var score = new ScorePreviewView();
        new ScoreSettingsApplier(score).Apply(settings);
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 400 };
        var window = new Window { Content = viewer, Width = 900, Height = 400 };
        window.Show();
        score.Refresh();
        Dispatcher.UIThread.RunJobs();
        score.BindPlayback(session);
        session.PlayOrPause();
        engine.RaiseTimelineChanged(MidiTimelineBuilder.Build(project, new PlaybackOptions()));
        PlayFrames(engine, session, fromMs: 0, toMs: 2000);

        Wheel(score, window, KeyModifiers.None, new Vector(0, -10));
        var held = viewer.Offset.Y;
        PlayFrames(engine, session, fromMs: 2000, toMs: 4000);

        Assert.Equal(held, viewer.Offset.Y, 1);
        window.Close();
    }

    /// <summary>Reports the playback clock every 50 ms from <paramref name="fromMs"/> and lets each frame run.</summary>
    private static void PlayFrames(AvaloniaPlaybackSessionTests.FakeEngine engine, AvaloniaPlaybackSession session, double fromMs, double toMs)
    {
        for (var ms = fromMs; ms < toMs; ms += 50)
        {
            engine.Report(new PlaybackPosition { Bar = 0, ElapsedMs = ms });
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
