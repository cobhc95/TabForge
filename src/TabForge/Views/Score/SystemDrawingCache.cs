using System.Windows.Media;

namespace TabForge.Views.Score;

/// <summary>
/// The retained engraving of each score system: a frozen drawing per system index, replayed while the system's playback
/// signature is unchanged. The cache is tied to one layout instance and one DPI key; either changing empties it. The
/// owner decides the signature and what must always be drawn live.
/// </summary>
internal sealed class SystemDrawingCache
{
    /// <summary>Systems kept before the ones that scrolled well out of view are dropped.</summary>
    private const int TrimThreshold = 24;

    private readonly Dictionary<int, (long Signature, Drawing Drawing)> _drawings = new();
    private object? _layout;
    private int _dpi = -1;

    /// <summary>Drops every retained drawing.</summary>
    public void Clear() => _drawings.Clear();

    /// <summary>Empties the cache when the layout instance or the DPI key differs from the one it was built for.</summary>
    public void Bind(object layout, int dpiKey)
    {
        if (ReferenceEquals(_layout, layout) && _dpi == dpiKey) return;
        _drawings.Clear();
        _layout = layout;
        _dpi = dpiKey;
    }

    /// <summary>Forgets one system (it is drawn live this pass).</summary>
    public void Remove(int system) => _drawings.Remove(system);

    /// <summary>The retained drawing for the system when its signature still matches.</summary>
    public bool TryGet(int system, long signature, out Drawing drawing)
    {
        if (_drawings.TryGetValue(system, out var cached) && cached.Signature == signature)
        {
            drawing = cached.Drawing;
            return true;
        }
        drawing = null!;
        return false;
    }

    /// <summary>Keeps a frozen drawing for the system under its signature.</summary>
    public void Store(int system, long signature, Drawing drawing) => _drawings[system] = (signature, drawing);

    /// <summary>Drops systems more than two beyond the drawn band once the cache has grown past its threshold.</summary>
    public void Trim(int firstSystem, int lastSystem)
    {
        if (_drawings.Count <= TrimThreshold) return;
        foreach (var key in _drawings.Keys.Where(k => k < firstSystem - 2 || k > lastSystem + 2).ToList())
            _drawings.Remove(key);
    }

    /// <summary>The retained drawings in system order (layout audit).</summary>
    public IReadOnlyList<(int System, Drawing Drawing)> Snapshot() =>
        _drawings.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value.Drawing)).ToList();
}
