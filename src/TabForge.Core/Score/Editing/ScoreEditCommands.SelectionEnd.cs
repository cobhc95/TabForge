using System.Linq;
using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

// ScoreEditCommands: where the cursor goes when a command ends the selection (GP5).
// Owns: ending the selection after Backspace / Delete, and the cursor Undo / Redo leave (Undo: the selection's last beat, else the cursor's
// beat; Redo: where the cursor was when that step was undone). Does not own: the undo history (DocumentEdits), the paste (PasteCommands.RunPaste).
// Tests: ScorePreviewEditorTests, UndoRulesTests.

public sealed partial class ScoreEditCommands
{
    // The cursor each Undo found, newest last, with the song's fingerprint before that Undo: Redo back to that song puts the cursor there.

    /// <summary>Backspace / Delete used the selection up: it ends, the cursor on the beat where it started.</summary>
    private void EndSelection(int measure, int cell)
    {
        _restSelection = null;
        _c.ClearSelection();
        PlaceCursor(measure, cell);
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
