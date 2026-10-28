using System.Text;
using System.Text.Json;
using TabForge.Playback;
using TabForge.Plugins;

// Owns: the bytes of a preset that comes with the app, written as the user's own copy: a built-in articulation, MIDI chain, MIDI processor or
//   note-effect preset becomes a file or entry the matching store can restore under a new name.
// Does not own: the stores that write it (IPresetRestorable), the built-in lists (the catalogue) or drum maps (DrumMapStore.DuplicateShipped).
// Tests: PresetUndoTests.
namespace TabForge.Presenters;

/// <summary>Turns a built-in preset into the bytes of an editable copy.</summary>
public static class PresetBuiltInCopy
{
    /// <summary>True when the kind's built-in presets can be copied here (drum maps are copied from their shipped file instead).</summary>
    public static bool CanCopy(PresetCatalogKind kind) =>
        kind is PresetCatalogKind.Articulation or PresetCatalogKind.MidiChain or PresetCatalogKind.MidiProcessor or PresetCatalogKind.EffectPreset;

    /// <summary>The bytes of the built-in preset <paramref name="name"/> of a node, or null when there is none.</summary>
    /// <param name="effects">The note-effect presets, for the effect kinds.</param>
    public static byte[]? BytesOf(PresetNode node, string name, IEffectPresetSource? effects = null)
    {
        switch (node.Kind)
        {
            case PresetCatalogKind.Articulation:
                return ArticulationPresets.FindBuiltIn(name) is { } articulation ? ArticulationPresets.ToBytes(articulation) : null;
            case PresetCatalogKind.MidiChain:
                return MidiProcessorCatalog.BuiltInPresets.FirstOrDefault(p => Same(p.Name, name)) is { Build: { } build }
                    ? JsonSerializer.SerializeToUtf8Bytes(build(), new JsonSerializerOptions { WriteIndented = true }) : null;
            case PresetCatalogKind.MidiProcessor when node.Key is { } type:
                return MidiProcessorCatalog.BuiltInProcessorPresets(type).FirstOrDefault(p => Same(p.Name, name)) is { Params: { } text } ? Encoding.UTF8.GetBytes(text) : null;
            case PresetCatalogKind.EffectPreset when node.Key is { } key:
                return effects?.BuiltInBytes(key, name);
            default:
                return null;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
