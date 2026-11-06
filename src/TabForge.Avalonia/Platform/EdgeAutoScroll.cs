using System;
using Avalonia.Threading;

namespace TabForge.Avalonia.Platform;

// Owns: scrolling a view while a held drag-select has been dragged past its top or bottom edge (the score, the timeline lanes, the piano roll):
//   the step per tick (it grows with the distance past the edge, capped), the stop at the content end and when the pointer comes back inside,
//   the one timer that runs only while the pointer is past an edge during such a drag, and the hold it puts on the view's playhead follow
//   meanwhile (HoldsFollow: from the first move past an edge until the pointer is back inside or the drag ends, the content end included).
// Does not own: the drag itself or its rectangle (each view keeps them, and its follow callback moves them by the scrolled amount), the
//   scroll range (each view's scroll callback clamps and reports what it applied).
// Tests: TabForge.Avalonia.Tests.EdgeAutoScrollTests.
// Threads: UI thread only.
internal sealed class EdgeAutoScroll
{
    /// <summary>The tick of the scroll while the pointer is past an edge (ms), and the step (DIPs per tick): the least, the growth per DIP past the edge, the most.</summary>
    public const double TickMs = 16, MinStep = 2, StepPerDip = 0.25, MaxStep = 40;

    private readonly Func<double, double> _scrollBy;
    private readonly Action<double> _follow;
    private DispatcherTimer? _timer;
    private double _step;

    /// <param name="scrollBy">Scrolls the view by the given DIPs (positive is down) within its range and returns the DIPs it really moved.</param>
    /// <param name="follow">Moves the drag by the DIPs just scrolled (the held pointer now lies that far further into the content).</param>
    public EdgeAutoScroll(Func<double, double> scrollBy, Action<double> follow)
    {
        _scrollBy = scrollBy;
        _follow = follow;
    }

    /// <summary>True while a held drag is past the view's edge (also once the content end stopped the scroll): the view's playhead follow waits.</summary>
    public bool HoldsFollow { get; private set; }

    /// <summary>True while the pointer is past an edge and the view scrolls on each tick.</summary>
    public bool IsScrolling => _timer?.IsEnabled == true;

    /// <summary>
    /// The scroll per tick for a pointer at <paramref name="pointer"/> along a view from <paramref name="start"/> to <paramref name="end"/>: 0 inside
    /// the view (on its edge too), else up (negative) or down (positive), growing with the distance past the edge up to <see cref="MaxStep"/>.
    /// </summary>
    public static double Step(double pointer, double start, double end)
    {
        if (!(end > start) || double.IsNaN(pointer)) return 0;
        var past = pointer < start ? pointer - start : pointer > end ? pointer - end : 0;
        if (past == 0) return 0;
        return Math.Sign(past) * Math.Min(MaxStep, MinStep + Math.Abs(past) * StepPerDip);
    }

    /// <summary>The DIPs a scroll by <paramref name="step"/> moves from <paramref name="offset"/> inside 0..<paramref name="maxOffset"/> (0 at the content end).</summary>
    public static double Clamped(double offset, double step, double maxOffset) =>
        Math.Clamp(offset + step, 0, Math.Max(0, maxOffset)) - offset;

    /// <summary>A held drag moved: scrolls on each tick while the pointer is past the view's edge, and stops once it is back inside.</summary>
    public void Update(double pointer, double start, double end)
    {
        _step = Step(pointer, start, end);
        if (_step == 0) { Stop(); return; }
        HoldsFollow = true;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(TickMs), DispatcherPriority.Input, (_, _) => Tick());
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>The drag ended (release, Escape or a lost capture): no more scrolling.</summary>
    public void Stop()
    {
        HoldsFollow = false;
        _timer?.Stop();
    }

    private void Tick()
    {
        var moved = _scrollBy(_step);
        if (moved == 0) { _timer?.Stop(); return; }   // the content end: the follow stays held; the next pointer move past the edge tries again
        _follow(moved);
    }
}
