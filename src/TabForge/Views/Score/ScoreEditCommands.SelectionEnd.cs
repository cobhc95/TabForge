using System.Linq;
using TabForge.Models;

namespace TabForge.Views.Score;

// ScoreEditCommands: where the cursor goes when a command ends the selection (GP5).
// Owns: ending the selection after Backspace / Delete, and the cursor Undo / Redo leave (Undo: the selection's last beat, else the cursor's
// beat; Redo: where the cursor was when that step was undone). Does not own: the undo history (DocumentEdits), the paste (EditCommands.RunPaste).
// Tests: TestGp5SelectionEndStories, TestGp5CursorStories (tests/full-suite/Editor/).

public sealed partial class ScoreEditCommands
{
    // The cursor each Undo found, newest last, with the song's fingerprint before that Undo: Redo back to that song puts the cursor there.
    private readonly List<(string Song, (int Measure, int Beat, bool Selected) Cursor)> _undoneCursors = new();

    /// <summary>Backspace / Delete used the selection up: it ends, the cursor on the beat where it started.</summary>
    private void EndSelection(int measure, int cell)
    {
        _restSelection = null;
        _c.ClearSelection();
        PlaceCursor(measure, cell);
    }

    /// <summary>
    /// Where Undo / Redo leave the cursor, read before they restore the song: the selection's last beat (score order), else the cursor, as its
    /// bar and its number among the bar's cursor positions (GP5 keeps the beat, and restored durations move the cells, not the beats).
    /// </summary>
    public (int Measure, int Beat, bool Selected)? CursorBeat()
    {
        var track = Track;
        if (track is null) return null;
        var (m, c) = (SelectedMeasure, SelectedCell);
        if (HasSelection) { var (_, _, m2, c2) = SelectionRangeOrdered(); m = m2; c = c2; }
        if (m < 0 || m >= track.Measures.Count) return null;
        var cells = CellsFor(track.Measures[m]);
        if (HasSelection && cells.Count > 0) c = CursorPositions.SnapToBeat(cells, Math.Clamp(c, 0, cells.Count - 1), preferNext: false);
        return (m, CursorPositions.Allowed(cells, SlotsFor(m)).Count(a => a < c), HasSelection);
    }

    /// <summary>
    /// <see cref="CursorBeat"/> before an Undo of the song with fingerprint <paramref name="song"/>, kept for the Redo back to it. A writing mark undone
    /// before this song step can no longer be redone first (Redo takes the song step back first).
    /// </summary>
    public (int Measure, int Beat, bool Selected)? CursorBeatForUndo(string song)
    {
        _writingRedo.Clear();
        var at = CursorBeat();
        if (at is { } cursor) _undoneCursors.Add((song, cursor));
        if (_undoneCursors.Count > 256) _undoneCursors.RemoveAt(0);
        return at;
    }

    /// <summary>
    /// After Undo / Redo restored the song (GP5): the selection ends and the cursor goes to the beat <see cref="CursorBeat"/> read. Redo to the song
    /// <paramref name="redoneTo"/> puts the cursor where it was when that step was undone (quiet GP5, work/gp5diff/i4micro: after `14 Right Undo Redo`
    /// the next fret goes after the 14, after `Ctrl+Right Undo Redo` into bar 2, after Redo of a paste onto the first pasted beat).
    /// </summary>
    public void AfterRestore((int Measure, int Beat, bool Selected)? before, string? redoneTo = null)
    {
        if (before is { Selected: true }) _c.ClearSelection();
        if (redoneTo is not null && _undoneCursors.Count > 0)
        {
            var (song, cursor) = _undoneCursors[^1];
            _undoneCursors.RemoveAt(_undoneCursors.Count - 1);
            if (song == redoneTo) before = cursor;
            else _undoneCursors.Clear();   // the history moved on: the kept cursors belong to steps that are gone
        }
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        if (before is not var (m, beat, _) || m >= track.Measures.Count) return;
        var positions = CursorPositions.Allowed(CellsFor(track.Measures[m]), SlotsFor(m));
        SelectedMeasure = m; SelectedCell = positions[Math.Min(beat, positions.Count - 1)];   // no seek: the window refreshes the views after the restore
    }

    private void PlaceCursor(int measure, int cell)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        measure = Math.Clamp(measure, 0, track.Measures.Count - 1);
        var cells = CellsFor(track.Measures[measure]);
        var at = cells.Count == 0 ? 0 : CursorPositions.Snap(cells, CursorPositions.SnapToBeat(cells, Math.Clamp(cell, 0, cells.Count - 1), preferNext: false), SlotsFor(measure));
        _c.SetPosition(measure, at, SelectedString);
    }
}
