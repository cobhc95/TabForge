using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

// TabEditorControl: mouse input and hit testing.
public sealed partial class TabEditorControl
{
    // ---------- mouse (hit testing, clicks and drags live in EditorInputController) ----------

    /// <summary>
    /// Resolve clicks on a sustained note to its actual beat cell instead of the empty sixteenth-grid
    /// slot underneath it. Notes are drawn at their rhythmic onset, while the old hit test used only
    /// the integer slot index; imported fractional onsets and any note longer than one slot could
    /// therefore leave a selectable cursor position in the middle of the visible note.
    /// The cursor may only sit on a real beat start or the single append slot (see <see cref="CursorPositions"/>);
    /// any other click snaps to the nearest allowed position.
    /// </summary>
    internal static int ResolveBeatHitCell(MeasureModel measure, double slotPosition, int fallbackCell,
        IReadOnlyList<TabCell>? voiceCells = null, int barSlots = 0)
    {
        if (!double.IsFinite(slotPosition)) return fallbackCell;
        return CursorPositions.Resolve(voiceCells ?? measure.Cells, slotPosition, barSlots);
    }

    /// <summary>True when the beat (measure, cell) lies inside the current score selection (false without a selection).</summary>
    public bool IsInSelection(int measure, int cell)
    {
        if (!HasSelection) return false;
        var (m1, c1, m2, c2) = SelectionCellRange;
        if (measure < m1 || measure > m2) return false;
        if (measure == m1 && cell < c1) return false;
        if (measure == m2 && cell > c2) return false;
        return true;
    }

    /// <summary>Selects a beat/string for editing without seeking playback (used by the note menu).</summary>
    public void SelectForEdit(int measure, int cell, int stringIndex)
    {
        ClearSelection(notify: false);
        SelectedMeasure = measure; SelectedCell = cell; SelectedString = stringIndex;
        SelectionChangedNow(seekPlayback: false);
    }

    /// <summary>Shift+click: extends the selection to the clicked beat (see <see cref="EditorInputController.ShiftClickExtend"/>).</summary>
    internal bool ShiftClickExtend(int measure, int cell, int stringIndex) => _input.ShiftClickExtend(measure, cell, stringIndex);

    /// <summary>Shift+F10 / the Menu key: the context menu for the caret.</summary>
    public bool RequestContextMenuAtCaret() => _input.RequestContextMenuAtCaret();

    /// <summary>True while the user drags a score range (the window defers side-panel refreshes).</summary>
    public bool IsDragSelecting => _input.IsDragSelecting;

    public event EventHandler<ContextMenuEventArgs>? ContextMenuRequested;
}
