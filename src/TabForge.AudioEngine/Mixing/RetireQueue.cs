namespace TabForge.AudioEngine.Mixing;

/// <summary>
/// The audio callback's epoch: incremented when a callback starts and again when it ends, so the value is odd
/// exactly while a callback runs. Anything the callback could have picked up before it was replaced is safe to
/// dispose once the epoch has moved past the value seen at replacement (see <see cref="RetireQueue"/>).
/// <see cref="Enter"/> / <see cref="Exit"/> are allocation-free and lock-free.
/// </summary>
public sealed class CallbackEpoch
{
    private long _value;

    /// <summary>Audio thread, first thing in the callback (a full fence: later reads of the chains cannot move above it).</summary>
    public void Enter() => Interlocked.Increment(ref _value);
    /// <summary>Audio thread, last thing in the callback.</summary>
    public void Exit() => Interlocked.Increment(ref _value);

    /// <summary>
    /// Engine thread, after the object was unpublished: the epoch now (a full fence first, so the unpublishing write is
    /// visible to any callback that starts after this read).
    /// </summary>
    public long Sample()
    {
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref _value);
    }

    public bool InCallback => (Sample() & 1) != 0;

    /// <summary>
    /// True once no callback that might hold an object retired at <paramref name="retiredAt"/> is still running:
    /// retired outside a callback (even) means immediately; retired during one (odd) means once that callback has exited.
    /// </summary>
    public bool HasPassed(long retiredAt) => Sample() >= ((retiredAt + 1) & ~1L);
}

/// <summary>
/// Objects the audio callback may still be using (replaced chains, plug-ins, clip players). Each is disposed on the
/// engine's main thread by <see cref="Collect"/> once its epoch barrier has passed - never on the real-time thread and
/// never on a fixed delay, so a stalled callback (a slow native plug-in) delays disposal instead of racing it.
/// Main thread only.
/// </summary>
public sealed class RetireQueue
{
    private readonly List<(IDisposable Item, CallbackEpoch? Epoch, long RetiredAt)> _items = new();

    public int Count => _items.Count;

    /// <summary>Queues <paramref name="item"/>; call after it was unpublished from the audio thread. A null epoch (no engine) frees it on the next collect.</summary>
    public void Add(IDisposable item, CallbackEpoch? epoch) => _items.Add((item, epoch, epoch?.Sample() ?? 0));

    /// <summary>Disposes every item the callback has provably let go of; returns how many.</summary>
    public int Collect()
    {
        var disposed = 0;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var (item, epoch, retiredAt) = _items[i];
            if (epoch is not null && !epoch.HasPassed(retiredAt)) continue;
            _items.RemoveAt(i);
            DisposeQuietly(item);
            disposed++;
        }
        return disposed;
    }

    /// <summary>Shutdown (audio stopped): disposes everything left.</summary>
    public void DisposeAll()
    {
        foreach (var (item, _, _) in _items) DisposeQuietly(item);
        _items.Clear();
    }

    private static void DisposeQuietly(IDisposable item)
    {
        try { item.Dispose(); }
        catch (Exception ex) { EngineLog.Write($"dispose failed: {ex.Message}"); }
    }
}
