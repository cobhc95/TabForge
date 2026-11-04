namespace TabForge.Presenters;

/// <summary>Whether the Up, Down and Remove buttons of the MIDI processing list are enabled.</summary>
public sealed record ListButtonStates(bool Up, bool Down, bool Remove);

/// <summary>
/// The MIDI processing window's processor list rules: the chain's size cap, where a new processor goes, where a moved one lands,
/// which list buttons are enabled, the row labels, and the detail texts when no known processor is selected.
/// Owns: those rules and texts, on indexes and counts only.
/// Does not own: the chain itself (the plug-in slot), the undo and engine update (the window's edit delegate), the catalog
///     (MidiProcessorCatalog) and the parameter editors.
/// Tests: MidiProcessingWindowTests.
/// </summary>
public static class MidiProcessingListPresenter
{
    /// <summary>The most processors one plug-in's MIDI chain holds.</summary>
    public const int MaxProcessors = 32;

    public const string EmptyDescription = "Search the catalog on the left and add a processor. They run in order, before the plug-in, on everything it plays: the song, live keys and other tracks routed to it.";
    public const string NoInstrumentDescription = "These processors act on the MIDI sent to a plug-in; add an instrument to use them.";
    public const string UnknownTypeDescription = "This processor type is not known to this version of TabForge; it is kept in the song and skipped.";
    public const string EffectChainNote = " On an effect they shape the MIDI this plug-in and the ones after it receive (the General MIDI synth hears processed MIDI only from a first-slot instrument's list).";

    /// <summary>A processor can be added while the chain holds fewer than <see cref="MaxProcessors"/>.</summary>
    public static bool CanAdd(int count) => count < MaxProcessors;

    /// <summary>A new processor goes right after the selected one, else at the end; the index is kept inside the chain.</summary>
    public static int InsertIndex(int? selectedIndex, int count) => Math.Clamp(selectedIndex is int index ? index + 1 : count, 0, count);

    /// <summary>The index a processor lands on when moved by <paramref name="delta"/>, or null when the move leaves the chain.</summary>
    public static int? MoveTarget(int from, int delta, int count)
    {
        var to = from + delta;
        return from < 0 || to < 0 || to >= count ? null : to;
    }

    /// <summary>Up needs a row above the selection, Down a row below it, and Remove a selection; with no selection all are off.</summary>
    public static ListButtonStates ButtonStates(int? selectedIndex, int count) => new(
        Up: selectedIndex is int up && up > 0,
        Down: selectedIndex is int down && down < count - 1,
        Remove: selectedIndex is not null);

    /// <summary>Up needs a row above the whole selection, Down a row below it, and Remove any selection; with none all are off.</summary>
    public static ListButtonStates ButtonStates(IReadOnlyList<int> selected, int count) => new(
        Up: selected.Count > 0 && selected.Min() > 0,
        Down: selected.Count > 0 && selected.Max() < count - 1,
        Remove: selected.Count > 0);

    /// <summary>Moves a multi-selection by <paramref name="delta"/> (±1) as one step, keeping the relative order and the gaps:
    /// each selected row swaps with the unselected neighbour in the move direction. Returns the new order (old indexes by new
    /// position), the new selection and the new focus, or null when the move leaves the chain.</summary>
    public static (IReadOnlyList<int> Order, IReadOnlyList<int> Selected, int Focus)? MoveMany(IReadOnlyList<int> selected, int focused, int delta, int count)
    {
        if ((delta != -1 && delta != 1) || selected.Count == 0 || count <= 0) return null;
        var set = selected.ToHashSet();
        if (delta < 0 && selected.Min() <= 0) return null;
        if (delta > 0 && selected.Max() >= count - 1) return null;
        var order = Enumerable.Range(0, count).ToList();
        var walk = delta < 0 ? selected.OrderBy(i => i).ToList() : selected.OrderByDescending(i => i).ToList();
        foreach (var old in walk)
        {
            var pos = order.IndexOf(old);
            var swap = pos + delta;
            if (pos < 0 || swap < 0 || swap >= count) return null;
            if (set.Contains(order[swap])) continue;   // the neighbour moves too: keep the block together
            (order[pos], order[swap]) = (order[swap], order[pos]);
        }
        var focusOld = set.Contains(focused) ? focused : selected.Min();
        var selectedNew = order.Select((old, pos) => (old, pos)).Where(t => set.Contains(t.old)).Select(t => t.pos).OrderBy(i => i).ToList();
        return (order, selectedNew, order.IndexOf(focusOld));
    }

    /// <summary>The row label of a processor: its catalog name, or its type marked as unknown.</summary>
    public static string RowLabel(string type, string? catalogName) => catalogName ?? $"{type} (unknown)";

    /// <summary>
    /// The detail title and description when no known processor is selected. <paramref name="selectedType"/> is null when
    /// nothing is selected. <paramref name="effectWithProcessors"/> adds the note on what an effect's processors shape.
    /// </summary>
    public static (string Title, string Description) UnknownDetail(string? selectedType, bool effectWithProcessors)
    {
        var description = selectedType is null ? EmptyDescription : UnknownTypeDescription;
        if (effectWithProcessors) description += EffectChainNote;
        return (selectedType ?? "No processors yet", description);
    }
}
