using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the gestures of the timeline bar highlight (spec barhighlight) with real pointer and key events: click, shift-click, a drag from a cell (white clips and one grey block over the cells it touched), the arrow keys, Esc, a click on empty
//   space, a right-click and its menu, the highlight kept per song across tab switches and never saved, each move as an undo step, and what a cell click leaves alone (selection, clips, track rows).
// Does not own: the model rules (TimelineBarHighlightModelTests), the edit commands (TimelineBarHighlightEditTests) or the drawing (TimelineBarHighlightDrawingTests).
// Tests: this class.
public sealed class TimelineBarHighlightInputTests
{
    private const int Bars = 12;

    // Rows: 0 and 1 guitars, 2 an audio track with a clip over bars 2-4, 3 a guitar.
    private static SongDocument NewSong()
    {
        var project = new SongProject();
        for (var t = 0; t < 2; t++) project.Tracks.Add(Guitar("Guitar " + (t + 1)));
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var span = BarRangeGaps.Span(project, 2, 4).GetValueOrDefault();
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From });
        project.Tracks.Add(audio);
        project.Tracks.Add(Guitar("Guitar 3"));
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    private static TrackModel Guitar(string name) => new()
    {
        Name = name,
        Measures = Enumerable.Range(0, Bars).Select(bar =>
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar });
            return measure;
        }).ToList(),
    };

    private sealed record Rig(Window Window, TimelineView View, TimelineInputController Input, SongDocument Doc)
    {
        public BarHighlight Highlight => Doc.Highlight;
        public Point Cell(int bar, int row) => View.TranslatePoint(new Point(View.BarX(bar) + 5, View.RowTop(row) + 10), Window) ?? default;
        public (int FirstTrack, int LastTrack, int FirstBar, int LastBar)? Block => Highlight.Has ? (Highlight.FirstTrack, Highlight.LastTrack, Highlight.FirstBar, Highlight.LastBar) : null;

        public Point ClipCentre()
        {
            var track = Doc.Project.Tracks[2];
            var box = TimelineAudioClips.ClipBounds(View, track, track.AudioClips[0]);
            return View.TranslatePoint(new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2), Window) ?? default;
        }

        public Point Ruler(int bar) => View.TranslatePoint(new Point(View.BarX(bar) + 5, TimelineView.RulerHeight / 2), Window) ?? default;

        public void Click(Point at, RawInputModifiers keys = RawInputModifiers.None, MouseButton button = MouseButton.Left)
        {
            Window.MouseDown(at, button, keys);
            Window.MouseUp(at, button, keys);
        }

        public void Press(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = View });
    }

    private static Rig Open(SongDocument? doc = null)
    {
        doc ??= NewSong();
        var view = new TimelineView(doc.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, doc);
        var window = new Window { Width = 1600, Height = 1100, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return new Rig(window, view, input, doc);
    }

    [AvaloniaFact]
    public void AClickHighlightsTheCellMovesThePlayheadAndAsksTheScoreToFollowButSelectsNothing()
    {
        var rig = Open();
        var seeks = new List<int>();
        var cells = new List<(int, int)>();
        rig.Input.BarSeekRequested += seeks.Add;
        rig.Input.CellClicked += (row, bar) => cells.Add((row, bar));

        rig.Click(rig.Cell(3, 1));
        rig.Window.Close();

        Assert.Equal((1, 1, 3, 3), rig.Block);
        Assert.Equal(new[] { 3 }, seeks);   // the playhead moves as before
        Assert.Equal(new[] { (1, 3) }, cells);   // the host switches the active track and scrolls the score to that bar
        Assert.False(rig.Doc.Selection.HasRange);
        Assert.Equal(FocusTarget.BarHighlight, rig.Doc.Selection.Focus);
    }

    [AvaloniaFact]
    public void AClickOnACellLeavesTheRangeSelectionAndTheSelectedRowsAsTheyWere()
    {
        var rig = Open();
        rig.Doc.Selection.SetRange(0, 5, 7, SelectionOrigin.Timeline);
        rig.Doc.Selection.SetRows(new[] { rig.Doc.Project.Tracks[0].Id });

        rig.Click(rig.Cell(3, 1));
        rig.Window.Close();

        Assert.Equal((SelectionScope.AllTracks, 5, 7), (rig.Doc.Selection.Scope, rig.Doc.Selection.StartBar, rig.Doc.Selection.EndBar));
        Assert.Single(rig.Doc.Selection.Rows);
        Assert.Equal((1, 3), (rig.Highlight.FirstTrack, rig.Highlight.FirstBar));
    }

    [AvaloniaFact]
    public void ACellClickReachesTheViewsAsOneSelectionChange()
    {
        var rig = Open();
        var at = rig.Cell(3, 1);

        rig.Window.MouseDown(at, MouseButton.Left);
        var before = rig.Doc.Selection.ChangeCount;
        rig.Window.MouseUp(at, MouseButton.Left);
        rig.Window.Close();

        Assert.Equal(1, rig.Doc.Selection.ChangeCount - before);
    }

    [AvaloniaFact]
    public void CtrlClickTogglesSingleCellsInAndOutOfTheHighlightAsOneUndoStepEach()
    {
        var rig = Open();
        rig.Click(rig.Cell(3, 1));
        var steps = rig.Doc.Undo.UndoCount;

        rig.Click(rig.Cell(7, 1), RawInputModifiers.Control);
        Assert.True(rig.Highlight.Contains(1, 3) && rig.Highlight.Contains(1, 7) && !rig.Highlight.Contains(1, 5));
        Assert.Equal(steps + 1, rig.Doc.Undo.UndoCount);

        rig.Click(rig.Cell(3, 1), RawInputModifiers.Control);
        Assert.True(!rig.Highlight.Contains(1, 3) && rig.Highlight.Contains(1, 7));
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void CtrlClickOnACellMovesNeitherThePlayheadNorTheScoreCursor()
    {
        var rig = Open();
        var seeks = new List<int>();
        var cells = new List<(int, int)>();
        rig.Click(rig.Cell(3, 1));
        rig.Input.BarSeekRequested += seeks.Add;
        rig.Input.CellClicked += (row, bar) => cells.Add((row, bar));

        rig.Click(rig.Cell(7, 1), RawInputModifiers.Control);
        rig.Window.Close();

        Assert.Empty(seeks);
        Assert.Empty(cells);
    }

    [AvaloniaFact]
    public void ShiftClickOnTheSameTrackExtendsAlongItAndOnAnotherTabTrackMakesTheRectangle()
    {
        var rig = Open();
        rig.Click(rig.Cell(3, 1));

        rig.Click(rig.Cell(6, 1), RawInputModifiers.Shift);
        Assert.Equal((1, 1, 3, 6), rig.Block);

        rig.Click(rig.Cell(8, 3), RawInputModifiers.Shift);
        Assert.Equal((1, 3, 3, 8), rig.Block);   // the anchor is still the first click
        rig.Click(rig.Cell(1, 0), RawInputModifiers.Shift);
        Assert.Equal((0, 1, 1, 3), rig.Block);
        rig.Window.Close();

        Assert.False(rig.Doc.Selection.HasRange);
    }

    [AvaloniaFact]
    public void ABoxDragFromACellMakesNoRangeNoLoopAndGreyBlocksTheCellsItTouches()
    {
        var rig = Open();
        rig.Doc.Selection.SetRows(new[] { rig.Doc.Project.Tracks[0].Id });

        rig.Window.MouseDown(rig.Cell(3, 1), MouseButton.Left);
        rig.Window.MouseMove(rig.Cell(5, 1));
        rig.Window.MouseMove(rig.Cell(6, 1));
        rig.Window.MouseUp(rig.Cell(6, 1), MouseButton.Left);
        rig.Window.Close();

        Assert.Equal((false, false), (rig.Doc.Selection.HasRange, rig.Doc.Selection.HasLoop));
        Assert.Equal((1, 1, 3, 6), rig.Block);   // the release highlights the tab cells the box touched, like Shift+click
        Assert.Empty(rig.Doc.Selection.Rows);
    }

    [AvaloniaFact]
    public void ARulerDragMakesTheLoopAndLeavesTheHighlightAlone()
    {
        var rig = Open();
        rig.Click(rig.Cell(1, 0));

        rig.Window.MouseDown(rig.Ruler(4), MouseButton.Left);
        rig.Window.MouseMove(rig.Ruler(6));
        rig.Window.MouseUp(rig.Ruler(6), MouseButton.Left);
        rig.Window.Close();

        Assert.Equal((4, 6), rig.Doc.Selection.LoopBars);
        Assert.Equal((0, 0, 1, 1), rig.Block);
    }

    [AvaloniaFact]
    public void ArrowsMoveTheHighlightAndShiftArrowsExtendItWhileTheLastClickWasOnIt()
    {
        var rig = Open();
        rig.Click(rig.Cell(3, 0));

        rig.Press(Key.Right);
        Assert.Equal((0, 0, 4, 4), rig.Block);
        rig.Press(Key.Down);
        Assert.Equal((1, 1, 4, 4), rig.Block);
        rig.Press(Key.Down);
        Assert.Equal((3, 3, 4, 4), rig.Block);   // the audio track has no cells: it is skipped
        rig.Press(Key.Left, KeyModifiers.Shift);
        rig.Press(Key.Left, KeyModifiers.Shift);
        Assert.Equal((3, 3, 2, 4), rig.Block);
        rig.Press(Key.Up, KeyModifiers.Shift);
        Assert.Equal((1, 3, 2, 4), rig.Block);
        rig.Press(Key.Left);
        Assert.Equal((1, 1, 1, 1), rig.Block);   // a plain arrow collapses to the one cell at the moved corner
        rig.Window.Close();

        Assert.False(rig.Doc.Selection.HasRange);
    }

    [AvaloniaFact]
    public void ArrowsDoNothingToTheHighlightWhenTheLastClickWasElsewhere()
    {
        var rig = Open();
        rig.Click(rig.Cell(3, 0));
        rig.Doc.Selection.SetFocus(FocusTarget.Clips);

        rig.Press(Key.Right);
        rig.Window.Close();

        Assert.Equal((0, 0, 3, 3), rig.Block);
    }

    [AvaloniaFact]
    public void EscClearsTheHighlightAndTheEditKeysLoseTheirFocus()
    {
        var rig = Open();
        rig.Doc.Selection.SetRange(0, 5, 7, SelectionOrigin.Timeline);
        rig.Click(rig.Cell(3, 0));

        rig.Press(Key.Escape);
        rig.Window.Close();

        Assert.False(rig.Highlight.Has);
        Assert.Equal(FocusTarget.None, rig.Doc.Selection.Focus);
        Assert.Equal((5, 7), rig.Doc.Selection.BarRange);   // Esc on the highlight does not touch the selection
    }

    [AvaloniaFact]
    public void AClickOnEmptyTimelineSpaceOrTheRulerClearsTheHighlightButAClipClickDoesNot()
    {
        var rig = Open();
        rig.Click(rig.Cell(1, 0));
        rig.Click(rig.ClipCentre());
        Assert.True(rig.Highlight.Has);   // a clip is not empty space
        rig.Click(rig.Ruler(4));
        Assert.True(rig.Highlight.Has);   // 25 Oct: a ruler click clears only the loop; the white stays

        rig.Click(rig.Cell(1, 0));
        rig.Click(rig.Cell(10, 2));   // the audio lane away from its clip
        rig.Window.Close();

        Assert.False(rig.Highlight.Has);
    }

    [AvaloniaFact]
    public void AClickOnAClipSelectsItMovesThePlayheadToTheClickedBarAndLeavesTheHighlight()
    {
        var rig = Open();
        rig.Click(rig.Cell(1, 0));
        var seeks = new List<int>();
        rig.Input.BarSeekRequested += seeks.Add;

        var centre = rig.ClipCentre();
        var clickedBar = rig.View.BarAtX(rig.Window.TranslatePoint(centre, rig.View)!.Value.X);   // 0.8.0: the bar under the pointer, not the clip start
        rig.Click(centre);
        rig.Window.Close();

        Assert.Equal(rig.Doc.Project.Tracks[2].AudioClips[0], rig.View.SelectedClip!.Clip);
        Assert.Equal(FocusTarget.Clips, rig.Doc.Selection.Focus);
        Assert.Equal(new[] { clickedBar }, seeks);
        Assert.Equal((0, 0, 1, 1), rig.Block);
    }

    [AvaloniaFact]
    public void TheHighlightIsKeptPerSongWhenAnotherTabShowsAndIsNeverSavedInTheFile()
    {
        var first = NewSong();
        var second = NewSong();
        var rig = Open(first);
        var hashBefore = ProjectService.ContentHash(first.Project);

        rig.Click(rig.Cell(3, 1));
        rig.Input.Show(second);
        Assert.False(rig.View.Highlight!.Has);   // the other song has its own
        rig.Click(rig.Cell(5, 0));
        rig.Input.Show(first);

        Assert.Same(first.Highlight, rig.View.Highlight);
        Assert.Equal((1, 1, 3, 3), (first.Highlight.FirstTrack, first.Highlight.LastTrack, first.Highlight.FirstBar, first.Highlight.LastBar));
        Assert.Equal((0, 5), (second.Highlight.FirstTrack, second.Highlight.FirstBar));
        Assert.Equal(hashBefore, ProjectService.ContentHash(first.Project));   // not part of the file
        Assert.False(first.IsDirty);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AScoreSelectionOrAShowOfTheSameSongDoesNotClearTheHighlight()
    {
        var rig = Open();
        rig.Click(rig.Cell(3, 1));

        rig.Doc.Selection.SetRange(1, 2, 3, SelectionOrigin.Editor);   // a score selection
        rig.Doc.Selection.SetFocus(FocusTarget.ScoreSelection);
        rig.Input.Show(rig.Doc);
        rig.Window.Close();

        Assert.Equal((1, 1, 3, 3), rig.Block);
    }

    [AvaloniaFact]
    public void EachHighlightMoveIsOneUndoStepAndUndoRedoShowThemAgain()
    {
        var rig = Open();
        rig.Click(rig.Cell(2, 0));
        rig.Click(rig.Cell(4, 1));
        rig.Press(Key.Right);

        Assert.Equal(3, rig.Doc.Undo.UndoCount);
        Assert.False(rig.Doc.IsDirty);
        DocumentEdits.Undo(rig.Doc);
        Assert.Equal((1, 1, 4, 4), rig.Block);
        DocumentEdits.Undo(rig.Doc);
        Assert.Equal((0, 0, 2, 2), rig.Block);
        DocumentEdits.Undo(rig.Doc);
        Assert.Null(rig.Block);
        DocumentEdits.Redo(rig.Doc);
        DocumentEdits.Redo(rig.Doc);
        DocumentEdits.Redo(rig.Doc);
        Assert.Equal((1, 1, 5, 5), rig.Block);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ClickingTheSameCellAgainAddsNoUndoStep()
    {
        var rig = Open();
        rig.Click(rig.Cell(2, 0));
        rig.Click(rig.Cell(2, 0));
        rig.Window.Close();

        Assert.Equal(1, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void ARightClickHighlightsTheCellAndOpensTheHighlightMenu()
    {
        var rig = Open();
        var at = rig.Cell(4, 1);

        rig.Window.MouseDown(at, MouseButton.Right);
        rig.Window.MouseUp(at, MouseButton.Right);
        Assert.Equal((1, 1, 4, 4), rig.Block);
        Assert.Equal(FocusTarget.BarHighlight, rig.Doc.Selection.Focus);
        var inView = new Point(rig.View.BarX(4) + 5, rig.View.RowTop(1) + 10);
        var rows = ContextMenuRows.TimelineAt(rig.View, inView).Where(r => r.Command != TimelineCommand.None).ToList();
        rig.Window.Close();

        Assert.Equal(new[] { "Copy", "Cut", "Clear", "Paste", "Loop these bars" }, rows.Select(r => r.Header).ToArray());
        Assert.Equal(new[] { "Highlight.Copy", "Highlight.Cut", "Highlight.Delete", "Highlight.Paste", "Highlight.SelectBars" }, rows.Select(ContextMenuRows.CommandIdOf).ToArray());
    }

    [AvaloniaFact]
    public void ARightClickInsideTheBlockKeepsTheBlockAndOutsideItMovesTheHighlightToTheCell()
    {
        var rig = Open();
        rig.Click(rig.Cell(4, 1));
        rig.Click(rig.Cell(6, 1), RawInputModifiers.Shift);

        rig.Window.MouseDown(rig.Cell(5, 1), MouseButton.Right);
        Assert.Equal((1, 1, 4, 6), rig.Block);
        rig.Window.MouseUp(rig.Cell(5, 1), MouseButton.Right);
        rig.Window.MouseDown(rig.Cell(9, 0), MouseButton.Right);
        rig.Window.Close();

        Assert.Equal((0, 0, 9, 9), rig.Block);
    }

    [AvaloniaFact]
    public void ARightClickOnAClipDoesNotHighlightAndTheLoopKeepsItsOwnMenuOffTheHighlight()
    {
        var rig = Open();
        rig.Doc.Selection.SetLoop(6, 8);

        rig.Window.MouseDown(rig.ClipCentre(), MouseButton.Right);
        Assert.False(rig.Highlight.Has);
        var onSelection = ContextMenuRows.TimelineAt(rig.View, new Point(rig.View.BarX(7) + 5, rig.View.RowTop(2) + 10)).ToList();
        rig.Window.Close();

        Assert.Contains(onSelection, r => r.Command == TimelineCommand.CopySelection);
    }
}
