using TabForge.Views.Score;
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

// Owns: caret movement: beat, bar, line and string moves, bar start and end, first and last bar, the move to an entered note, and
//   the song-end test (AtSongEnd).
// Does not own: the key map (TabEditorControl.Keyboard.cs) and the snapping rules (CursorPositions.cs).
// Tests: TestEditorNavigation.

public sealed partial class TabEditorControl
{
    // ---------- navigation (the reference behaviour) ----------

    /// <summary>True when Right at the cursor in the last bar adds a bar (see CursorPositions.AtEnd).</summary>
    public bool AtSongEnd()
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0 || SelectedMeasure != track.Measures.Count - 1) return false;
        return CursorPositions.AtEnd(CellsFor(track.Measures[SelectedMeasure]), SelectedCell, SlotsFor(SelectedMeasure));
    }

    /// <summary>Puts the caret on the first beat of a bar.</summary>
    public void MoveToBarStart(int measure)
    {
        ClearSelection(); SelectedMeasure = measure; SelectedCell = Snap(measure, 0); SelectionChangedNow(true);
    }

    /// <summary>Plain navigation ends any selection first, as GP5: Delete, paste and duration keys then act on the cursor; Shift+arrows keep it.</summary>
    public void MoveBeat(int direction, bool keepSelection = false)
    {
        if (!keepSelection) ClearSelection();
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        if (direction > 0) MoveForwardBeat(track);
        else MoveBackBeat(track);
    }

    /// <summary>Moves the caret one beat on; <paramref name="seekPlayback"/> is false after an edit so playback never jumps.</summary>
    private void MoveForwardBeat(TrackModel track, bool seekPlayback = true)
    {
        if (SelectedMeasure >= track.Measures.Count) return;
        var bar = CellsFor(track.Measures[SelectedMeasure]);
        var left = SelectedCell >= 0 && SelectedCell < bar.Count ? bar[SelectedCell] : null;
        var next = CursorPositions.RightTarget(bar, SelectedCell, SlotsFor(SelectedMeasure), pad: true);
        if (next >= 0) SelectedCell = next;
        else if (SelectedMeasure + 1 < track.Measures.Count) { SelectedMeasure++; SelectedCell = Snap(SelectedMeasure, 0); }
        else return;
        Effects.TakeLengthOfBeatLeft(left, bar);   // a new beat copies the one before it (GP5)
        SelectionChangedNow(seekPlayback);
    }

    private void MoveBackBeat(TrackModel track)
    {
        if (SelectedMeasure >= track.Measures.Count) return;
        var prev = CursorPositions.Previous(CellsFor(track.Measures[SelectedMeasure]), SelectedCell, SlotsFor(SelectedMeasure));
        if (prev >= 0) SelectedCell = prev;
        else if (SelectedMeasure > 0)
        {
            SelectedMeasure--;
            SelectedCell = CursorPositions.BarEndCursor(CellsFor(track.Measures[SelectedMeasure]), SlotsFor(SelectedMeasure));
        }
        else SelectedCell = Snap(0, 0);
        SelectionChangedNow();
    }

    public void MoveBar(int direction)
    {
        var track = Track;
        if (track is null) return;
        ClearSelection();
        SelectedMeasure = Math.Clamp(SelectedMeasure + direction, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = Snap(SelectedMeasure, SelectedCell);
        SelectionChangedNow();
    }

    public void MoveLine(int direction)
    {
        var track = Track;
        if (track is null) return;
        ClearSelection();
        var layout = Layout.GetLayout(track);
        var system = Math.Clamp(layout.SystemForMeasure(SelectedMeasure) + direction, 0, layout.SystemCount - 1);
        var column = layout.Measure(SelectedMeasure).ColumnIndex;
        var row = layout.Systems[system];
        SelectedMeasure = row.Measures[Math.Min(column, row.Measures.Count - 1)].MeasureIndex;
        SelectedCell = Snap(SelectedMeasure, SelectedCell);
        SelectionChangedNow();
    }

    /// <summary>
    /// The cursor moves between strings with the up/down arrows; past the top or bottom string it wraps round
    /// within the same beat, as GP5.
    /// </summary>
    public void MoveString(int direction)
    {
        var track = Track;
        if (track is null) return;
        ClearSelection();
        var count = Math.Max(1, track.StringTunings.Count);
        SelectedString = ((SelectedString + direction) % count + count) % count;
        SelectionChangedNow();
    }

    /// <summary>The allowed cursor cell (a real beat or the append slot) of a bar for the grid cell <paramref name="cell"/>.</summary>
    private int Snap(int measure, int cell) => Track is { } t && measure >= 0 && measure < t.Measures.Count ? CursorPositions.Snap(CellsFor(t.Measures[measure]), cell, SlotsFor(measure)) : 0;

    public void MoveToBarStart() { if (Track is { Measures.Count: > 0 } track) { ClearSelection(); SetPosition(SelectedMeasure, Snap(SelectedMeasure, 0), SelectedString); } }

    public void MoveToBarEnd()
    {
        var track = Track;
        if (track is null) return;
        ClearSelection();
        SelectedCell = CursorPositions.EndTarget(CellsFor(track.Measures[Math.Clamp(SelectedMeasure, 0, track.Measures.Count - 1)]), SelectedCell, SlotsFor(Math.Clamp(SelectedMeasure, 0, track.Measures.Count - 1)));
        SelectionChangedNow();
    }

    public void MoveToFirstBar() { if (Track is { Measures.Count: > 0 } track) { ClearSelection(); SetPosition(0, Snap(0, 0), SelectedString); } }

    public void MoveToLastBar()
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        ClearSelection();   // the last written beat, as GP5
        SetPosition(track.Measures.Count - 1, CursorPositions.LastWritten(CellsFor(track.Measures[^1]), SlotsFor(track.Measures.Count - 1)), SelectedString);
    }

    /// <summary>Alt+Left / Alt+Right: step through entered notes and hear them.</summary>
    public void MoveToEnteredNote(int direction)
    {
        var track = Track;
        if (track is null) return;
        ClearSelection();
        var m = SelectedMeasure; var c = SelectedCell;
        for (var guard = 0; guard < 4096; guard++)
        {
            if (direction > 0)
            {
                c++;
                if (c >= SlotsFor(m)) { m++; c = 0; }
                if (m >= track.Measures.Count) return;
            }
            else
            {
                c--;
                if (c < 0) { m--; if (m < 0) return; c = SlotsFor(m) - 1; }
            }
            var cells = m < track.Measures.Count ? CellsFor(track.Measures[m]) : new List<TabCell>();
            if (m < track.Measures.Count && c < cells.Count && cells[c].Notes.Count > 0)
            {
                SelectedMeasure = m; SelectedCell = c;
                SelectionChangedNow();
                var note = cells[c].Notes.OrderByDescending(n => n.StringIndex).FirstOrDefault(n => n.StringIndex == SelectedString)
                           ?? cells[c].Notes.First();
                SelectedString = note.StringIndex;
                SelectionChangedNow();
                return;
            }
        }
    }
}
