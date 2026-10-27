using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll gestures the user asked for on 26 Oct: the Erase box (one step; Shift adds the selection, Ctrl toggles; a click erases one
//   note), Ctrl-click toggling and Shift-click adding, velocity bars dragged directly (a selected bar moves every selected bar by the same amount),
//   the retired Velocity tool (key 7) with Alt-drag on a note instead, the needle only while playing, and the sideways scroll following the timeline.
// Does not own: the first gestures (PianoRollViewTests) or the pane switch (PianoRollPaneSwitchTests).
// Tests: PianoRollSurface, PianoRollView.
public sealed class PianoRollGestureTests
{
    // C4 at beat 1, E4 at beat 2 and G4 at beat 3, each half a beat... (0.5 s at 120 bpm)
    private static SongDocument Three()
    {
        var document = PianoRollSongs.Midi();
        document.Project.Tracks[0].AudioClips[0].Notes!.Add(new ClipNote(1.5, 0.5, 67, 110));
        return document;
    }

    private static (PianoRollView View, Window Window) Show(SongDocument document, PianoRollHost? host = null)
    {
        var view = new PianoRollView(host ?? new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    private static int[] Pitches(SongDocument document) => document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Pitch).ToArray();

    [AvaloniaFact]
    public void AnEraseBoxDeletesTheNotesInItAsOneStepAndAClickDeletesOne()
    {
        var document = Three();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.Tool = PianoRollTool.Erase;
        var notes = view.Presenter.Notes;
        var (first, second) = (surface.NoteRect(notes[0]), surface.NoteRect(notes[1]));
        var steps = document.Undo.UndoCount;

        surface.Press(new Point(first.Left - 3, second.Top - 3), KeyModifiers.None);
        surface.DragTo(new Point(second.Right + 3, first.Bottom + 3));
        Assert.Equal(steps, document.Undo.UndoCount);
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(new[] { 67 }, Pitches(document));

        surface.Press(surface.NoteRect(view.Presenter.Notes[0]).Center, KeyModifiers.None);
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Pitches(document));
        window.Close();
    }

    [AvaloniaFact]
    public void ACtrlEraseBoxTogglesItsNotesAgainstTheSelection()
    {
        var document = Three();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.SelectAll();
        view.Presenter.Tool = PianoRollTool.Erase;
        var first = surface.NoteRect(view.Presenter.Notes[0]);
        surface.Press(new Point(first.Left - 3, first.Top - 3), KeyModifiers.Control);
        surface.DragTo(new Point(first.Right + 3, first.Bottom + 3));
        surface.Release(KeyModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { 60 }, Pitches(document));   // everything selected but the boxed note went
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlClickTogglesANoteAndShiftClickAddsOne()
    {
        var document = Three();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.SelectAll();
        var middle = surface.NoteRect(view.Presenter.Notes[1]).Center;
        surface.Press(middle, KeyModifiers.Control);
        surface.Release(KeyModifiers.Control);
        Assert.Equal(new[] { 0, 2 }, view.Presenter.Selected.OrderBy(i => i).ToArray());
        surface.Press(middle, KeyModifiers.Control);
        surface.Release(KeyModifiers.Control);
        Assert.Equal(3, view.Presenter.Selected.Count);
        view.Presenter.Select(new[] { 0 });
        surface.Press(surface.NoteRect(view.Presenter.Notes[2]).Center, KeyModifiers.Shift);
        surface.Release(KeyModifiers.Shift);
        Assert.Equal(new[] { 0, 2 }, view.Presenter.Selected.OrderBy(i => i).ToArray());
        surface.Press(middle, KeyModifiers.None);
        surface.Release(KeyModifiers.None);
        Assert.Equal(new[] { 1 }, view.Presenter.Selected.ToArray());   // a plain click keeps only that note
        window.Close();
    }

    [AvaloniaFact]
    public void ADraggedSelectedVelocityBarMovesEverySelectedBarTogether()
    {
        var document = Three();
        var (view, window) = Show(document);
        var surface = view.Surface;
        view.Presenter.Select(new[] { 0, 2 });
        var x = surface.XOf(view.Presenter.Notes[0].Start) + 1;
        var from = surface.VelocityY(90);
        surface.Press(new Point(x, from), KeyModifiers.None);
        surface.DragTo(new Point(x, surface.VelocityY(70)));
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var velocities = document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Velocity).ToArray();
        Assert.InRange(velocities[0], 68, 72);   // 90 down by about 20
        Assert.Equal(100, velocities[1]);         // not selected: untouched
        Assert.InRange(velocities[2], 88, 92);    // 110 down by the same amount
        window.Close();
    }

    [AvaloniaFact]
    public void TheVelocityToolIsRetiredAndAltDragOnANoteSetsItsVelocity()
    {
        var document = Three();
        var (view, window) = Show(document);
        var surface = view.Surface;
        Assert.False(surface.HandleKey(Key.D7, KeyModifiers.None));
        var at = surface.NoteRect(view.Presenter.Notes[1]).Center;
        var steps = document.Undo.UndoCount;
        surface.Press(at, KeyModifiers.Alt);
        surface.DragTo(at + new Point(0, -20));
        surface.Release(KeyModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(110, document.Project.Tracks[0].AudioClips[0].Notes![1].Velocity);   // 100 up by 20 px / 2
        Assert.Equal(1.0, document.Project.Tracks[0].AudioClips[0].Notes![1].StartSec, 6);   // not moved
        window.Close();
    }

    [AvaloniaFact]
    public void TheSidewaysScrollFollowsTheTimelineAndAHandScrollMovesIt()
    {
        var document = Three();
        double? timeline = null;
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, ScrollTimeline = s => timeline = s });
        view.FollowTimeline(2.0);   // the timeline's left edge at 2 s: beat 4 at the piano roll's left edge, at its own zoom
        Assert.Equal(4 * view.Surface.PixelsPerBeat, view.Surface.ScrollX, 6);
        Assert.Null(timeline);   // following never pushes back
        view.Surface.ZoomIn();
        view.FollowTimeline(2.0);
        Assert.Equal(4 * view.Surface.PixelsPerBeat, view.Surface.ScrollX, 6);
        view.Surface.ScrollTo(8 * view.Surface.PixelsPerBeat, view.Surface.ScrollY);
        view.Surface.UserScrolled!();
        Assert.Equal(4.0, timeline!.Value, 6);
        using (window.CaptureRenderedFrame()) { }
        Assert.True(double.IsNaN(view.Surface.Playhead.DrawnX));   // no playback session: no marker, so no needle
        window.Close();
    }
}
