// Owns: the Colour tracks dialog's logic without UI types: which track rows are ticked, the range that ticks a run of rows, the
//       colouring of the ticked rows (returning which rows took it), and the flag that says the undo capture is still due and the named track colour palette.
// Does not own: the window, the check boxes, swatches, range combo boxes and palette buttons, the track model (the caller sets the colour
//       on the rows it is given), and the undo stack.
// Tests: no dedicated test class.
using TabForge.Platform.Theme;

namespace TabForge.Presenters;

/// <summary>The track rows a colour was applied to, and whether this was the first change (the caller captures undo before it).</summary>
public sealed record TrackColourChange(IReadOnlyList<int> TrackIndexes, bool IsFirstChange);

/// <summary>
/// Tick state for the track rows, and colouring the ticked rows. <see cref="ColourTicked"/> returns null and changes nothing when no
/// row is ticked. Thread: UI thread only (it is driven by the Colour tracks window).
/// </summary>
public sealed class TrackColoursPresenter
{
    /// <summary>The named track colours the picker shows: the shared list <see cref="NamedColourSwatches.All"/>.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette = NamedColourSwatches.All;

    private readonly bool[] _ticked;
    private bool _changed;

    public TrackColoursPresenter(int trackCount) => _ticked = new bool[trackCount];

    /// <summary>Whether the row at <paramref name="index"/> is ticked.</summary>
    public bool IsTicked(int index) => _ticked[index];

    /// <summary>Ticks or unticks one row.</summary>
    public void SetTicked(int index, bool ticked) => _ticked[index] = ticked;

    /// <summary>Ticks the rows from <paramref name="fromIndex"/> to <paramref name="toIndex"/> inclusive, in either order, and unticks every other row.</summary>
    public void TickRange(int fromIndex, int toIndex)
    {
        var first = Math.Min(fromIndex, toIndex);
        var last = Math.Max(fromIndex, toIndex);
        for (var i = 0; i < _ticked.Length; i++) _ticked[i] = i >= first && i <= last;
    }

    /// <summary>The rows to colour, with whether this is the first change. Null when no row is ticked.</summary>
    public TrackColourChange? ColourTicked()
    {
        var ticked = Enumerable.Range(0, _ticked.Length).Where(i => _ticked[i]).ToList();
        if (ticked.Count == 0) return null;
        var isFirst = !_changed;
        _changed = true;
        return new TrackColourChange(ticked, isFirst);
    }
}
