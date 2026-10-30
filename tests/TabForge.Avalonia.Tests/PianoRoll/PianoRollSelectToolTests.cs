using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll Select tool (the arrow, key V, the default tool): a click on empty grid clears and writes nothing, a drag boxes, a double-click adds,
//   a right-drag selects, notes move and resize as in Write; Write keeps drawing; and the S split and Ctrl+Shift+G glue keys with their hover preview and messages.
// Does not own: the edit maths (PianoRollEdits tests) or the catalogue's other rows.
// Tests: PianoRollSurface, PianoRollEdits.
public sealed class PianoRollSelectToolTests
{
    private static (PianoRollView View, Window Window, List<string> Said) Show(SongDocument document)
    {
        var said = new List<string>();
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Report = said.Add });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window, said);
    }

    private static Point Empty(PianoRollSurface s) => new(s.XOf(20), s.YOf(70));

    private static int Count(SongDocument d) => d.Project.Tracks[0].AudioClips[0].Notes!.Count;

    [AvaloniaFact]
    public void SelectIsTheDefaultToolAndAClickOnEmptyGridClearsAndWritesNothing()
    {
        var document = PianoRollSongs.Midi();
        var (view, window, _) = Show(document);
        try
        {
            Assert.Equal(PianoRollTool.Pointer, view.Presenter.Tool);
            view.Presenter.SelectAll();
            var steps = document.Undo.UndoCount;
            view.Surface.Press(Empty(view.Surface), KeyModifiers.None);
            view.Surface.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(view.Presenter.Selected);
            Assert.Equal(2, Count(document));
            Assert.Equal(steps, document.Undo.UndoCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SelectDragBoxSelectsRightDragSelectsAndDoubleClickAdds()
    {
        var document = PianoRollSongs.Midi();
        var (view, window, _) = Show(document);
        try
        {
            var s = view.Surface;
            var (a, b) = (s.NoteRect(view.Presenter.Notes[0]), s.NoteRect(view.Presenter.Notes[1]));
            s.Press(new Point(a.Left - 3, b.Top - 3), KeyModifiers.None);
            s.DragTo(new Point(b.Right + 3, a.Bottom + 3));
            s.Release(KeyModifiers.None);
            Assert.Equal(2, view.Presenter.Selected.Count);
            view.Presenter.ClearSelection();
            Assert.True(s.PressRight(new Point(a.Left - 3, b.Top - 3), KeyModifiers.None));
            s.DragTo(new Point(b.Right + 3, a.Bottom + 3));
            s.Release(KeyModifiers.None);
            Assert.Equal(2, view.Presenter.Selected.Count);
            Assert.Equal(2, Count(document));
            s.Press(Empty(s), KeyModifiers.None, clicks: 2);
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Count(document));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SelectMovesAndResizesLikeWriteAndWriteStillWritesOnAClick()
    {
        var document = PianoRollSongs.Midi();
        var (view, window, _) = Show(document);
        try
        {
            var s = view.Surface;
            var r = s.NoteRect(view.Presenter.Notes[0]);
            s.Press(new Point(r.Center.X, r.Center.Y), KeyModifiers.None);
            s.DragTo(new Point(r.Center.X, r.Center.Y - s.RowPx * 2));
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(document.Project.Tracks[0].AudioClips[0].Notes!, n => n.Pitch == 62);
            view.Presenter.Tool = PianoRollTool.Select;
            s.Press(Empty(s), KeyModifiers.None);
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Count(document));   // Write: a click on empty grid writes a note
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void VPicksSelectAndTheNumberKeysKeepTheirMeaning()
    {
        var (view, window, _) = Show(PianoRollSongs.Midi());
        try
        {
            view.Presenter.Tool = PianoRollTool.Erase;
            Assert.True(view.Surface.HandleKey(Key.V, KeyModifiers.None));
            Assert.Equal(PianoRollTool.Pointer, view.Presenter.Tool);
            Assert.True(view.Surface.HandleKey(Key.D1, KeyModifiers.None));
            Assert.Equal(PianoRollTool.Select, view.Presenter.Tool);
            Assert.True(view.Surface.HandleKey(Key.D3, KeyModifiers.None));
            Assert.Equal(PianoRollTool.Split, view.Presenter.Tool);
            Assert.True(view.Surface.HandleKey(Key.D5, KeyModifiers.None));
            Assert.Equal(PianoRollTool.Glue, view.Presenter.Tool);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SCutsTheSelectedNotesTheCutCrossesInOneStepAndSaysWhenNothingIsSelected()
    {
        var document = PianoRollSongs.Midi();
        var (view, window, said) = Show(document);
        try
        {
            var s = view.Surface;
            Assert.True(s.HandleKey(Key.S, KeyModifiers.None));
            Assert.Contains(said, m => m.Contains("Select notes"));
            view.Presenter.SelectAll();
            var steps = document.Undo.UndoCount;
            var r = s.NoteRect(view.Presenter.Notes[0]);
            s.HoverAt(new Point(r.Center.X, r.Center.Y));
            Assert.True(s.HandleKey(Key.S, KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Count(document));   // only the first note is crossed
            Assert.Equal(steps + 1, document.Undo.UndoCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSplitToolPreviewIsASnappedBeatOverANoteOnly()
    {
        var (view, window, _) = Show(PianoRollSongs.Midi());
        try
        {
            var s = view.Surface;
            view.Presenter.Tool = PianoRollTool.Split;
            var r = s.NoteRect(view.Presenter.Notes[0]);
            s.HoverAt(new Point(r.Center.X, r.Center.Y));
            Assert.NotNull(s.SplitPreviewBeat);
            s.HoverAt(Empty(s));
            Assert.Null(s.SplitPreviewBeat);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void GlueJoinsTouchingOverlappingAndOneStepGapsOfOnePitchOnly()
    {
        var notes = new List<PianoRollNote>
        {
            new(0, 1, 60, 80), new(1, 1, 60, 90), new(1.5, 1, 60, 70), new(2.75, 1, 60, 60),   // touch, overlap, then a 0.25 gap (ends 2.5, next starts 2.75)
            new(0, 1, 64, 80), new(2, 1, 64, 80),   // another pitch, a full beat apart
        };
        var (result, pairs) = PianoRollEdits.GlueTouching(notes, Enumerable.Range(0, 6).ToList(), 0.25);
        Assert.Equal(3, pairs);   // three joins on pitch 60 (a 0.25 gap is within one step)
        Assert.Equal(3, result.Notes.Count);   // one 60 note, two 64 notes
        var glued = result.Notes.Single(n => n.Pitch == 60);
        Assert.Equal((0, 3.75, 80), (glued.Start, glued.End, glued.Velocity));
        Assert.Equal(2, result.Notes.Count(n => n.Pitch == 64));
        var (_, none) = PianoRollEdits.GlueTouching(notes, new[] { 4, 5 }, 0.25);
        Assert.Equal(0, none);
    }

    [AvaloniaFact]
    public void CtrlShiftGGluesInOneStepAndSaysWhenThereIsNothing()
    {
        var document = PianoRollSongs.Midi();
        var clip = document.Project.Tracks[0].AudioClips[0];
        clip.Notes![1] = new ClipNote(1.0, 0.5, 60, 100);   // same pitch as the first, touching it (0.5 s = 1 beat at 120 bpm)
        var (view, window, said) = Show(document);
        try
        {
            var s = view.Surface;
            Assert.True(s.HandleKey(Key.G, KeyModifiers.Control | KeyModifiers.Shift));
            Assert.Contains(said, m => m.Contains("two or more"));
            view.Presenter.SelectAll();
            var steps = document.Undo.UndoCount;
            Assert.True(s.HandleKey(Key.G, KeyModifiers.Control | KeyModifiers.Shift));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Count(document));
            Assert.Equal(steps + 1, document.Undo.UndoCount);
            Assert.Contains(said, m => m == "Glued 1 pair");
            view.Presenter.SelectAll();
            s.HandleKey(Key.G, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Contains(said, m => m.Contains("two or more") || m == "Nothing to glue");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheNewKeysAreRegisteredOnceAndRebindable()
    {
        var keys = new HotkeySettings();
        foreach (var (id, gesture) in new[] { ("PianoRoll.SelectTool", "V"), ("PianoRoll.Split", "S"), ("PianoRoll.Glue", "Ctrl+Shift+G"), ("PianoRoll.Snap", "Alt+S") })
        {
            Assert.Equal(1, HotkeyCatalog.All.Count(a => a.Id == id));
            Assert.Equal(gesture, HotkeyCatalog.GestureFor(keys, id));
        }
        keys.Set("PianoRoll.Split", 1, "Ctrl+Alt+J");
        Assert.Equal("Ctrl+Alt+J", HotkeyCatalog.GestureFor(keys, "PianoRoll.Split"));
        Assert.Equal("S", HotkeyCatalog.GestureFor(new HotkeySettings(), "Clip.Split"));   // the timeline's own S stays
    }
}
