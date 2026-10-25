namespace TabForge.Presenters;

/// <summary>
/// The value-based effect editors' rules without UI types (trill, grace note, harmonic).
/// Owns: the preset row label, when Delete preset is enabled, the save-failure text, which harmonic kinds take a fret, and reading a whole-number setting with its fallback.
/// Does not own: the dialog and fields (Views/EffectEditors/ValuesEffectDialog, OrnamentEditors), the preset store (EffectPresetStore)
///     or the model writes (OrnamentEdits).
/// Tests: no dedicated test class.
/// </summary>
public static class EffectValuesPresenter
{
    /// <summary>The message when a preset cannot be saved.</summary>
    public const string SaveFailedMessage = "That name is taken by a built-in preset, too long, or the list is full.";

    /// <summary>The preset list row: the person's own presets are marked "(mine)".</summary>
    public static string PresetLabel(string name, bool builtIn) => builtIn ? name : name + "  (mine)";

    /// <summary>Delete preset works only on a selected preset of the person's own.</summary>
    public static bool CanDelete(bool hasSelection, bool selectedIsBuiltIn) => hasSelection && !selectedIsBuiltIn;

    /// <summary>Artificial (1) and tapped (2) harmonics name a harmonic fret; the other kinds do not.</summary>
    public static bool UsesHarmonicFret(int type) => type is 1 or 2;

    /// <summary>The rounded value of <paramref name="key"/>, or <paramref name="fallback"/> when the preset lacks it.</summary>
    public static int IntValue(IReadOnlyDictionary<string, double> values, string key, int fallback) =>
        values.TryGetValue(key, out var x) ? (int)Math.Round(x) : fallback;
}
