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

// TabEditorControl: cursor navigation (the reference behaviour).
public sealed partial class TabEditorControl
{
    // ---------- navigation (the reference behaviour) ----------

    /// <summary>Move to the next/previous beat. Advances by the cell's own duration.</summary>
    public void MoveBeat(int direction)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        if (direction > 0) MoveForwardBeat(track);
        else MoveBackBeat(track);
    }

    private void MoveForwardBeat(TrackModel track)
    {
        if (SelectedMeasure >= track.Measures.Count) return;
        var next = CursorPositions.Next(CellsFor(track.Measures[SelectedMeasure]), SelectedCell);
        if (next >= 0) SelectedCell = next;
        else if (SelectedMeasure + 1 < track.Measures.Count) { SelectedMeasure++; SelectedCell = Snap(SelectedMeasure, 0); }
        else return;
        SelectionChangedNow();
    }

    private void MoveBackBeat(TrackModel track)
    {
        if (SelectedMeasure >= track.Measures.Count) return;
        var prev = CursorPositions.Previous(CellsFor(track.Measures[SelectedMeasure]), SelectedCell);
        if (prev >= 0) SelectedCell = prev;
        else if (SelectedMeasure > 0)
        {
            SelectedMeasure--;
            SelectedCell = CursorPositions.Allowed(CellsFor(track.Measures[SelectedMeasure])).Last();
        }
        else SelectedCell = Snap(0, 0);
        SelectionChangedNow();
    }

    public void MoveBar(int direction)
    {
        var track = Track;
        if (track is null) return;
        SelectedMeasure = Math.Clamp(SelectedMeasure + direction, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = Snap(SelectedMeasure, SelectedCell);
        SelectionChangedNow();
    }

    public void MoveLine(int direction)
    {
        var track = Track;
        if (track is null) return;
        var layout = GetScoreLayout(track);
        var system = Math.Clamp(layout.SystemForMeasure(SelectedMeasure) + direction, 0, layout.SystemCount - 1);
        var column = layout.Measure(SelectedMeasure).ColumnIndex;
        var row = layout.Systems[system];
        SelectedMeasure = row.Measures[Math.Min(column, row.Measures.Count - 1)].MeasureIndex;
        SelectedCell = Snap(SelectedMeasure, SelectedCell);
        SelectionChangedNow();
    }

    /// <summary>
    /// The cursor moves between strings with the up/down arrows. Moving past the top
    /// string steps back a beat, past the bottom string steps forward a beat (fast chord entry).
    /// </summary>
    public void MoveString(int direction)
    {
        var track = Track;
        if (track is null) return;
        var next = SelectedString + direction;
        if (next < 0)
        {
            MoveBeat(-1);
            SelectedString = Math.Max(0, track.StringTunings.Count - 1);
        }
        else if (next >= track.StringTunings.Count)
        {
            MoveBeat(1);
            SelectedString = 0;
        }
        else
        {
            SelectedString = next;
        }
        SelectionChangedNow();
    }

    /// <summary>The allowed cursor cell (a real beat or the append slot) of a bar for the grid cell <paramref name="cell"/>.</summary>
    private int Snap(int measure, int cell) => Track is { } t && measure >= 0 && measure < t.Measures.Count ? CursorPositions.Snap(CellsFor(t.Measures[measure]), cell) : 0;

    public void MoveToBarStart() { if (Track is { Measures.Count: > 0 } track) SetPosition(SelectedMeasure, Snap(SelectedMeasure, 0), SelectedString); }

    public void MoveToBarEnd()
    {
        var track = Track;
        if (track is null) return;
        SelectedCell = CursorPositions.Allowed(CellsFor(track.Measures[Math.Clamp(SelectedMeasure, 0, track.Measures.Count - 1)])).Last();
        SelectionChangedNow();
    }

    public void MoveToFirstBar() { if (Track is { Measures.Count: > 0 } track) SetPosition(0, Snap(0, 0), SelectedString); }

    public void MoveToLastBar()
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return;
        SetPosition(track.Measures.Count - 1, Snap(track.Measures.Count - 1, 0), SelectedString);
    }

    /// <summary>Alt+Left / Alt+Right: step through entered notes and hear them.</summary>
    public void MoveToEnteredNote(int direction)
    {
        var track = Track;
        if (track is null) return;
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
