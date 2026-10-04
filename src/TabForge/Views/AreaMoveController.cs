using System.Windows.Input;

namespace TabForge.Views;

/// <summary>What the area move needs from the timeline (implemented by <see cref="TrackTimeline"/>).</summary>
internal interface IAreaMoveHost
{
    bool Focusable { get; set; }
    Cursor Cursor { get; set; }
    bool Focus();
    void InvalidateVisual();
    void RefreshOverlay();
    int BarAt(double x);
    double XOfBar(int bar);
    /// <summary>The move ended: the insert-before bar, or -1 when cancelled.</summary>
    void RaiseAreaMoveFinished(int target);
}

// Owns: the "move selection" gesture state (active, selected bar range, insert-before target) and its target picking.
// Does not own: the outline over the moved bars (ArrangementPanel), the bar move itself (the host of AreaMoveFinished),
// drawing the target line (TrackTimeline.Render.cs reads Target).
// Tests: TestTimelineBasics.
/// <summary>Moving the selected bar range on the timeline: the next click picks where it goes, Esc or a right-click cancels.</summary>
internal sealed class AreaMoveController
{
    private readonly IAreaMoveHost _host;

    public AreaMoveController(IAreaMoveHost host) => _host = host;

    internal bool Active { get; private set; }
    internal int Start { get; private set; }
    internal int End { get; private set; }
    /// <summary>Insert-before bar under the pointer (-1 = none yet).</summary>
    internal int Target { get; private set; } = -1;

    internal void Begin(int start, int end)
    {
        Active = true;
        Start = start;
        End = end;
        Target = -1;
        _host.Focusable = true;
        _host.Focus();
        _host.Cursor = Cursors.SizeWE;
    }

    internal void Finish(int target)
    {
        Active = false;
        Target = -1;
        _host.Cursor = null!;
        _host.InvalidateVisual();
        _host.RaiseAreaMoveFinished(target);
    }

    /// <summary>The pointer moved during the move: the target line follows the nearest bar boundary.</summary>
    internal void PointerMoved(double x)
    {
        var target = TargetAt(x);
        if (target != Target) { Target = target; _host.RefreshOverlay(); }
    }

    // Insert-before position nearest the pointer (bar boundary).
    internal int TargetAt(double x)
    {
        var bar = _host.BarAt(x);
        return x - _host.XOfBar(bar) > (_host.XOfBar(bar + 1) - _host.XOfBar(bar)) / 2 ? bar + 1 : bar;
    }
}
