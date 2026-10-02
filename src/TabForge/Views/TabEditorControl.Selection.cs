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

namespace TabForge.Views;

// TabEditorControl: selection and clipboard.
public sealed partial class TabEditorControl
{
    // ---------- selection / clipboard ----------

    public void BeginSelection()
    {
        _anchorMeasure = SelectedMeasure;
        _anchorCell = SelectedCell;
        _selectionEndMeasure = SelectedMeasure;
        _selectionEndCell = SelectedCell;
        _selecting = true;
        InvalidateVisual();
    }

    public void ExtendSelection(int direction)
    {
        if (!_selecting) BeginSelection();
        var m = _selectionEndMeasure; var c = _selectionEndCell;
        var track = Track; if (track is null) return;
        c += direction;
        if (c >= SlotsFor(m)) { m++; c = 0; }
        if (c < 0) { m--; c = m >= 0 ? SlotsFor(m) - 1 : 0; }
        if (m < 0 || m >= track.Measures.Count) return;
        _selectionEndMeasure = m; _selectionEndCell = c;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection(bool notify = true)
    {
        var hadSelection = HasSelection;
        _selecting = false;
        _anchorMeasure = _selectionEndMeasure = -1;
        InvalidateVisual();
        if (notify && hadSelection) SelectionChangedNow(seekPlayback: false);
    }

    /// <summary>Inclusive score-grid cell bounds of the current selection, ordered by score position.</summary>
    public (int StartMeasure, int StartCell, int EndMeasure, int EndCell) SelectionCellRange
    {
        get
        {
            if (!HasSelection) return (SelectedMeasure, SelectedCell, SelectedMeasure, SelectedCell);
            var (m1, c1, m2, c2) = SelectionRange();
            if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
            return (m1, c1, m2, c2);
        }
    }

    /// <summary>Selects a whole-bar range, for range selections initiated in the arrangement timeline.</summary>
    public void SelectMeasureRange(int startMeasure, int endMeasure) =>
        SelectRange(Math.Min(startMeasure, endMeasure), 0, Math.Max(startMeasure, endMeasure), -1);

    /// <summary>
    /// Selects from (startMeasure, startCell) to (endMeasure, endCell) inclusive; endCell -1 = the end of the bar.
    /// Used to mirror the shared selection model. Never seeks playback.
    /// </summary>
    public void SelectRange(int startMeasure, int startCell, int endMeasure, int endCell)
    {
        if (ClampRange(startMeasure, startCell, endMeasure, endCell) is not var (m1, c1, m2, c2)) return;
        SelectedMeasure = m1;
        SelectedCell = c1;
        _anchorMeasure = m1;
        _anchorCell = c1;
        _selectionEndMeasure = m2;
        _selectionEndCell = c2;
        _selecting = true;
        SelectionChangedNow(seekPlayback: false);
    }

    /// <summary>True when the score already shows exactly this range (same clamping as <see cref="SelectRange"/>).</summary>
    public bool SelectionMatches(int startMeasure, int startCell, int endMeasure, int endCell)
    {
        if (!HasSelection || ClampRange(startMeasure, startCell, endMeasure, endCell) is not var (m1, c1, m2, c2)) return false;
        return SelectionCellRange == (m1, c1, m2, c2);
    }

    private (int, int, int, int)? ClampRange(int startMeasure, int startCell, int endMeasure, int endCell)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return null;
        var last = track.Measures.Count - 1;
        var m1 = Math.Clamp(startMeasure, 0, last);
        var m2 = Math.Clamp(endMeasure, m1, last);
        var c1 = Math.Clamp(startCell, 0, Math.Max(0, SlotsFor(m1) - 1));
        var lastCell = Math.Max(0, SlotsFor(m2) - 1);
        var c2 = endCell < 0 ? lastCell : Math.Clamp(endCell, 0, lastCell);
        if (m1 == m2 && c2 < c1) c2 = c1;
        return (m1, c1, m2, c2);
    }

    /// <summary>Selects every beat of the current track (Ctrl+A, TuxGuitar "select all").</summary>
    public void SelectAll()
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        _anchorMeasure = 0;
        _anchorCell = 0;
        _selectionEndMeasure = track.Measures.Count - 1;
        _selectionEndCell = Math.Max(0, SlotsFor(_selectionEndMeasure) - 1);
        _selecting = true;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private (int m1, int c1, int m2, int c2) SelectionRange()
    {
        var a = (_anchorMeasure, _anchorCell);
        var b = (_selectionEndMeasure, _selectionEndCell);
        return (a.Item1, a.Item2, b.Item1, b.Item2);
    }

    /// <summary>
    /// The selection as a score clip (design 3.1): whole bars give a Bars clip (both voices, bar settings), anything else a Beats
    /// clip of the active voice; with no selection, the beat at the cursor. Null with a user-facing <paramref name="error"/>.
    /// </summary>
    public ScoreClip? CaptureClip(out string? error)
    {
        error = null;
        if (_project is null || Track is null) return null;
        var (m1, c1, m2, c2) = SelectionCellRange;
        try
        {
            var songId = TimelineClips.SongId(_project);
            return HasSelection
                ? ClipboardService.CaptureSelection(_project, SelectedTrackIndex, ActiveVoiceIndex, m1, c1, m2, c2, songId)
                : ClipboardService.CaptureBeats(_project, SelectedTrackIndex, ActiveVoiceIndex, m1, c1, m1, c1, songId);
        }
        catch (System.IO.InvalidDataException ex) { error = ex.Message; return null; }
    }

    /// <summary>Where a paste lands: the active track and voice at the selection start (the cursor without a selection).</summary>
    public PasteTarget PasteTarget
    {
        get
        {
            var (m1, c1, _, _) = SelectionCellRange;
            return new PasteTarget(SelectedTrackIndex, ActiveVoiceIndex, m1, c1);
        }
    }

    /// <summary>
    /// Cut: clears what <paramref name="clip"/> (just captured from the selection) took, as one undo step: whole bars are emptied
    /// (not deleted), beats become rests in the active voice. Returns true when anything changed.
    /// </summary>
    public bool CutSelection(ScoreClip clip)
    {
        if (_project is null || Track is null) return false;
        var (m1, c1, m2, c2) = SelectionCellRange;
        EditStarting?.Invoke(this, EventArgs.Empty);   // one undo step
        var changed = EditCommands.CutClear(_project, clip.Kind, SelectedTrackIndex, ActiveVoiceIndex, m1, c1, m2, HasSelection ? c2 : c1);
        if (changed) EditedNow();
        return changed;
    }

    /// <summary>Tells the host the project changed outside the editor's own commands (paste), and refreshes the score.</summary>
    /// <param name="markTimeline">False when the edit that changed the song already invalidated the playback timeline (once): only the editor's refresh is wanted.</param>
    public void NotifyEdited(bool markTimeline = true) => EditedNow(markTimeline);
}
