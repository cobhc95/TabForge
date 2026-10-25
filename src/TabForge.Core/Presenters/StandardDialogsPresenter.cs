using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>
/// The logic of the small standard dialogs (GpDialogs) as plain data.
/// Owns: the scope texts, the direction names and labels, the marker colours, and parsing and clamping what the user typed.
/// Does not own: the key and denominator choices (SignatureChoices), building or showing the windows (GpDialogs, DialogHost) or colour text validation (Visualization.ColourText).
/// Tests: no dedicated test class.
/// </summary>
public static class StandardDialogsPresenter
{
    public static readonly string[] DirectionNames = { "Segno", "Coda", "DoubleCoda", "Fine", "DaCapo", "DalSegno", "ToCoda", "DaCapoAlCoda", "DalSegnoAlCoda" };

    public static readonly IReadOnlyDictionary<string, string> DirectionLabels = new Dictionary<string, string>
    {
        ["Segno"] = "Segno destination", ["Coda"] = "Coda destination", ["DoubleCoda"] = "Double Coda destination",
        ["Fine"] = "Fine (stop after a navigation jump)", ["DaCapo"] = "Da Capo (jump to beginning)",
        ["DalSegno"] = "Dal Segno (jump to Segno)", ["ToCoda"] = "To Coda (jump to Coda)",
        ["DaCapoAlCoda"] = "Da Capo al Coda", ["DalSegnoAlCoda"] = "Dal Segno al Coda"
    };

    /// <summary>The section marker colours the picker shows: the shared list <see cref="NamedColourSwatches.Sections"/>.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> MarkerColours = NamedColourSwatches.Sections;

    /// <summary>The line under a time or key signature: where the change applies.</summary>
    public static string ScopeText(string kind, string? selectedBars) => selectedBars is null
        ? $"Applies from this bar until the next {kind} change."
        : $"Applies to the selected {selectedBars}.";

    /// <summary>The time signature the user typed, beats clamped to 1-32; null when either part is not a number.</summary>
    public static (int Num, int Denom, bool OnlyThisBar)? TimeSignature(string beats, string? beatValue, bool onlyThisBar) =>
        int.TryParse(beats, out var n) && int.TryParse(beatValue, out var d) ? (Math.Clamp(n, 1, 32), d, onlyThisBar) : null;

    /// <summary>The key list index for a signature (-7 Cb to +7 C#), and back.</summary>
    public static int KeyIndex(int signature) => signature + 7;
    public static int KeySignature(int index) => index - 7;

    public static string[] SplitDirections(string? current) => (current ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The stored directions: tokens this dialog does not know, kept first, then the ticked names.</summary>
    public static string JoinDirections(IEnumerable<string> existing, IEnumerable<string> ticked)
    {
        var known = new HashSet<string>(DirectionNames, StringComparer.OrdinalIgnoreCase);
        return string.Join(',', existing.Where(token => !known.Contains(token)).Concat(ticked));
    }

    public static int ClampEnding(int ending) => Math.Clamp(ending, 0, 8);

    /// <summary>The alternate ending typed (0-8), or null when it is not accepted.</summary>
    public static int? ParseEnding(string text) => int.TryParse(text, out var value) && value is >= 0 and <= 8 ? value : null;

    /// <summary>The marker's trimmed name and colour, or null for a blank name or no colour.</summary>
    public static (string Title, string Colour)? MarkerResult(string name, string? colour) =>
        !string.IsNullOrWhiteSpace(name) && colour is not null ? (name.Trim(), colour) : null;

    public static int? ClampedInt(string text, int min, int max) => int.TryParse(text, out var v) ? Math.Clamp(v, min, max) : null;

    /// <summary>A tuning typed as MIDI numbers (clamped 12-96) or note names such as E4; unknown tokens are skipped.</summary>
    public static List<int> ParseTuning(string s)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var m = 12; m < 100; m++) map[MusicTheoryService.NoteName(m)] = m;
        var tunings = new List<int>();
        foreach (var token in s.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var v)) tunings.Add(Math.Clamp(v, 12, 96));
            else if (map.TryGetValue(token.Trim(), out var mv)) tunings.Add(mv);
        }
        return tunings;
    }
}
