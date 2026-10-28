using System.Globalization;
using TabForge.Playback;
using TabForge.Plugins;

// Owns: what the preset manager lists: the tree's nodes (every kind, with one child per plug-in or processor type that has presets), the
//   presets of a node (the ones that come with the app first, marked built-in, then the user's in their order, with the file's date) and
//   the search across every kind.
// Does not own: the stores and their files (IPresetStoreFactory, PresetFolderStore, DrumMapStore), the window, or changing a preset.
// Tests: PresetCatalogueTests, PresetManagerWindowTests.
namespace TabForge.Presenters;

/// <summary>A place in the preset manager's tree: a kind, or one plug-in / processor type under a kind (<see cref="Key"/>).</summary>
public sealed record PresetNode(PresetCatalogKind Kind, string? Key, string Title, string Where)
{
    /// <summary>A kind whose presets live under its children (plug-ins, processor types).</summary>
    public bool IsGroup => Key is null && Kind is PresetCatalogKind.PluginPreset or PresetCatalogKind.MidiProcessor or PresetCatalogKind.EffectPreset;
    public bool Same(PresetNode other) => Kind == other.Kind && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One preset as the manager lists it; a built-in one comes with the app and has no file of the user's.</summary>
public sealed record PresetEntry(PresetNode Node, string Name, bool BuiltIn, DateTime? Modified);

/// <summary>The preset manager's catalogue, read from the user's preset folders and the built-ins in code.</summary>
public sealed class PresetCataloguePresenter : IPresetCatalogue
{
    private const string WhereSeparator = " › ";
    private const char SearchSeparator = ' ';
    private const string DateThisYear = "d MMM";
    private const string DateAnotherYear = "d MMM yyyy";

    private sealed record GroupRow(PresetCatalogKind Kind, string Title);

    private static readonly GroupRow[] GroupRows =
    {
        new(PresetCatalogKind.PluginPreset, "Plug-in presets"),
        new(PresetCatalogKind.FxChain, "FX chains"),
        new(PresetCatalogKind.Container, "Containers"),
        new(PresetCatalogKind.MidiChain, "MIDI chains"),
        new(PresetCatalogKind.MidiProcessor, "MIDI processors"),
        new(PresetCatalogKind.DrumMap, "Drum maps"),
        new(PresetCatalogKind.Articulation, "Articulations"),
        new(PresetCatalogKind.EffectPreset, "Bend / tremolo"),
    };


    private readonly IPresetStoreFactory _stores;

    /// <param name="stores">The stores to read; the app's real folders by default.</param>
    public PresetCataloguePresenter(IPresetStoreFactory? stores = null) => _stores = stores ?? PresetStoreFactory.Default;

    /// <summary>The top rows of the tree, in their fixed order.</summary>
    public IReadOnlyList<PresetNode> Groups() =>
        GroupRows.Select(row => new PresetNode(row.Kind, null, row.Title, row.Title)).ToList();

    /// <summary>The rows under a group: the plug-ins or processor types that have presets.</summary>
    public IReadOnlyList<PresetNode> Children(PresetNode group)
    {
        if (!group.IsGroup) return Array.Empty<PresetNode>();
        if (group.Kind == PresetCatalogKind.EffectPreset)
            return (_stores.Effects?.Kinds ?? Array.Empty<(string Key, string Title)>()).Select(e => new PresetNode(group.Kind, e.Key, e.Title, $"{group.Title}{WhereSeparator}{e.Title}")).ToList();
        var plugin = group.Kind == PresetCatalogKind.PluginPreset;
        var keys = plugin
            ? PresetFolderStore.PluginKeys()
            : PresetFolderStore.MidiProcessorTypes()
                .Concat(MidiProcessorCatalog.All.Where(info => MidiProcessorCatalog.BuiltInProcessorPresets(info.Type).Count > 0).Select(info => info.Type))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var children = new List<PresetNode>();
        foreach (var key in keys)
        {
            var title = plugin ? key : MidiProcessorCatalog.Find(key)?.Name ?? key;
            children.Add(new PresetNode(group.Kind, key, title, $"{group.Title}{WhereSeparator}{title}"));
        }
        children.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        return children;
    }

    /// <summary>The node of a kind and key (the group itself when the kind has no children or the key is empty).</summary>
    public PresetNode Node(PresetCatalogKind kind, string? key)
    {
        var group = Groups().First(g => g.Kind == kind);
        if (!group.IsGroup || key is not { Length: > 0 }) return group;
        return Children(group).FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new PresetNode(kind, key, kind == PresetCatalogKind.MidiProcessor ? MidiProcessorCatalog.Find(key)?.Name ?? key : key, group.Title);
    }

    /// <summary>The names that come with the app in a node; no user preset may take one.</summary>
    public IReadOnlyList<string> BuiltIns(PresetNode node) => node.Kind switch
    {
        PresetCatalogKind.MidiChain => MidiProcessorCatalog.BuiltInPresets.Select(preset => preset.Name).ToList(),
        PresetCatalogKind.MidiProcessor when node.Key is { } type => MidiProcessorCatalog.BuiltInProcessorPresets(type).Select(preset => preset.Name).ToList(),
        PresetCatalogKind.DrumMap => DrumMapLibrary.BuiltInNames(),
        PresetCatalogKind.Articulation => ArticulationPresets.BuiltIn.Select(preset => preset.Name).ToList(),
        PresetCatalogKind.EffectPreset when node.Key is { } key && _stores.Effects is { } effects => effects.BuiltInNames(key),
        _ => Array.Empty<string>(),
    };

    /// <summary>The presets a node lists; a group lists the presets of all its children. Built-ins come first, then the user's order.</summary>
    public IReadOnlyList<PresetEntry> Entries(PresetNode node)
    {
        if (node.IsGroup) return Children(node).SelectMany(Entries).ToList();
        var entries = BuiltIns(node).Select(name => new PresetEntry(node, name, true, null)).ToList();
        if (_stores.StoreOf(node.Kind, node.Key) is { } store)
            entries.AddRange(store.Names().Select(name => new PresetEntry(node, name, false, _stores.Modified(store, name))));
        return entries;
    }

    /// <summary>Every preset of every kind whose name holds all the words typed, built-ins included.</summary>
    public IReadOnlyList<PresetEntry> Search(string text)
    {
        var words = text.Split(SearchSeparator, StringSplitOptions.RemoveEmptyEntries);
        return Groups().SelectMany(Entries).Where(entry => words.All(word => entry.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    /// <summary>"12 Oct"; the year is added when it is not this year.</summary>
    public static string DateText(DateTime? when, DateTime today) =>
        when is not { } date ? "" : date.ToString(date.Year == today.Year ? DateThisYear : DateAnotherYear, CultureInfo.CurrentCulture);
}
