using System.Globalization;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Views.EffectEditors;

// Owns: the built-in preset lists and the user presets of every effect editor (kept in AppSettings.EffectPresets, keyed by editor kind).
// Does not own: choosing a preset in the dialog (CurveEffectDialog) or saving the settings file (the host).
// Tests: no dedicated test class.
public sealed class EffectPresetStore
{
    public const int MaxPerKind = 24;
    private readonly AppSettings _settings;

    public EffectPresetStore(AppSettings settings) => _settings = settings;

    /// <summary>Built-in presets first, then the user's, in the order saved.</summary>
    public IReadOnlyList<(EffectPresetEntry Preset, bool BuiltIn)> List(EffectEditorKind kind)
    {
        var key = kind.ToString();
        return BuiltIn(kind).Select(p => (p, true))
            .Concat(_settings.EffectPresets.Where(p => p.Kind == key).Select(p => (p, false))).ToList();
    }

    /// <summary>Saves (or replaces) a user preset; false for an empty name, a built-in name or a full list.</summary>
    public bool Save(EffectEditorKind kind, string name, IEnumerable<BendPointModel> points, Dictionary<string, double>? values = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 40) return false;
        var key = kind.ToString();
        if (BuiltIn(kind).Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
        var existing = _settings.EffectPresets.FirstOrDefault(p => p.Kind == key && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null && _settings.EffectPresets.Count(p => p.Kind == key) >= MaxPerKind) return false;
        var entry = existing ?? new EffectPresetEntry { Kind = key, Name = name };
        entry.Points = points.Select(p => p.Clone()).ToList();
        entry.Values = values is null ? new() : new(values);
        if (existing is null) _settings.EffectPresets.Add(entry);
        return true;
    }

    /// <summary>Deletes a user preset; built-in presets cannot be deleted.</summary>
    public bool Delete(EffectEditorKind kind, string name) =>
        _settings.EffectPresets.RemoveAll(p => p.Kind == kind.ToString() && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    // Offsets 0..60 across the note; values in quarter-tones (4 = a whole tone).
    private static EffectPresetEntry P(EffectEditorKind kind, string name, string curve) => new()
    {
        Kind = kind.ToString(), Name = name,
        Points = curve.Split(' ').Select(x => x.Split(':')).Select(a => new BendPointModel
        {
            Offset = double.Parse(a[0], CultureInfo.InvariantCulture),
            Value = double.Parse(a[1], CultureInfo.InvariantCulture)
        }).ToList()
    };

    public static IReadOnlyList<EffectPresetEntry> BuiltIn(EffectEditorKind kind) => kind switch
    {
        EffectEditorKind.Bend => BendPresets,
        EffectEditorKind.TremoloBar => TremoloPresets,
        EffectEditorKind.Trill => TrillPresets,
        EffectEditorKind.Grace => GracePresets,
        EffectEditorKind.Harmonic => HarmonicPresets,
        _ => Array.Empty<EffectPresetEntry>()
    };

    private static readonly EffectPresetEntry[] BendPresets =
    {
        P(EffectEditorKind.Bend, "Bend", "0:0 30:4 60:4"),
        P(EffectEditorKind.Bend, "Bend/Release", "0:0 20:4 40:4 60:0"),
        P(EffectEditorKind.Bend, "Bend/Release/Bend", "0:0 15:4 30:0 45:4 60:4"),
        P(EffectEditorKind.Bend, "Pre-Bend", "0:4 60:4"),
        P(EffectEditorKind.Bend, "Pre-Bend/Release", "0:4 30:4 60:0"),
    };

    private static readonly EffectPresetEntry[] TremoloPresets =
    {
        P(EffectEditorKind.TremoloBar, "Dip", "0:0 30:-4 60:0"),
        P(EffectEditorKind.TremoloBar, "Dive", "0:0 60:-8"),
        P(EffectEditorKind.TremoloBar, "Release up", "0:-8 60:0"),
        P(EffectEditorKind.TremoloBar, "Inverted Dip", "0:0 30:4 60:0"),
        P(EffectEditorKind.TremoloBar, "Return", "0:0 15:-4 45:-4 60:0"),
        P(EffectEditorKind.TremoloBar, "Release down", "0:4 60:0"),
    };

    // Number presets come from the shared table (TabForge.Presets.OrnamentPresets), so the WPF and Avalonia editors list the same ones.
    private static EffectPresetEntry[] Ornaments(EffectEditorKind kind, IEnumerable<OrnamentPreset> presets) => presets.Select(p => new EffectPresetEntry
    {
        Kind = kind.ToString(), Name = p.Name, Values = new Dictionary<string, double>(p.Values)
    }).ToArray();

    private static readonly EffectPresetEntry[] TrillPresets = Ornaments(EffectEditorKind.Trill, OrnamentPresets.Trill);
    private static readonly EffectPresetEntry[] GracePresets = Ornaments(EffectEditorKind.Grace, OrnamentPresets.Grace);
    private static readonly EffectPresetEntry[] HarmonicPresets = Ornaments(EffectEditorKind.Harmonic, OrnamentPresets.Harmonic);
}
