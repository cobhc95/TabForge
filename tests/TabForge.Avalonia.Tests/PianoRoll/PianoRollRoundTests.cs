using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the 26 Oct round's items: a double-click on empty grid inserts a note and on a note deletes it; the Length dropdown (Grid, values,
//   Last used); ghost notes (off by default, other takes and tracks, read-only); middle-drag pan; the live value in the info line during a
//   value drag; relative info-line values (+12, -5); Ctrl+drag copies and Alt while dragging ignores the snap.
// Does not own: the earlier items (the other PianoRoll test classes).
// Tests: PianoRollSurface, PianoRollView, PianoRollSnap.NewLength, PianoRollGhosts, PianoRollPositionText.ParseDelta.
public sealed class PianoRollRoundTests
{
    private static (MainWindow Window, PianoRollView Roll) Open(SongDocument song)
    {
        var window = new MainWindow(first: song) { Width = 1600, Height = 1000 };
        window.Show();
        window.UpdateLayout();
        return (window, window.GetLogicalDescendants().OfType<PianoRollView>().Single());
    }

    private static void Close(MainWindow window, SongDocument song)
    {
        song.MarkClean();
        window.Close();
    }

    private static Point Note(PianoRollSurface s, PianoRollNote note) => s.NoteRect(note).Center;

    [AvaloniaFact]
    public void ADoubleClickOnEmptyGridInsertsANoteAndOnANoteDeletesIt()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        var empty = new Point(s.XOf(5) + 2, s.YOf(67) + s.RowPx / 2);
        s.Press(empty, KeyModifiers.None, 1);
        s.Release(KeyModifiers.None);
        s.Press(empty, KeyModifiers.None, 2);   // the double-click's second press lands on the note the first one made
        s.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(roll.Presenter.Notes, n => n.Pitch == 67);   // inserted, kept

        var e4 = roll.Presenter.Notes.First(n => n.Pitch == 64);
        s.Press(Note(s, e4), KeyModifiers.None, 1);
        s.Release(KeyModifiers.None);
        s.Press(Note(s, e4), KeyModifiers.None, 2);
        s.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(roll.Presenter.Notes, n => n.Pitch == 64);   // a double-click on a note deletes it
        Close(window, song);
    }

    [Fact]
    public void TheLengthDropdownGivesGridValuesOrTheLastUsedLength()
    {
        var map = PianoRollTimeMap.Written(SongDocument.BlankProject());
        var snap = new SnapSettings { Grid = "1/8" };
        Assert.Equal("Grid", PianoRollSnap.Lengths[0]);
        Assert.Contains("Last used", PianoRollSnap.Lengths);
        Assert.Equal(0.5, PianoRollSnap.NewLength(map, snap, 0, "Grid", 0), 6);
        Assert.Equal(4, PianoRollSnap.NewLength(map, snap, 0, "1/1", 0), 6);
        Assert.Equal(1.0 / 3, PianoRollSnap.NewLength(map, snap, 0, "1/8T", 0), 6);
        Assert.Equal(1.5, PianoRollSnap.NewLength(map, snap, 0, "1/4D", 0), 6);
        Assert.Equal(2, PianoRollSnap.NewLength(map, snap, 0, "Last used", 2), 6);
        Assert.Equal(0.5, PianoRollSnap.NewLength(map, snap, 0, "Last used", 0), 6);   // nothing drawn yet: the grid
        Assert.Equal("Grid", new EditingSettings().PianoRollNoteLength);
    }

    [Fact]
    public void GhostNotesShowTheOtherTakesAndTracksAndAreOffByDefault()
    {
        Assert.False(new EditingSettings().PianoRollGhosts);
        var song = PianoRollSongs.Midi().Project;
        var other = PianoRollSongs.Tab().Project.Tracks[0];
        song.Tracks.Add(other);
        var hidden = new AudioClip { Name = "old take", StartSec = 0, SourceLengthSec = 8, FileLengthSec = 8, Lane = 1, Notes = new List<ClipNote> { new(2, 0.5, 72, 80) } };
        song.Tracks[0].AudioClips.Add(hidden);
        song.Tracks[0].Lanes.AddRange(new[] { new ClipLane { Plays = true }, new ClipLane { Plays = false } });
        var map = PianoRollTimeMap.Performed(song);
        var ghosts = PianoRollGhosts.Read(song, 0, map);
        Assert.Contains(ghosts, g => g.Pitch == 72 && System.Math.Abs(g.Start - 4) < 1e-6);   // the silent take
        Assert.Contains(ghosts, g => g.Pitch == 60 && System.Math.Abs(g.Start) < 1e-6);        // the tab track's note
        Assert.DoesNotContain(ghosts, g => g.Pitch == 64);                                       // the shown take is not a ghost
    }

    [AvaloniaFact]
    public void AMiddleDragPansTheView()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        var (x, y) = (s.ScrollX, s.ScrollY);
        var at = new Point(s.XOf(4), s.YOf(60));
        Assert.True(s.Press(at, KeyModifiers.None, 1, middle: true));
        s.DragTo(at + new Point(-120, -60));
        s.Release(KeyModifiers.None);
        Assert.True(s.ScrollX > x);
        Assert.True(s.ScrollY > y);
        Close(window, song);
    }

    [AvaloniaFact]
    public void TheInfoLineFollowsAValueDragAndAcceptsRelativeValues()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        var r = s.NoteRect(roll.Presenter.Notes[1]);
        s.Press(new Point(r.Center.X, r.Top + 1), KeyModifiers.None);
        s.DragTo(new Point(r.Center.X, r.Top - 19));
        var velocity = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "PianoRollVelocity");
        Assert.Equal("110", velocity.Text);   // live during the drag
        s.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(-5.0, PianoRollPositionText.ParseDelta("-5", beats: false));
        Assert.Equal(1.5, PianoRollPositionText.ParseDelta("+1.2", beats: true));
        Assert.Null(PianoRollPositionText.ParseDelta("64", beats: false));
        var pitch = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "PianoRollPitch");
        roll.Presenter.Select(new[] { 1 });
        roll.RefreshInfo();
        pitch.Text = "+12";
        roll.Apply(pitch);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(song.Project.Tracks[0].AudioClips[0].Notes!, n => n.Pitch == 76);
        Close(window, song);
    }

    [AvaloniaFact]
    public void CtrlDragCopiesANoteAndAltWhileDraggingIgnoresTheSnap()
    {
        var song = PianoRollSongs.Midi();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        var c4 = roll.Presenter.Notes.First(n => n.Pitch == 60);
        var from = s.TranslatePoint(Note(s, c4), window)!.Value;
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        for (var i = 1; i <= 8; i++) window.MouseMove(from + new Point(i * s.PixelsPerBeat / 4, 0), RawInputModifiers.Control);
        window.MouseUp(from + new Point(2 * s.PixelsPerBeat, 0), MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, song.Project.Tracks[0].AudioClips[0].Notes!.Count(n => n.Pitch == 60));   // copied: the original stays

        var e4 = roll.Presenter.Notes.First(n => n.Pitch == 64);
        var at = s.TranslatePoint(Note(s, e4), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        for (var i = 1; i <= 6; i++) window.MouseMove(at + new Point(i * s.PixelsPerBeat * 0.37 / 6, 0), RawInputModifiers.Alt);
        window.MouseUp(at + new Point(s.PixelsPerBeat * 0.37, 0), MouseButton.Left, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        var moved = roll.Presenter.Notes.First(n => n.Pitch == 64).Start;
        Assert.Equal(e4.Start + 0.37, moved, 2);   // not on the 1/4 grid
        Close(window, song);
    }
}
