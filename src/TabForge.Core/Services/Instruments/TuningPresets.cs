using TabForge.Models;

namespace TabForge.Services;

// Owns: the string tuning presets offered by Track properties and the six-string list of the global tuning window.
// Does not own: applying a tuning to a track (TrackSetup.ApplyTuning) or the windows that list the presets.
// Tests: TuningPresetCatalogTests (data, search, apply, user rules).
/// <summary>
/// The preset table. Constant after load, so any thread may read it; the order is the order the windows list the presets in.
/// Built-in data comes from Docs/Orchestration/Tuning-Presets-Research.md: only octave-confirmed real tunings are
/// listed here. Pattern-derived, letters-only and inferred-octave rows from that research are deliberately absent
/// (see TuningPresetCatalogTests.DubiousResearchEntriesAreExcluded).
/// </summary>
public static class TuningPresets
{
    public const string GuitarGroup = "Guitar";
    public const string BassGroup = "Bass";
    public const string UkuleleGroup = "Ukulele";
    public const string MandolinGroup = "Mandolin";
    public const string BanjoGroup = "Banjo";
    public const string ViolinGroup = "Violin";
    public const string ViolaGroup = "Viola";
    public const string CelloGroup = "Cello";
    public const string DoubleBassGroup = "Double bass";
    public const string LapSteelGroup = "Lap steel";
    public const string DulcimerGroup = "Dulcimer";

    // One table per string count (the static-state ratchet allows eight): the original rows stay
    // verbatim first inside their count so existing orders never change, verified additions follow.
    private static readonly (string Group, TuningPreset Preset)[] SixString =
    {
        (GuitarGroup, new TuningPreset("Standard E", new[] { 64, 59, 55, 50, 45, 40 })),
        (GuitarGroup, new TuningPreset("Eb standard (half step down)", new[] { 63, 58, 54, 49, 44, 39 })),
        (GuitarGroup, new TuningPreset("D standard", new[] { 62, 57, 53, 48, 43, 38 })),
        (GuitarGroup, new TuningPreset("C# standard", new[] { 61, 56, 52, 47, 42, 37 })),
        (GuitarGroup, new TuningPreset("C standard", new[] { 60, 55, 51, 46, 41, 36 })),
        (GuitarGroup, new TuningPreset("Drop D", new[] { 64, 59, 55, 50, 45, 38 })),
        (GuitarGroup, new TuningPreset("Drop C#", new[] { 63, 58, 54, 49, 44, 37 })),
        (GuitarGroup, new TuningPreset("Drop C", new[] { 62, 57, 53, 48, 43, 36 })),
        (GuitarGroup, new TuningPreset("Drop B", new[] { 61, 56, 52, 47, 42, 35 })),
        (GuitarGroup, new TuningPreset("Drop A", new[] { 59, 54, 50, 45, 40, 33 })),
        (GuitarGroup, new TuningPreset("Open G (D G D G B D)", new[] { 62, 59, 55, 50, 43, 38 })),
        (GuitarGroup, new TuningPreset("Open D (D A D F# A D)", new[] { 62, 57, 54, 50, 45, 38 })),
        (GuitarGroup, new TuningPreset("DADGAD", new[] { 62, 57, 55, 50, 45, 38 })),
        (GuitarGroup, new TuningPreset("Bb standard", new[] { 58, 53, 49, 44, 39, 34 })),
        (GuitarGroup, new TuningPreset("B standard (BEADF#B)", new[] { 59, 54, 50, 45, 40, 35 })),
        (GuitarGroup, new TuningPreset("A standard (baritone)", new[] { 57, 52, 48, 43, 38, 33 })),
        (GuitarGroup, new TuningPreset("Drop G", new[] { 57, 52, 48, 43, 38, 31 })),
        (GuitarGroup, new TuningPreset("Double Drop D", new[] { 62, 59, 55, 50, 45, 38 })),
        (GuitarGroup, new TuningPreset("Open E (E B E G# B E)", new[] { 64, 59, 56, 52, 47, 40 })),
        (GuitarGroup, new TuningPreset("Open A (E A C# E A E)", new[] { 64, 57, 52, 49, 45, 40 })),
        (GuitarGroup, new TuningPreset("Open B (B F# B F# B D#)", new[] { 63, 59, 54, 47, 42, 35 })),
        (GuitarGroup, new TuningPreset("Open C (C G C G C E)", new[] { 64, 60, 55, 48, 43, 36 })),
        (GuitarGroup, new TuningPreset("Open F (C F C F A F)", new[] { 65, 57, 53, 48, 41, 36 })),
        (GuitarGroup, new TuningPreset("Open Dm (D A D F A D)", new[] { 62, 57, 53, 50, 45, 38 })),
        (GuitarGroup, new TuningPreset("Open Gm (D G D G Bb D)", new[] { 62, 58, 55, 50, 43, 38 })),
        (GuitarGroup, new TuningPreset("G6 (D G D G B E)", new[] { 64, 59, 55, 50, 43, 38 })),
        (GuitarGroup, new TuningPreset("C6 (C A C G C E)", new[] { 64, 60, 55, 48, 45, 36 })),
        (GuitarGroup, new TuningPreset("All-fourths (E A D G C F)", new[] { 65, 60, 55, 50, 45, 40 })),
        (GuitarGroup, new TuningPreset("NST (Fripp C G D A E G)", new[] { 67, 64, 57, 50, 43, 36 })),
        (GuitarGroup, new TuningPreset("Nashville high-strung", new[] { 64, 59, 67, 62, 57, 52 })),
        (BassGroup, new TuningPreset("Standard B (high C)", new[] { 48, 43, 38, 33, 28, 23 })),
        (BassGroup, new TuningPreset("Drop A", new[] { 48, 43, 38, 33, 28, 21 })),
        (BassGroup, new TuningPreset("Guitar-interval (B E A D F# B)", new[] { 47, 42, 38, 33, 28, 23 })),
        (DoubleBassGroup, new TuningPreset("6-string (B + C)", new[] { 48, 43, 38, 33, 28, 23 })),
        (LapSteelGroup, new TuningPreset("C6 (C E G A C E)", new[] { 64, 60, 57, 55, 52, 48 })),
        (LapSteelGroup, new TuningPreset("A6 (A C# E F# A C# E)", new[] { 64, 61, 57, 54, 49, 45 })),
        (LapSteelGroup, new TuningPreset("Open E", new[] { 64, 59, 56, 52, 47, 40 })),
        (LapSteelGroup, new TuningPreset("Open D", new[] { 62, 57, 54, 50, 45, 38 })),
        (LapSteelGroup, new TuningPreset("Open G (low bass)", new[] { 62, 59, 55, 50, 43, 38 })),
        (LapSteelGroup, new TuningPreset("Dobro G (high bass)", new[] { 62, 59, 55, 50, 47, 43 })),
        (LapSteelGroup, new TuningPreset("Open A (low bass)", new[] { 64, 61, 57, 52, 45, 40 })),
        (LapSteelGroup, new TuningPreset("E7 (low-E form)", new[] { 64, 59, 56, 52, 50, 40 })),
        (LapSteelGroup, new TuningPreset("C13", new[] { 64, 60, 57, 55, 52, 46 })),
    };
    private static readonly (string Group, TuningPreset Preset)[] SevenString =
    {
        (GuitarGroup, new TuningPreset("Standard B", new[] { 64, 59, 55, 50, 45, 40, 35 })),
        (GuitarGroup, new TuningPreset("Drop A", new[] { 64, 59, 55, 50, 45, 40, 33 })),
        (GuitarGroup, new TuningPreset("A standard", new[] { 62, 57, 53, 48, 43, 38, 33 })),
        (GuitarGroup, new TuningPreset("Bb standard", new[] { 63, 58, 54, 49, 44, 39, 34 })),
        (GuitarGroup, new TuningPreset("G# standard", new[] { 61, 56, 52, 47, 42, 37, 32 })),
        (GuitarGroup, new TuningPreset("G standard", new[] { 60, 55, 51, 46, 41, 36, 31 })),
        (GuitarGroup, new TuningPreset("Drop G", new[] { 62, 57, 53, 48, 43, 38, 31 })),
        (GuitarGroup, new TuningPreset("Drop F", new[] { 60, 55, 51, 46, 41, 36, 29 })),
        (GuitarGroup, new TuningPreset("Open C Townsend (G C G C G C E)", new[] { 64, 60, 55, 48, 43, 36, 31 })),
        (GuitarGroup, new TuningPreset("GCG Periphery (G C G C F A D)", new[] { 62, 57, 53, 48, 43, 36, 31 })),
        // Kept verbatim: the research notes this name suggests a low F# while the notes are
        // Tobias BEADGCF (low B, high F); the corrected Low-F# tuning below carries its own name.
        (BassGroup, new TuningPreset("Standard F# (high F)", new[] { 53, 48, 43, 38, 33, 28, 23 })),
        (BassGroup, new TuningPreset("Low F# (F# B E A D G C)", new[] { 48, 43, 38, 33, 28, 23, 18 })),
    };
    private static readonly (string Group, TuningPreset Preset)[] EightString =
    {
        (GuitarGroup, new TuningPreset("Standard F#", new[] { 64, 59, 55, 50, 45, 40, 35, 30 })),
        (GuitarGroup, new TuningPreset("Drop E", new[] { 64, 59, 55, 50, 45, 40, 35, 28 })),
        (GuitarGroup, new TuningPreset("F standard (half step down)", new[] { 63, 58, 54, 49, 44, 39, 34, 29 })),
        (GuitarGroup, new TuningPreset("E standard (whole step down)", new[] { 62, 57, 53, 48, 43, 38, 33, 28 })),
        (GuitarGroup, new TuningPreset("Eb standard", new[] { 61, 56, 52, 47, 42, 37, 32, 27 })),
        (GuitarGroup, new TuningPreset("D standard", new[] { 62, 54, 51, 46, 41, 36, 31, 26 })),
        (GuitarGroup, new TuningPreset("Drop A + E", new[] { 64, 59, 55, 50, 45, 40, 33, 28 })),
        (GuitarGroup, new TuningPreset("High A", new[] { 69, 64, 59, 55, 50, 45, 40, 35 })),
        (MandolinGroup, new TuningPreset("Standard GDAE", new[] { 76, 76, 69, 69, 62, 62, 55, 55 })),
        (LapSteelGroup, new TuningPreset("C6 console", new[] { 67, 64, 60, 57, 55, 52, 48, 45 })),
        (LapSteelGroup, new TuningPreset("A6 Remington", new[] { 64, 61, 57, 54, 52, 49, 45, 42 })),
        (LapSteelGroup, new TuningPreset("E13 McAuliffe", new[] { 64, 61, 59, 56, 54, 50, 44, 40 })),
        (LapSteelGroup, new TuningPreset("E13 Helms", new[] { 68, 64, 61, 59, 56, 52, 50, 47 })),
        (LapSteelGroup, new TuningPreset("E9 (pedal subset)", new[] { 64, 59, 56, 54, 52, 50, 47, 44 })),
    };
    private static readonly (string Group, TuningPreset Preset)[] NineString =
    {
        (GuitarGroup, new TuningPreset("Standard C#", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25 })),
        (GuitarGroup, new TuningPreset("B standard (low B)", new[] { 62, 57, 53, 48, 43, 38, 33, 28, 23 })),
        (GuitarGroup, new TuningPreset("Drop B", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 23 })),
        (GuitarGroup, new TuningPreset("High A", new[] { 69, 64, 59, 55, 50, 45, 40, 35, 30 })),
        (GuitarGroup, new TuningPreset("Drop E + B", new[] { 64, 59, 55, 50, 45, 40, 35, 28, 23 })),
        (GuitarGroup, new TuningPreset("C standard (low C)", new[] { 63, 58, 54, 49, 44, 39, 34, 29, 24 })),
    };
    private static readonly (string Group, TuningPreset Preset)[] TenString =
    {
        (GuitarGroup, new TuningPreset("ER standard (fourths)", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25, 20 })),
        (GuitarGroup, new TuningPreset("Yepes classical (re-entrant)", new[] { 64, 59, 55, 50, 46, 45, 44, 42, 40, 36 })),
        (GuitarGroup, new TuningPreset("Romantic Decacorde", new[] { 64, 59, 55, 50, 45, 43, 41, 40, 38, 36 })),
        (GuitarGroup, new TuningPreset("High A", new[] { 69, 64, 59, 55, 50, 45, 40, 35, 30, 25 })),
        (GuitarGroup, new TuningPreset("Drop F#", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25, 18 })),
    };
    private static readonly (string Group, TuningPreset Preset)[] FourString =
    {
        (BassGroup, new TuningPreset("Standard E", new[] { 43, 38, 33, 28 })),
        (BassGroup, new TuningPreset("Eb standard", new[] { 42, 37, 32, 27 })),
        (BassGroup, new TuningPreset("D standard", new[] { 41, 36, 31, 26 })),
        (BassGroup, new TuningPreset("Drop D", new[] { 43, 38, 33, 26 })),
        (BassGroup, new TuningPreset("Drop C", new[] { 41, 36, 31, 24 })),
        (BassGroup, new TuningPreset("C# standard", new[] { 40, 35, 30, 25 })),
        (BassGroup, new TuningPreset("C standard", new[] { 39, 34, 29, 24 })),
        (BassGroup, new TuningPreset("B standard (BEAD)", new[] { 38, 33, 28, 23 })),
        (BassGroup, new TuningPreset("Drop B", new[] { 40, 35, 30, 23 })),
        (BassGroup, new TuningPreset("Drop A", new[] { 38, 33, 28, 21 })),
        (BassGroup, new TuningPreset("Tenor (ADGC)", new[] { 48, 43, 38, 33 })),
        (BassGroup, new TuningPreset("Piccolo", new[] { 55, 50, 45, 40 })),
        (BassGroup, new TuningPreset("All-fifths (CGDA)", new[] { 45, 38, 31, 24 })),
        (BassGroup, new TuningPreset("Low A (ADGC low)", new[] { 36, 31, 26, 21 })),
        (UkuleleGroup, new TuningPreset("C6 high-G", new[] { 69, 64, 60, 67 })),
        (UkuleleGroup, new TuningPreset("C6 low-G", new[] { 69, 64, 60, 55 })),
        (UkuleleGroup, new TuningPreset("D6 (A D F# B)", new[] { 71, 66, 62, 69 })),
        (UkuleleGroup, new TuningPreset("Slack-key open-C (high G)", new[] { 67, 64, 60, 67 })),
        (UkuleleGroup, new TuningPreset("Slack-key open-C (low G)", new[] { 67, 64, 60, 55 })),
        (UkuleleGroup, new TuningPreset("Baritone DGBE", new[] { 64, 59, 55, 50 })),
        (UkuleleGroup, new TuningPreset("Baritone high-D (rare)", new[] { 64, 59, 55, 62 })),
        (BanjoGroup, new TuningPreset("Tenor CGDA", new[] { 69, 62, 55, 48 })),
        (BanjoGroup, new TuningPreset("Irish GDAE", new[] { 64, 57, 50, 43 })),
        (BanjoGroup, new TuningPreset("Chicago DGBE", new[] { 64, 59, 55, 50 })),
        (BanjoGroup, new TuningPreset("Plectrum CGBD", new[] { 62, 59, 55, 48 })),
        (ViolinGroup, new TuningPreset("Standard fifths", new[] { 76, 69, 62, 55 })),
        (ViolaGroup, new TuningPreset("Standard fifths", new[] { 69, 62, 55, 48 })),
        (CelloGroup, new TuningPreset("Standard fifths", new[] { 57, 50, 43, 36 })),
        (DoubleBassGroup, new TuningPreset("Standard orchestral", new[] { 43, 38, 33, 28 })),
        (DoubleBassGroup, new TuningPreset("C extension (low C)", new[] { 43, 38, 33, 24 })),
        (DoubleBassGroup, new TuningPreset("Solo (+ step)", new[] { 45, 40, 35, 30 })),
        (DoubleBassGroup, new TuningPreset("Fifths (C G D A)", new[] { 45, 38, 31, 24 })),
        (DoubleBassGroup, new TuningPreset("Fifths-high (G D A E)", new[] { 52, 45, 38, 31 })),
    };
    private static readonly (string Group, TuningPreset Preset)[] FiveString =
    {
        (BassGroup, new TuningPreset("Standard B", new[] { 43, 38, 33, 28, 23 })),
        (BassGroup, new TuningPreset("Standard E, high C", new[] { 48, 43, 38, 33, 28 })),
        (BassGroup, new TuningPreset("Drop A", new[] { 43, 38, 33, 28, 21 })),
        (BassGroup, new TuningPreset("A standard", new[] { 41, 36, 31, 26, 21 })),
        (BassGroup, new TuningPreset("Low F# (F# B E A D)", new[] { 38, 33, 28, 23, 18 })),
        (BassGroup, new TuningPreset("Drop D (B D A D G)", new[] { 43, 38, 33, 26, 23 })),
        (BanjoGroup, new TuningPreset("Open G bluegrass (gDGBD)", new[] { 62, 59, 55, 50, 67 })),
        (BanjoGroup, new TuningPreset("Double-C (gCGCD)", new[] { 62, 60, 55, 48, 67 })),
        (BanjoGroup, new TuningPreset("Drop-C (gCGBD)", new[] { 62, 59, 55, 48, 67 })),
        (BanjoGroup, new TuningPreset("G-modal Sawmill (gDGCD)", new[] { 62, 60, 55, 50, 67 })),
        (BanjoGroup, new TuningPreset("Open D (f#DF#AD)", new[] { 62, 57, 54, 50, 66 })),
        (BanjoGroup, new TuningPreset("Open D high-A (aDF#AD)", new[] { 62, 57, 54, 50, 69 })),
        (BanjoGroup, new TuningPreset("Open C (gCGCE)", new[] { 64, 60, 55, 48, 67 })),
        (BanjoGroup, new TuningPreset("G-minor (gDGBbD)", new[] { 62, 58, 55, 50, 67 })),
        (BanjoGroup, new TuningPreset("Double-D (aDGDE)", new[] { 64, 62, 57, 50, 69 })),
        (BanjoGroup, new TuningPreset("Open A (aEAC#E)", new[] { 64, 61, 57, 52, 69 })),
        (ViolinGroup, new TuningPreset("5-string low-C", new[] { 76, 69, 62, 55, 48 })),
        (ViolaGroup, new TuningPreset("5-string high-E (pomposa)", new[] { 76, 69, 62, 55, 48 })),
        (DoubleBassGroup, new TuningPreset("5-string low-B", new[] { 43, 38, 33, 28, 23 })),
        (DoubleBassGroup, new TuningPreset("5-string high-C (rare)", new[] { 48, 43, 38, 33, 28 })),
    };
    // Dulcimer holds both 3-string and doubled-melody 4-string rows (5/6-string dulcimers are doublings, no new pitches).
    private static readonly (string Group, TuningPreset Preset)[] DulcimerStrings =
    {
        (DulcimerGroup, new TuningPreset("DAD Mixolydian", new[] { 62, 57, 50 })),
        (DulcimerGroup, new TuningPreset("DAA Ionian", new[] { 57, 57, 50 })),
        (DulcimerGroup, new TuningPreset("DAC Aeolian", new[] { 60, 57, 50 })),
        (DulcimerGroup, new TuningPreset("DAG Dorian", new[] { 57, 55, 50 })),
        (DulcimerGroup, new TuningPreset("DGD Reverse Ionian", new[] { 62, 55, 50 })),
        (DulcimerGroup, new TuningPreset("CGC (C-form DAD)", new[] { 60, 55, 48 })),
        (DulcimerGroup, new TuningPreset("CGG (C-form DAA)", new[] { 55, 55, 48 })),
        (DulcimerGroup, new TuningPreset("DADD (doubled melody)", new[] { 62, 62, 57, 50 })),
        (DulcimerGroup, new TuningPreset("DAAA", new[] { 57, 57, 57, 50 })),
    };

    /// <summary>The six-string guitar presets as (name, tuning) pairs, in list order.</summary>
    public static IEnumerable<(string Name, int[] HighToLow)> SixStringPresets => SixString.Where(e => e.Group == GuitarGroup).Select(e => (e.Preset.Name, e.Preset.HighToLow));

    /// <summary>
    /// The full instrument catalog: every built-in preset with its instrument group, in list order.
    /// Guitar and bass entries come first so family preference is stable; string counts run 3 to 10.
    /// </summary>
    public static IEnumerable<(string Group, TuningPreset Preset)> Catalog => SixString.Concat(SevenString).Concat(EightString).Concat(NineString).Concat(TenString).Concat(FourString).Concat(FiveString).Concat(DulcimerStrings);

    /// <summary>The presets for an instrument with this many strings. Presets depend on the instrument as well as the count: a 6-string bass is not a guitar. Empty when there are none.</summary>
    public static IEnumerable<TuningPreset> For(int strings, bool bass) => (strings, bass) switch
    {
        (3, _) => DulcimerStrings.Where(e => e.Preset.HighToLow.Length == 3).Select(e => e.Preset),
        (4, _) => FourString.Where(e => e.Group == BassGroup).Select(e => e.Preset),
        (5, _) => FiveString.Where(e => e.Group == BassGroup).Select(e => e.Preset),
        (6, true) => SixString.Where(e => e.Group == BassGroup).Select(e => e.Preset),
        (6, false) => SixString.Where(e => e.Group == GuitarGroup).Select(e => e.Preset),
        (7, true) => SevenString.Where(e => e.Group == BassGroup).Select(e => e.Preset),
        (7, false) => SevenString.Where(e => e.Group == GuitarGroup).Select(e => e.Preset),
        (8, _) => EightString.Where(e => e.Group == GuitarGroup).Select(e => e.Preset),
        (9, _) => NineString.Select(e => e.Preset),
        (10, _) => TenString.Select(e => e.Preset),
        _ => Array.Empty<TuningPreset>()
    };
}

/// <summary>A named tuning, high to low like <see cref="TrackModel.StringTunings"/>.</summary>
public sealed record TuningPreset(string Name, int[] HighToLow);
