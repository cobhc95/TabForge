using System.Diagnostics;
using NAudio.Wave;

namespace TabForge.AudioEngine.Output;

// Owns: the DirectSound output's sizing rule (buffer from the user's block size, never below a floor) and its refill monitor: every refill NAudio's
//   polling thread asks of the mix is timed, so a late or slow one is counted and shows in the engine log (at most one line per second).
// Does not own: opening the device (AudioOutputFactory) or the mix itself (MixEngine).
// Tests: DirectSoundWatchTests.

/// <summary>
/// Wraps the mix for DirectSound. NAudio's DirectSoundOut owns a buffer of two halves, each <c>latency</c> ms long, and asks for one
/// half per notification, so a refill is <c>latency</c> ms of audio and the whole buffer is twice that. The wrapper raises the priority
/// of NAudio's polling thread on its first refill, and counts refills that came late (gap above 1.5 halves), ran dry (gap above two
/// halves: the device played silence or repeated data, an audible crackle) or were slow to render (over half a period).
/// The audio thread only writes counters; the log line is posted to the engine main thread, at most once per second.
/// </summary>
internal sealed class DirectSoundWatch : ISampleProvider
{
    /// <summary>The smallest <c>latency</c> given to NAudio (ms per half): below it the refill thread is starved by ordinary scheduling jitter.</summary>
    public const int MinLatencyMs = 100;

    /// <summary>The <c>latency</c> for the user's block size: four blocks, never below <see cref="MinLatencyMs"/>.</summary>
    public static int LatencyMs(int bufferFrames, int sampleRate)
    {
        var blockMs = Math.Max(1, (int)Math.Ceiling(bufferFrames * 1000.0 / Math.Max(1, sampleRate)));
        return Math.Max(MinLatencyMs, blockMs * 4);
    }

    private readonly ISampleProvider _source;
    private readonly EngineThreads? _threads;
    private readonly Action _report;
    private readonly double _periodMs;
    private long _last, _calls, _late, _dry, _slow, _maxGapTicks, _maxCostTicks, _lastReportTicks, _reportedBad;
    private bool _raised;

    public DirectSoundWatch(ISampleProvider source, int latencyMs, EngineThreads? threads)
    {
        _source = source; _threads = threads; _periodMs = latencyMs;
        _report = Report;   // once, so posting a report allocates nothing
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Refills seen, late ones, ones that ran the buffer dry, and slow renders (totals since the output opened).</summary>
    public (long Calls, long Late, long Dry, long Slow) Counts => (Volatile.Read(ref _calls), Volatile.Read(ref _late), Volatile.Read(ref _dry), Volatile.Read(ref _slow));

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_raised) { _raised = true; _threads?.RaiseRenderPriority(); }
        var start = Stopwatch.GetTimestamp();
        var read = _source.Read(buffer, offset, count);
        Observe(start, Stopwatch.GetTimestamp());
        return read;
    }

    /// <summary>One refill that began at <paramref name="start"/> and ended at <paramref name="end"/> (Stopwatch ticks). Audio thread: no allocation, no lock.</summary>
    internal void Observe(long start, long end)
    {
        var calls = _calls + 1;
        Volatile.Write(ref _calls, calls);
        var last = _last; _last = start;
        if (calls <= 4 || last == 0) return;   // the first calls prefill the buffer back to back
        var ticksPerMs = Stopwatch.Frequency / 1000.0;
        var gap = start - last; var cost = end - start;
        if (gap > _maxGapTicks) Volatile.Write(ref _maxGapTicks, gap);
        if (cost > _maxCostTicks) Volatile.Write(ref _maxCostTicks, cost);
        var bad = false;
        if (gap > 2 * _periodMs * ticksPerMs) { Volatile.Write(ref _dry, _dry + 1); bad = true; }
        else if (gap > 1.5 * _periodMs * ticksPerMs) { Volatile.Write(ref _late, _late + 1); bad = true; }
        if (cost > 0.5 * _periodMs * ticksPerMs) { Volatile.Write(ref _slow, _slow + 1); bad = true; }
        if (!bad || _threads is null) return;
        if (_lastReportTicks != 0 && end - _lastReportTicks < Stopwatch.Frequency) return;   // one line per second
        _lastReportTicks = end;
        _threads.Post(_report);
    }

    /// <summary>Engine main thread: the counters so far, with the worst gap and render time seen.</summary>
    private void Report()
    {
        var (calls, late, dry, slow) = Counts;
        var bad = late + dry + slow;
        if (bad == _reportedBad) return;
        _reportedBad = bad;
        var perMs = Stopwatch.Frequency / 1000.0;
        _threads?.Log.Write($"DirectSound refills: {calls} so far, late {late}, ran dry {dry}, slow {slow}; worst gap {Volatile.Read(ref _maxGapTicks) / perMs:F1} ms and render {Volatile.Read(ref _maxCostTicks) / perMs:F1} ms (a refill is {_periodMs:F0} ms of audio; buffer {2 * _periodMs:F0} ms)");
    }
}
