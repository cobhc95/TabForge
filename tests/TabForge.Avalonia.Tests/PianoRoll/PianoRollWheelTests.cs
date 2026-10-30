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
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Shift+wheel with notes selected (0.8.6): the shown lane moves one per notch (ten with Ctrl) and clamps at its ends; a burst is one undo step that
//   commits once the wheel rests (the song waits until then); with no selection Shift+wheel scrolls the keys and leaves the values alone; a plain wheel only zooms.
// Does not own: the zoom and scroll rules (PianoRollRealPathTests), the velocity drag (PianoRollVelocityDragTests).
// Tests: PianoRollSurface.
public sealed class PianoRollWheelTests
{
    private static (PianoRollView View, Window Window, PianoRollSurface Surface, SongDocument Document) Show(int first, int second)
    {
        var document = PianoRollSongs.Midi();
        var notes = document.Project.Tracks[0].AudioClips[0].Notes!;
        notes[0] = new ClipNote(0.5, 0.5, 60, first);
        notes[1] = new ClipNote(1.0, 0.5, 64, second);
        var view = new PianoRollView(new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        view.Presenter.Select(new[] { 0, 1 });
        return (view, window, view.Surface, document);
    }

    private static Point Grid(PianoRollSurface surface) => new(surface.GridLeft + 40, surface.GridTop + 40);

    /// <summary>One Shift+wheel notch over the grid (the lane is below it; Shift applies wherever the pointer is).</summary>
    private static void ShiftNotch(PianoRollSurface surface, int notches, bool ctrl = false) => surface.Wheel(notches, Grid(surface), ctrl, shift: true, alt: false);

    private static int[] Velocities(SongDocument document) => document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Velocity).ToArray();

    private static void Settle(PianoRollSurface surface)
    {
        surface.FlushLaneWheel();   // what the rest timer does after the wheel stops
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ShiftWheelUpRaisesTheSelectedVelocitiesOneAPieceAndClampsAtTheMaximumAsOneUndoStep()
    {
        var (view, window, surface, document) = Show(125, 100);
        var steps = document.Undo.UndoCount;
        for (var i = 0; i < 5; i++) ShiftNotch(surface, 1);
        Assert.True(surface.LaneWheelPending);
        Assert.Equal(new[] { 125, 100 }, Velocities(document));   // the burst waits for the wheel to rest: the song is unchanged until then
        Assert.Equal(steps, document.Undo.UndoCount);
        Settle(surface);
        Assert.False(surface.LaneWheelPending);
        Assert.Equal(new[] { 127, 105 }, Velocities(document));   // 125 clamps at 127 while 100 climbs by five
        Assert.Equal(steps + 1, document.Undo.UndoCount);   // five notches, one undo step
        Assert.NotNull(DocumentEdits.Undo(document));
        Assert.Equal(new[] { 125, 100 }, Velocities(document));
        window.Close();
    }

    [AvaloniaFact]
    public void ShiftWheelDownLowersTheSelectedVelocities()
    {
        var (_, window, surface, document) = Show(90, 100);
        ShiftNotch(surface, -1);
        ShiftNotch(surface, -1);
        ShiftNotch(surface, -1);
        Settle(surface);
        Assert.Equal(new[] { 87, 97 }, Velocities(document));
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlShiftWheelMovesTheVelocitiesByTen()
    {
        var (_, window, surface, document) = Show(90, 100);
        ShiftNotch(surface, 1, ctrl: true);
        Settle(surface);
        Assert.Equal(new[] { 100, 110 }, Velocities(document));
        window.Close();
    }

    [AvaloniaFact]
    public void WithNoSelectionShiftWheelScrollsTheKeysAndLeavesTheValuesAlone()
    {
        var (view, window, surface, document) = Show(90, 100);
        view.Presenter.ClearSelection();
        var steps = document.Undo.UndoCount;
        var scroll = surface.ScrollY;
        surface.Wheel(-1, Grid(surface), ctrl: false, shift: true, alt: false);
        Assert.True(surface.ScrollY > scroll);   // the same vertical scroll as before
        Assert.False(surface.LaneWheelPending);
        Settle(surface);
        Assert.Equal(new[] { 90, 100 }, Velocities(document));
        Assert.Equal(steps, document.Undo.UndoCount);
        window.Close();
    }

    [AvaloniaFact]
    public void APlainWheelStillOnlyZoomsWithNotesSelected()
    {
        var (_, window, surface, document) = Show(90, 100);
        var zoom = surface.PixelsPerBeat;
        surface.Wheel(1, Grid(surface), ctrl: false, shift: false, alt: false);
        Assert.True(surface.PixelsPerBeat > zoom);
        Assert.False(surface.LaneWheelPending);
        Settle(surface);
        Assert.Equal(new[] { 90, 100 }, Velocities(document));
        window.Close();
    }

    [AvaloniaFact]
    public void AControllerLaneMovesOnlyItsPointsInTheSelectedNotesSpan()
    {
        var (view, window, surface, document) = Show(90, 100);
        var clip = document.Project.Tracks[0].AudioClips[0];
        clip.Controllers = new List<ClipController> { new(0.5, 7, 50), new(2.0, 7, 70) };   // beat 1 (inside note 0's span) and beat 4 (outside it)
        view.ShowLane(7);
        view.Presenter.Select(new[] { 0 });   // note 0 covers beats 1 to 2
        for (var i = 0; i < 4; i++) ShiftNotch(surface, 1);
        Settle(surface);
        Assert.Equal(new[] { (1.0, 54), (4.0, 70) }, view.Presenter.Points(7).Select(p => (Math.Round(p.Beat, 6), p.Value)).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void AControllerPointRaisedPastTheLaneMaximumClampsAtIt()
    {
        var (view, window, surface, document) = Show(90, 100);
        document.Project.Tracks[0].AudioClips[0].Controllers = new List<ClipController> { new(0.5, 7, 125) };
        view.ShowLane(7);
        view.Presenter.Select(new[] { 0 });
        for (var i = 0; i < 5; i++) ShiftNotch(surface, 1);
        Settle(surface);
        Assert.Equal(new[] { (1.0, 127) }, view.Presenter.Points(7).Select(p => (Math.Round(p.Beat, 6), p.Value)).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void AControllerLaneWithNoPointsInTheSpanLeavesShiftWheelToScroll()
    {
        var (view, window, surface, document) = Show(90, 100);
        document.Project.Tracks[0].AudioClips[0].Controllers = new List<ClipController> { new(2.0, 7, 70) };   // beat 4: outside note 0's span
        view.ShowLane(7);
        view.Presenter.Select(new[] { 0 });
        var scroll = surface.ScrollY;
        ShiftNotch(surface, 1);
        Assert.False(surface.LaneWheelPending);
        Assert.NotEqual(scroll, surface.ScrollY);
        Settle(surface);
        Assert.Equal(new[] { (4.0, 70) }, view.Presenter.Points(7).Select(p => (Math.Round(p.Beat, 6), p.Value)).ToArray());
        window.Close();
    }
}
