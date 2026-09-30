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
        var cell = CurrentCell();
        var step = cell is not null && (cell.Notes.Count > 0 || cell.IsRest) ? MusicTime.CellSlotsRounded(cell) : CurrentDurationSlots();
        var slots = SlotsFor(SelectedMeasure);
        var next = SelectedCell + Math.Max(1, step);
        // A tuplet's rounded slot can fall before the stride lands; hop onto any real beat in between so
        // keyboard navigation never skips a written note.
        var measure = SelectedMeasure < track.Measures.Count ? track.Measures[SelectedMeasure] : null;
        if (measure is not null)
        {
            var nextBeat = MusicTime.BeatSlots(CellsFor(measure)).FirstOrDefault(s => s > SelectedCell, -1);
            if (nextBeat > SelectedCell && nextBeat < next) next = nextBeat;
        }
        if (next >= slots)
        {
            // Bar complete: GP moves to the next bar.
            if (SelectedMeasure + 1 < track.Measures.Count)
            {
                SelectedMeasure++;
                SelectedCell = 0;
            }
            else SelectedCell = Math.Max(0, slots - 1);
        }
        else SelectedCell = next;
        SelectionChangedNow();
    }

    private void MoveBackBeat(TrackModel track)
    {
        if (SelectedMeasure >= track.Measures.Count) return;
        var measure = track.Measures[SelectedMeasure];
        var cells = CellsFor(measure);
        var onsets = new SortedSet<int>();
        var i = 0;
        while (i < cells.Count)
        {
            onsets.Add(i);
            var d = MusicTime.ConsumeSlots(cells[i]);
            i += Math.Max(1, (int)Math.Ceiling(d - 0.001));
        }
        foreach (var beat in MusicTime.BeatSlots(cells)) onsets.Add(beat);
        var prev = onsets.Where(o => o < SelectedCell).DefaultIfEmpty(-1).Max();
        if (prev >= 0) SelectedCell = prev;
        else if (SelectedMeasure > 0) { SelectedMeasure--; SelectedCell = 0; }
        else SelectedCell = 0;
        SelectionChangedNow();
    }

    public void MoveBar(int direction)
    {
        var track = Track;
        if (track is null) return;
        SelectedMeasure = Math.Clamp(SelectedMeasure + direction, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = Math.Min(SelectedCell, SlotsFor(SelectedMeasure) - 1);
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
        SelectedCell = Math.Min(SelectedCell, SlotsFor(SelectedMeasure) - 1);
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

    public void MoveToBarStart() => SetPosition(SelectedMeasure, 0, SelectedString);

    public void MoveToBarEnd()
    {
        var track = Track;
        if (track is null) return;
        SelectedCell = Math.Max(0, SlotsFor(SelectedMeasure) - 1);
        SelectionChangedNow();
    }

    public void MoveToFirstBar() => SetPosition(0, 0, SelectedString);

    public void MoveToLastBar()
    {
        var track = Track;
        if (track is null) return;
        SetPosition(Math.Max(0, track.Measures.Count - 1), 0, SelectedString);
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
