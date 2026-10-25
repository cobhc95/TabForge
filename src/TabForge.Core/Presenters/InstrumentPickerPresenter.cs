// Owns: the Choose instrument window's logic without UI types: its texts, the families in catalogue order, the chosen sound, which
//       families are open (focus-family mode opens only the current one), the live search filter (every typed word in the name or
//       family), what each family header shows (open or closed, how many sounds) and the tile tips.
// Does not own: the catalogue (the app's InstrumentCatalog, passed in as plain tuples), the icons, the window or the audition.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>
/// Choose instrument state. Entries are (Name, Family, Program, IsDrumKit) tuples in catalogue order; families keep the order they first
/// appear in. Thread: UI thread only (it is driven by the window).
/// </summary>
public sealed class InstrumentPickerPresenter
{
    public const string Title = "Choose instrument";
    public const string SearchHint = "Search instruments (e.g. \"bass\", \"strings\", \"drum\")…";
    public const string SelectButton = "Select";
    public const string CancelButton = "Cancel";

    /// <summary>What a family header shows: whether the header is visible, whether its tiles are, whether the family is open, and how many sounds it lists under the search.</summary>
    public readonly record struct FamilyState(string Family, bool HeaderVisible, bool TilesVisible, bool Open, int Count);

    private readonly Dictionary<string, bool> _open;
    private string[] _words = [];

    public InstrumentPickerPresenter(IReadOnlyList<(string Name, string Family, int Program, bool IsDrumKit)> entries, string? current, bool focusFamily)
    {
        Entries = entries;
        FocusFamily = focusFamily;
        Families = entries.Select(e => e.Family).Distinct().ToList();
        var match = entries.FirstOrDefault(e => string.Equals(e.Name, current, StringComparison.OrdinalIgnoreCase));
        Chosen = current;
        CurrentFamily = match.Name is null ? null : match.Family;
        _open = Families.ToDictionary(f => f, f => !focusFamily || CurrentFamily is null || f == CurrentFamily);
    }

    public IReadOnlyList<(string Name, string Family, int Program, bool IsDrumKit)> Entries { get; }

    public IReadOnlyList<string> Families { get; }

    /// <summary>The family of the current sound, or null when it is not in the list.</summary>
    public string? CurrentFamily { get; }

    /// <summary>True when family headers open and close with a click.</summary>
    public bool FocusFamily { get; }

    /// <summary>The chosen sound's name: the current one until a tile is picked.</summary>
    public string? Chosen { get; private set; }

    public bool IsChosen(int index) => string.Equals(Entries[index].Name, Chosen, StringComparison.OrdinalIgnoreCase);

    public void Choose(int index) => Chosen = Entries[index].Name;

    public void SetSearch(string? text) => _words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public bool Searching => _words.Length > 0;

    /// <summary>True when the entry's name or family contains every typed word.</summary>
    public bool Matches(int index)
    {
        var hay = Entries[index].Name + " " + Entries[index].Family;
        return _words.All(word => hay.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Index of the first matching entry, or -1; Enter in the search box picks it.</summary>
    public int FirstMatch() => Enumerable.Range(0, Entries.Count).FirstOrDefault(Matches, -1);

    public void ToggleFamily(string family) => _open[family] = !_open[family];

    /// <summary>Each family's header and tiles under the current search; while searching, matches show in every family.</summary>
    public FamilyState StateOf(string family)
    {
        var count = Enumerable.Range(0, Entries.Count).Count(i => Entries[i].Family == family && Matches(i));
        var shown = count > 0 && (Searching || _open[family]);
        return new FamilyState(family, count > 0, shown, shown, count);
    }

    /// <summary>The tile tip, e.g. "Grand Piano  ·  GM program 1".</summary>
    public string TipOf(int index) =>
        $"{Entries[index].Name}  ·  {(Entries[index].IsDrumKit ? "drum kit, channel 10" : $"GM program {Entries[index].Program + 1}")}";
}
