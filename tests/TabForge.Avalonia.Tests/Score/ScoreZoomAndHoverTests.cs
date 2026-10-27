using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score's zoom gestures, the hover outline, and the playing highlight of the shown track. Ctrl+wheel and the View zoom commands step the zoom by Core's steps (the
//   numpad keys included, through the hotkey catalogue); page mode fits the width, a window resize refits, and a hand zoom stops the refit;
//   the hover outline is drawn while the song plays.
// Does not own: the zoom rules (ScoreZoomRules), the key table (MainWindowKeyboard), the playback engine (the session's fake engine).
// Tests: this file.
public class ScoreZoomAndHoverTests
{
    private const double ViewerWidth = 900;

    private static (ScorePreviewView Score, ScrollViewer Viewer, Window Window) Show()
    {
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = ViewerWidth, Height = 600 };
        var window = new Window { Content = viewer, Width = ViewerWidth, Height = 600 };
        window.Show();
        Pump(window);
        return (score, viewer, window);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The zoom the page fits the viewer's width at (Core's zoom range applied), as Fit width computes it in page mode.</summary>
    private static double PageFitZoom(ScrollViewer viewer) =>
        Math.Clamp(Math.Max(1, viewer.Viewport.Width - 2) / ScoreGeometryProbe.PageWidth, ScoreZoomRules.MinFactor, ScoreZoomRules.MaxFactor);

    [AvaloniaFact]
    public void CtrlWheelUpStepsTheZoomOneStepAndRaisesTheZoomChange()
    {
        var (score, viewer, window) = Show();
        score.SetZoom(1);
        var raised = 0;
        score.ZoomChanged += () => raised++;

        score.RaiseEvent(new PointerWheelEventArgs(score, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), window,
            new Point(200, 150), 0, new PointerPointProperties(RawInputModifiers.Control, PointerUpdateKind.Other), KeyModifiers.Control, new Vector(0, 1))
        { RoutedEvent = InputElement.PointerWheelChangedEvent });

        Assert.Equal(ScoreZoomRules.Step(1, 1), score.Zoom, 6);
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void ViewZoomCommandsAndNumpadHotkeysStepTheZoomAndTheEditorLeavesThemToTheWindow()
    {
        var (score, _, window) = Show();
        var commands = new MainWindowCommands(window, () => { });
        MainWindowScoreRows.Register(commands, score, null, window);
        score.SetZoom(1);

        Assert.True(commands.Handles("View.ZoomIn") && commands.Handles("View.ZoomOut"));
        commands.Run("View.ZoomIn");
        Assert.Equal(ScoreZoomRules.Step(1, 1), score.Zoom, 6);
        commands.Run("View.ZoomOut");
        Assert.Equal(1, score.Zoom, 6);

        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        Assert.Equal("View.ZoomIn", map[HotkeyCatalog.Canonical("Ctrl+OemPlus")]);
        Assert.Equal("View.ZoomIn", map[HotkeyCatalog.Canonical("Ctrl+Add")]);
        Assert.Equal("View.ZoomOut", map[HotkeyCatalog.Canonical("Ctrl+OemMinus")]);
        Assert.Equal("View.ZoomOut", map[HotkeyCatalog.Canonical("Ctrl+Subtract")]);

        score.Probe.Cursor = (0, 0, 0);
        Assert.False(score.Editor.Key(AvaloniaEditorKeys.ToEditorKey(Key.OemPlus), "OemPlus", true, false, false));
        Assert.False(score.Editor.Key(AvaloniaEditorKeys.ToEditorKey(Key.Add), "Add", true, false, false));
        Assert.False(score.Editor.Key(AvaloniaEditorKeys.ToEditorKey(Key.OemMinus), "OemMinus", true, false, false));
        Assert.False(score.Editor.Key(AvaloniaEditorKeys.ToEditorKey(Key.Subtract), "Subtract", true, false, false));
    }

    [AvaloniaFact]
    public void PageModeFitsTheWidthARefitFollowsTheWindowAndAHandZoomStopsIt()
    {
        var (score, viewer, window) = Show();
        score.Probe.Appearance.CenterSystems = false;   // page mode
        score.PageLayoutChanged();
        Pump(window);
        Assert.Equal(PageFitZoom(viewer), score.Zoom, 3);

        viewer.Width = 1200;
        window.Width = 1200;
        Pump(window);
        Assert.Equal(PageFitZoom(viewer), score.Zoom, 3);

        score.ZoomStep(1);
        var handZoom = score.Zoom;
        viewer.Width = ViewerWidth;
        window.Width = ViewerWidth;
        Pump(window);
        Assert.Equal(handZoom, score.Zoom, 6);

        score.FitWidth();
        Pump(window);
        Assert.Equal(PageFitZoom(viewer), score.Zoom, 3);
    }

    [AvaloniaFact]
    public void HoverOutlineIsDrawnWhileTheSongPlays()
    {
        var (score, _, window) = Show();
        var project = FullDemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
        score.BindPlayback(session);
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = 2, ElapsedMs = timeline.Bars[2].StartMs + 1 });
        session.PumpFrame();
        Pump(window);
        Assert.True(score.Probe.PlaybackMeasure >= 0, "The score does not show a playing bar.");

        var track = score.Probe.Track!;
        var system = score.Probe.Layout.GetLayout(track).Systems[0];
        var y = score.Probe.TabTop(0);
        score.Editor.HoverEnd();
        var withoutHover = DrawnRects(score);
        Assert.True(score.Editor.HoverAt(system.Measures[2].X + 2, y));
        var withHover = DrawnRects(score);

        Assert.True(score.Probe.HoverMeasure >= 0 && score.Probe.HoverCell >= 0, "The pointer over a beat did not set the hover.");
        Assert.NotEmpty(withHover.Except(withoutHover));
    }


    [AvaloniaFact]
    public void PlayingLightsTheShownTracksNotesNotTrackZero()
    {
        var project = FullDemoSongFactory.Create();
        var shownTrack = 1;
        var (bar, expected) = FindBarWhereTrackDiffersFromTrackZero(project, shownTrack);
        var (score, _, window) = Show();
        score.ShowTrack(shownTrack);
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
        score.BindPlayback(session);
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = bar, ElapsedMs = timeline.Bars.First(b => b.Bar == bar).StartMs + 1 });
        session.PumpFrame();
        Pump(window);

        var lit = score.Probe.Play.Sounding.Where(note => note.bar == bar && note.cell == 0).Select(note => note.s).OrderBy(s => s).ToArray();
        Assert.Equal(expected, lit);
    }

    /// <summary>A bar where the track's first beat has notes on strings that the track-zero first beat does not play, and the strings it plays.</summary>
    private static (int Bar, int[] Strings) FindBarWhereTrackDiffersFromTrackZero(TabForge.Models.SongProject project, int track)
    {
        for (var bar = 0; bar < project.Tracks[track].Measures.Count; bar++)
        {
            var strings = project.Tracks[track].Measures[bar].Cells[0].Notes.Select(note => note.StringIndex).OrderBy(s => s).ToArray();
            var zero = project.Tracks[0].Measures[bar].Cells[0].Notes.Select(note => note.StringIndex).OrderBy(s => s).ToArray();
            if (strings.Length > 0 && !strings.SequenceEqual(zero)) return (bar, strings);
        }
        throw new InvalidOperationException("The demo song has no bar where this track's first beat differs from track 0's.");
    }

    /// <summary>The rectangles the score draws on its page (every bar, no playhead), as the renderer hands them to a draw context.</summary>
    private static List<(double X, double Y, double Width, double Height)> DrawnRects(ScorePreviewView score)
    {
        var recorder = new TrackRowSelectionTests.RectRecorder();
        score.Probe.DrawPage(recorder, int.MaxValue, withPlayhead: false);
        return recorder.Rects;
    }
}
