using System;
using System.Linq;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Views.Score;

// Owns: what a right-click on the score page lands on (the beat and string under the pointer, whether a note sits there, whether the beat is
//   inside the selection) and the cursor move a note or beat menu makes before it opens (SelectForEdit).
// Does not own: the hit rule (ScoreHitRule through ScoreGeometryProbe.HitTest), the menu rows (Core ScoreMenus, ScoreNoteMenus), the pointer
//   events (Avalonia ScorePreviewInput) or the selection state (EditorSelectionState).
// Tests: TabForge.Avalonia.Tests.ScoreContextMenuTests.

/// <summary>A right-click's target on the page: the beat and string, and whether the pointer is on a beat, on a note, or inside the selection.</summary>
internal readonly record struct ScoreContextHit(int Measure, int Cell, int String, bool OverBeat, bool OnNote, bool InsideSelection);

internal sealed partial class ScorePreviewEditor
{
    /// <summary>The right-click target at page point (<paramref name="x"/>, <paramref name="y"/>).</summary>
    internal ScoreContextHit ContextHitAt(double x, double y)
    {
        var (measure, cell, stringIndex) = _probe.HitTest(x, y);
        var overBeat = y >= _probe.BeatAreaTop && measure >= 0 && measure < TrackNow.Measures.Count;
        if (!overBeat) return new ScoreContextHit(measure, cell, stringIndex, false, false, false);
        var cells = CellsFor(TrackNow.Measures[measure]);
        var onNote = cell >= 0 && cell < cells.Count && cells[cell].Notes.Any(note => note.StringIndex == stringIndex);
        var insideSelection = HasSelection && SelectionRange() is var (m1, c1, m2, c2) && ScoreEditorGeometry.IsInRange(measure, cell, m1, c1, m2, c2);
        return new ScoreContextHit(measure, cell, stringIndex, true, onNote, insideSelection);
    }

    /// <summary>Puts the cursor on a beat and string and drops any range, so a menu opened on that beat acts on it.</summary>
    internal void SelectForEdit(int measure, int cell, int stringIndex)
    {
        Sel.Clear();
        _probe.Cursor = (measure, Math.Clamp(cell, 0, Math.Max(0, LastCellOf(measure))), stringIndex);
    }
}
