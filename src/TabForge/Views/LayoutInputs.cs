using System.Windows.Threading;

namespace TabForge.Views;

/// <summary>
/// Owns: "did the real inputs of this layout change?" (compare a small value tuple) and a restartable settle delay.
/// Does not own: the layout itself. A handler that reacts to SizeChanged/ScrollChanged recomputes only when
/// <see cref="Changed"/> says its own inputs differ (the score: width, zoom, mode; not height).
/// Tests: TestSplitterDragKeepsScore.
/// </summary>
internal struct InputGate<T> where T : IEquatable<T>
{
    private T _last;
    private bool _has;

    /// <summary>True the first time and whenever <paramref name="inputs"/> differ from the previous call; records them.</summary>
    public bool Changed(T inputs)
    {
        if (_has && _last.Equals(inputs)) return false;
        _last = inputs; _has = true;
        return true;
    }
}

/// <summary>Runs an action once, <c>delay</c> after the last <see cref="Request"/> (a drag that keeps moving keeps postponing it).</summary>
internal sealed class SettleAction
{
    private readonly DispatcherTimer _timer;

    public SettleAction(Action action, int delayMs = 100)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(delayMs) };
        _timer.Tick += (_, _) => { _timer.Stop(); action(); };
    }

    public void Request() { _timer.Stop(); _timer.Start(); }
    public void Cancel() => _timer.Stop();
}
