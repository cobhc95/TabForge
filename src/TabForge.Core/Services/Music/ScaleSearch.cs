using System.Text;

namespace TabForge.Services;

/// <summary>One scale of the full list: a key and a scale of the library.</summary>
/// <param name="Root">The key's pitch class (0 = C).</param>
/// <param name="Scale">The scale name in the library (MusicTheoryService.Scales).</param>
/// <param name="Notes">The note names ("D E F G A B♭ C").</param>
public sealed record ScaleSearchHit(int Root, string Scale, string Notes)
{
    /// <summary>The "Root Scale" highlight text ("D Natural Minor").</summary>
    public string Highlight => $"{MusicTheoryService.NoteNames[Root]} {Scale}";

    /// <summary>The scale as the readout words it ("D minor").</summary>
    public string Display(bool flats = false, bool musical = false) => ScaleFamilies.Display(Root, Scale, flats, musical);
}

// Owns: the full scale list (every scale of the library in all 12 keys, grouped by library order) and the "Find a scale…" filter: by scale name, key,
//   common aliases (Natural Minor = Aeolian = minor, Major = Ionian...) and, when every word is a note letter, by the notes a scale contains.
// Does not own: the scale tables (MusicTheoryService), the popup that shows the list, the fit percentage (the caller adds it).
// Tests: TabForge.Avalonia.Tests.ScaleSearchTests.
// Threads: any (immutable after construction); one filter pass is a few hundred cheap string checks.
public sealed class ScaleSearch
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Major"] = "ionian maj major scale",
        ["Natural Minor"] = "aeolian minor min natural minor scale",
        ["Harmonic Minor"] = "harmonic minor harm min",
        ["Melodic Minor"] = "jazz minor ascending melodic minor mel min",
        ["Major Pentatonic"] = "major pent pentatonic major maj pent",
        ["Minor Pentatonic"] = "minor pent pentatonic minor min pent",
        ["Dorian"] = "dorian mode ii",
        ["Phrygian"] = "phrygian mode iii",
        ["Lydian"] = "lydian mode iv",
        ["Mixolydian"] = "mixolydian mode v dominant",
        ["Locrian"] = "locrian mode vii",
        ["Diminished"] = "octatonic half whole whole half dim",
        ["Whole Tone"] = "whole-tone hexatonic",
        ["Blues"] = "blues minor blues",
        ["Major Blues"] = "major blues blues major",
        ["Bebop Dominant"] = "bebop dominant jazz",
        ["Bebop Major"] = "bebop major jazz",
        ["Bebop Dorian"] = "bebop dorian jazz",
        ["Altered"] = "super locrian altered dominant diminished whole tone jazz",
        ["Lydian Dominant"] = "lydian dominant acoustic overtone mixolydian #4 jazz",
        ["Lydian Augmented"] = "lydian augmented jazz",
        ["Half-Whole Diminished"] = "half whole diminished dominant diminished jazz octatonic",
        ["Locrian Natural 2"] = "locrian natural 2 half diminished jazz",
        ["Mixolydian b6"] = "mixolydian b6 hindu jazz",
        ["Dorian b2"] = "dorian b2 phrygian natural 6 jazz",
        ["Hungarian Minor"] = "hungarian minor gypsy minor",
        ["Hungarian Major"] = "hungarian major",
        ["Phrygian Dominant"] = "phrygian dominant spanish gypsy gypsy freygish",
        ["Double Harmonic"] = "double harmonic arabic byzantine",
        ["Hirajoshi"] = "hirajoshi japanese",
        ["In-Sen"] = "in-sen insen japanese",
        ["Iwato"] = "iwato japanese",
        ["Kumoi"] = "kumoi japanese",
        ["Neapolitan Minor"] = "neapolitan minor",
        ["Neapolitan Major"] = "neapolitan major",
        ["Persian"] = "persian",
        ["Enigmatic"] = "enigmatic verdi",
        ["Prometheus"] = "prometheus mystic",
        ["Augmented"] = "augmented hexatonic",
    };

    private readonly List<(ScaleSearchHit Hit, string Words, int Mask)> _entries = new();

    public ScaleSearch()
    {
        foreach (var (scale, intervals) in MusicTheoryService.Scales)
            for (var root = 0; root < 12; root++)
            {
                var mask = 0;
                foreach (var i in intervals) mask |= 1 << ((root + i) % 12);
                var notes = string.Join(" ", ScaleFamilies.NotesOf(root, scale));
                var words = (scale + " " + (Aliases.TryGetValue(scale, out var a) ? a : "")).ToLowerInvariant();
                _entries.Add((new ScaleSearchHit(root, scale, notes), words, mask));
            }
    }

    /// <summary>Every scale in every key, in library order.</summary>
    public IReadOnlyList<ScaleSearchHit> All => _entries.Select(e => e.Hit).ToList();

    /// <summary>The scales that match <paramref name="query"/> (everything for a blank query). "d harm": key D and "harm" in the name; "dorian": every key;
    /// "c#": the C# scales; "D E F G A": the scales that contain all those notes.</summary>
    public IReadOnlyList<ScaleSearchHit> Find(string? query)
    {
        var words = (query ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All;
        var pitches = words.Select(NoteOf).ToArray();
        if (words.Length >= 2 && pitches.All(p => p is not null))
        {
            var need = pitches.Aggregate(0, (m, p) => m | (1 << p!.Value));
            return _entries.Where(e => (e.Mask & need) == need).Select(e => e.Hit).ToList();
        }
        int? root = pitches[0];
        var text = words.Skip(root is null ? 0 : 1).Select(w => w.ToLowerInvariant()).ToArray();
        return _entries.Where(e => (root is null || e.Hit.Root == root) && text.All(w => e.Words.Contains(w))).Select(e => e.Hit).ToList();
    }

    /// <summary>The pitch class of a note word ("c#", "Bb", "e♭", "D"); null when the word is not a note.</summary>
    public static int? NoteOf(string word)
    {
        if (word.Length is < 1 or > 2) return null;
        var letter = char.ToUpperInvariant(word[0]);
        if (letter is < 'A' or > 'G') return null;
        int[] steps = { 9, 11, 0, 2, 4, 5, 7 };
        var pc = steps[letter - 'A'];
        if (word.Length == 2)
        {
            if (word[1] is '#' or '♯') pc++;
            else if (word[1] is 'b' or '♭') pc--;
            else return null;
        }
        return ((pc % 12) + 12) % 12;
    }

    /// <summary>The fit of a scale to a passage's note weights as a share of the played weight that lies in the scale (0 to 1); 0 for no notes.</summary>
    public static double Fit(ScaleSearchHit hit, PitchHistogram histogram)
    {
        var total = histogram.Weights.Sum();
        if (total <= 0) return 0;
        var inside = 0.0;
        for (var pc = 0; pc < 12; pc++)
            if (MusicTheoryService.Scales.TryGetValue(hit.Scale, out var iv) && iv.Contains(((pc - hit.Root) % 12 + 12) % 12)) inside += histogram.Weights[pc];
        return inside / total;
    }

    /// <summary>The empty-search label: "88 scales" etc.</summary>
    public static string CountText(int count) => count == 1 ? "1 scale" : $"{count} scales";
}
