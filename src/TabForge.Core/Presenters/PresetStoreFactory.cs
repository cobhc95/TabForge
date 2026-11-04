using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Playback;
using TabForge.Plugins;

// Owns: the store of a preset kind (and key) for the preset manager, and the file date it shows for a stored preset.
// Does not own: the files themselves (PresetFolderStore, DrumMapStore), the catalogue (PresetCataloguePresenter) or any window.
// Tests: PresetCatalogueTests, PresetManagerWindowTests.
namespace TabForge.Presenters;

/// <summary>The store of each preset kind, and the file date of a preset that has a file.</summary>
public sealed class PresetStoreFactory : IPresetStoreFactory
{
    /// <param name="effects">The note-effect presets (they live in the app settings); without them that kind has no store.</param>
    public PresetStoreFactory(IEffectPresetSource? effects = null) => Effects = effects;

    /// <summary>The note-effect presets; null when the factory was built without them.</summary>
    public IEffectPresetSource? Effects { get; }

    /// <summary>The factory without note-effect presets; the app builds its own with the settings, a test supplies its own <see cref="IPresetStoreFactory"/>.</summary>
    public static PresetStoreFactory Default { get; } = new();

    /// <summary>The store of a kind; null when the kind needs a key and none is given.</summary>
    public IPresetStore? StoreOf(PresetCatalogKind kind, string? key) => kind switch
    {
        PresetCatalogKind.PluginPreset => key is { Length: > 0 } ? PresetFolderStore.Plugin(key) : null,
        PresetCatalogKind.FxChain => PresetFolderStore.FxChains(),
        PresetCatalogKind.Container => PresetFolderStore.Containers(),
        PresetCatalogKind.MidiChain => PresetFolderStore.MidiChains(),
        PresetCatalogKind.MidiProcessor => key is { Length: > 0 } ? PresetFolderStore.MidiProcessor(key) : null,
        PresetCatalogKind.DrumMap => new DrumMapStore(),
        PresetCatalogKind.Articulation => ArticulationPresets.UserStore(),
        PresetCatalogKind.EffectPreset => key is { Length: > 0 } ? Effects?.StoreOf(key) : null,
        _ => null,
    };

    /// <summary>The file date the manager shows for a stored preset; null when it has no file or the date cannot be read.</summary>
    public DateTime? Modified(IPresetStore store, string name)
    {
        try
        {
            var path = store switch { PresetFolderStore files => files.PathOf(name), DrumMapStore maps => maps.PathOf(name), _ => null };
            return path is not null && File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return null; }
    }

}
