// Owns: the read-only contract between the preset manager and the stores of each preset kind, so a test can supply an in-memory store.
// Does not own: the stores themselves (PresetFolderStore, DrumMapStore), the catalogue (PresetCataloguePresenter) or any window.
// Tests: PresetCatalogueTests, PresetManagerWindowTests.
using TabForge.Plugins;

namespace TabForge.Presenters;

/// <summary>The store of each preset kind, and the file date the manager shows for a stored preset.</summary>
public interface IPresetStoreFactory
{
    /// <summary>The store of a kind; null when the kind needs a key and none is given.</summary>
    IPresetStore? StoreOf(PresetCatalogKind kind, string? key);

    /// <summary>The file date the manager shows for a stored preset; null when it has no file or the date cannot be read.</summary>
    DateTime? Modified(IPresetStore store, string name);

    /// <summary>The note-effect presets (bend, tremolo bar...); null when the factory has none.</summary>
    IEffectPresetSource? Effects => null;
}
