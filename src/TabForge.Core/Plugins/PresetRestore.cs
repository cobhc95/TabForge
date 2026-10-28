using System.Text.Json;

namespace TabForge.Plugins;

// Owns: the two things a store needs to give a deleted preset back and to move presets in and out as files: reading one preset's bytes and
//   writing bytes back at a place in the order (IPresetRestorable), and the delete that can be taken back (PresetDeleteUndo).
// Does not own: the stores' files (PresetFolderStore, DrumMapStore, EffectPresetListStore), the question words or the toast that offers Undo.
// Tests: PresetUndoTests.

/// <summary>A preset store whose presets can be read as bytes and written back, so a delete can be undone and a preset exported or imported.</summary>
public interface IPresetRestorable : IPresetStore
{
    /// <summary>The file extension of an exported preset, with the dot (".json").</summary>
    string FileExtension { get; }
    /// <summary>The bytes of a preset; null when it cannot be read.</summary>
    byte[]? Snapshot(string name);
    /// <summary>Writes bytes as the preset <paramref name="name"/> at a place in the order (clamped); a preset of that name is replaced.</summary>
    void Restore(string name, byte[] bytes, int index);
    /// <summary>True when the bytes are a preset of this store (a bad file is refused before anything is written).</summary>
    bool Accepts(byte[] bytes);
}

/// <summary>One delete of one or more presets of one store, which <see cref="Undo"/> puts back where they were.</summary>
public sealed class PresetDeleteUndo
{
    private readonly IPresetRestorable? _store;
    private readonly List<(string Name, byte[] Bytes, int Index)> _kept;

    private PresetDeleteUndo(IPresetRestorable? store, List<(string Name, byte[] Bytes, int Index)> kept) => (_store, _kept) = (store, kept);

    /// <summary>The names deleted, in their order.</summary>
    public IReadOnlyList<string> Names => _kept.Select(k => k.Name).ToList();
    /// <summary>True when the presets were read before the delete, so <see cref="Undo"/> can restore them.</summary>
    public bool CanUndo => _store is not null && _kept.Count > 0;

    /// <summary>Deletes the presets at once, remembering each one's bytes and place. A store that cannot restore deletes them with no undo.</summary>
    public static PresetDeleteUndo Delete(IPresetStore store, IReadOnlyList<string> names)
    {
        var kept = new List<(string, byte[], int)>();
        var order = store.Names().ToList();
        if (store is IPresetRestorable restorable)
        {
            foreach (var name in names)
            {
                var index = order.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && restorable.Snapshot(name) is { } bytes) kept.Add((order[index], bytes, index));
            }
        }
        foreach (var name in names) store.Delete(name);
        return new PresetDeleteUndo(store as IPresetRestorable, kept.OrderBy(k => k.Item3).ToList());
    }

    /// <summary>Puts every deleted preset back at its old place (lowest place first, so each lands where it was).</summary>
    public void Undo()
    {
        if (_store is null) return;
        foreach (var (name, bytes, index) in _kept) _store.Restore(name, bytes, index);
    }
}

/// <summary>The checks every file-backed store shares for the bytes of an imported preset.</summary>
public static class PresetBytes
{
    /// <summary>True when the bytes parse as one JSON value (the form of every preset kind that is not a binary plug-in state).</summary>
    public static bool IsJson(byte[] bytes)
    {
        try { using var _ = JsonDocument.Parse(bytes); return true; }
        catch (JsonException) { return false; }
    }
}
