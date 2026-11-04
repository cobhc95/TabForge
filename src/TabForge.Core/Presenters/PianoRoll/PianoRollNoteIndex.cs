namespace TabForge.Presenters;

// Owns: an immutable interval index for one reloaded piano-roll note snapshot; queries fill caller-owned scratch lists.
// Does not own: notes, selection, playback position or drawing. Thread: UI thread, like PianoRollPresenter.
// Tests: PianoRollNoteIndexTests.
public sealed class PianoRollNoteIndex
{
    private readonly IReadOnlyList<PianoRollNote> _notes;
    private readonly int[] _order;
    private readonly double[] _maxEnd;

    public PianoRollNoteIndex(IReadOnlyList<PianoRollNote> notes)
    {
        _notes = notes;
        _order = Enumerable.Range(0, notes.Count).OrderBy(i => notes[i].Start).ThenBy(i => i).ToArray();
        _maxEnd = new double[notes.Count];
        Build(0, _order.Length);
    }

    private double Build(int first, int end)
    {
        if (first >= end) return double.NegativeInfinity;
        var mid = first + (end - first) / 2;
        return _maxEnd[mid] = Math.Max(_notes[_order[mid]].End, Math.Max(Build(first, mid), Build(mid + 1, end)));
    }

    /// <summary>All notes touching [from, to], in original draw order; includes long sustains beginning before the range.</summary>
    public void Query(double from, double to, List<int> result)
    {
        result.Clear();
        if (to < from || double.IsNaN(from) || double.IsNaN(to)) return;
        Visit(0, _order.Length, from, to, result);
        result.Sort();
    }

    private void Visit(int first, int end, double from, double to, List<int> result)
    {
        if (first >= end) return;
        var mid = first + (end - first) / 2;
        if (_maxEnd[mid] < from || _notes[_order[first]].Start > to) return;
        Visit(first, mid, from, to, result);
        var note = _notes[_order[mid]];
        if (note.Start <= to && note.End >= from) result.Add(_order[mid]);
        if (note.Start <= to) Visit(mid + 1, end, from, to, result);
    }
}
