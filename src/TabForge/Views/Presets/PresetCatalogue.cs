using System.IO;
using TabForge.Plugins;

namespace TabForge.Views.Presets;

/// <summary>A place in the preset manager's tree: a kind, or one plug-in / processor type under a kind (<see cref="Key"/>).</summary>
internal sealed record PresetNode(PresetKind Kind, string? Key, string Title, string Where)
{
    /// <summary>A kind whose presets live under its children (plug-ins, processor types).</summary>
    public bool IsGroup => Key is null && Kind is PresetKind.PluginPreset or PresetKind.MidiProcessor;
    public bool Same(PresetNode other) => Kind == other.Kind && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One preset as the manager lists it; a built-in one comes with the app and has no file of the user's.</summary>
internal sealed record PresetEntry(PresetNode Node, string Name, bool BuiltIn, DateTime? Modified);

// Owns: what the preset manager lists: the tree's nodes (every kind, with one child per plug-in or processor type that has presets), the
//   presets of a node (the ones that come with the app first, marked built-in, then the user's in their order, with the file's date) and
//   the search across every kind.
// Does not own: the files and their order (PresetFolderStore, DrumMapStore), the window (PresetManagerWindow) or changing a preset.
// Tests: TestPresetManagerWindow.
internal static class PresetCatalogue
{
    private static readonly (PresetKind Kind, string Title)[] Kinds =
    {
        (PresetKind.PluginPreset, "Plug-in presets"), (PresetKind.FxChain, "FX chains"), (PresetKind.Container, "Containers"),
        (PresetKind.MidiChain, "MIDI chains"), (PresetKind.MidiProcessor, "MIDI processors"), (PresetKind.DrumMap, "Drum maps"),
    };

    /// <summary>The top rows of the tree, in their fixed order.</summary>
    internal static IReadOnlyList<PresetNode> Groups() => Kinds.Select(k => new PresetNode(k.Kind, null, k.Title, k.Title)).ToList();

    /// <summary>The rows under a group: the plug-ins or processor types that have presets.</summary>
    internal static IReadOnlyList<PresetNode> Children(PresetNode group)
    {
        if (!group.IsGroup) return Array.Empty<PresetNode>();
        var plugin = group.Kind == PresetKind.PluginPreset;
        var keys = plugin
            ? PresetFolderStore.PluginKeys()
            : PresetFolderStore.MidiProcessorTypes().Concat(MidiProcessorCatalog.All.Where(i => MidiProcessorCatalog.BuiltInProcessorPresets(i.Type).Count > 0).Select(i => i.Type))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return keys.Select(k => (Key: k, Title: plugin ? k : MidiProcessorCatalog.Find(k)?.Name ?? k)).OrderBy(k => k.Title, StringComparer.OrdinalIgnoreCase)
            .Select(k => new PresetNode(group.Kind, k.Key, k.Title, $"{group.Title} › {k.Title}")).ToList();
    }

    /// <summary>The node of a kind and key (the group itself when the key is empty).</summary>
    internal static PresetNode Node(PresetKind kind, string? key)
    {
        var group = Groups().First(g => g.Kind == kind);
        if (!group.IsGroup || key is not { Length: > 0 }) return group;
        return Children(group).FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new PresetNode(kind, key, kind == PresetKind.MidiProcessor ? MidiProcessorCatalog.Find(key)?.Name ?? key : key, group.Title);
    }

    /// <summary>The names that come with the app in a node; no user preset may take one.</summary>
    internal static IReadOnlyList<string> BuiltIns(PresetNode node) => node.Kind switch
    {
        PresetKind.MidiChain => MidiProcessorCatalog.BuiltInPresets.Select(p => p.Name).ToList(),
        PresetKind.MidiProcessor when node.Key is { } type => MidiProcessorCatalog.BuiltInProcessorPresets(type).Select(p => p.Name).ToList(),
        PresetKind.DrumMap => DrumMapLibrary.All().Where(m => !m.IsUser).Select(m => m.Name).ToList(),
        _ => Array.Empty<string>(),
    };

    /// <summary>The presets a node lists; a group lists the presets of all its children.</summary>
    internal static IReadOnlyList<PresetEntry> Entries(PresetNode node)
    {
        if (node.IsGroup) return Children(node).SelectMany(Entries).ToList();
        var list = BuiltIns(node).Select(n => new PresetEntry(node, n, true, null)).ToList();
        if (PresetManager.StoreOf(node.Kind, node.Key) is { } store) list.AddRange(store.Names().Select(n => new PresetEntry(node, n, false, Modified(store, n))));
        return list;
    }

    /// <summary>Every preset of every kind whose name holds all the words typed.</summary>
    internal static IReadOnlyList<PresetEntry> Search(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Groups().SelectMany(Entries).Where(e => words.All(w => e.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    /// <summary>"12 Oct"; the year is added when it is not this year.</summary>
    internal static string DateText(DateTime? when, DateTime today) =>
        when is not { } d ? "" : d.ToString(d.Year == today.Year ? "d MMM" : "d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);

    private static DateTime? Modified(IPresetStore store, string name)
    {
        try
        {
            var path = store switch { PresetFolderStore files => files.PathOf(name), DrumMapStore maps => maps.PathOf(name), _ => null };
            return path is not null && File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return null; }
    }
}
