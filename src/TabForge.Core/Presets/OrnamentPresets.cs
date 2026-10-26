using System.Globalization;

namespace TabForge.Presets;

/// <summary>A built-in preset of the trill, grace-note or harmonic editor: its name and its values by editor key.</summary>
public sealed record OrnamentPreset(string Name, IReadOnlyDictionary<string, double> Values);

// Owns: the built-in number presets of the trill, grace-note and harmonic editors, the one list the editors show.
// Does not own: the user's presets (AppSettings.EffectPresets), the dialogs, or what the keys mean (EffectValuesPresenter).
// Key: none (static data). Ceiling: fixed lists. Thread: read-only after the static initialiser.
// Tests: none dedicated, TabForge.Avalonia.Tests.OrnamentEditorFlowTests.
public static class OrnamentPresets
{
    // Trill: Step (frets above the note), Speed (16/32/64). Grace: Step, Dead, Before (1 before the beat), Speed, Dynamic (0..7, -1 keeps the
    // note's own), Transition (0 none, 1 bend, 2 slide, 3 hammer). Harmonic: Type (0 natural, 1 artificial, 2 tapped, 3 pinch, 4 semi), Fret.
    public static IReadOnlyList<OrnamentPreset> Trill { get; } = Build(
        ("Half step, 16th", "Step=1 Speed=16"),
        ("Whole step, 16th", "Step=2 Speed=16"),
        ("Minor third, 16th", "Step=3 Speed=16"),
        ("Half step, 32nd", "Step=1 Speed=32"),
        ("Whole step, 32nd", "Step=2 Speed=32"),
        ("Whole step, 64th", "Step=2 Speed=64"));

    public static IReadOnlyList<OrnamentPreset> Grace { get; } = Build(
        ("Hammer-on from below", "Step=-2 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=3"),
        ("Pull-off from above", "Step=2 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=3"),
        ("Slide up", "Step=-3 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=2"),
        ("Slide down", "Step=3 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=2"),
        ("Quick grace", "Step=-1 Dead=0 Before=1 Speed=64 Dynamic=-1 Transition=0"),
        ("On the beat", "Step=-2 Dead=0 Before=0 Speed=32 Dynamic=-1 Transition=0"),
        ("Bend grace", "Step=-2 Dead=0 Before=1 Speed=32 Dynamic=-1 Transition=1"),
        ("Dead note", "Step=0 Dead=1 Before=1 Speed=32 Dynamic=-1 Transition=0"));

    public static IReadOnlyList<OrnamentPreset> Harmonic { get; } = Build(
        ("Natural", "Type=0 Fret=12"),
        ("Artificial, octave (+12)", "Type=1 Fret=12"),
        ("Artificial, fifth (+7)", "Type=1 Fret=7"),
        ("Artificial, two octaves (+5)", "Type=1 Fret=5"),
        ("Tapped, octave (12)", "Type=2 Fret=12"),
        ("Pinch", "Type=3 Fret=12"),
        ("Semi", "Type=4 Fret=12"));

    private static IReadOnlyList<OrnamentPreset> Build(params (string Name, string Values)[] rows) =>
        rows.Select(row => new OrnamentPreset(row.Name, row.Values.Split(' ').Select(x => x.Split('=')).ToDictionary(a => a[0], a => double.Parse(a[1], CultureInfo.InvariantCulture)))).ToList();
}
