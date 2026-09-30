using System.Windows.Media;

namespace TabForge.Shell;

/// <summary>
/// A display-synchronised replacement for a DispatcherTimer: <see cref="Tick"/> fires once per frame
/// WPF actually renders (60/120/144 Hz — whatever the monitor runs at), aligned with composition, so
/// animations never judder against the refresh. It is only attached to CompositionTarget.Rendering
/// while started, so a stopped ticker costs nothing. An optional <see cref="Interval"/> caps the rate.
/// </summary>
public sealed class FrameTicker
{
    private bool _attached;
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private TimeSpan _lastTick = TimeSpan.MinValue;

    public event EventHandler? Tick;

    /// <summary>Minimum time between ticks; <see cref="TimeSpan.Zero"/> = every rendered frame.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.Zero;

    public bool IsEnabled => _attached;

    public void Start()
    {
        if (_attached) return;
        _attached = true;
        _lastTick = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_attached) return;
        _attached = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can be raised more than once per frame; RenderingTime identifies the frame.
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (now == _lastFrame) return;
        _lastFrame = now;
        if (Interval > TimeSpan.Zero && _lastTick != TimeSpan.MinValue && now - _lastTick < Interval - TimeSpan.FromMilliseconds(1)) return;
        _lastTick = now;
        Tick?.Invoke(this, EventArgs.Empty);
    }
}
