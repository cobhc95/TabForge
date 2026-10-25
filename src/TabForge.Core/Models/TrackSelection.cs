namespace TabForge.Models;

// Owns: the multi-selection of track list rows by track id (plain, Shift range, Ctrl toggle), its anchor, and which tracks an action on a clicked row covers.
// Does not own: the id list itself (SelectionModel.Rows, so it is saved with each undo step), the views, or the edits that act on the tracks (TrackGroupEdits).
// Tests: TrackSelectionFollowUpTests.

/// <summary>How a click on a track row changes the selection.</summary>
public enum TrackPick { Plain, Range, Toggle }

/// <summary>The track rows selected in the track list, owned by the document's <see cref="SelectionModel"/>. Ids, never indexes, so a reorder or undo keeps them.</summary>
public sealed class TrackSelection
{
    private readonly SelectionModel _model;
    private Guid? _anchor;

    /// <summary>The track a Shift-range click extends from (the last plain or Ctrl click); kept in the undo step so undo brings it back.</summary>
    public Guid? Anchor => _anchor;

    public TrackSelection(SelectionModel model) => _model = model;

    /// <summary>Applies a click on <paramref name="row"/>: Plain selects only it, Range selects every track from the anchor to it, Toggle adds or removes it (and becomes the anchor).</summary>
    public void Click(IReadOnlyList<TrackModel> tracks, int row, TrackPick pick, SelectionOrigin origin = SelectionOrigin.Timeline)
    {
        if (row < 0 || row >= tracks.Count) return;
        var id = tracks[row].Id;
        var anchorRow = _anchor is { } a ? IndexOf(tracks, a) : -1;
        IEnumerable<Guid> next;
        if (pick == TrackPick.Range && anchorRow >= 0)
            next = tracks.Skip(Math.Min(anchorRow, row)).Take(Math.Abs(row - anchorRow) + 1).Select(t => t.Id);
        else if (pick == TrackPick.Toggle)
            next = _model.Rows.Contains(id) ? _model.Rows.Where(r => r != id) : _model.Rows.Append(id);
        else
            next = new[] { id };
        if (pick != TrackPick.Range || anchorRow < 0) _anchor = id;
        _model.SetRows(next.Distinct(), origin);
    }

    /// <summary>Sets the anchor to <paramref name="anchor"/> when <paramref name="project"/> still has that track, else none.</summary>
    public void SetAnchor(Guid? anchor, SongProject project) => _anchor = anchor is { } id && project.Tracks.Any(t => t.Id == id) ? id : null;

    /// <summary>Is the track at <paramref name="row"/> selected.</summary>
    public bool Contains(IReadOnlyList<TrackModel> tracks, int row) => row >= 0 && row < tracks.Count && _model.Rows.Contains(tracks[row].Id);

    /// <summary>The row indexes of the selected tracks, ascending (ids the song no longer has are skipped).</summary>
    public IReadOnlyList<int> Rows(IReadOnlyList<TrackModel> tracks) =>
        Enumerable.Range(0, tracks.Count).Where(i => _model.Rows.Contains(tracks[i].Id)).ToList();

    /// <summary>The rows an action on <paramref name="clicked"/> covers: every selected track when the clicked one is selected, else just the clicked one.</summary>
    public IReadOnlyList<int> Targets(IReadOnlyList<TrackModel> tracks, int clicked) =>
        Contains(tracks, clicked) ? Rows(tracks) : clicked >= 0 && clicked < tracks.Count ? new[] { clicked } : Array.Empty<int>();

    private static int IndexOf(IReadOnlyList<TrackModel> tracks, Guid id)
    {
        for (var i = 0; i < tracks.Count; i++) if (tracks[i].Id == id) return i;
        return -1;
    }
}
