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

// Owns: the velocity lane drag of a multi-selection (27 Oct fix): every selected bar moves by the same amount from its press-time velocity, sideways drift
//   never turns the drag into a paint (no jump to 127 / 0 / the middle), repeated drags behave alike, and one drag is one undo step.
// Does not own: the other lane gestures (PianoRollGestureTests, PianoRollViewTests).
// Tests: PianoRollSurface.
public sealed class PianoRollVelocityDragTests
{
    private static readonly int[] Start = { 90, 100, 110, 60, 120 };

    private static SongDocument Five()
    {
        var document = PianoRollSongs.Midi();
        var notes = document.Project.Tracks[0].AudioClips[0].Notes!;
        notes.Add(new ClipNote(1.5, 0.5, 67, 110));
        notes.Add(new ClipNote(2.0, 0.5, 69, 60));
        notes.Add(new ClipNote(2.5, 0.5, 71, 120));
        return document;
    }

    private static (PianoRollView View, Window Window, PianoRollSurface Surface) Show(SongDocument document)
    {
        var view = new PianoRollView(new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        view.Presenter.Select(new[] { 0, 1, 2, 3, 4 });
        return (view, window, view.Surface);
    }

    private static int[] Velocities(SongDocument document) => document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Velocity).ToArray();

    private static void Finish(PianoRollSurface surface)
    {
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ASmallDragMovesEverySelectedBarByTheSameSmallAmountEvenWithSidewaysDrift()
    {
        var document = Five();
        var (view, window, surface) = Show(document);
        var x = surface.XOf(view.Presenter.Notes[1].Start) + 1;
        var y = surface.VelocityY(100);
        surface.Press(new Point(x, y), KeyModifiers.None);
        surface.DragTo(new Point(x + 40, y - 3));   // drifts well off the bar while moving up 3 px
        var delta = surface.VelocityAt(y - 3) - surface.VelocityAt(y);
        Finish(surface);
        Assert.Equal(Start.Select(v => System.Math.Clamp(v + delta, 0, 127)).ToArray(), Velocities(document));
        Assert.InRange(delta, 1, 9);   // a few px is a few velocity steps, not a jump
        window.Close();
    }

    [AvaloniaFact]
    public void DraggingToTheTopClampsEveryBarAndBackDownRestoresThemWithoutSnappingToTheMiddle()
    {
        var document = Five();
        var (view, window, surface) = Show(document);
        var x = surface.XOf(view.Presenter.Notes[2].Start) + 1;
        var y = surface.VelocityY(110);
        surface.Press(new Point(x, y), KeyModifiers.None);
        surface.DragTo(new Point(x + 25, surface.VelocityY(127) - 20));
        surface.DragTo(new Point(x - 25, surface.VelocityY(127) - 40));
        surface.DragTo(new Point(x + 10, y - 6));   // back near the start, drifting
        var delta = surface.VelocityAt(y - 6) - surface.VelocityAt(y);
        Finish(surface);
        Assert.Equal(Start.Select(v => System.Math.Clamp(v + delta, 0, 127)).ToArray(), Velocities(document));

        window.Close();
    }

    [AvaloniaFact]
    public void ADragToTheTopMakesEveryBarAtLeastItsPressVelocityAndNeverTheMiddle()
    {
        var document = Five();
        var (view, window, surface) = Show(document);
        var x = surface.XOf(view.Presenter.Notes[4].Start) + 1;
        surface.Press(new Point(x, surface.VelocityY(120)), KeyModifiers.None);
        surface.DragTo(new Point(x + 30, surface.VelocityY(127) - 30));
        Finish(surface);
        Assert.All(Velocities(document).Zip(Start), pair => Assert.True(pair.First >= pair.Second));
        Assert.Equal(127, Velocities(document)[4]);
        window.Close();
    }

    [AvaloniaFact]
    public void RepeatedDragsInARowBehaveIdenticallyAndEachIsOneUndoStep()
    {
        var document = Five();
        var (view, window, surface) = Show(document);
        var x = surface.XOf(view.Presenter.Notes[0].Start) + 1;
        var y = surface.VelocityY(90);
        var steps = document.Undo.UndoCount;
        var before = Velocities(document);
        for (var round = 1; round <= 3; round++)
        {
            surface.Press(new Point(x, y), KeyModifiers.None);
            surface.DragTo(new Point(x + 20, y + 5));
            surface.DragTo(new Point(x - 20, y + 10));
            var delta = surface.VelocityAt(y + 10) - surface.VelocityAt(y);
            Finish(surface);
            var expected = before.Select(v => System.Math.Clamp(v + delta, 0, 127)).ToArray();
            Assert.Equal(expected, Velocities(document));
            Assert.Equal(steps + round, document.Undo.UndoCount);
            before = expected;
        }
        window.Close();
    }

    [AvaloniaFact]
    public void ARedrawDuringTheDragKeepsThePressTimeBase()
    {
        var document = Five();
        var (view, window, surface) = Show(document);
        var x = surface.XOf(view.Presenter.Notes[3].Start) + 1;
        var y = surface.VelocityY(60);
        surface.Press(new Point(x, y), KeyModifiers.None);
        surface.DragTo(new Point(x, y - 10));
        surface.InvalidateVisual();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        surface.DragTo(new Point(x + 15, y - 20));
        var delta = surface.VelocityAt(y - 20) - surface.VelocityAt(y);
        Finish(surface);
        Assert.Equal(Start.Select(v => System.Math.Clamp(v + delta, 0, 127)).ToArray(), Velocities(document));
        window.Close();
    }
}
