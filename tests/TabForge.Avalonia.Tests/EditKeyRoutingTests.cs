using System;
using System.Threading;
using TabForge.Avalonia.Shell;
using System.Linq;
using System.Text.Json;
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

// Owns: the end-to-end routing of Ctrl+X/Delete and the Edit menu, the palette and the track row menu by the focus target in a real window: a header press asks
//   before a track goes (Cancel keeps the song and the clipboard, OK is one undo step), a ruler or lane press moves the focus off the track row, a score press
//   takes the focus from a track row, and the last track stays.
// Does not own: the routing table (EditRoutingTests), the Delete clip / range commands (TimelineDeleteRoutingTests, MainWindowClipKeysTests).
// Tests: this class.
public sealed class EditKeyRoutingTests
{
    private sealed class Rig
    {
        public required SongDocument Song { get; init; }
        public required MainWindow Window { get; init; }
        public required TimelineView Timeline { get; init; }
        public required TrackHeaderColumn Header { get; init; }
        public required ScorePreviewView Score { get; init; }
        public required SelectionModel Model { get; init; }
        public MainWindowCommands Commands => (MainWindowCommands)Timeline.ContextMenus.Commands;
        public ClipboardService Clipboard => Timeline.ContextMenus.Clipboard!;

        public void Close()
        {
            Song.MarkClean();   // closing a dirty window asks to save
            Window.Close();
        }
    }

    private static Rig Open(int tracks = 3, TabWindowRegistry? windows = null)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < tracks) MainWindowTrackMenu.AddTrack(song, TrackKind.Audio);
        if (song.Project.Tracks.Count > tracks) song.Project.Tracks.RemoveRange(tracks, song.Project.Tracks.Count - tracks);
        song.MarkClean();
        var window = new MainWindow(windows: windows, first: song);
        window.Show();
        window.UpdateLayout();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        return new Rig
        {
            Song = song,
            Window = window,
            Timeline = arrangement.Timeline,
            Header = window.GetVisualDescendants().OfType<TrackHeaderColumn>().First(),
            Score = window.GetLogicalDescendants().OfType<ScorePreviewView>().First(),
            Model = arrangement.Input.Selection,
        };
    }

    private static void Click(Rig rig, Visual on, Point local)
    {
        var at = on.TranslatePoint(local, rig.Window) ?? local;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickHeader(Rig rig, int row)
    {
        var name = TrackRowCells.Column("name", rig.Header.NameColumnWidth);
        Click(rig, rig.Header, new Point(name.Left + name.Width / 2, rig.Timeline.RowTop(row) + 10));
    }

    private static void ClickLane(Rig rig, int row, int bar) => Click(rig, rig.Timeline, new Point(rig.Timeline.BarX(bar) + 5, rig.Timeline.RowTop(row) + 10));

    private static void ClickRuler(Rig rig, int bar) => Click(rig, rig.Timeline, new Point(rig.Timeline.BarX(bar) + 5, TimelineView.RulerHeight / 2));

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

    private static void Answer(PromptWindow prompt, string button)
    {
        prompt.GetLogicalDescendants().OfType<Button>().Single(candidate => (candidate.Content as string) == button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> done)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.True(done());
    }

    private static void MarkBar(SongDocument song, int track, int bar, int fret)
    {
        var notes = song.Project.Tracks[track].Measures[bar].Cells[0].Notes;
        notes.Clear();
        notes.Add(new TabNote { StringIndex = 0, Fret = fret });
    }

    private static bool HasFret(SongDocument song, int track, int bar, int fret) =>
        song.Project.Tracks[track].Measures[bar].Cells[0].Notes.Any(n => n.Fret == fret);

    [AvaloniaFact]
    public void ARangeCopiedInWindowAPastesInWindowB()
    {
        var a = Open();
        MarkBar(a.Song, 0, 2, 17);
        a.Clipboard.CopyRange(RangeCapture.Capture(a.Song.Project, 2, 2));
        var b = Open(windows: a.Window.Windows);
        Assert.Same(a.Clipboard, b.Clipboard);   // one clipboard for the whole app
        Assert.False(HasFret(b.Song, 0, 0, 17));

        b.Commands.Run(EditRouting.RangePasteId);   // bar commands never use the loop: lands at the playhead bar (0)
        WaitUntil(() => HasFret(b.Song, 0, 0, 17));
        a.Close();
        b.Close();
    }

    [AvaloniaFact]
    public void HeaderFocusedCtrlVPastesACopiedRangeAtThatTrackAndThePlayheadBar()
    {
        var rig = Open();
        MarkBar(rig.Song, 0, 2, 17);
        rig.Clipboard.CopyRange(RangeCapture.Capture(rig.Song.Project, 2, 2));
        Assert.False(HasFret(rig.Song, 1, 0, 17));
        ClickHeader(rig, 1);
        Assert.Equal(FocusTarget.TrackRow, rig.Model.Focus);
        Press(rig, Key.V, KeyModifiers.Control);

        WaitUntil(() => HasFret(rig.Song, 1, 0, 17));   // the range's first track landed on the focused header's track, at the playhead bar (0)
        Assert.Equal(3, rig.Song.Project.Tracks.Count);   // no track was pasted
        rig.Close();
    }

    [AvaloniaFact]
    public void HeaderFocusedCtrlVWithACopiedTrackStillPastesTheTrack()
    {
        var rig = Open();
        rig.Clipboard.CopyRange(RangeCapture.Capture(rig.Song.Project, 2, 2));
        rig.Clipboard.CopyTrack(rig.Song.Project.Tracks[0]);   // the track is the newest copy
        ClickHeader(rig, 1);
        Press(rig, Key.V, KeyModifiers.Control);

        WaitUntil(() => rig.Song.Project.Tracks.Count == 4);
        rig.Close();
    }

    [AvaloniaFact]
    public void HeaderClickThenCtrlXAsksDeleteTrack()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        ClickHeader(rig, 1);
        Press(rig, Key.X, KeyModifiers.Control);

        var prompt = Prompt(rig);
        Assert.NotNull(prompt);
        Assert.Equal(before, rig.Song.Project.Tracks.Count);   // nothing goes until OK
        Answer(prompt!, "Cancel");
        rig.Close();
    }

    [AvaloniaFact]
    public void CancelKeepsTrackAndClipboardUntouched()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        ClickHeader(rig, 1);
        Press(rig, Key.X, KeyModifiers.Control);
        Answer(Prompt(rig)!, "Cancel");

        Assert.Equal(before, rig.Song.Project.Tracks.Count);
        Assert.False(rig.Clipboard.HasTrack);
        Assert.Null(DocumentEdits.Undo(rig.Song));   // no edit was recorded
        rig.Close();
    }

    [AvaloniaFact]
    public void OkRemovesTrackAsOneUndoStep()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        var name = rig.Song.Project.Tracks[1].Name;
        ClickHeader(rig, 1);
        Press(rig, Key.X, KeyModifiers.Control);
        Answer(Prompt(rig)!, "Delete");

        Assert.Equal(before - 1, rig.Song.Project.Tracks.Count);
        Assert.True(rig.Clipboard.HasTrack);   // the clipboard is touched only after OK
        Assert.NotNull(DocumentEdits.Undo(rig.Song));
        Assert.Equal(before, rig.Song.Project.Tracks.Count);
        Assert.Equal(name, rig.Song.Project.Tracks[1].Name);
        rig.Close();
    }

    [AvaloniaFact]
    public void ScoreSelectionThenHeaderClickClearsSelection()
    {
        var rig = Open();
        rig.Model.SetRange(0, 1, 2, SelectionOrigin.Editor);   // a score selection on the track the header click will hit
        Assert.True(rig.Model.HasRange);
        ClickHeader(rig, 0);

        Assert.False(rig.Model.HasRange);
        Assert.Equal(FocusTarget.TrackRow, rig.Model.Focus);
        Press(rig, Key.X, KeyModifiers.Control);
        var prompt = Prompt(rig);
        Assert.NotNull(prompt);   // the header click made Ctrl+X a track question, never a note cut
        Answer(prompt!, "Cancel");
        rig.Close();
    }

    [AvaloniaFact]
    public void CtrlXAfterEmptyLaneClickNeverCutsTrackZero()
    {
        var rig = Open();
        ClickHeader(rig, 1);
        var trackZero = JsonSerializer.Serialize(rig.Song.Project.Tracks[0]);
        var before = rig.Song.Project.Tracks.Count;
        ClickLane(rig, 1, 2);
        Press(rig, Key.X, KeyModifiers.Control);

        // Spec barhighlight: a click on a tab cell puts the edit keys on the grey highlight, so Ctrl+X asks about the highlighted bar (never about a track).
        Assert.Equal(FocusTarget.BarHighlight, rig.Model.Focus);
        var prompt = Prompt(rig);
        Assert.Equal("Clear highlighted bars", prompt!.Title);
        Answer(prompt, "Cancel");
        Assert.Equal(before, rig.Song.Project.Tracks.Count);
        Assert.Equal(trackZero, JsonSerializer.Serialize(rig.Song.Project.Tracks[0]));
        rig.Close();
    }

    [AvaloniaFact]
    public void RulerClickAfterHeaderClickClearsTrackRowFocus()
    {
        var rig = Open();
        ClickHeader(rig, 1);
        Assert.True(rig.Timeline.TrackRowFocused);
        ClickRuler(rig, 2);

        Assert.False(rig.Timeline.TrackRowFocused);
        Assert.Equal(FocusTarget.Loop, rig.Model.Focus);
        Press(rig, Key.X, KeyModifiers.Control);
        Assert.Null(Prompt(rig));
        rig.Close();
    }

    [AvaloniaFact]
    public void DeleteKeyOnHeaderStillAsks()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        ClickHeader(rig, 2);
        Press(rig, Key.Delete);
        var prompt = Prompt(rig);
        Assert.NotNull(prompt);
        Answer(prompt!, "Delete");

        Assert.Equal(before - 1, rig.Song.Project.Tracks.Count);
        rig.Close();
    }

    [AvaloniaFact]
    public void MenuCutRoutesByFocus()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        ClickLane(rig, 1, 2);
        rig.Commands.Run("Edit.Cut");
        var barsPrompt = Prompt(rig);
        Assert.Equal("Clear highlighted bars", barsPrompt!.Title);   // a cell press keeps Edit.Cut on the highlighted bars (spec barhighlight), not on a track
        Answer(barsPrompt, "Cancel");

        ClickHeader(rig, 1);
        rig.Commands.Run("Edit.Cut");
        var prompt = Prompt(rig);
        Assert.NotNull(prompt);   // a header press sends the same menu row to the track
        Answer(prompt!, "Cancel");
        Assert.Equal(before, rig.Song.Project.Tracks.Count);
        rig.Close();
    }

    [AvaloniaFact]
    public void PaletteCutRoutesByFocus()
    {
        var rig = Open();
        Assert.Contains(MainWindowCommandPalette.Entries(rig.Commands, new HotkeySettings()), entry => entry.Id == "Edit.Cut");
        ClickHeader(rig, 1);
        rig.Commands.Run("Edit.Cut");   // the palette runs the chosen id through the command table
        var prompt = Prompt(rig);
        Assert.NotNull(prompt);
        Answer(prompt!, "Cancel");

        ClickRuler(rig, 2);
        rig.Commands.Run("Edit.Cut");
        Assert.Null(Prompt(rig));
        rig.Close();
    }

    [AvaloniaFact]
    public void ContextMenuTrackCutAsks()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        rig.Timeline.ContextMenus.ClickedTrack = 1;
        rig.Commands.Run("TrackRow.Cut");
        var prompt = Prompt(rig);
        Assert.NotNull(prompt);
        Answer(prompt!, "Cancel");

        Assert.Equal(before, rig.Song.Project.Tracks.Count);
        Assert.False(rig.Clipboard.HasTrack);
        rig.Close();
    }

    [AvaloniaFact]
    public void ScoreKeepsItsOwnKeysWhenScoreFocused()
    {
        var rig = Open();
        var before = rig.Song.Project.Tracks.Count;
        ClickHeader(rig, 1);
        Assert.Equal(FocusTarget.TrackRow, rig.Model.Focus);
        rig.Score.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(FocusTarget.ScoreSelection, rig.Model.Focus);

        rig.Score.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, Source = rig.Score });
        rig.Score.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeyModifiers = KeyModifiers.Control, Source = rig.Score });
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Prompt(rig));
        Assert.Equal(before, rig.Song.Project.Tracks.Count);
        rig.Close();
    }

    [AvaloniaFact]
    public void LastTrackCannotBeDeleted()
    {
        var rig = Open(tracks: 1);
        ClickHeader(rig, 0);
        Press(rig, Key.X, KeyModifiers.Control);
        rig.Commands.Run("TrackRow.Delete");

        Assert.Null(Prompt(rig));
        Assert.Single(rig.Song.Project.Tracks);
        Assert.False(rig.Clipboard.HasTrack);
        rig.Close();
    }
}
