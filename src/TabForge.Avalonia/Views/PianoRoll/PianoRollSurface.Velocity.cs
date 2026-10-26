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

    private bool _relative;   // the press was on a selected bar and the pointer stays on it: every selected bar moves by the same amount
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

    private bool PressLane(Point p)
    {
        _gesture = Gesture.Velocity;
        _pressNote = BarAt(p.X);
        _pressVelocity = VelocityAt(p.Y);
        _relative = _pressNote >= 0 && Presenter.Selected.Contains(_pressNote);
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
        var notes = Presenter.Notes;
        var now = VelocityAt(p.Y);
        if (_relative && Math.Abs(p.X - _press.X) <= BarReach + StemWidth && !_dragModifiers.HasFlag(KeyModifiers.Shift))
        {
            _velocityPreview.Clear();
            var delta = now - VelocityAt(_press.Y);
            foreach (var i in Presenter.Selected) _velocityPreview[i] = PianoRollEdits.ClampVelocity(notes[i].Velocity + delta);
            return;
        }
        if (_relative) { _relative = false; _velocityPreview.Clear(); }   // the sweep left the bar: from here it paints
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
