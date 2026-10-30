using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's selection and tool-drag gestures: the Select tool's box marquee and lasso, the split line,
//       the glue drag, the mute paint, the key strip's pitch-row selection and Invert selection.
// Does not own: the first gestures (PianoRollViewTests, PianoRollGestureTests) or the pure edits (PianoRollEditTests).
// Tests: PianoRollSelection, PianoRollEdits (SplitMany, FlipMute), PianoRollPresenter, PianoRollSurface.
public sealed class PianoRollSelectTests
{
    private static readonly IReadOnlyList<PianoRollNote> Three = new[]
    {
        new PianoRollNote(0, 1, 60, 80),
        new PianoRollNote(2, 1, 64, 100),
        new PianoRollNote(4, 1, 67, 110),
    };

    [Fact]
    public void TheLassoCatchesCentresInsideAndNotesOverlappingItsOutline()
    {
        var polygon = new List<(double Beat, double Pitch)> { (1.5, 63), (2.5, 63), (2.5, 65), (1.5, 65) };
        Assert.Equal(new[] { 1 }, PianoRollSelection.InPolygon(Three, polygon));   // only E4's centre is inside
        var edge = new List<(double Beat, double Pitch)> { (0.5, 59), (0.5, 61) };   // a line through C4's left half
        Assert.Equal(new[] { 0 }, PianoRollSelection.InPolygon(Three, edge.Concat(new[] { (0.6, 61.0), (0.6, 59.0) }).ToList()));
        Assert.Empty(PianoRollSelection.InPolygon(Three, new List<(double Beat, double Pitch)> { (9, 60), (10, 60) }));   // too few points: nothing
    }

    [Fact]
    public void TheSplitLineCrossesANoteAtItsPitchRow()
    {
        var beat = PianoRollSelection.CrossBeat(Three[0], (0.5, 59), (1.5, 61));
        Assert.Equal(1.0, beat, 6);   // through pitch 60 at beat 1
        Assert.True(double.IsNaN(PianoRollSelection.CrossBeat(Three[2], (0.5, 59), (1.5, 61))));   // G4 far away: missed
    }

    [Fact]
    public void SplitManyCutsEveryCrossedNoteInOneEdit()
    {
        var split = PianoRollEdits.SplitMany(Three, new[] { (0, 0.25), (2, 4.5) });
        Assert.Equal(5, split.Notes.Count);
        Assert.Equal(0.25, split.Notes[0].Length, 6);
        Assert.Equal(0.5, split.Notes[2].Length, 6);
        Assert.Equal(4, split.Selected.Count);   // both halves of both notes
    }

    [Fact]
    public void FlipMuteTogglesEachNoteOnItsOwn()
    {
        var notes = new[] { new PianoRollNote(0, 1, 60, 80), new PianoRollNote(1, 1, 62, 80, Muted: true) };
        var flipped = PianoRollEdits.FlipMute(notes, new[] { 0, 1 });
        Assert.Equal(new[] { true, false }, flipped.Notes.Select(n => n.Muted).ToArray());
    }

    [Fact]
    public void InvertSelectionKeepsOnlyTheUnselectedNotes()
    {
        var document = PianoRollSongs.Midi();
        var presenter = PianoRollSongs.Presenter(document);
        presenter.Select(new[] { 0 });
        presenter.InvertSelection();
        Assert.Equal(new[] { 1 }, presenter.Selected.ToArray());
        presenter.InvertSelection();
        Assert.Equal(new[] { 0 }, presenter.Selected.ToArray());
    }

    [Fact]
    public void BothMenusOfferInvertSelection()
    {
        var note = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: true, CanPaste: false, InsideLoop: false));
        var row = Assert.Single(note, r => r.Id == PianoRollMenus.InvertSelection);
        Assert.Equal("Ctrl+I", row.Shortcut);
        var empty = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: false, CanPaste: false, InsideLoop: false, HasNotes: true));
        Assert.Contains(empty, r => r.Id == PianoRollMenus.InvertSelection);
    }

    private static (PianoRollView View, Window Window) Show(SongDocument document)
    {
        var view = new PianoRollView(new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    [AvaloniaFact]
    public void ALeftDragOnEmptyGridDrawsABoxSelection()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.Tool = PianoRollTool.Pointer;
        var first = surface.NoteRect(view.Presenter.Notes[0]);
        surface.Press(new Point(first.Left - 5, first.Center.Y), KeyModifiers.None);
        surface.DragTo(new Point(first.Right + 5, first.Bottom + 2));
        surface.Release(KeyModifiers.None);
        Assert.Equal(new[] { 0 }, view.Presenter.Selected.ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void AKeyRightClickSelectsItsPitchRowAndShiftAddsTheRange()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.Tool = PianoRollTool.Select;
        surface.Press(new Point(10, surface.YOf(60) + 2), KeyModifiers.None);   // a left click only auditions: the selection stays
        surface.Release(KeyModifiers.None);
        Assert.Empty(view.Presenter.Selected);
        Assert.True(surface.PressRight(new Point(10, surface.YOf(60) + 2), KeyModifiers.None));
        surface.Release(KeyModifiers.None);
        Assert.Equal(new[] { 0 }, view.Presenter.Selected.ToArray());
        Assert.True(surface.PressRight(new Point(10, surface.YOf(64) + 2), KeyModifiers.Shift));
        surface.Release(KeyModifiers.Shift);
        Assert.Equal(new[] { 0, 1 }, view.Presenter.Selected.OrderBy(i => i).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void AMutePaintFlipsEachTouchedNoteOnce()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.Tool = PianoRollTool.Mute;
        var steps = document.Undo.UndoCount;
        surface.Press(surface.NoteRect(view.Presenter.Notes[0]).Center, KeyModifiers.None);
        surface.DragTo(surface.NoteRect(view.Presenter.Notes[1]).Center);
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(steps + 1, document.Undo.UndoCount);   // one gesture: one step
        Assert.All(document.Project.Tracks[0].AudioClips[0].Notes!, n => Assert.True(n.Muted));
        window.Close();
    }
}
