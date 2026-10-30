using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's Write tool (click, drag, right-drag box, Alt lasso, the hover ghost), the Select tool's click, the Erase box's pending look and
//   one-step delete, the ruler and grid click-to-seek, and the dashed cut marker that shows where S cuts.
// Does not own: the edit maths (PianoRollEdits tests) or the other gestures (PianoRollSelectToolTests, PianoRollGestureTests).
// Tests: PianoRollSurface.
public sealed class PianoRollOverlayTests
{
    private static (PianoRollView View, Window Window) Show(SongDocument document)
    {
        var view = new PianoRollView(new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    private static Point Empty(PianoRollSurface s) => new(s.XOf(20), s.YOf(70));

    private static int Count(SongDocument d) => d.Project.Tracks[0].AudioClips[0].Notes!.Count;

    private static Point Ruler() => new(0, PianoRollSurface.RulerSize / 2);

    private static void Click(PianoRollSurface s, Point p, KeyModifiers m = KeyModifiers.None)
    {
        s.Press(p, m);
        s.Release(m);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void WriteClickWritesASnapLengthNoteAndDragWritesTheDraggedLength()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var s = view.Surface;
            view.Presenter.Tool = PianoRollTool.Select;
            Click(s, Empty(s));
            Assert.Equal(3, Count(document));
            var click = view.Presenter.Notes.Single(n => n.Pitch == 70);
            Assert.Equal(20, click.Start, 3);
            Assert.Equal(s.NewLength(20), click.Length, 3);
            s.Press(new Point(s.XOf(30), s.YOf(72)), KeyModifiers.None);
            s.DragTo(new Point(s.XOf(32.5), s.YOf(72)));
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var dragged = view.Presenter.Notes.Single(n => n.Pitch == 72);
            Assert.Equal(30, dragged.Start, 3);
            Assert.InRange(dragged.Length, 2.2, 2.8);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WriteRightDragBoxesAndAltRightDragLassosWhilePointerClickWritesNothingAndLeftDragBoxes()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var s = view.Surface;
            var (a, b) = (s.NoteRect(view.Presenter.Notes[0]), s.NoteRect(view.Presenter.Notes[1]));
            var (from, to) = (new Point(a.Left - 3, b.Top - 3), new Point(b.Right + 3, a.Bottom + 3));
            view.Presenter.Tool = PianoRollTool.Select;
            Assert.True(s.PressRight(from, KeyModifiers.None));
            s.DragTo(to);
            s.Release(KeyModifiers.None);
            Assert.Equal(2, view.Presenter.Selected.Count);
            view.Presenter.ClearSelection();
            Assert.True(s.PressRight(from, KeyModifiers.Alt));
            s.DragTo(new Point(to.X, from.Y));
            s.DragTo(to);
            s.DragTo(new Point(from.X, to.Y));
            s.Release(KeyModifiers.Alt);
            Assert.True(view.Presenter.Selected.Count > 0);
            Assert.Equal(2, Count(document));
            view.Presenter.Tool = PianoRollTool.Pointer;
            view.Presenter.ClearSelection();
            s.Press(from, KeyModifiers.None);
            s.DragTo(to);
            s.Release(KeyModifiers.None);
            Assert.Equal(2, view.Presenter.Selected.Count);
            Click(s, Empty(s));
            Assert.Equal(2, Count(document));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheGhostShowsForWriteOnlyFollowsChordAndScaleLockAndHidesOverNotesAndOffGrid()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var s = view.Surface;
            var steps = document.Undo.UndoCount;
            view.Presenter.Tool = PianoRollTool.Select;
            s.HoverAt(Empty(s));
            Assert.NotNull(s.Ghost);
            Assert.Equal(70, s.Ghost!.Value.Pitch);
            Assert.Equal(20, s.Ghost.Value.Beat, 3);
            var repaints = s.OverlayRepaints;
            s.HoverAt(new Point(Empty(s).X + 1, Empty(s).Y));   // same cell: no repaint for the overlay
            Assert.Equal(repaints, s.OverlayRepaints);
            var single = s.LastOverlayDirty.New.Height;
            s.Chord = "Maj";
            s.HoverAt(new Point(Empty(s).X, Empty(s).Y + 1));
            s.HoverAt(new Point(Empty(s).X + s.PixelsPerBeat, Empty(s).Y));
            Assert.True(s.LastOverlayDirty.New.Height > single);
            s.Chord = "Off";
            s.LockScale = new HashSet<int> { 0, 2, 4, 5, 7, 9, 11 };
            s.HoverAt(new Point(s.XOf(20), s.YOf(61) + 1));
            Assert.Contains(s.Ghost!.Value.Pitch, new[] { 60, 62 });   // C# moves onto the scale
            s.LockScale = null;
            var r = s.NoteRect(view.Presenter.Notes[0]);
            s.HoverAt(new Point(r.Center.X, r.Center.Y));
            Assert.Null(s.Ghost);
            s.HoverAt(new Point(5, r.Center.Y));
            Assert.Null(s.Ghost);
            s.HoverAt(Empty(s));
            foreach (var tool in new[] { PianoRollTool.Pointer, PianoRollTool.Erase, PianoRollTool.Split })
            {
                view.Presenter.Tool = tool;
                Assert.Null(s.Ghost);
            }
            view.Presenter.Tool = PianoRollTool.Select;
            s.Press(Empty(s), KeyModifiers.None);
            Assert.Null(s.Ghost);   // the growing note is the ghost
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(steps + 1, document.Undo.UndoCount);   // only the click wrote; hovering never changed the song
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EraseBoxKeepsNotesTintedUntilReleaseThenDeletesInOneStepAndEscCancels()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var s = view.Surface;
            view.Presenter.Tool = PianoRollTool.Erase;
            var (a, b) = (s.NoteRect(view.Presenter.Notes[0]), s.NoteRect(view.Presenter.Notes[1]));
            var (from, to) = (new Point(a.Left - 3, b.Top - 3), new Point(b.Right + 3, a.Bottom + 3));
            var steps = document.Undo.UndoCount;
            s.Press(from, KeyModifiers.None);
            Assert.Null(s.EraseLabel);
            s.DragTo(to);
            Assert.Equal(2, s.PendingErase.Count);
            Assert.Equal("2 notes will be deleted", s.EraseLabel);
            Assert.Equal(2, Count(document));
            Assert.Equal(steps, document.Undo.UndoCount);
            s.CancelGesture();
            Assert.Empty(s.PendingErase);
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, Count(document));
            s.Press(Empty(s), KeyModifiers.None);
            s.DragTo(new Point(Empty(s).X + 20, Empty(s).Y + 20));
            Assert.Null(s.EraseLabel);
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(steps, document.Undo.UndoCount);
            s.Press(from, KeyModifiers.None);
            s.DragTo(to);
            s.Release(KeyModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Count(document));
            Assert.Equal(steps + 1, document.Undo.UndoCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRulerSeeksAnywhereAndScrubsAndTheSelectToolSeeksOnEmptyGridButWriteAndNotesDoNot()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var s = view.Surface;
            var seeks = new List<double>();
            s.Seek = seeks.Add;
            s.Playing = () => true;   // while playing the same seek runs
            foreach (var beat in new[] { 4.0, 8.0, 12.0 })
                Click(s, new Point(s.XOf(beat), Ruler().Y));
            Assert.Equal(new[] { 4.0, 8.0, 12.0 }, seeks.Select(x => Math.Round(x, 3)).ToArray());
            seeks.Clear();
            s.Press(new Point(s.XOf(4), Ruler().Y), KeyModifiers.None);
            s.DragTo(new Point(s.XOf(9), Ruler().Y));
            Assert.Equal(9, s.PlayheadPreview!.Value, 3);
            s.Release(KeyModifiers.None);
            Assert.Equal(9, Assert.Single(seeks), 3);
            seeks.Clear();
            view.Presenter.Tool = PianoRollTool.Pointer;
            view.Presenter.SelectAll();
            Click(s, Empty(s));
            Assert.Empty(view.Presenter.Selected);
            Assert.Equal(20, Assert.Single(seeks), 2);
            seeks.Clear();
            var r = s.NoteRect(view.Presenter.Notes[0]);
            Click(s, new Point(r.Center.X, r.Center.Y));
            view.Presenter.Tool = PianoRollTool.Select;
            Click(s, Empty(s));
            Assert.Empty(seeks);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCutMarkerShowsWhereSCutsForSelectedCrossedNotesOnly()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var s = view.Surface;
            var r = s.NoteRect(view.Presenter.Notes[0]);
            var inside = new Point(r.Left + r.Width * 0.4, r.Center.Y);
            s.HoverAt(inside);
            Assert.Null(s.CutMarkerBeat());   // nothing selected
            view.Presenter.Select(new[] { 0 });
            s.HoverAt(inside);
            var at = s.CutMarkerBeat();
            Assert.NotNull(at);
            Assert.Equal(at!.Value, s.CutBeat());
            s.HoverAt(new Point(s.XOf(view.Presenter.Notes[0].Start), r.Center.Y));
            Assert.Null(s.CutMarkerBeat());   // at the note's edge S cuts nothing
            s.HoverAt(new Point(s.XOf(view.Presenter.Notes[1].Start + 0.5), r.Center.Y));
            Assert.Null(s.CutMarkerBeat());   // the crossed note is not selected
            s.HoverAt(inside, KeyModifiers.Alt);
            Assert.Equal(s.BeatAt(inside.X), s.CutMarkerBeat()!.Value, 6);
            s.Snap = () => null;
            s.HoverAt(inside);
            Assert.Equal(s.BeatAt(inside.X), s.CutMarkerBeat()!.Value, 6);
            s.Snap = () => new SnapSettings { Grid = "1/4" };
            s.PlayheadBeat = () => 1.5;
            s.HoverAt(new Point(5, 5));   // off the grid: the playhead decides
            Assert.Equal(1.5, s.CutMarkerBeat()!.Value, 3);
            s.HoverAt(inside);
            view.Presenter.Tool = PianoRollTool.Erase;
            Assert.Null(s.CutMarkerBeat());
            view.Presenter.Tool = PianoRollTool.Pointer;
            var repaints = s.OverlayRepaints;
            s.HoverAt(new Point(inside.X + 1, inside.Y));
            Assert.True(s.OverlayRepaints - repaints <= 1);
            var expected = s.CutMarkerBeat()!.Value;
            Assert.True(s.HandleKey(Key.S, KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.Presenter.Notes, n => Math.Abs(n.Start - expected) < 1e-6);
        }
        finally { window.Close(); }
    }
}
