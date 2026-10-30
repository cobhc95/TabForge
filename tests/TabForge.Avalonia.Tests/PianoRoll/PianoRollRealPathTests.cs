using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll in the real window (MainWindow's composition: the pane switch, the song, undo, the timeline sync) driven by real pointer
//   presses, moves and releases, for a MIDI track and a tab track: a drawn note stays, a box selects the notes it touches, a dragged note moves,
//   a selected note keeps its fill, the needle renders and moves while the song plays. These are the paths the user hit on 26 Oct.
// Does not own: the gestures' rules (PianoRollViewTests, PianoRollGestureTests).
// Tests: MainWindow + EditorPaneSwitch + PianoRollView + PianoRollSurface.
public sealed class PianoRollRealPathTests
{
    private static (MainWindow Window, PianoRollView Roll) Open(SongDocument song, AvaloniaPlaybackSessions? playback = null)
    {
        var window = new MainWindow(first: song, playback: playback) { Width = 1600, Height = 1000 };
        window.Show();
        window.UpdateLayout();
        var roll = window.GetLogicalDescendants().OfType<PianoRollView>().Single();
        Assert.True(roll.IsEffectivelyVisible);
        Assert.True(roll.Surface.Bounds.Height > 100);
        return (window, roll);
    }

    private static Point At(MainWindow window, PianoRollSurface surface, double beat, int pitch) =>
        surface.TranslatePoint(new Point(surface.XOf(beat) + 2, surface.YOf(pitch) + surface.RowPx / 2), window)!.Value;

    private static Point At(MainWindow window, PianoRollSurface surface, Point p) => surface.TranslatePoint(p, window)!.Value;

    private static void Drag(MainWindow window, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None, MouseButton button = MouseButton.Left)
    {
        window.MouseDown(from, button, modifiers);
        for (var i = 1; i <= 10; i++) window.MouseMove(from + (to - from) * (i / 10.0), modifiers);
        window.MouseUp(to, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Close(MainWindow window, SongDocument song)
    {
        song.MarkClean();
        window.Close();
    }

    [AvaloniaFact]
    public void OnAMidiTrackADrawnNoteStaysABoxSelectsAndADraggedNoteMoves()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        Assert.False(roll.Presenter.IsTab);

        Assert.Equal(PianoRollTool.Pointer, roll.Presenter.Tool);   // the Select tool box-selects on empty grid; drawing is the Draw tool
        roll.Presenter.Tool = PianoRollTool.Draw;
        var steps = song.Undo.UndoCount;
        Drag(window, At(window, surface, 3, 62), At(window, surface, 3.9, 62));
        Assert.Equal(steps + 1, song.Undo.UndoCount);
        Assert.Equal(3, roll.Presenter.Notes.Count);   // the drawn note stays
        Assert.Contains(song.Project.Tracks[0].AudioClips[0].Notes!, n => n.Pitch == 62);

        roll.Presenter.Tool = PianoRollTool.Select;
        roll.Presenter.ClearSelection();
        var first = surface.NoteRect(roll.Presenter.Notes[0]);
        var second = surface.NoteRect(roll.Presenter.Notes[1]);
        // The box only partly covers the two notes (any overlap selects).
        Drag(window, At(window, surface, new Point(first.Left + first.Width / 2, second.Top + 2)), At(window, surface, new Point(second.Left + 4, first.Bottom - 2)), button: MouseButton.Right);
        Assert.Equal(new[] { 0, 1 }, roll.Presenter.Selected.OrderBy(i => i).ToArray());
        Assert.NotEqual("No notes selected", window.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text?.EndsWith("selected") == true).Text);

        steps = song.Undo.UndoCount;
        var c4 = roll.Presenter.Notes.First(n => n.Pitch == 60);
        Drag(window, At(window, surface, c4.Start + 0.25, 60), At(window, surface, c4.Start + 1.25, 60));
        Assert.Equal(steps + 1, song.Undo.UndoCount);
        Assert.Contains(roll.Presenter.Notes, n => n.Pitch == 60 && System.Math.Abs(n.Start - (c4.Start + 1)) < 1e-6);
        Close(window, song);
    }

    [AvaloniaFact]
    public void OnATabTrackADrawnNoteIsWrittenAfterThePromptAndStays()
    {
        var song = PianoRollSongs.Tab();
        song.Project.Tracks[0].EditorView = TrackEditorView.PianoRoll;
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        Assert.True(roll.Presenter.IsTab);
        roll.Presenter.Tool = PianoRollTool.Draw;
        Drag(window, At(window, surface, 2, 64), At(window, surface, 2.9, 64));
        var prompt = window.OwnedWindows.OfType<PromptWindow>().Single();
        prompt.GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as string) == "Edit the tab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, roll.Presenter.Notes.Count);   // the drawn note stays after the answer
        Assert.Contains(song.Project.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes), n => n.MidiValue == 64);
        Close(window, song);
    }

    [AvaloniaFact]
    public void ASelectedNoteKeepsItsFillAndTheNeedleRendersWhilePlaying()
    {
        var song = PianoRollSongs.Midi();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var (window, roll) = Open(song, sessions);
        var surface = roll.Surface;
        using (window.CaptureRenderedFrame()) { }
        Assert.False(double.IsNaN(surface.Playhead.DrawnX));   // stopped: the needle stays at the stop position, under the marker
        Assert.Equal(surface.Playhead.MarkerX, surface.Playhead.DrawnX);

        sessions.For(song)!.PlayOrPause();
        Assert.True(sessions.For(song)!.IsPlaying);
        surface.Playhead.InvalidateVisual();
        using (window.CaptureRenderedFrame()) { }
        Assert.False(double.IsNaN(surface.Playhead.DrawnX));   // playing: the needle is drawn
        Assert.True(surface.Playhead.DrawnX >= surface.GridLeft);
        sessions.For(song)!.Stop();
        Close(window, song);
    }

    [Fact]
    public void ASelectedNoteKeepsItsFillAndOnlyAMutedNoteIsHollow()
    {
        Assert.Equal((true, true), PianoRollSurface.NoteLook(selected: true, muted: false));
        Assert.Equal((true, false), PianoRollSurface.NoteLook(selected: false, muted: false));
        Assert.Equal((false, false), PianoRollSurface.NoteLook(selected: false, muted: true));
        Assert.Equal((false, true), PianoRollSurface.NoteLook(selected: true, muted: true));
    }

    [AvaloniaFact]
    public void TheWheelZoomsTimeCtrlZoomsKeysShiftScrollsKeysAndAltScrollsTime()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        var at = new Point(surface.XOf(2), surface.YOf(62));
        var (ppb, row) = (surface.PixelsPerBeat, surface.RowPx);
        surface.Wheel(1, at, ctrl: false, shift: false, alt: false);
        Assert.True(surface.PixelsPerBeat > ppb);
        Assert.Equal(2, surface.BeatAt(at.X), 6);   // zoomed around the pointer
        surface.Wheel(1, at, ctrl: true, shift: false, alt: false);
        Assert.True(surface.RowPx > row);
        var (x, y) = (surface.ScrollX, surface.ScrollY);
        surface.Wheel(-1, at, ctrl: false, shift: true, alt: false);   // Shift: up and down through the pitches
        Assert.True(surface.ScrollY > y);
        Assert.Equal(x, surface.ScrollX);
        surface.Wheel(-1, at, ctrl: false, shift: false, alt: true);   // Alt: sideways
        Assert.True(surface.ScrollX > x);
        Close(window, song);
    }

    [AvaloniaFact]
    public void TheWheelOverTheKeyStripScrollsUpAndDownAndOverTheGridStillZoomsTime()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        var strip = new Point(surface.GridLeft / 2, surface.YOf(62));
        var (ppb, y) = (surface.PixelsPerBeat, surface.ScrollY);
        surface.Wheel(-1, strip, ctrl: false, shift: false, alt: false);
        Assert.NotEqual(y, surface.ScrollY);   // vertical offset changed
        Assert.Equal(ppb, surface.PixelsPerBeat);   // zoom unchanged
        surface.Wheel(1, new Point(surface.XOf(2), surface.YOf(62)), ctrl: false, shift: false, alt: false);
        Assert.True(surface.PixelsPerBeat > ppb);   // over the grid: time zoom as before
        Close(window, song);
    }

    [AvaloniaFact]
    public void ClickingANoteSelectsItUnchangedAndItsVelocityBarIsHighlighted()
    {
        var song = PianoRollSongs.Midi(mutedSecond: true);
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        var before = song.Project.Tracks[0].AudioClips[0].Notes!.ToList();
        var unselected = surface.BarColour(0);
        var c4 = surface.NoteRect(roll.Presenter.Notes[0]).Center;
        window.MouseDown(At(window, surface, c4), MouseButton.Left);
        window.MouseUp(At(window, surface, c4), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { 0 }, roll.Presenter.Selected.ToArray());
        Assert.Equal(before, song.Project.Tracks[0].AudioClips[0].Notes!);   // a click never changes the note
        Assert.False(roll.Presenter.Notes[0].Muted);
        var selected = surface.BarColour(0);
        Assert.Equal(255, selected.A);   // drawn opaque: visible and highlighted
        Assert.NotEqual(unselected, selected);
        Assert.True(surface.BarColour(1).A > 0);   // a muted note keeps a (dimmed) bar
        Assert.True(surface.BarColour(1).A < 255);
        Close(window, song);
    }

    [AvaloniaFact]
    public void ALeftDragOnEmptyGridBoxSelectsARightDragSelectsAndARightClickOpensTheMenu()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        // A left-drag on empty grid draws the selection box (Shift adds, Ctrl toggles); drawing is the Draw tool.
        roll.Presenter.Tool = PianoRollTool.Draw;
        Drag(window, At(window, surface, 3, 62), At(window, surface, 4.95, 65));
        Assert.Contains(roll.Presenter.Notes, n => n.Pitch == 65 && System.Math.Abs(n.Start - 3) < 1e-6 && n.Length >= 1.9);
        Assert.DoesNotContain(roll.Presenter.Notes, n => n.Pitch == 62);

        roll.Presenter.Tool = PianoRollTool.Pointer;
        roll.Presenter.ClearSelection();
        var boxFirst = surface.NoteRect(roll.Presenter.Notes[0]);
        Drag(window, At(window, surface, new Point(boxFirst.Left - 4, boxFirst.Top - 4)), At(window, surface, new Point(boxFirst.Right + 4, boxFirst.Bottom + 4)));
        Assert.Equal(new[] { 0 }, roll.Presenter.Selected.OrderBy(i => i).ToArray());   // a left-drag box selects

        roll.Presenter.ClearSelection();
        var menus = roll.MenusOpened;
        var first = surface.NoteRect(roll.Presenter.Notes[0]);
        var second = surface.NoteRect(roll.Presenter.Notes[1]);
        Drag(window, At(window, surface, new Point(first.Left + 1, second.Top + 1)), At(window, surface, new Point(second.Right - 1, first.Bottom - 1)), button: MouseButton.Right);
        Assert.Equal(2, roll.Presenter.Selected.Count);
        Assert.Equal(menus, roll.MenusOpened);   // a right drag selects; no menu

        var empty = At(window, surface, 6, 70);
        window.MouseDown(empty, MouseButton.Right);
        window.MouseUp(empty, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(menus + 1, roll.MenusOpened);   // a right click opens the menu
        Assert.Equal(2, roll.Presenter.Selected.Count);   // and leaves the selection alone
        Close(window, song);
    }

    [AvaloniaFact]
    public void EscapeCancelsADrawInProgress()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var surface = roll.Surface;
        var steps = song.Undo.UndoCount;
        var from = At(window, surface, 3, 62);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(At(window, surface, 4.5, 62));
        surface.Focus();
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.MouseUp(At(window, surface, 4.5, 62), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(steps, song.Undo.UndoCount);
        Assert.Equal(2, roll.Presenter.Notes.Count);
        Close(window, song);
    }

    [AvaloniaFact]
    public void TheRulerShowsThePlayheadTriangleWhenStoppedItsEdgeArrowScrollsToItAndDraggingItSeeks()
    {
        var song = PianoRollSongs.Midi();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var (window, roll) = Open(song, sessions);
        var s = roll.Surface;
        var session = sessions.For(song)!;
        using (window.CaptureRenderedFrame()) { }
        Assert.False(double.IsNaN(s.Playhead.MarkerX));   // stopped: the triangle shows, and the needle under it
        Assert.Equal(s.Playhead.MarkerX, s.Playhead.DrawnX);

        session.Seek(3, 0);   // bar 4, off to the right at a close zoom
        for (var i = 0; i < 6; i++) s.ZoomIn();
        s.ScrollTo(0, s.ScrollY);
        using (window.CaptureRenderedFrame()) { }
        Assert.Equal(1, s.Playhead.EdgeArrow);
        s.Press(new Point(s.Bounds.Width - 2, PianoRollSurface.RulerSize / 2), KeyModifiers.None);   // the arrow
        s.Release(KeyModifiers.None);
        using (window.CaptureRenderedFrame()) { }
        Assert.Equal(0, s.Playhead.EdgeArrow);
        Assert.False(double.IsNaN(s.Playhead.MarkerX));

        var marker = s.Playhead.MarkerX;
        s.Press(new Point(marker, PianoRollSurface.RulerSize - 3), KeyModifiers.None);   // grab the triangle
        s.DragTo(new Point(marker + 4 * s.PixelsPerBeat / 4, PianoRollSurface.RulerSize - 3));
        s.Release(KeyModifiers.None);
        Assert.Equal(3, session.Playhead.Bar);
        Assert.True(session.Playhead.Cell > 0);   // moved later inside bar 4
        Close(window, song);
    }
}
