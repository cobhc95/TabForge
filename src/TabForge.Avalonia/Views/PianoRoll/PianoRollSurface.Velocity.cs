using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the velocity lane's gestures: a press sets the bar under it; a sweep paints (each bar the pointer passes takes its height); Shift draws a
//       straight ramp from the press to the pointer; with a selection only the selected bars are shaped; a drag held on a selected bar moves every
//       selected bar by the same amount. The whole drag is one step, committed on release. The lane shows the preview; the velocities commit on release as one step (no tab prompt).
// Does not own: the lane's drawing (PianoRollSurface.Drawing) or the commit (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double BarReach = 4;

    private bool _relative;   // the press was on a selected bar: for the whole drag every selected bar moves by the same amount from its press-time velocity
    private readonly Dictionary<int, int> _pressVelocities = new();   // the selected bars' velocities at the press (the base of a relative drag)
    private int _pressVelocity;
    private Point _lastPaint;   // where the paint sweep's last step ended

    /// <summary>The velocity at height <paramref name="y"/> in the lane.</summary>
    internal int VelocityAt(double y) =>
        PianoRollEdits.ClampVelocity((int)Math.Round((LaneTop + Lane - HeadRadius - y) / Math.Max(1, Lane - 2 * HeadRadius - 2) * 127));

    /// <summary>The note whose bar is nearest to <paramref name="x"/> within reach, or -1.</summary>
    private int BarAt(double x)
    {
        var best = -1;
        var bestDistance = BarReach + StemWidth;
        var notes = Presenter.Notes;
        for (var i = 0; i < notes.Count; i++)
        {
            var distance = Math.Abs(x - (XOf(notes[i].Start) + StemWidth / 2));
            if (distance <= bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>True when a selected note's bar is within reach of <paramref name="x"/> (chord notes share a bar position: any selected one counts).</summary>
    private bool SelectedBarAt(double x)
    {
        var notes = Presenter.Notes;
        foreach (var i in Presenter.Selected)
            if (i >= 0 && i < notes.Count && Math.Abs(x - (XOf(notes[i].Start) + StemWidth / 2)) <= BarReach + StemWidth) return true;
        return false;
    }

    private bool PressLane(Point p)
    {
        _gesture = Gesture.Velocity;
        _pressNote = BarAt(p.X);
        _pressVelocity = VelocityAt(p.Y);
        _relative = SelectedBarAt(p.X);
        _pressVelocities.Clear();
        if (_relative) foreach (var i in Presenter.Selected) if (i >= 0 && i < Presenter.Notes.Count) _pressVelocities[i] = Presenter.Notes[i].Velocity;
        _lastPaint = p;
        _moved = true;   // a press alone sets the bar
        _velocityPreview.Clear();
        DragLane(p);
        return true;
    }

    /// <summary>The bars a sweep may shape: the selected notes' when there is a selection, else every bar.</summary>
    private bool Shapeable(int index) => Presenter.Selected.Count == 0 || Presenter.Selected.Contains(index);

    // A lane drag: on a selected bar held in place, the selection moves together; Shift draws a straight ramp from the press to the pointer;
    // otherwise the sweep paints: every bar the pointer passes takes the pointer's height (interpolated between two moves, so a fast sweep skips none).
    private void DragLane(Point p)
    {
        var now = VelocityAt(p.Y);
        if (_relative && !_dragModifiers.HasFlag(KeyModifiers.Shift))   // sideways drift never turns it into a paint: the base is the press-time snapshot
        {
            _velocityPreview.Clear();
            var delta = now - VelocityAt(_press.Y);
            foreach (var (i, velocity) in _pressVelocities) _velocityPreview[i] = PianoRollEdits.ClampVelocity(velocity + delta);
            return;
        }
        if (_dragModifiers.HasFlag(KeyModifiers.Shift))
        {
            _velocityPreview.Clear();
            Paint(_press, p, ramp: true);
            return;
        }
        Paint(_lastPaint, p, ramp: false);
        _lastPaint = p;
    }

    /// <summary>Sets the shapeable bars between <paramref name="from"/> and <paramref name="to"/> (x) to the height along the segment between them.</summary>
    private void Paint(Point from, Point to, bool ramp)
    {
        var notes = Presenter.Notes;
        var left = Math.Min(from.X, to.X) - BarReach;
        var right = Math.Max(from.X, to.X) + BarReach;
        var span = to.X - from.X;
        var (start, end) = ramp ? (_pressVelocity, VelocityAt(to.Y)) : (VelocityAt(from.Y), VelocityAt(to.Y));
        for (var i = 0; i < notes.Count; i++)
        {
            var x = XOf(notes[i].Start) + StemWidth / 2;
            if (x < left || x > right || !Shapeable(i)) continue;
            var share = Math.Abs(span) < 1 ? 1 : Math.Clamp((x - from.X) / span, 0, 1);
            _velocityPreview[i] = PianoRollEdits.ClampVelocity((int)Math.Round(start + (end - start) * share));
        }
    }

    /// <summary>The lane drag's velocities as an edit of the whole note list (the selection kept).</summary>
    private PianoRollEditResult VelocityResult()
    {
        var list = Presenter.Notes.ToList();
        foreach (var (index, velocity) in _velocityPreview)
            if (index >= 0 && index < list.Count) list[index] = list[index] with { Velocity = velocity };
        return new PianoRollEditResult(list, Presenter.Selected.ToList());
    }
}
