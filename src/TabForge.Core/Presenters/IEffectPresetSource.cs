// Owns: the read-only contract between the preset manager's catalogue and the note-effect presets (bend, tremolo bar, trill, grace note, harmonic): which
//   kinds there are, the names and bytes of the ones that come with the app, and the store of the user's own of each kind.
// Does not own: the presets themselves (EffectPresetStore, EffectPresetListStore), the catalogue or any window. It lives here so the catalogue need not
//   name the effect editors' namespace: the editors' side implements it and the app hands it to the stores factory.
// Tests: PresetUndoTests.
using TabForge.Plugins;

namespace TabForge.Presenters;

/// <summary>The note-effect presets as the preset manager sees them.</summary>
public interface IEffectPresetSource
{
    /// <summary>The effect kinds that keep presets, as (key, title of its row in the manager).</summary>
    IReadOnlyList<(string Key, string Title)> Kinds { get; }

    /// <summary>The names of the presets of a kind that come with the app.</summary>
    IReadOnlyList<string> BuiltInNames(string key);

    /// <summary>The bytes of a built-in preset, as the kind's store restores them as a copy; null when there is none.</summary>
    byte[]? BuiltInBytes(string key, string name);

    /// <summary>The user's presets of a kind; null for an unknown key.</summary>
    IPresetStore? StoreOf(string key);
}
