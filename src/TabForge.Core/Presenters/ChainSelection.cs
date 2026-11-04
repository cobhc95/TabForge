namespace TabForge.Presenters;

/// <summary>
/// The index-based multi-selection shared by the FX chain list and the MIDI processing chain list:
/// click (alone, Ctrl toggle, Shift range from the anchor, Ctrl+Shift adds the range), select all,
/// the rubber band (rows it touches, added to the rows it started with), the focused row (the one the
/// detail pane shows) and its anchor.
/// Owns: the selected indexes, the anchor and the focused index, on counts only.
/// Does not own: what a row shows, the row controls, the chain model, the undo step.
/// Tests: FxChainListTests (through FxChainList), MidiProcessingChainMultiSelectTests (through the MIDI window).
/// </summary>
public sealed class ChainSelection
{
    private readonly HashSet<int> _selected = new();
    private readonly HashSet<int> _bandBase = new();
    private int _anchor = -1;
    private int _focused = -1;
    private bool _banding;

    /// <summary>The selected rows, ascending.</summary>
    public IReadOnlyList<int> SelectedRows => _selected.OrderBy(i => i).ToList();

    /// <summary>The focused row (the detail pane follows it); -1 with no rows.</summary>
    public int FocusedIndex => _focused;

    /// <summary>The anchor a Shift-click extends from; -1 when none.</summary>
    public int Anchor => _anchor;

    /// <summary>True while a rubber band is open.</summary>
    public bool IsBanding => _banding;

    /// <summary>True when the row is selected.</summary>
    public bool IsSelected(int index) => _selected.Contains(index);

    /// <summary>Clears the selection (the list was rebuilt empty).</summary>
    public void Clear()
    {
        _selected.Clear();
        _bandBase.Clear();
        _anchor = -1;
        _focused = -1;
        _banding = false;
    }

    /// <summary>A click on a row: alone selects it, Ctrl toggles it, Shift selects from the anchor to it (Ctrl+Shift adds that range).</summary>
    public void Click(int index, int count, bool ctrl = false, bool shift = false)
    {
        if (count <= 0 || index < 0 || index >= count) return;
        var set = ctrl ? _selected.ToHashSet() : new HashSet<int>();
        if (shift && _anchor >= 0 && _anchor < count)
        {
            for (var i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) set.Add(i);
        }
        else
        {
            if (!ctrl || !set.Remove(index)) set.Add(index);
            _anchor = index;
        }
        if (set.Count == 0) set.Add(index);   // the pane always shows a row: the last selected row stays
        Apply(set, set.Contains(index) ? index : set.Contains(_focused) ? _focused : set.Min());
    }

    /// <summary>Selects every row (Ctrl+A); the focus stays where it is.</summary>
    public void SelectAll(int count)
    {
        if (count <= 0) return;
        Apply(Enumerable.Range(0, count).ToHashSet(), Math.Max(0, _focused));
    }

    /// <summary>Collapses a multi-selection to the focused row. False when there was nothing to do.</summary>
    public bool CollapseToFocused()
    {
        if (_selected.Count < 2) return false;
        var focus = Math.Max(0, _focused);
        Apply(new HashSet<int> { focus }, focus);
        return true;
    }

    /// <summary>Moves the focus without changing the selection (a press on a selected row keeps it for a possible drag; the release selects it alone).</summary>
    public void SetFocused(int index, int count)
    {
        if (index < 0 || index >= count) return;
        _focused = index;
    }

    /// <summary>Replaces the selection (a list change the window drove): selects <paramref name="selected"/> that still exist and focuses <paramref name="focus"/>.</summary>
    public void Replace(IEnumerable<int> selected, int focus, int count)
    {
        if (count <= 0) { Clear(); return; }
        focus = Math.Clamp(focus, 0, count - 1);
        var keep = selected.Where(i => i >= 0 && i < count).ToHashSet();
        if (!keep.Contains(focus)) keep = new HashSet<int> { focus };
        Apply(keep, focus);
        _anchor = focus;
    }

    /// <summary>Starts a rubber band: <paramref name="add"/> keeps the rows already selected.</summary>
    public void BeginBand(bool add)
    {
        _bandBase.Clear();
        if (add) foreach (var i in _selected) _bandBase.Add(i);
        _banding = true;
    }

    /// <summary>Selects the rows the band touches, added to the rows it started with.</summary>
    public void UpdateBand(IEnumerable<int> hits)
    {
        var set = _bandBase.ToHashSet();
        foreach (var h in hits) set.Add(h);
        if (set.Count > 0) Apply(set, set.Contains(_focused) ? _focused : set.Min());
    }

    /// <summary>Ends a rubber band, keeping its selection.</summary>
    public void EndBand()
    {
        _banding = false;
        _bandBase.Clear();
    }

    /// <summary>The rows a band between two heights touches (content coordinates).</summary>
    public static List<int> BandHits(IReadOnlyList<double> tops, IReadOnlyList<double> heights, double y0, double y1)
    {
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var hits = new List<int>();
        for (var i = 0; i < tops.Count; i++)
            if (tops[i] < bottom && tops[i] + heights[i] > top) hits.Add(i);
        return hits;
    }

    private void Apply(HashSet<int> set, int focus)
    {
        _selected.Clear();
        foreach (var i in set) _selected.Add(i);
        _focused = focus;
    }
}
