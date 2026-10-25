using TabForge.Models;

namespace TabForge.Services;

// Owns: the string tuning presets offered by Track properties (WPF and Avalonia) and the six-string list of the global tuning window.
// Does not own: applying a tuning to a track (TrackSetup.ApplyTuning) or the windows that list the presets.
// Tests: no dedicated test class.
/// <summary>
/// The preset table. Constant after load, so any thread may read it; the order is the order the windows list the presets in.
/// </summary>
public static class TuningPresets
{
    private static readonly TuningPreset[] SixString =
    {
        new("Standard E", new[] { 64, 59, 55, 50, 45, 40 }),
        new("Eb standard (half step down)", new[] { 63, 58, 54, 49, 44, 39 }),
        new("D standard", new[] { 62, 57, 53, 48, 43, 38 }),
        new("C# standard", new[] { 61, 56, 52, 47, 42, 37 }),
        new("C standard", new[] { 60, 55, 51, 46, 41, 36 }),
        new("Drop D", new[] { 64, 59, 55, 50, 45, 38 }),
        new("Drop C#", new[] { 63, 58, 54, 49, 44, 37 }),
        new("Drop C", new[] { 62, 57, 53, 48, 43, 36 }),
        new("Drop B", new[] { 61, 56, 52, 47, 42, 35 }),
        new("Drop A", new[] { 59, 54, 50, 45, 40, 33 }),
        new("Open G (D G D G B D)", new[] { 62, 59, 55, 50, 43, 38 }),
        new("Open D (D A D F# A D)", new[] { 62, 57, 54, 50, 45, 38 }),
        new("DADGAD", new[] { 62, 57, 55, 50, 45, 38 }),
    };
    private static readonly TuningPreset[] SevenString =
    {
        new("Standard B", new[] { 64, 59, 55, 50, 45, 40, 35 }),
        new("Drop A", new[] { 64, 59, 55, 50, 45, 40, 33 }),
        new("A standard", new[] { 62, 57, 53, 48, 43, 38, 33 }),
    };
    private static readonly TuningPreset[] EightString =
    {
        new("Standard F#", new[] { 64, 59, 55, 50, 45, 40, 35, 30 }),
        new("Drop E", new[] { 64, 59, 55, 50, 45, 40, 35, 28 }),
    };
    private static readonly TuningPreset[] FourStringBass =
    {
        new("Standard E", new[] { 43, 38, 33, 28 }),
        new("Eb standard", new[] { 42, 37, 32, 27 }),
        new("D standard", new[] { 41, 36, 31, 26 }),
        new("Drop D", new[] { 43, 38, 33, 26 }),
        new("Drop C", new[] { 41, 36, 31, 24 }),
    };
    private static readonly TuningPreset[] FiveStringBass =
    {
        new("Standard B", new[] { 43, 38, 33, 28, 23 }),
        new("Standard E, high C", new[] { 48, 43, 38, 33, 28 }),
        new("Drop A", new[] { 43, 38, 33, 28, 21 }),
        new("A standard", new[] { 41, 36, 31, 26, 21 }),
    };
    private static readonly TuningPreset[] SixStringBass =
    {
        new("Standard B (high C)", new[] { 48, 43, 38, 33, 28, 23 }),
        new("Drop A", new[] { 48, 43, 38, 33, 28, 21 }),
    };
    private static readonly TuningPreset[] SevenStringBass =
    {
        new("Standard F# (high F)", new[] { 53, 48, 43, 38, 33, 28, 23 }),
    };
    private static readonly TuningPreset[] NineString =
    {
        new("Standard C#", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25 }),
    };

    /// <summary>The six-string guitar presets as (name, tuning) pairs, in list order.</summary>
    public static IEnumerable<(string Name, int[] HighToLow)> SixStringPresets => SixString.Select(p => (p.Name, p.HighToLow));

    /// <summary>The presets for an instrument with this many strings. Presets depend on the instrument as well as the count: a 6-string bass is not a guitar. Empty when there are none.</summary>
    public static IEnumerable<TuningPreset> For(int strings, bool bass) => (strings, bass) switch
    {
        (4, _) => FourStringBass,
        (5, _) => FiveStringBass,
        (6, true) => SixStringBass,
        (6, false) => SixString,
        (7, true) => SevenStringBass,
        (7, false) => SevenString,
        (8, _) => EightString,
        (9, _) => NineString,
        _ => Array.Empty<TuningPreset>()
    };
}

/// <summary>A named tuning, high to low like <see cref="TrackModel.StringTunings"/>.</summary>
public sealed record TuningPreset(string Name, int[] HighToLow);
