using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the controller lanes (CC and pitch bend on a MIDI track: a drag draws a line of points on the snap step, the Erase tool erases a span,
//       committed on release as one step, drawn as held steps with a resting line), the Velocity tool (drag a note up or down: the selection's
//       velocities follow), and step input (a piano key adds a note, or a chord stamp, at the step cursor and moves it on by a snap step;
//       Right / Left move the cursor, Backspace takes the last step back).
// Does not own: the lane points and their storage (PianoRollPresenter, PianoRollControllers), the velocity lane (PianoRollSurface.Velocity).
// Tests: TabForge.Avalonia.Tests.PianoRollControllerTests, PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double LanePad = 4, PointRadius = 2.5, VelocityToolPixels = 2;

    private int _pressValue;
    private IReadOnlyList<PianoRollPoint> _points = Array.Empty<PianoRollPoint>();

    /// <summary>The lane shown under the grid: PianoRollControllers.Velocity or a controller number (128 = pitch bend).</summary>
    internal int LaneId { get; private set; } = PianoRollControllers.Velocity;

    /// <summary>Shows lane <paramref name="lane"/> with its points (read by the view from the presenter).</summary>
    internal void ShowLane(int lane, IReadOnlyList<PianoRollPoint> points)
    {
        LaneId = lane;
        _points = points;
        InvalidateVisual();
    }

    /// <summary>The chord stamp new notes take ("Off" draws single notes).</summary>
    internal string Chord { get; set; } = "Off";

    /// <summary>The tab voice new notes go into (0 or 1; always 0 on a MIDI track).</summary>
    internal int DrawVoice { get; set; }

    /// <summary>Step input: a piano key adds a note at <see cref="StepBeat"/> and moves it on.</summary>
    internal bool StepInput { get; set; }

    internal double StepBeat { get; set; }

    private double ControllerY(int value)
    {
        var min = PianoRollControllers.Min(LaneId);
        var max = PianoRollControllers.Max(LaneId);
        return LaneTop + Lane - LanePad - (value - min) / (double)Math.Max(1, max - min) * Math.Max(1, Lane - 2 * LanePad);
    }

    private int ControllerAt(double y)
    {
        var min = PianoRollControllers.Min(LaneId);
        var max = PianoRollControllers.Max(LaneId);
        return PianoRollControllers.Clamp(LaneId, (int)Math.Round(min + (LaneTop + Lane - LanePad - y) / Math.Max(1, Lane - 2 * LanePad) * (max - min)));
    }

    private bool PressController(Point p)
    {
        _gesture = Gesture.Controller;
        _pressBeat = PianoRollSnap.Floor(Presenter.Map, Snap(), BeatAt(p.X), _alt);
        _dragBeat = _pressBeat;
        _pressValue = ControllerAt(p.Y);
        _moved = true;
        return true;
    }

    /// <summary>The lane edit of the drag: a line of points on the snap step (or an erased span with the Erase tool), as one step.</summary>
    private Task<bool> CommitController()
    {
        var to = PianoRollSnap.Floor(Presenter.Map, Snap(), _dragBeat, _alt);
        if (Presenter.Tool == PianoRollTool.Erase)
            return Presenter.CommitPointsAsync(LaneId, PianoRollControllers.Erase(_points, _pressBeat, to + StepAt(to) / 2), "Erase controller");
        var points = PianoRollControllers.DrawLine(_points, LaneId, _pressBeat, _pressValue, to, ControllerAt(_now.Y), StepAt(_pressBeat));
        return Presenter.CommitPointsAsync(LaneId, points, "Draw controller");
    }

    private void DragVelocityTool(Point p)
    {
        _velocityPreview.Clear();
        var delta = (int)Math.Round((_press.Y - p.Y) / VelocityToolPixels);
        foreach (var i in Presenter.Selected) _velocityPreview[i] = PianoRollEdits.ClampVelocity(Presenter.Notes[i].Velocity + delta);
        if (_velocityPreview.TryGetValue(_pressNote, out var value) && value != _shownValue) { _shownValue = value; ValuePreview?.Invoke(value); }   // the info line follows
    }

    private int _shownValue = -1;

    /// <summary>A value drag's current value (the pressed note's), for the info line; raised only when it changes.</summary>
    internal Action<int>? ValuePreview { get; set; }

    private void DrawControllerLane(IDrawContext d)
    {
        var colour = TrackColour() ?? Colours.Get("AccentBrush");
        var rest = new UiStroke(Ink("PianoRollLineBeat"), Line);
        d.Line(rest, GridLeft, ControllerY(PianoRollControllers.Centre(LaneId)), GridLeft + ViewportWidth, ControllerY(PianoRollControllers.Centre(LaneId)));
        var points = _gesture == Gesture.Controller ? PreviewPoints() : _points;
        var stroke = new UiStroke(colour, StrongLine);
        var value = PianoRollControllers.Centre(LaneId);
        var x = GridLeft;
        foreach (var point in points)
        {
            var px = XOf(point.Beat);
            d.Line(stroke, x, ControllerY(value), px, ControllerY(value));
            d.Line(stroke, px, ControllerY(value), px, ControllerY(point.Value));
            d.Ellipse(colour, null, px, ControllerY(point.Value), PointRadius, PointRadius);
            (x, value) = (px, point.Value);
        }
        d.Line(stroke, x, ControllerY(value), GridLeft + ViewportWidth, ControllerY(value));
    }

    private IReadOnlyList<PianoRollPoint> PreviewPoints()
    {
        var to = PianoRollSnap.Floor(Presenter.Map, Snap(), _dragBeat, _alt);
        return Presenter.Tool == PianoRollTool.Erase ? PianoRollControllers.Erase(_points, _pressBeat, to)
            : PianoRollControllers.DrawLine(_points, LaneId, _pressBeat, _pressValue, to, ControllerAt(_now.Y), StepAt(_pressBeat));
    }

    /// <summary>Step input: a note (or the chord stamp) of one snap step at the step cursor; the cursor moves on.</summary>
    /// <summary>With <paramref name="sameStep"/> (keys played together) the note joins the last step's chord instead of starting a new step.</summary>
    internal void StepAdd(int pitch, int velocity = DefaultVelocity, bool sameStep = false)
    {
        if (_pending) return;
        if (sameStep && _lastStepLength > 0) StepBeat -= _lastStepLength;
        var length = NewLength(StepBeat);
        var root = new PianoRollNote(StepBeat, length, pitch, PianoRollEdits.ClampVelocity(velocity), Voice: DrawVoice);
        Run(Presenter.CommitAsync(PianoRollEdits.AddMany(Presenter.Notes, PianoRollEdits.Chord(root, Chord)), PianoRollEditKind.Notes, "Step input"));
        StepBeat += length;
        _lastStepLength = length;
        Reveal(StepBeat);
    }

    private double _lastStepLength;

    private bool StepKey(Key key)
    {
        var step = NewLength(StepBeat);
        switch (key)
        {
            case Key.Right: StepBeat += step; break;
            case Key.Left: StepBeat = Math.Max(0, StepBeat - step); break;
            case Key.Back:
                StepBeat = Math.Max(0, StepBeat - step);
                var at = Enumerable.Range(0, Presenter.Notes.Count).Where(i => Math.Abs(Presenter.Notes[i].Start - StepBeat) < 1e-6).ToList();
                if (at.Count > 0) Run(Presenter.CommitAsync(PianoRollEdits.Delete(Presenter.Notes, at), PianoRollEditKind.Notes, "Step back"));
                break;
            default: return false;
        }
        Reveal(StepBeat);
        InvalidateVisual();
        return true;
    }
}
