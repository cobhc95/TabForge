using System.Diagnostics;

namespace TabForge.AudioEngine.Mixing;

/// <summary>
/// Real-time health of the audio callback: how long each callback took against its deadline (block duration), how
/// regularly it was called, deadline misses (a likely audible dropout), late calls, and bytes allocated on the audio
/// thread. Recorded lock-free and allocation-free by the audio thread; read (and reset) by the engine thread.
/// Durations go into a fixed histogram of 0.1 ms buckets (0–50 ms) for p95 / p99 / max.
/// </summary>
public sealed class CallbackMetrics
{
    private const int Buckets = 500;               // 0.1 ms each, the last bucket catches everything longer
    private readonly long[] _histogram = new long[Buckets];
    private long _calls, _misses, _late, _allocated, _maxTicks;
    private long _lastStart;
    private int _blockFrames;                    // frames per callback the device actually delivers (the last one seen)

    /// <summary>Audio thread, once per callback.</summary>
    public void Record(long startTicks, long endTicks, int frames, int sampleRate, long allocatedBytes)
    {
        var duration = endTicks - startTicks;
        var deadline = frames * (double)Stopwatch.Frequency / Math.Max(1, sampleRate);
        var bucket = (int)Math.Min(Buckets - 1, duration * 10_000.0 / Stopwatch.Frequency);
        Interlocked.Increment(ref _histogram[bucket]);
        Interlocked.Increment(ref _calls);
        if (duration > deadline) Interlocked.Increment(ref _misses);
        // Called well after the previous callback's period: the device (or the OS) starved the callback.
        var last = _lastStart;
        if (last != 0 && startTicks - last > deadline * 1.5) Interlocked.Increment(ref _late);
        _lastStart = startTicks;
        _blockFrames = frames;
        if (allocatedBytes > 0) Interlocked.Add(ref _allocated, allocatedBytes);
        if (duration > Volatile.Read(ref _maxTicks)) Volatile.Write(ref _maxTicks, duration);
    }

    private long _midiDeferred;

    /// <summary>
    /// M-05, audio thread: a callback found the pending-MIDI list full (the rest waits in the shared ring: late, not lost). Messages a
    /// chain's buffers had no room for are dropped and counted in <see cref="TrackChain.MidiDropped"/>.
    /// </summary>
    public void CountMidiDeferred() => Interlocked.Increment(ref _midiDeferred);

    /// <param name="MidiDropped">MIDI messages dropped because a chain's event buffer was full (all chains of the process).</param>
    /// <param name="MidiDeferred">Callbacks that left MIDI in the shared ring because the pending list was full.</param>
    public readonly record struct Snapshot(long Calls, double P95Ms, double P99Ms, double MaxMs, long DeadlineMisses, long LateCalls, long AllocatedBytes,
        long MidiDropped = 0, long MidiDeferred = 0, int BlockFrames = 0);

    /// <summary>Engine thread: the figures since the last call, then starts a new window.</summary>
    public Snapshot TakeAndReset()
    {
        var counts = new long[Buckets];
        for (var i = 0; i < Buckets; i++) counts[i] = Interlocked.Exchange(ref _histogram[i], 0);
        var calls = Interlocked.Exchange(ref _calls, 0);
        var snapshot = new Snapshot(calls, Percentile(counts, calls, 0.95), Percentile(counts, calls, 0.99),
            Interlocked.Exchange(ref _maxTicks, 0) * 1000.0 / Stopwatch.Frequency,
            Interlocked.Exchange(ref _misses, 0), Interlocked.Exchange(ref _late, 0), Interlocked.Exchange(ref _allocated, 0),
            Interlocked.Exchange(ref TrackChain.MidiDropped, 0), Interlocked.Exchange(ref _midiDeferred, 0), Volatile.Read(ref _blockFrames));
        return snapshot;
    }

    private static double Percentile(long[] counts, long total, double q)
    {
        if (total <= 0) return 0;
        var target = (long)Math.Ceiling(total * q);
        long seen = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            seen += counts[i];
            if (seen >= target) return (i + 1) / 10.0;
        }
        return counts.Length / 10.0;
    }
}
