using System;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the eviction order of <see cref="BoundedLruCache{TKey,TValue}"/>: a full cache drops the entry used least recently, a read refreshes an
///   entry, and a re-add replaces the value without growing the cache.
/// Does not own: what the draw caches store (AvaloniaDrawContextTests, AvaloniaPathCacheTests).
/// Tests: this class.
/// </summary>
public class BoundedLruCacheTests
{
    [Fact]
    public void FullCacheDropsTheLeastRecentlyUsedEntryAndKeepsHotOnes()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.Add("a", 1);
        cache.Add("b", 2);
        Assert.True(cache.TryGet("a", out var a) && a == 1);   // "a" is now the most recent; "b" is the least
        cache.Add("c", 3);

        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out var c) && c == 3);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void ReAddReplacesTheValueWithoutEvicting()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.Add("a", 1);
        cache.Add("b", 2);
        cache.Add("a", 10);

        Assert.True(cache.TryGet("a", out var a) && a == 10);
        Assert.True(cache.TryGet("b", out var b) && b == 2);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void NonPositiveCapacityFailsLoudly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLruCache<string, int>(0));
    }

    [Fact]
    public void WeightBudgetEvictsLruAndBypassesOversizedValues()
    {
        var cache = new BoundedLruCache<string, int>(10, 8, static (_, value) => value);
        cache.Add("a", 3);
        cache.Add("b", 3);
        Assert.True(cache.TryGet("a", out _));
        cache.Add("c", 4);
        Assert.False(cache.TryGet("b", out _));
        Assert.Equal(7, cache.Weight);
        cache.Add("huge", 9);
        Assert.Equal(7, cache.Weight);
        Assert.False(cache.TryGet("huge", out _));
        cache.Add("a", 1);
        Assert.Equal(5, cache.Weight);
        Assert.Equal(2, cache.Count);
    }
}
