using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: Shift+wheel while notes are selected (0.8.6): the shown lane's values move by one per wheel notch (ten with Ctrl), clamped to the lane's
//       range: the selected notes' velocities, or a controller lane's points inside the selected notes' time span (pitch bend and program too).
//       A burst of notches is one preview and one undo step; it commits when the wheel rests, before the next press or key, when the selection
//       or lane changes, or when the surface leaves the screen. With no selection (or no lane points in the span) Shift+wheel keeps scrolling the keys.
// Does not own: the scroll (PianoRollSurface.Input), the lane drawing (PianoRollSurface.Lanes, PianoRollSurface.Drawing), the velocity commit
//       (PianoRollSurface.Velocity) or the controller commit (PianoRollPresenter.CommitPointsAsync).
// Tests: TabForge.Avalonia.Tests.PianoRollWheelTests.
public sealed partial class PianoRollSurface
{
    private static readonly TimeSpan WheelRest = TimeSpan.FromMilliseconds(500);
    private const int LaneWheelStep = 1, LaneWheelCtrlStep = 10;
    private const double SpanEpsilon = 1e-6;

    private DispatcherTimer? _wheelRest;
    private int[]? _wheelNotes;                            // the selection the pending burst adjusts (null: no burst pending)
    private int _wheelLane;                                // the lane the pending burst adjusts
    private IReadOnlyList<PianoRollPoint>? _wheelPoints;   // a controller lane's preview points (null for the velocity lane)

    /// <summary>True while wheel notches wait to commit as one step.</summary>
    internal bool LaneWheelPending => _wheelNotes is not null;

    /// <summary>Shift+wheel with notes selected: moves the shown lane's values by the notches and returns true. False leaves the wheel to scroll.</summary>
    private bool LaneWheel(double notches, bool ctrl)
    {
        if (!LaneShown || Busy || Presenter.Track is null) return false;
        var notes = Presenter.Notes;
        var selected = Presenter.Selected.Where(i => i >= 0 && i < notes.Count).OrderBy(i => i).ToArray();
        if (selected.Length == 0) { FlushLaneWheel(); return false; }
        if (_wheelNotes is not null && (_wheelLane != LaneId || !_wheelNotes.SequenceEqual(selected))) FlushLaneWheel();   // the burst was for another selection or lane
        if (_wheelNotes is null && !StartLaneWheel(selected)) return false;
        var step = (ctrl ? LaneWheelCtrlStep : LaneWheelStep) * Math.Sign(notches);
        if (_wheelLane == PianoRollControllers.Velocity)
            foreach (var i in selected) _velocityPreview[i] = PianoRollEdits.ClampVelocity(_velocityPreview[i] + step);
        else
        {
            var (from, to) = SpanOf(selected);
            _wheelPoints = _wheelPoints!.Select(p => p.Beat >= from - SpanEpsilon && p.Beat <= to + SpanEpsilon
                ? p with { Value = PianoRollControllers.Clamp(_wheelLane, p.Value + step) } : p).ToList();
        }
        RestLaneWheel();
        InvalidateVisual();
        return true;
    }

    /// <summary>Starts a burst: the preview holds the selection's velocities (or the lane's points) as they are now. False: nothing to adjust.</summary>
    private bool StartLaneWheel(int[] selected)
    {
        _wheelLane = LaneId;
        if (_wheelLane == PianoRollControllers.Velocity)
        {
            _velocityPreview.Clear();
            foreach (var i in selected) _velocityPreview[i] = Presenter.Notes[i].Velocity;
        }
        else
        {
            var (from, to) = SpanOf(selected);
            if (!_points.Any(p => p.Beat >= from - SpanEpsilon && p.Beat <= to + SpanEpsilon)) return false;
            _wheelPoints = _points.ToList();
        }
        _wheelNotes = selected;
        return true;
    }

    /// <summary>The time span the selected notes cover, in beats (from the first start to the last end).</summary>
    private (double From, double To) SpanOf(int[] selected)
    {
        var notes = Presenter.Notes;
        return (selected.Min(i => notes[i].Start), selected.Max(i => notes[i].End));
    }

    /// <summary>Commits the pending wheel notches as one undo step (the values the wheel showed). Nothing when none are pending.</summary>
    internal void FlushLaneWheel()
    {
        _wheelRest?.Stop();
        if (_wheelNotes is null) return;
        var lane = _wheelLane;
        var points = _wheelPoints;
        _wheelNotes = null;
        _wheelPoints = null;
        if (lane == PianoRollControllers.Velocity)
        {
            var commit = Presenter.CommitAsync(VelocityResult(), PianoRollEditKind.Expression, "Adjust velocity");
            _velocityPreview.Clear();
            Run(commit);
        }
        else Run(Presenter.CommitPointsAsync(lane, points ?? Array.Empty<PianoRollPoint>(), "Adjust " + PianoRollControllers.NameOf(lane)));
    }

    /// <summary>Restarts the rest timer: the burst commits once the wheel has been still for the rest time.</summary>
    private void RestLaneWheel()
    {
        if (_wheelRest is null)
        {
            _wheelRest = new DispatcherTimer { Interval = WheelRest };
            _wheelRest.Tick += (_, _) => FlushLaneWheel();
        }
        _wheelRest.Stop();
        _wheelRest.Start();
    }

    /// <summary>A surface that leaves the screen commits what the wheel showed, so no notch is lost.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FlushLaneWheel();
        base.OnDetachedFromVisualTree(e);
    }
}
