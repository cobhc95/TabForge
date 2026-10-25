using System.Threading;

namespace TabForge.Playback;

// Owns: the loop bounds the drawn playhead is held within while a song loops, and the hold rule: the position waits just before the loop end
//     until the scheduler wraps (the clock runs on between scheduler ticks), and after a wrap it never shows the bar before the loop start while
//     the output latency catches up.
// Does not own: the bounds themselves (PlaybackOrder.LoopBounds, computed by the scheduler), the wrap (PlaybackEngine's scheduler), the drawing.
// Tests: TabForge.Avalonia.Tests.TransportLoopCountInTests (LoopOverBarsThreeAndFourWrapsBackToBarThree).
// Threads: Set runs on the scheduler thread, Hold on the UI thread; the two bounds are read and written with Volatile.
public sealed class LoopPlayheadHold
{
    /// <summary>How far before the loop end the drawn position waits for the wrap (inside the loop's last bar), in ms.</summary>
    public const double EndHoldMs = 0.5;

    private double _startMs = double.NaN;
    private double _endMs = double.NaN;

    /// <summary>The loop bounds the scheduler wraps within (timeline ms); unavailable bounds hold nothing.</summary>
    public void Set(double startMs, double endMs, bool available)
    {
        Volatile.Write(ref _startMs, available ? startMs : double.NaN);
        Volatile.Write(ref _endMs, available ? endMs : double.NaN);
    }

    /// <summary>The drawn position for <paramref name="ms"/>: unchanged unless <paramref name="looping"/>; the start floor applies once the loop has <paramref name="wrapped"/>.</summary>
    public double Hold(double ms, bool looping, bool wrapped)
    {
        var end = Volatile.Read(ref _endMs);
        if (!looping || double.IsNaN(end)) return ms;
        if (ms >= end) return end - EndHoldMs;
        var start = Volatile.Read(ref _startMs);
        return wrapped && ms < start ? start : ms;
    }
}
