namespace TabForge.Services;

// Owns: the families the scale readout groups the scale library into (Major, Minor, Pentatonic, Blues, Harmonic minor, Melodic minor, Modes, Exotic), which are on
//   by default, the scales of each, and how common each scale is (the prior that breaks close fits).
// Does not own: the scale tables (MusicTheoryService.Scales) or the detection (SectionScaleDetector).
// Tests: TabForge.Avalonia.Tests.SectionScaleDetectorTests.
public static class ScaleFamilies
{
    public const string Major = "Major", Minor = "Minor", Pentatonic = "Pentatonic", Blues = "Blues",
        HarmonicMinor = "Harmonic minor", MelodicMinor = "Melodic minor", Modes = "Modes", Exotic = "Exotic";

    /// <summary>Every family, in the order the popup lists its chips.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Major, Minor, Pentatonic, Blues, HarmonicMinor, MelodicMinor, Modes, Exotic };

    /// <summary>The families that are on until the user changes them.</summary>
    public static readonly IReadOnlyList<string> DefaultOn = new[] { Major, Minor, Pentatonic, Blues, HarmonicMinor, MelodicMinor };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ScalesByFamily = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        [Major] = new[] { "Major" },
        [Minor] = new[] { "Natural Minor" },
        [Pentatonic] = new[] { "Major Pentatonic", "Minor Pentatonic" },
        [Blues] = new[] { "Blues" },
        [HarmonicMinor] = new[] { "Harmonic Minor" },
        [MelodicMinor] = new[] { "Melodic Minor" },
        [Modes] = new[] { "Dorian", "Phrygian", "Lydian", "Mixolydian", "Locrian" },
        [Exotic] = new[] { "Diminished", "Whole Tone" },
    };

    /// <summary>The scale names of a family; empty for an unknown family.</summary>
    public static IReadOnlyList<string> ScalesOf(string family) => ScalesByFamily.TryGetValue(family, out var scales) ? scales : Array.Empty<string>();

    /// <summary>The scales of the given families, without repeats, in the library order.</summary>
    public static IReadOnlyList<string> ScalesOf(IEnumerable<string> families)
    {
        var chosen = new HashSet<string>(families.SelectMany(ScalesOf), StringComparer.OrdinalIgnoreCase);
        return MusicTheoryService.Scales.Keys.Where(chosen.Contains).ToList();
    }

    /// <summary>The known family names of <paramref name="families"/> in the standard order (unknown names are dropped).</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? families)
    {
        var wanted = new HashSet<string>(families ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return All.Where(wanted.Contains).ToList();
    }

    /// <summary>How common a scale is, as a bonus in score points: major and minor, then pentatonic and blues, then harmonic and melodic minor, then the modes, then the exotic scales.</summary>
    public static double Prior(string scale) => scale switch
    {
        "Major" or "Natural Minor" => 6,
        "Major Pentatonic" or "Minor Pentatonic" or "Blues" => 3,
        "Harmonic Minor" or "Melodic Minor" => 1.5,
        "Diminished" or "Whole Tone" => -2,
        _ => 0,
    };

    /// <summary>The wording of a scale in a readout: "minor" for Natural Minor, else the name in lower case ("major", "minor pentatonic", "blues").</summary>
    public static string Wording(string scale) => scale.Equals("Natural Minor", StringComparison.OrdinalIgnoreCase) ? "minor" : scale.ToLowerInvariant();

    private static readonly IReadOnlyList<string> FlatNames = new[] { "C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B" };
    private static readonly IReadOnlyList<string> PlainSharpNames = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly IReadOnlyList<string> KeySignatureNames = new[] { "C", "C#", "D", "E♭", "E", "F", "F#", "G", "A♭", "A", "B♭", "B" };

    /// <summary>True when the key of this scale is a flat key by the key signature (F, B♭, E♭, A♭, D♭ major and their relative minors); the tritone key is spelled F#.</summary>
    public static bool PrefersFlats(int root, string scale)
    {
        var minor = MusicTheoryService.Scales.TryGetValue(scale, out var iv) && iv.Contains(3) && !iv.Contains(4);
        var major = (((root + (minor ? 3 : 0)) % 12) + 12) % 12;
        return major is 1 or 3 or 5 or 8 or 10;
    }

    /// <summary>The name of a pitch class: flats when <paramref name="flats"/>, else sharps (D#); <paramref name="musical"/> spells the sharp side like the key signature (E♭ A♭ B♭ with C# F#).</summary>
    public static string NoteName(int pitchClass, bool flats, bool musical = false) =>
        (flats ? FlatNames : musical ? KeySignatureNames : PlainSharpNames)[((pitchClass % 12) + 12) % 12];

    /// <summary>The notes of a scale ("D# F G G# A# C D"; with <paramref name="musical"/> spelled like its key: "E♭ F G A♭ B♭ C D").</summary>
    public static IReadOnlyList<string> NotesOf(int root, string scale, bool flats = false, bool musical = false)
    {
        var spell = musical && (flats || PrefersFlats(root, scale));
        var iv = MusicTheoryService.Scales.TryGetValue(scale, out var found) ? found : MusicTheoryService.Scales["Major"];
        return iv.Select(i => NoteName(root + i, spell, musical)).ToList();
    }

    /// <summary>"A minor", "D# major" ("E♭ major" with <paramref name="musical"/>); <paramref name="flats"/> (a flat song key) forces flats, only with musical names.</summary>
    public static string Display(int root, string scale, bool flats = false, bool musical = false) =>
        $"{NoteName(root, musical && (flats || PrefersFlats(root, scale)), musical)} {Wording(scale)}";
}
