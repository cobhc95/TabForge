// Owns: the kinds of user presets the preset manager organises, and which of them need a key.
// Does not own: their stores (PresetStoreFactory), their tree and entries (PresetCataloguePresenter) or any window.
// Tests: PresetCatalogueTests.
namespace TabForge.Presenters;

/// <summary>
/// The kinds of user presets. <see cref="PluginPreset"/> is keyed by a plug-in preset folder name and <see cref="MidiProcessor"/>
/// by a processor type; the other kinds have one store each and no key.
/// </summary>
public enum PresetCatalogKind
{
    PluginPreset,
    FxChain,
    Container,
    MidiChain,
    MidiProcessor,
    DrumMap,
}
