// Owns: the read-only contract between the preset manager window and the catalogue it lists; the window takes this seam so a test can
//   supply a fake catalogue and a fake store without touching the user's preset folders.
// Does not own: what the catalogue reads (PresetCataloguePresenter), the stores (IPresetStoreFactory) or any window.
// Tests: PresetCatalogueTests, PresetManagerWindowTests.
namespace TabForge.Presenters;

/// <summary>What the preset manager lists: the tree, the presets of a node and the search across every kind.</summary>
public interface IPresetCatalogue
{
    /// <summary>The top rows of the tree, in their fixed order.</summary>
    IReadOnlyList<PresetNode> Groups();

    /// <summary>The rows under a group: the plug-ins or processor types that have presets.</summary>
    IReadOnlyList<PresetNode> Children(PresetNode group);

    /// <summary>The node of a kind and key (the group itself when the kind has no children or the key is empty).</summary>
    PresetNode Node(PresetCatalogKind kind, string? key);

    /// <summary>The names that come with the app in a node; no user preset may take one.</summary>
    IReadOnlyList<string> BuiltIns(PresetNode node);

    /// <summary>The presets a node lists; a group lists the presets of all its children.</summary>
    IReadOnlyList<PresetEntry> Entries(PresetNode node);

    /// <summary>Every preset of every kind whose name holds all the words typed, built-ins included.</summary>
    IReadOnlyList<PresetEntry> Search(string text);
}
