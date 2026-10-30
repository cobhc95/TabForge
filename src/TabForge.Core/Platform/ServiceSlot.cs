namespace TabForge.Platform;

// Owns: one process-wide service held behind a static readonly slot, so a static facade can delegate to an instance and a test can swap it.
// Does not own: the service or its state; the slot only keeps the current instance.
// Tests: ScopedServiceTests.

/// <summary>
/// Holds the current instance of a process-wide service. The slot itself is a static readonly field; the mutable part is the
/// instance it points at, so a static facade delegates instead of owning static mutable state.
/// </summary>
public sealed class ServiceSlot<T> where T : class
{
    private T _current;

    public ServiceSlot(T current) => _current = current;

    /// <summary>The current instance (a reference swap is visible to every thread).</summary>
    public T Current => Volatile.Read(ref _current);

    /// <summary>Makes <paramref name="replacement"/> current until the returned scope is disposed (which puts the previous one back).</summary>
    public IDisposable Use(T replacement)
    {
        var previous = Interlocked.Exchange(ref _current, replacement);
        return new Restore(this, previous);
    }

    private sealed class Restore(ServiceSlot<T> slot, T previous) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref slot._current, previous);
    }
}
