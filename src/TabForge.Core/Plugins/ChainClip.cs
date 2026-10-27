using System.Text.Json;

namespace TabForge.Plugins;

// Owns: copying rows of an FX chain to a piece of text (the plug-ins with their saved states and, for a container, its items) and making new plug-ins
//   from that text again: the Copy, Cut, Paste and Duplicate of the FX chain window.
// Does not own: reading the live states from the engine (the window asks first), the chain edit and its undo step (the window), the tree rules (ChainTree).
// Tests: TabForge.Avalonia.Tests.FxChainClipboardTests.
public static class ChainClip
{
    /// <summary>Plug-ins and rows made from a snapshot: <see cref="Items"/> hold the same <see cref="Plugins"/> (containers included), all new objects.</summary>
    public sealed record Pasted(List<PluginSlot> Plugins, List<ChainItem> Items);

    /// <summary>The selected rows of <paramref name="rig"/> as text: the packed form of a chain file (the containers ride on the first plug-in). Null when no plug-in is in them.</summary>
    public static string? Snapshot(RigPreset rig, IReadOnlyCollection<object> tags)
    {
        // Everything below works on a copy: writing the tree sets bypass flags and container runs on the objects it walks.
        var copy = JsonSerializer.Deserialize<RigPreset>(JsonSerializer.SerializeToUtf8Bytes(rig))!;
        var want = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var tag in tags)
        {
            var i = tag is PluginSlot p ? rig.Plugins.IndexOf(p) : -1;
            var j = tag is ChainContainer c && rig.Containers is { } own ? own.IndexOf(c) : -1;
            if (i >= 0 && i < copy.Plugins.Count) want.Add(copy.Plugins[i]);
            else if (j >= 0 && copy.Containers is { } boxes && j < boxes.Count) want.Add(boxes[j]);
        }
        var picked = new List<ChainItem>();
        void Pick(List<ChainItem> items) { foreach (var item in items) { if (want.Contains(item.Tag)) picked.Add(item); else Pick(item.Children); } }   // a picked container takes its items with it
        Pick(ChainTree.Tree(copy));
        var only = new RigPreset();
        ChainTree.Write(only, picked);
        if (only.Plugins.Count == 0) return null;
        ChainTree.Pack(only.Plugins, only.Containers);
        return JsonSerializer.Serialize(only.Plugins);
    }

    /// <summary>New plug-ins (new ids, the saved states kept) and the rows for them from a snapshot; null when it holds no plug-in or is not a snapshot.</summary>
    public static Pasted? Restore(string snapshot)
    {
        List<PluginSlot>? plugins;
        try { plugins = JsonSerializer.Deserialize<List<PluginSlot>>(snapshot); }
        catch (JsonException) { return null; }
        if (plugins is not { Count: > 0 } || plugins.Any(p => p is null)) return null;
        var packed = plugins[0].ChainContainers;
        foreach (var p in plugins) { p.Id = Guid.NewGuid().ToString("N"); p.ChainContainers = null; }
        foreach (var c in packed ?? new()) if (c is not null) c.Id = Guid.NewGuid().ToString("N");   // a copy is a new container, never the one it came from
        return new Pasted(plugins, ChainTree.Tree(plugins, packed));
    }
}
