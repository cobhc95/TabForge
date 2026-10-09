using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>Resizing the timeline dock while playing: the playhead never jumps, the timeline is not re-drawn without a size change, and the rows end flush with the pane.</summary>
public static partial class SelfTest
{
    private static (ArrangementPanel Panel, DockWorkspace Workspace, FitStage Stage, SongProject Song) ResizeStage(int tracks, int bars, double height)
    {
        var song = new SongProject();
        for (var i = 0; i < tracks; i++) song.Tracks.Add(MixerTestTrack("T" + i, TrackKind.Guitar, 30));
        foreach (var t in song.Tracks) t.Measures = Enumerable.Range(0, bars).Select(_ => new MeasureModel()).ToList();
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var workspace = new DockWorkspace(new Window());
        var stage = new FitStage(workspace, 1000, height);
        workspace.SetEditorContent(new Border());
        workspace.RegisterPanel("timeline", "Arrangement", panel, 440, 112, "timeline", "score-editor");
        workspace.RestoreLayout(null);
        stage.UpdateLayout(); PumpUi();
        return (panel, workspace, stage, song);
    }

    private static void TestResizeDuringPlayback()
    {
        var (panel, workspace, stage, song) = ResizeStage(10, 60, 900);
        using var alive = KeepAlive();
        var worstJump = 0.0; var worstMs = 0.0; var renders = 0; var steps = 0;
        var clock = new Stopwatch();
        for (var i = 0; i < 40; i++)
        {
            var bar = 5 + i / 4; var fraction = (i % 4) / 4.0;
            var before = panel.TimelineRenderCount;
            clock.Restart();
            panel.SetPlayhead(bar, fraction, playbackActive: true);
            stage.Height = 900 - (i % 20) * 12;   // a splitter or window drag: the dock around the timeline changes height every frame
            PumpUi();
            clock.Stop();
            worstMs = Math.Max(worstMs, clock.Elapsed.TotalMilliseconds);
            worstJump = Math.Max(worstJump, Math.Abs(panel.PlayheadLineLeft - panel.TimelineXOfBar(bar, fraction)));
            renders += panel.TimelineRenderCount - before; steps++;
        }
        Log.Add($"  info  resize during playback: {renders} timeline renders over {steps} resize steps, worst step {worstMs:0.0} ms, worst playhead offset {worstJump:0.00} px");
        Check("resize during playback: the playhead stays on its bar position", worstJump < 0.5, $"{worstJump:0.00}px");
        Check("resize during playback: the timeline is not re-drawn on every resize step", renders <= steps / 2, $"{renders} renders in {steps} steps");
    }

    /// <summary>Dragging the score/timeline splitter during playback: no score relayout on a height-only change, one for a width change, the playhead keeps its place.</summary>
    private static void TestSplitterDragKeepsScore()
    {
        RunInWindowFixture((window, _) =>
        {
            var dock = LtField<DockWorkspace>(window, "_dockWorkspace")!;
            var editor = LtField<TabEditorControl>(window, "Editor")!;
            var panel = LtField<ArrangementPanel>(window, "Arrangement")!;
            var engine = LtField<object>(editor, "_layout")!;
            int Generation() => LtField<int>(engine, "_scoreGeneration");
            SettleLifetimeDispatcher();
            var scoreBefore = Generation(); var renderBefore = panel.TimelineRenderCount;
            var worstMs = 0.0; var totalMs = 0.0; var worstJump = 0.0;
            var clock = new Stopwatch();
            var baseHeight = Math.Max(150, panel.ActualHeight);
            for (var i = 0; i < 40; i++)
            {
                var wave = i < 20 ? i : 40 - i;
                clock.Restart();
                panel.SetPlayhead(0, (i % 8) / 8.0, playbackActive: true);
                dock.SimulateSplitterDrag("timeline", baseHeight + 100 - wave * 8, complete: false);
                PumpUi();
                clock.Stop();
                worstMs = Math.Max(worstMs, clock.Elapsed.TotalMilliseconds); totalMs += clock.Elapsed.TotalMilliseconds;
                if (panel.PlayheadLineVisible) worstJump = Math.Max(worstJump, Math.Abs(panel.PlayheadLineLeft - panel.TimelineXOfBar(0, (i % 8) / 8.0)));
            }
            var relayouts = Generation() - scoreBefore; var renders = panel.TimelineRenderCount - renderBefore;
            Log.Add($"  info  splitter drag during playback: {relayouts} score relayouts, {renders} timeline renders over 40 steps, worst step {worstMs:0.0} ms, mean {totalMs / 40:0.0} ms");
            Check("splitter drag: a height-only drag does not lay the score out again", relayouts == 0, $"{relayouts} relayouts");
            Check("splitter drag: the timeline redraws at most once per layout pass (two per step)", renders <= 80, $"{renders} renders");
            Check("splitter drag: the playhead keeps its place", worstJump < 0.5, $"{worstJump:0.00}px");
            Check("splitter drag: the score keeps its size", editor.ActualHeight > 10 && editor.ActualWidth > 10, $"{editor.ActualWidth:0}x{editor.ActualHeight:0}");

            Check("splitter drag: the shade keeps the real row height (cropped, never scaled)", ResizeShade.RowScale == 1);
            var before = Generation();
            var scroll = LtField<System.Windows.Controls.ScrollViewer>(window, "ScoreScroll")!; scroll.Width = scroll.ActualWidth - 100; window.UpdateLayout(); SettleLifetimeDispatcher();
            Log.Add($"  info  splitter drag: a width change laid the score out {Generation() - before} time(s)");
            Check("splitter drag: a width change lays the score out once", Generation() - before >= 1 && Generation() - before <= 2, $"{Generation() - before}");
        });
    }

    /// <summary>Mouse sweep over the timeline during playback, a bounded vertical drag, and lanes-only audio rows.</summary>
    private static void TestTimelineHoverAndAudioRows()
    {
        var (panel, workspace, stage, song) = ResizeStage(6, 60, 700);
        using var alive = KeepAlive();
        var audio = MixerTestTrack("Audio", TrackKind.Audio, 30);
        audio.AudioClips.Add(new AudioClip { File = "x.wav", Name = "x", StartSec = 0, SourceLengthSec = 1, FileLengthSec = 1 });
        song.Tracks.Add(audio);
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>()); stage.UpdateLayout(); PumpUi();
        var lanes = ArrangementPanel.LaneCountOf(audio);
        Check("audio row: lanes only (height = lanes x lane height, no notation row)",
            lanes >= 1 && Math.Abs(ArrangementPanel.RowHeightOf(song, audio) - lanes * ArrangementPanel.AudioLaneHeight) < 0.01 &&
            Math.Abs(ArrangementPanel.RowHeightOf(song, song.Tracks[0]) - ArrangementPanel.RowHeightFor(song)) < 0.01, $"{ArrangementPanel.RowHeightOf(song, audio)}");
        Check("audio row: the track list row has the same height", panel.TrackRowActualHeights.Count == song.Tracks.Count &&
            Math.Abs(panel.TrackRowActualHeights[^1] - ArrangementPanel.RowHeightOf(song, audio)) < 1.0, $"{(panel.TrackRowActualHeights.Count > 0 ? panel.TrackRowActualHeights[^1] : -1)}");

        var timeline = panel.TimelineForTest;
        var before = timeline.RenderCount;
        for (var i = 0; i < 200; i++)
        {
            panel.SetPlayhead(i / 40, (i % 8) / 8.0, playbackActive: true);
            panel.SimulateHover(new Point(20 + i * 9 % 600, ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + (i * 7) % 150));
            PumpUi();
        }
        var sweep = timeline.RenderCount - before;
        Log.Add($"  info  hover sweep during playback: {sweep} full timeline renders over 200 moves");
        Check("hover sweep during playback: at most one full timeline render", sweep <= 1, $"{sweep}");

        before = timeline.RenderCount;
        for (var i = 0; i < 40; i++) { stage.Height = 700 - (i % 20) * 12; PumpUi(); }
        var drag = timeline.RenderCount - before;
        Log.Add($"  info  vertical drag: {drag} full timeline renders over 40 steps");
        Check("vertical drag: the timeline re-renders a bounded number of times (not once per step)", drag <= 8, $"{drag}");
    }

    private static void TestTrackRowsEndFlush()
    {
        foreach (var tracks in new[] { 1, 5, 10, 20 })
            foreach (var height in new[] { 300.0, 700, 1300 })
            {
                var (panel, workspace, stage, song) = ResizeStage(tracks, 12, height);
                using var alive = KeepAlive();
                var fit = new Controllers.TrackListFitController(new FitHost { Arrangement = panel, Dock = workspace }, workspace);
                fit.FitToTracks(); stage.UpdateLayout(); PumpUi(); fit.Flush();
                var content = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + panel.TrackRowActualHeights.Sum() + (panel.AddLaneRow?.ActualHeight ?? 0);   // the Add-track lane is part of the content: the rows end flush against it, it ends flush with the pane
                var gap = panel.ActualHeight - content;
                Check($"rows end flush: {tracks} tracks in a {height:0}px window leave no gap", gap <= 1.5 || panel.ActualHeight <= 116 /* the dock's own 112px minimum is taller than one row */, $"gap {gap:0.0}px");
            }
    }
}
