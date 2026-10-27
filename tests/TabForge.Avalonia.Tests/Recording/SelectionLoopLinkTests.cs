using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the link between the white selection and the loop region: a released score selection sets the loop (Loop on) unless a loop plays, the stepped
//   Escape (white, then the loop as one undo step), the loop wrapping within the region, Select all, each song keeping its loop, the ruler drag, a plain
//   score click clearing the white and a note-made loop, and a loop always tinting its notes in the score (selected clips never light notes).
// Does not own: the model's own rules (Core SelectionModel), the engine's wrap arithmetic (PlaybackEngine).
// Tests: SelectionLoopLink, TimelineInputController, ScorePreviewEditor.
public class SelectionLoopLinkTests
{
    private sealed record Rig(MainWindow Window, AvaloniaPlaybackSessionTests.FakeEngine Engine, AvaloniaPlaybackSession Session,
        ScorePreviewView Score, ArrangementView Arrangement, SelectionModel Model);

    private static Rig Open()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: document, playback: sessions);
        window.Show();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        return new Rig(window, engine, sessions.For(document)!, window.GetLogicalDescendants().OfType<ScorePreviewView>().Single(), arrangement, arrangement.Input.Selection);
    }

    [AvaloniaFact]
    public void ReleasedScoreSelectionSetsTheLoopRangeButLeavesLoopOff()
    {
        var rig = Open();
        rig.Score.Editor.SelectRange(2, 0, 4, -1);
        Assert.Equal((2, 4), rig.Model.BarRange);
        rig.Score.RaiseSelectionReleased();
        Assert.True(rig.Model.HasLoop);
        Assert.Equal((2, 4), rig.Model.LoopBars);
        Assert.False(rig.Model.LoopRepeat);
        Assert.False(rig.Session.Loop);
        Assert.True(rig.Score.Editor.HasSelection);

        rig.Model.SetLoop(6, 7);   // a new score selection replaces the old loop range
        rig.Score.Editor.SelectRange(1, 0, 2, -1);
        rig.Score.RaiseSelectionReleased();
        Assert.Equal((1, 2), rig.Model.LoopBars);
        Assert.False(rig.Model.LoopRepeat);
        rig.Session.Stop();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ARulerLoopReplacesAScoreLoopAndItsSelectedNotesAndViceVersa()
    {
        var rig = Open();
        rig.Score.Editor.SelectRange(2, 0, 4, -1);
        rig.Score.RaiseSelectionReleased();
        rig.Window.Songs.Active.Selection.SetLoop(6, 7);
        rig.Arrangement.Input.Selection.GetType();
        rig.Window.Songs.Active.Selection.GetType();
        Assert.Equal((6, 7), rig.Model.LoopBars);
        Assert.False(rig.Score.Editor.HasSelection);
        rig.Score.Editor.SelectRange(1, 0, 2, -1);
        rig.Score.RaiseSelectionReleased();
        Assert.Equal((1, 2), rig.Model.LoopBars);
        Assert.True(rig.Score.Editor.HasSelection);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ACellSelectionDeselectsTheNotesInOneUndoStepAndUndoBringsThemBack()
    {
        var rig = Open();
        rig.Score.Editor.SelectRange(1, 0, 3, -1);
        var song = rig.Window.Songs.Active;
        var steps = song.Undo.UndoCount;
        song.Highlight.Set(rig.Score.Editor.SelectedTrackIndex, 8);
        DocumentEdits.RecordHighlight(song, default, "Highlight bars");
        Assert.False(rig.Score.Editor.HasSelection);
        Assert.Equal(steps + 1, song.Undo.UndoCount);
        DocumentEdits.Undo(song);
        Assert.True(rig.Model.HasScoreSelection);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AScoreDragClearsTheWhiteCellsAndTheOldLoopInOneUndoStepAndUndoBringsThemBack()
    {
        var rig = Open();
        var song = rig.Window.Songs.Active;
        var track = rig.Score.Editor.SelectedTrackIndex;
        song.Highlight.Set(track, 8);
        rig.Model.SetLoop(6, 7);
        var steps = song.Undo.UndoCount;
        rig.Score.Editor.SelectRange(1, 0, 2, -1);
        rig.Score.RaiseSelectionReleased();
        Assert.False(song.Highlight.Has);
        Assert.Equal((1, 2), rig.Model.LoopBars);
        Assert.Equal(steps + 1, song.Undo.UndoCount);
        DocumentEdits.Undo(song);
        Assert.True(song.Highlight.Has);
        Assert.Equal((6, 7), rig.Model.LoopBars);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TimelineCellSelectionClearsTheScoreSelection()
    {
        var rig = Open();
        rig.Score.Editor.SelectRange(1, 0, 3, -1);
        var highlight = rig.Window.Songs.Active.Highlight;
        highlight.Set(rig.Score.Editor.SelectedTrackIndex, 8);
        highlight.Toggle(rig.Score.Editor.SelectedTrackIndex, 10);
        Assert.False(rig.Score.Editor.HasSelection);   // 25 Oct: one selection at a time, a new timeline selection deselects the notes
        Assert.False(rig.Score.Editor.HasMirrorBars);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void EscapeClearsSelectionsFirstThenTheRulerLoopAndUndoBringsTheLoopBack()
    {
        var rig = Open();
        rig.Model.SetLoop(2, 5);
        rig.Model.SetLoopRepeat(true);
        rig.Score.Editor.SelectRange(1, 0, 1, -1);
        rig.Session.PlayOrPause();
        void Esc() => rig.Arrangement.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = rig.Arrangement });
        Esc();
        Assert.False(rig.Model.HasRange);
        Assert.False(rig.Score.Editor.HasSelection);
        Assert.Equal((2, 5), rig.Model.LoopBars);
        Esc();   // nothing white left: the loop goes and Loop turns off, playback carries on
        Assert.False(rig.Model.HasLoop);
        Assert.True(rig.Session.Loop);   // clearing never switches the Loop button off
        Assert.True(rig.Session.IsPlaying);
        DocumentEdits.Undo(rig.Window.Songs.Active);
        Assert.Equal((2, 5), rig.Model.LoopBars);
        rig.Session.Stop();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void APlainScoreClickClearsTheNotesButNeverARulerLoop()
    {
        var rig = Open();
        rig.Score.Editor.SelectRange(2, 0, 4, -1);
        rig.Score.RaiseSelectionReleased();
        rig.Score.RaisePlainClicked();
        Assert.Equal((false, true, false), (rig.Model.HasRange, rig.Model.HasLoop, rig.Score.Editor.HasSelection));   // the loop stays

        rig.Model.SetLoop(5, 6);   // a ruler loop
        rig.Score.RaisePlainClicked();
        Assert.Equal((5, 6), rig.Model.LoopBars);
        rig.Window.Close();
    }


    [AvaloniaFact]
    public void ARulerLoopOnlyTintsItsNotesAndDeleteLeavesThemAlone()
    {
        var rig = Open();
        rig.Model.SetLoop(3, 5);
        rig.Score.Editor.SetPosition(0, 0, 0);
        Assert.False(rig.Score.Editor.HasSelection);   // so Delete and the other edit commands see only the cursor beat, never the loop
        Assert.Equal((0, 0, 0, 0), rig.Score.Editor.SelectionCellRange);
        Assert.Equal((3, 0, 5), rig.Score.Probe.LoopTint is { } t2 ? (t2.m1, t2.c1, t2.m2) : default);
        rig.Model.ClearLoop();
        Assert.Null(rig.Score.Probe.LoopTint);
        rig.Window.Close();
    }



    [AvaloniaFact]
    public void SelectedClipsNeverLightNotesInTheScore()
    {
        var rig = Open();

        var project = rig.Window.Songs.Active.Project;
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var span = BarRangeGaps.Span(project, 1, 2).GetValueOrDefault();
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From };
        audio.AudioClips.Add(clip);
        project.Tracks.Add(audio);
        rig.Model.SetClips(new[] { new TimelineClipSelection(audio, clip) });
        Assert.False(rig.Score.Editor.HasSelection);   // a clip (take) selection never selects score notes
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ARealTimelineBoxDragSelectsItsClipsButNoNotesInTheScore()
    {
        var rig = Open();
        var project = rig.Window.Songs.Active.Project;
        var audio = project.Tracks[0];
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From };
        audio.AudioClips.Add(clip);
        var view = rig.Arrangement.Timeline;
        view.Reload(project);
        rig.Window.UpdateLayout();
        var box = TimelineAudioClips.ClipBounds(view, audio, clip);
        var y = (box.Top + box.Bottom) / 2;
        Point At(double x) => view.TranslatePoint(new Point(x, y), rig.Window) ?? default;
        var from = At(view.BarX(9) + 5);
        var to = At(view.BarX(2) + 20);
        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(new Point((from.X + to.X) / 2, to.Y));
        rig.Window.MouseMove(to);
        rig.Window.MouseUp(to, MouseButton.Left);
        Assert.Equal(1, rig.Model.ClipCount);
        Assert.False(rig.Model.HasLoop);
        Assert.False(rig.Score.Editor.HasSelection);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void LoopOnWrapsPlaybackWithinTheRegionAndFollowsALiveChange()
    {
        var rig = Open();
        rig.Model.SetLoop(3, 5);
        rig.Model.SetLoopRepeat(true);
        Assert.True(rig.Model.LoopRepeat);
        Assert.True(rig.Arrangement.Timeline.LoopOn);
        rig.Session.PlayOrPause();
        Assert.True(rig.Engine.LastOptions!.Loop);
        Assert.Equal((3, 5), (rig.Engine.LastOptions.LoopStartBar, rig.Engine.LastOptions.LoopEndBar));

        rig.Model.SetLoop(6, 7);
        Assert.Equal((6, 7, 0, -1), rig.Engine.LiveLoopRange);
        rig.Model.SetLoopRepeat(false);   // the Loop button off keeps the region
        Assert.False(rig.Session.Loop);
        Assert.Equal((6, 7), rig.Model.LoopBars);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TurningLoopOffKeepsTheRegionByDefault()
    {
        var rig = Open();
        rig.Model.SetLoop(1, 2);
        rig.Model.SetLoopRepeat(true);
        rig.Model.SetLoopRepeat(false);
        Assert.Equal(((1, 2), false), (rig.Model.LoopBars, rig.Model.LoopRepeat));
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void SelectAllSelectsEveryBarAndLeavesTheLoop()
    {
        var rig = Open();
        var bars = rig.Score.Editor.Project!.Tracks.Max(t => t.Measures.Count);
        rig.Score.SelectAll();
        Assert.Equal((0, bars - 1), rig.Model.BarRange);
        Assert.False(rig.Model.HasLoop);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TabSwitchRestoresEachDocumentsSelectionAndLoop()
    {
        var rig = Open();
        var first = rig.Window.Songs.Active;
        rig.Score.Editor.SelectRange(2, 0, 4, -1);   // a score selection in the first song
        rig.Window.Songs.New();
        var second = rig.Window.Songs.Active;
        Assert.False(second.Selection.HasRange);
        Assert.False(rig.Score.Editor.HasSelection);
        second.Selection.SetLoop(0, 1);   // a ruler loop in the second
        Assert.Equal((0, 1), (rig.Arrangement.Timeline.LoopStart, rig.Arrangement.Timeline.LoopEnd));

        rig.Window.Songs.Switch(0);
        Assert.Equal((2, 4), first.Selection.BarRange);
        Assert.Equal(-1, rig.Arrangement.Timeline.LoopStart);
        Assert.Equal(2, rig.Score.Editor.SelectionCellRange.StartMeasure);

        rig.Window.Songs.Switch(1);
        Assert.Equal((0, 1), second.Selection.LoopBars);
        Assert.Equal((0, 1), (rig.Arrangement.Timeline.LoopStart, rig.Arrangement.Timeline.LoopEnd));
        Assert.Equal((2, 4), first.Selection.BarRange);   // switching away left the first one alone
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TearOffKeepsTheLoop()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        var moving = source.Songs.Active;
        moving.Selection.SetLoop(1, 3);

        var torn = source.TabDrag.TearOff(1, new PixelPoint(300, 200));

        Assert.Same(moving, torn.Songs.Active);
        Assert.Equal((1, 3), moving.Selection.LoopBars);
        var arrangement = torn.GetLogicalDescendants().OfType<ArrangementView>().Single();
        Assert.Equal((1, 3), (arrangement.Timeline.LoopStart, arrangement.Timeline.LoopEnd));
        Assert.Same(moving.Selection, arrangement.Input.Selection);
        torn.Close();
        source.Close();
    }

    [AvaloniaFact]
    public void ClickingALaneCellSelectsTheTrackMovesTheScoreCursorAndRevealsTheBar()
    {
        var rig = Open();
        var timeline = rig.Arrangement.Timeline;
        var bar = 10;
        rig.Model.SetLoop(1, 2);
        var at = timeline.TranslatePoint(new Point(timeline.BarX(bar) + 5, timeline.RowTop(1) + 10), rig.Window)!.Value;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Assert.Equal(1, rig.Window.GetLogicalDescendants().OfType<ScorePreviewView>().Single().Editor.SelectedTrackIndex);
        Assert.Equal((bar, 0), (rig.Score.Editor.SelectedMeasure, rig.Score.Editor.SelectedCell));
        // A cell click highlights the cell (grey) and never changes the loop.
        Assert.Equal((1, 2), rig.Model.LoopBars);
        var highlight = rig.Arrangement.Timeline.Highlight!;
        Assert.Equal((1, bar, bar), (highlight.FirstTrack, highlight.FirstBar, highlight.LastBar));
        var viewer = rig.Score.GetLogicalAncestors().OfType<ScrollViewer>().First();
        Assert.True(viewer.Offset.Y > 0);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DraggingOnTheRulerMakesTheLoopAndALaneDragMakesNoRange()
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, doc);
        var window = new Window { Width = 1300, Height = 400, Content = view };
        window.Show();
        Point Lane(int bar) => new(view.BarX(bar) + 5, view.RowTop(0) + 10);
        Point Ruler(int bar) => new(view.BarX(bar) + 5, TimelineView.RulerHeight / 2);
        window.MouseDown(Lane(2), MouseButton.Left);
        window.MouseMove(Lane(3));
        window.MouseMove(Lane(4));
        window.MouseUp(Lane(4), MouseButton.Left);
        Assert.False(input.Selection.HasRange);
        Assert.False(input.Selection.HasLoop);

        window.MouseDown(Ruler(2), MouseButton.Left);
        window.MouseMove(Ruler(3));
        window.MouseMove(Ruler(4));
        window.MouseUp(Ruler(4), MouseButton.Left);
        Assert.Equal((2, 4), input.Selection.LoopBars);
        Assert.False(input.Selection.HasRange);
        window.MouseDown(Lane(6), MouseButton.Left);
        window.MouseUp(Lane(6), MouseButton.Left);
        Assert.Equal((2, 4), input.Selection.LoopBars);   // a plain click leaves the loop
        Assert.Equal((0, 6, 6), (doc.Highlight.FirstTrack, doc.Highlight.FirstBar, doc.Highlight.LastBar));
        window.Close();
    }

    // A Band card's staff wired to the link must give the wiring back when its card is dropped (a closed pane, a deleted track), so the link does not grow.
    [AvaloniaFact]
    public void DetachingACardStaffGivesItsWiringBack()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var tabs = new MainWindowTabs(document, _ => System.Threading.Tasks.Task.FromResult(PromptButton.Cancel), _ => System.Threading.Tasks.Task.FromResult(false));
        using var link = new SelectionLoopLink(tabs, new ScorePreviewView(), null);
        var staffs = Enumerable.Range(0, 5).Select(_ => new ScorePreviewView()).ToList();
        foreach (var staff in staffs) link.AttachStaff(staff, document);
        Assert.Equal(5, link.StaffWireCount);
        foreach (var staff in staffs) link.DetachStaff(staff);
        Assert.Equal(0, link.StaffWireCount);
        link.DetachStaff(staffs[0]);   // a staff that is not wired is ignored
        Assert.Equal(0, link.StaffWireCount);
    }

    // A window whose last song has left and which has closed must not keep reacting to the shared playback: a play in another window
    // used to reach its empty tab list. The open window's band view plays, ticks and seeks through the same session.
    [AvaloniaFact]
    public void BandViewPlaybackSurvivesAnotherWindowThatClosedWithNoSongs()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var kept = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var main = new MainWindow(first: kept, playback: sessions);
        main.Show();
        var leaving = new MainWindow(first: new SongDocument(() => null) { Project = DemoSongFactory.Create() }, playback: sessions);
        leaving.Show();
        leaving.Songs.Detach(0);
        leaving.Close();

        var session = sessions.For(kept)!;
        var band = new BandView(kept);
        band.BindPlayback(session);
        Assert.InRange(band.ShownTrackIndices.Count, 2, BandView.DefaultShownCount);
        session.PlayOrPause();
        for (var frame = 0; frame < 3; frame++) session.PumpFrame();
        session.Seek(2, 0);
        session.PumpFrame();
        session.Stop();
        main.Close();
    }
}
