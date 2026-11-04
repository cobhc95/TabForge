namespace TabForge.Platform;

/// <summary>
/// A dictionary with a ceiling: when it is full, the entry read or written least recently goes. Reading an entry makes it the most recent one.
/// Owns: the recency order, entry ceiling and optional owner-supplied weight budget. Does not own: what the values hold (no disposal).
/// Thread: not thread-safe; the owner uses it from one thread (the UI thread for the Avalonia draw caches).
/// Tests: BoundedLruCacheTests (TabForge.Avalonia.Tests).
/// </summary>
public sealed class BoundedLruCache<TKey, TValue>(int capacity, long weightBudget = long.MaxValue, Func<TKey, TValue, long>? weightOf = null) where TKey : notnull
{
    private sealed record Entry(TKey Key, TValue Value, long Weight);
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _nodes = new();
    private readonly LinkedList<Entry> _recency = new();

    /// <summary>The most entries held at once; a write beyond it drops the least recent one.</summary>
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity), "A cache holds at least one entry.");

    public int Count => _nodes.Count;
    public long WeightBudget { get; } = weightBudget > 0 ? weightBudget : throw new ArgumentOutOfRangeException(nameof(weightBudget));
    public long Weight { get; private set; }

    /// <summary>Returns the value for <paramref name="key"/> and makes it the most recent entry; false when it is not held.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_nodes.TryGetValue(key, out var node))
        {
            if (!ReferenceEquals(node, _recency.First)) { _recency.Remove(node); _recency.AddFirst(node); }
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> as the most recent entry, replacing any value already there.</summary>
    public void Add(TKey key, TValue value)
    {
        var weight = weightOf?.Invoke(key, value) ?? 1;
        if (weight < 0) throw new ArgumentOutOfRangeException(nameof(value), "Cache weight must not be negative.");
        if (_nodes.TryGetValue(key, out var existing))
        {
            Weight -= existing.Value.Weight;
            _recency.Remove(existing);
            _nodes.Remove(key);
        }
        // Oversized values are returned to their caller, never retained or allowed to evict the hot small entries.
        if (weight > WeightBudget) return;
        while (_nodes.Count >= Capacity || Weight > WeightBudget - weight)
        {
            var least = _recency.Last!;
            Weight -= least.Value.Weight;
            _recency.RemoveLast();
            _nodes.Remove(least.Value.Key);
        }
        _nodes[key] = _recency.AddFirst(new Entry(key, value, weight));
        Weight += weight;
    }
}
