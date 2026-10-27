using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the keys and menus on the timeline bar highlight in a real window (spec barhighlight): Copy takes exactly the highlighted cells, Delete and Cut ask (and "Don't ask again" stops it), Paste overwrites
//   from the top-left highlighted cell and adds bars past the end, the three edit target settings decide between the highlight and a selection, a cell click leaves the loop and the score selection alone,
//   and the status bar names the highlighted cell.
// Does not own: the model rules (TimelineBarHighlightModelTests) or the gestures (TimelineBarHighlightInputTests).
// Tests: this class.
public sealed class TimelineBarHighlightEditTests
{
    private const int Bars = 12;

    private sealed class Rig : IDisposable
    {
        public required SongDocument Song { get; init; }
        public required MainWindow Window { get; init; }
        public required TimelineView Timeline { get; init; }
        public required MainWindowSettings Settings { get; init; }
        public required string Folder { get; init; }
        public required ScorePreviewView Score { get; init; }
        public SelectionModel Model => Song.Selection;
        public BarHighlight Highlight => Song.Highlight;
        public ClipboardService Clipboard => Timeline.ContextMenus.Clipboard!;

        public void Dispose()
        {
            Song.MarkClean();   // closing a dirty window asks to save
            Window.Close();
            Settings.Dispose();
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }
    }

    // Tracks: 0 and 1 guitars, 2 an audio track with a clip over bars 2-4, 3 a guitar; the fret of a guitar bar is its index.
    private static SongDocument NewSong()
    {
        var project = new SongProject();
        TrackModel Guitar(string name) => new()
        {
            Name = name,
            Measures = Enumerable.Range(0, Bars).Select(bar =>
            {
                var measure = new MeasureModel { Number = bar + 1 };
                measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar });
                return measure;
            }).ToList(),
        };
        project.Tracks.Add(Guitar("Guitar 1"));
        project.Tracks.Add(Guitar("Guitar 2"));
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var span = BarRangeGaps.Span(project, 2, 4).GetValueOrDefault();
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From });
        project.Tracks.Add(audio);
        project.Tracks.Add(Guitar("Guitar 3"));
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    private static Rig Open(string? editTarget = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-highlight-" + Guid.NewGuid().ToString("N"));
        var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
        if (editTarget is not null) settings.Settings.Editing.EditTarget = editTarget;
        var song = NewSong();
        var window = new MainWindow(settings, first: song) { Width = 1800, Height = 1000 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        return new Rig
        {
            Song = song, Window = window, Timeline = arrangement.Timeline, Settings = settings, Folder = folder,
            Score = window.GetLogicalDescendants().OfType<ScorePreviewView>().First(),
        };
    }

    private static Point CellPoint(Rig rig, int bar, int row) => rig.Timeline.TranslatePoint(new Point(rig.Timeline.BarX(bar) + 5, rig.Timeline.RowTop(row) + 10), rig.Window) ?? default;

    private static void Click(Rig rig, int bar, int row, RawInputModifiers keys = RawInputModifiers.None)
    {
        var at = CellPoint(rig, bar, row);
        rig.Window.MouseDown(at, MouseButton.Left, keys);
        rig.Window.MouseUp(at, MouseButton.Left, keys);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Rig rig, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        rig.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = rig.Window });
        Dispatcher.UIThread.RunJobs();
    }

    private static PromptWindow? Prompt(Rig rig)
    {
        Dispatcher.UIThread.RunJobs();
        return rig.Window.OwnedWindows.OfType<PromptWindow>().SingleOrDefault();
    }

    private static void Answer(PromptWindow prompt, string button, bool dontAskAgain = false)
    {
        if (dontAskAgain) prompt.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        prompt.GetLogicalDescendants().OfType<Button>().Single(candidate => (candidate.Content as string) == button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> done)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.True(done());
    }

    private static int? Fret(SongDocument song, int track, int bar) =>
        song.Project.Tracks[track].Measures[bar].Cells[0].Notes.Select(n => (int?)n.Fret).FirstOrDefault();

    // Highlights tracks 0-1 x bars 2-3 with a click and a shift-click.
    private static void HighlightBlock(Rig rig)
    {
        Click(rig, 2, 0);
        Click(rig, 3, 1, RawInputModifiers.Shift);
        Assert.Equal((0, 1, 2, 3), (rig.Highlight.FirstTrack, rig.Highlight.LastTrack, rig.Highlight.FirstBar, rig.Highlight.LastBar));
    }

    [AvaloniaFact]
    public void CtrlCCopiesExactlyTheHighlightedCellsAndNeverAsks()
    {
        using var rig = Open();
        HighlightBlock(rig);

        Press(rig, Key.C, KeyModifiers.Control);

        Assert.Null(Prompt(rig));
        var copied = rig.Clipboard.TryGetRange()!;
        Assert.Equal(new[] { rig.Song.Project.Tracks[0].Id, rig.Song.Project.Tracks[1].Id }, copied.Tracks.Select(t => t.SourceTrackId).ToArray());
        Assert.Equal((2, 2), (copied.FirstBar, copied.BarCount));
        Assert.All(copied.Tracks, t => Assert.Empty(t.Clips));
        Assert.Equal(Fret(rig.Song, 0, 2), copied.Tracks[0].Bars[0].Cells[0].Notes[0].Fret);
    }

    [AvaloniaFact]
    public void DeleteAsksTheQuestionCancelKeepsTheNotesAndClearKeepsTheBarsInOneUndoStep()
    {
        using var rig = Open();
        HighlightBlock(rig);
        var steps = rig.Song.Undo.UndoCount;

        Press(rig, Key.Delete);
        var prompt = Prompt(rig)!;
        Assert.Equal("Clear the notes in 4 highlighted bars? The bars stay.", prompt.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "PromptMessage").Text);
        Answer(prompt, "Cancel");
        Assert.Equal(2, Fret(rig.Song, 0, 2));
        Assert.Equal(steps, rig.Song.Undo.UndoCount);

        Press(rig, Key.Delete);
        Answer(Prompt(rig)!, "Clear");

        WaitUntil(() => Fret(rig.Song, 0, 2) is null);
        Assert.All(new[] { (0, 2), (0, 3), (1, 2), (1, 3) }, cell => Assert.Null(Fret(rig.Song, cell.Item1, cell.Item2)));
        Assert.Equal(Bars, rig.Song.Project.Tracks[0].Measures.Count);
        Assert.Equal(4, Fret(rig.Song, 0, 4));   // the neighbours stay
        Assert.Equal(1, rig.Song.Undo.UndoCount - steps);
        DocumentEdits.Undo(rig.Song);
        Assert.Equal(2, Fret(rig.Song, 0, 2));
        Assert.Equal(3, Fret(rig.Song, 1, 3));
        Assert.Equal((0, 1, 2, 3), (rig.Highlight.FirstTrack, rig.Highlight.LastTrack, rig.Highlight.FirstBar, rig.Highlight.LastBar));
    }

    [AvaloniaFact]
    public void CutAsksThenCopiesAndClearsAndCancelLeavesTheClipboardAlone()
    {
        using var rig = Open();
        HighlightBlock(rig);

        Press(rig, Key.X, KeyModifiers.Control);
        Answer(Prompt(rig)!, "Cancel");
        Assert.Null(rig.Clipboard.TryGetRange());
        Assert.Equal(2, Fret(rig.Song, 0, 2));

        Press(rig, Key.X, KeyModifiers.Control);
        Answer(Prompt(rig)!, "Clear");

        WaitUntil(() => Fret(rig.Song, 0, 2) is null);
        Assert.Equal(2, rig.Clipboard.TryGetRange()!.Tracks.Count);
        Assert.Equal(2, rig.Clipboard.TryGetRange()!.Tracks[0].Bars[0].Cells[0].Notes[0].Fret);
    }

    [AvaloniaFact]
    public void CutWithACtrlAddedCellOutsideTheRectangleDoesNothing()
    {
        using var rig = Open();
        HighlightBlock(rig);
        Click(rig, 6, 0, RawInputModifiers.Control);
        var kept = Fret(rig.Song, 0, 6);
        Assert.True(rig.Highlight.Contains(0, 6));

        var before = Fret(rig.Song, 0, 2);
        Press(rig, Key.X, KeyModifiers.Control);

        Assert.Null(Prompt(rig));   // a non-rectangular selection: no partial Cut
        Assert.Equal(before, Fret(rig.Song, 0, 2));
        Assert.Equal(kept, Fret(rig.Song, 0, 6));
    }

    [AvaloniaFact]
    public void DontAskAgainClearsDirectlyFromThenOnAndThePreferencesRowTurnsTheQuestionBackOn()
    {
        using var rig = Open();
        HighlightBlock(rig);

        Press(rig, Key.Delete);
        Answer(Prompt(rig)!, "Clear", dontAskAgain: true);
        WaitUntil(() => Fret(rig.Song, 0, 2) is null);
        Assert.False(rig.Settings.Settings.Editing.ConfirmHighlightClear);

        Click(rig, 6, 1);
        Press(rig, Key.Delete);
        WaitUntil(() => Fret(rig.Song, 1, 6) is null);
        Assert.Null(Prompt(rig));

        SettingsCatalog.Build(rig.Settings.Settings).Single(d => d.Key == "editing.confirmhighlightclear").Set(true);
        Click(rig, 7, 1);
        Press(rig, Key.Delete);
        Assert.NotNull(Prompt(rig));
        Answer(Prompt(rig)!, "Cancel");
    }

    [AvaloniaFact]
    public void PasteOverwritesFromTheTopLeftHighlightedCellWithTheFirstCopiedTrackOnTheHighlightedTrackAsOneUndoStep()
    {
        using var rig = Open();
        HighlightBlock(rig);
        Press(rig, Key.C, KeyModifiers.Control);
        Click(rig, 5, 1);
        var steps = rig.Song.Undo.UndoCount;

        Press(rig, Key.V, KeyModifiers.Control);

        WaitUntil(() => Fret(rig.Song, 1, 5) == 2);
        Assert.Equal(new int?[] { 4, 2, 3, 7 }, new[] { Fret(rig.Song, 1, 4), Fret(rig.Song, 1, 5), Fret(rig.Song, 1, 6), Fret(rig.Song, 1, 7) });   // the first copied track (track 0) landed on track 1, overwriting bars 5-6
        Assert.Equal(5, Fret(rig.Song, 0, 5));   // the second copied track would be the audio track below: skipped, nothing else moved
        Assert.Equal(Bars, rig.Song.Project.Tracks[0].Measures.Count);
        Assert.Equal(1, rig.Song.Undo.UndoCount - steps);
        DocumentEdits.Undo(rig.Song);
        Assert.Equal(5, Fret(rig.Song, 1, 5));
        Assert.Equal(6, Fret(rig.Song, 1, 6));
    }

    [AvaloniaFact]
    public void PasteBeyondTheLastBarAddsBarsWithTheAutoBarsHelperAndUndoTakesThemAway()
    {
        using var rig = Open();
        HighlightBlock(rig);
        Press(rig, Key.C, KeyModifiers.Control);
        Click(rig, 7, 0);
        rig.Highlight.Set(0, Bars - 1);   // the last bar (the timeline shows it; the cell is set directly to stay inside the window)

        Press(rig, Key.V, KeyModifiers.Control);

        WaitUntil(() => rig.Song.Project.Tracks[0].Measures.Count == Bars + 1);
        Assert.Equal(Bars + 1, rig.Song.Project.Tracks[1].Measures.Count);
        Assert.Equal((2, 3), (Fret(rig.Song, 0, Bars - 1), Fret(rig.Song, 0, Bars)));
        Assert.Equal((0, 1, Bars - 1, Bars), (rig.Highlight.FirstTrack, rig.Highlight.LastTrack, rig.Highlight.FirstBar, rig.Highlight.LastBar));   // the highlight covers what was pasted
        DocumentEdits.Undo(rig.Song);
        Assert.Equal(Bars, rig.Song.Project.Tracks[0].Measures.Count);
    }

    // A loop made by dragging on the ruler (focus Loop), over bars 5-7 on every track.
    private static void DragRange(Rig rig)
    {
        Point Ruler(int bar) => rig.Timeline.TranslatePoint(new Point(rig.Timeline.BarX(bar) + 5, TimelineView.RulerHeight / 2), rig.Window) ?? default;
        rig.Window.MouseDown(Ruler(5), MouseButton.Left);
        rig.Window.MouseMove(Ruler(7));
        rig.Window.MouseUp(Ruler(7), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("Last clicked", true, 1)]
    [InlineData("Last clicked", false, 4)]
    [InlineData("Highlight always wins", true, 1)]
    [InlineData("Highlight always wins", false, 1)]
    [InlineData("Selection always wins", true, 4)]
    [InlineData("Selection always wins", false, 4)]
    public void CopyActsOnTheHighlightOrTheRangeSelectionAsTheEditTargetSettingSays(string setting, bool highlightClickedLast, int expectedTracks)
    {
        using var rig = Open(setting);
        if (highlightClickedLast) { DragRange(rig); Click(rig, 2, 0); }
        else { Click(rig, 2, 0); DragRange(rig); }

        Press(rig, Key.C, KeyModifiers.Control);

        // 25 Oct: the loop (what DragRange makes) is never a bar-command target, so only the highlight copies; the loop-only cases copy nothing.
        if (expectedTracks == 4) Assert.Null(rig.Clipboard.TryGetRange());
        else Assert.Equal(expectedTracks, rig.Clipboard.TryGetRange()!.Tracks.Count);   // the highlight copies its one tab cell, the range copies every track
    }

    [AvaloniaFact]
    public void ACellClickLeavesTheLoopAreaAndTheScoreSelectionOnTheSameTrackAsTheyWere()
    {
        using var rig = Open();
        DragRange(rig);
        var loop = rig.Model.LoopSnapshot();

        Click(rig, 2, 1);

        Assert.Equal((5, 7), rig.Model.LoopBars);
        Assert.Equal(loop, rig.Model.LoopSnapshot());
        rig.Model.Clear(SelectionOrigin.Command);
        Click(rig, 2, 0);
        rig.Score.Editor.SelectRange(6, 0, 7, -1);   // a score selection on the shown track 0
        Assert.True(rig.Model.HasScoreSelection);

        Click(rig, 9, 0);

        Assert.False(rig.Model.HasScoreSelection);   // 25 Oct: a new timeline selection deselects the score notes
        Assert.Equal((0, 0, 9, 9), (rig.Highlight.FirstTrack, rig.Highlight.LastTrack, rig.Highlight.FirstBar, rig.Highlight.LastBar));
    }

    [AvaloniaFact]
    public void TheStatusBarNamesTheMeasureTheTrackAndTheCellOfTheHighlight()
    {
        using var rig = Open();

        Click(rig, 4, 1);

        var position = rig.Window.GetVisualDescendants().OfType<StatusBarView>().Single().GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusPosition");
        Assert.StartsWith("Bar 5  ·  ", position.Text);
        Assert.EndsWith("  ·  Guitar 2  ·  cell 1", position.Text);
    }
}
