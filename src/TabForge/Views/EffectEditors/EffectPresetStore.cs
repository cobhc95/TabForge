using System.Globalization;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.EffectEditors;

// Owns: the built-in preset lists and the user presets of every effect editor (kept in AppSettings.EffectPresets, keyed by editor kind).
// Does not own: choosing a preset in the dialog (CurveEffectDialog) or saving the settings file (the host).
// Tests: TestEffectCurveMath, TestEffectEditors.
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

    // Number presets: the editor's value keys. Trill: Step (frets above the note), Speed (16/32/64). Grace: Step, Dead, Before (1 before the beat),
    // Speed, Dynamic (0..7, -1 keeps the note's own), Transition (0 none, 1 bend, 2 slide, 3 hammer). Harmonic: Type (0 natural, 1 artificial, 2 tapped, 3 pinch, 4 semi), Fret.
    private static EffectPresetEntry V(EffectEditorKind kind, string name, string values) => new()
    {
        Kind = kind.ToString(), Name = name,
        Values = values.Split(' ').Select(x => x.Split('=')).ToDictionary(a => a[0], a => double.Parse(a[1], CultureInfo.InvariantCulture))
    };

    private static readonly EffectPresetEntry[] TrillPresets =
    {
        V(EffectEditorKind.Trill, "Half step, 16th", "Step=1 Speed=16"),
        V(EffectEditorKind.Trill, "Whole step, 16th", "Step=2 Speed=16"),
        V(EffectEditorKind.Trill, "Minor third, 16th", "Step=3 Speed=16"),
        V(EffectEditorKind.Trill, "Half step, 32nd", "Step=1 Speed=32"),
        V(EffectEditorKind.Trill, "Whole step, 32nd", "Step=2 Speed=32"),
        V(EffectEditorKind.Trill, "Whole step, 64th", "Step=2 Speed=64"),
    };

    private static readonly EffectPresetEntry[] GracePresets =
    {
        V(EffectEditorKind.Grace, "Hammer-on from below", "Step=-2 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=3"),
        V(EffectEditorKind.Grace, "Pull-off from above", "Step=2 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=3"),
        V(EffectEditorKind.Grace, "Slide up", "Step=-3 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=2"),
        V(EffectEditorKind.Grace, "Slide down", "Step=3 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=2"),
        V(EffectEditorKind.Grace, "Quick grace", "Step=-1 Dead=0 Before=1 Speed=64 Dynamic=-1 Transition=0"),
        V(EffectEditorKind.Grace, "On the beat", "Step=-2 Dead=0 Before=0 Speed=32 Dynamic=-1 Transition=0"),
        V(EffectEditorKind.Grace, "Bend grace", "Step=-2 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=1"),
        V(EffectEditorKind.Grace, "Dead note", "Step=0 Dead=1 Before=1 Speed=32 Dynamic=-1 Transition=0"),
    };

    private static readonly EffectPresetEntry[] HarmonicPresets =
    {
        V(EffectEditorKind.Harmonic, "Natural", "Type=0 Fret=12"),
        V(EffectEditorKind.Harmonic, "Artificial, octave (+12)", "Type=1 Fret=12"),
        V(EffectEditorKind.Harmonic, "Artificial, fifth (+7)", "Type=1 Fret=7"),
        V(EffectEditorKind.Harmonic, "Artificial, two octaves (+5)", "Type=1 Fret=5"),
        V(EffectEditorKind.Harmonic, "Tapped, octave (12)", "Type=2 Fret=12"),
        V(EffectEditorKind.Harmonic, "Pinch", "Type=3 Fret=12"),
        V(EffectEditorKind.Harmonic, "Semi", "Type=4 Fret=12"),
    };
}
