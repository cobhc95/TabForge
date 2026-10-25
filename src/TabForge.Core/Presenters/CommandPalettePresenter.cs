// Owns: the command palette's logic without UI types: its entries, the fuzzy filter and ranking for a query, the selected row, and the command chosen.
// Does not own: the window, the text box and list, key and mouse routing, closing the window, or building entries from the hotkey catalogue (the view does that).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>One command in the palette: its id, its "Category: Name" title, the key text shown beside it, and its description.</summary>
public sealed record CommandPaletteEntry(string Id, string Title, string Key, string Description);

/// <summary>
/// Command palette state. <see cref="SetQuery"/> recomputes the matching commands; the selection is an index into them, or -1 when
/// nothing is selected. Thread: UI thread only (it is driven by the palette window).
/// </summary>
public sealed class CommandPalettePresenter
{
    private readonly List<CommandPaletteEntry> _all;
    private List<CommandPaletteEntry> _shown = new();
    private int _selectedIndex = -1;

    public CommandPalettePresenter(IEnumerable<CommandPaletteEntry> entries) => _all = entries.ToList();

    /// <summary>The commands matching the current query, best match first; all commands by title when the query is empty.</summary>
    public IReadOnlyList<CommandPaletteEntry> Shown => _shown;

    /// <summary>The selected row's index in <see cref="Shown"/>, or -1 when no row is selected.</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>The command id the user picked, or null when nothing has been chosen.</summary>
    public string? ChosenId { get; private set; }

    /// <summary>Filters and ranks the commands for <paramref name="query"/>, then selects the first row when any command matches.</summary>
    public void SetQuery(string query)
    {
        var trimmed = query.Trim();
        _shown = trimmed.Length == 0
            ? _all.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList()
            : _all.Select(e => (Entry: e, Score: Score(trimmed, e.Title)))
                  .Where(x => x.Score > 0)
                  .OrderByDescending(x => x.Score).ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
                  .Select(x => x.Entry).ToList();
        _selectedIndex = _shown.Count > 0 ? 0 : -1;
    }

    /// <summary>Selects the row at <paramref name="index"/>; an index outside <see cref="Shown"/> clears the selection.</summary>
    public void Select(int index) => _selectedIndex = index >= 0 && index < _shown.Count ? index : -1;

    /// <summary>Moves the selection by <paramref name="delta"/> rows, stopping at the first and the last row.</summary>
    public void Move(int delta)
    {
        if (_shown.Count == 0) return;
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _shown.Count - 1);
    }

    /// <summary>Chooses the selected command. Returns false, and chooses nothing, when no row is selected.</summary>
    public bool Choose()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _shown.Count) return false;
        ChosenId = _shown[_selectedIndex].Id;
        return true;
    }

    /// <summary>
    /// Fuzzy score: 0 = no match. Every query character must appear in order (case-insensitive); consecutive
    /// runs and word starts score higher, and earlier/tighter matches beat scattered ones.
    /// </summary>
    public static int Score(string query, string text)
    {
        if (query.Length == 0) return 1;
        var score = 0;
        var t = 0;
        var run = 0;
        foreach (var c in query)
        {
            if (char.IsWhiteSpace(c)) { run = 0; continue; }
            var found = false;
            while (t < text.Length)
            {
                var same = char.ToLowerInvariant(text[t]) == char.ToLowerInvariant(c);
                var wordStart = t == 0 || !char.IsLetterOrDigit(text[t - 1]);
                t++;
                if (!same) { run = 0; continue; }
                run++;
                score += 1 + run * 2 + (wordStart ? 6 : 0);
                found = true;
                break;
            }
            if (!found) return 0;
        }
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 20;
        return score;
    }
}
