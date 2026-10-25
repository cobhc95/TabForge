namespace TabForge.Platform;

/// <summary>
/// A dictionary with a ceiling: when it is full, the entry read or written least recently goes. Reading an entry makes it the most recent one.
/// Owns: the recency order of its entries and the ceiling. Does not own: what the values are, or anything they hold (no disposal).
/// Thread: not thread-safe; the owner uses it from one thread (the UI thread for the Avalonia draw caches).
/// Tests: BoundedLruCacheTests (TabForge.Avalonia.Tests).
/// </summary>
public sealed class BoundedLruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _nodes = new();
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _recency = new();

    /// <summary>The most entries held at once; a write beyond it drops the least recent one.</summary>
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity), "A cache holds at least one entry.");

    public int Count => _nodes.Count;

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
        if (_nodes.TryGetValue(key, out var existing))
        {
            _recency.Remove(existing);
            _nodes.Remove(key);
        }
        else if (_nodes.Count >= Capacity)
        {
            var least = _recency.Last!;
            _recency.RemoveLast();
            _nodes.Remove(least.Value.Key);
        }
        _nodes[key] = _recency.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
    }
}
