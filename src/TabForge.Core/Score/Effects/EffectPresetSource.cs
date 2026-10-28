using System.Text.Json;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Views.EffectEditors;

// Owns: the note-effect presets as the preset manager's source: the five effect kinds with their titles, the built-in names and bytes (EffectPresetStore),
//   and a store of the user's presets of a kind over the settings they live in.
// Does not own: the preset lists themselves (EffectPresetStore, EffectPresetListStore) or the manager.
// Tests: PresetUndoTests.
public sealed class EffectPresetSource : IEffectPresetSource
{
    private readonly Func<AppSettings?> _settings;
    private readonly Action? _save;

    /// <param name="settings">The settings that hold the user's presets (null before they load: no kind has a store).</param>
    /// <param name="save">Writes the settings after a change.</param>
    public EffectPresetSource(Func<AppSettings?> settings, Action? save) => (_settings, _save) = (settings, save);

    public IReadOnlyList<(string Key, string Title)> Kinds { get; } = new[]
    {
        (nameof(EffectEditorKind.Bend), "Bends"), (nameof(EffectEditorKind.TremoloBar), "Tremolo bars"), (nameof(EffectEditorKind.Trill), "Trills"),
        (nameof(EffectEditorKind.Grace), "Grace notes"), (nameof(EffectEditorKind.Harmonic), "Harmonics"),
    };

    public IReadOnlyList<string> BuiltInNames(string key) =>
        Enum.TryParse<EffectEditorKind>(key, out var kind) ? EffectPresetStore.BuiltIn(kind).Select(p => p.Name).ToList() : Array.Empty<string>();

    public byte[]? BuiltInBytes(string key, string name) =>
        Enum.TryParse<EffectEditorKind>(key, out var kind) && EffectPresetStore.BuiltIn(kind).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { } preset
            ? JsonSerializer.SerializeToUtf8Bytes(preset) : null;

    public IPresetStore? StoreOf(string key) =>
        Enum.TryParse<EffectEditorKind>(key, out var kind) && _settings() is { } settings ? new EffectPresetStore(settings).Store(kind, _save) : null;
}
