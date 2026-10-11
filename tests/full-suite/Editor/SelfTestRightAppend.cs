using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Right at the song's end appends one bar and moves into it (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static TabEditorControl RightAppendEditor(out TrackModel track, bool fill)
    {
        var editor = NewEditor(out var project, out track);
        var t = track;
        editor.AppendBarAtEnd = () =>
        {
            t.Measures.Add(new MeasureModel { Number = t.Measures.Count + 1 });
            if (fill) BarFill.FillBars(project, 0, t.Measures.Count - 1, t.Measures.Count - 1);
            editor.MoveToBarStart(t.Measures.Count - 1);
        };
        return editor;
    }

    private static void TestRightAppendsOneBar()
    {
        foreach (var fill in new[] { false, true })
        {
            var editor = RightAppendEditor(out var track, fill);
            editor.SetPosition(track.Measures.Count - 1, 0, 0);
            for (var press = 1; press <= 3; press++)
            {
                var before = track.Measures.Count;
                var guard = 0;
                // an empty bar is one step: the first press inside it appends
                editor.TryHandleKey(Key.Right, ModifierKeys.None);
                Eq($"fill={fill} press {press}: one bar added", before + 1, track.Measures.Count);
                Eq($"fill={fill} press {press}: cursor in the new bar", before, editor.SelectedMeasure);
                Eq($"fill={fill} press {press}: first cell", 0, editor.SelectedCell);
                if (guard++ > 0) break;
            }
            // Shift+Right never appends; Right mid-song never appends.
            var count = track.Measures.Count;
            editor.TryHandleKey(Key.Right, ModifierKeys.Shift);
            Eq($"fill={fill} shift+right does not append", count, track.Measures.Count);
            editor.SetPosition(0, 0, 0);
            editor.TryHandleKey(Key.Right, ModifierKeys.None);
            Eq($"fill={fill} mid-song right does not append", count, track.Measures.Count);
        }
    }

    private static void TestRightAppendsFromFullLastBar()
    {
        var editor = RightAppendEditor(out var track, true);
        var last = track.Measures.Count - 1;
        for (var i = 0; i < 4; i++)
        {
            var cell = track.Measures[last].Cells[i * 4];
            cell.DurationDenominator = 4;
            cell.Notes.Add(new TabNote { StringIndex = 0, Fret = i, MidiValue = 64 + i });
        }
        editor.SetPosition(last, 0, 0);
        var presses = 0;
        while (track.Measures.Count == last + 1 && presses++ < 20) editor.TryHandleKey(Key.Right, ModifierKeys.None);
        Check($"full last bar: Right appends within a few presses (took {presses})", track.Measures.Count == last + 2 && presses <= 6);
        Eq("cursor in the new bar", last + 1, editor.SelectedMeasure);
    }

    private static void TestRightAppendsAnyMeter()
    {
        foreach (var (num, den) in new[] { (4, 4), (3, 4), (6, 8), (7, 8), (5, 4), (12, 8), (2, 2), (9, 16) })
        {
            var editor = RightAppendEditor(out var track, true);
            foreach (var m in track.Measures) { m.TimeSigNum = num; m.TimeSigDenom = den; m.Cells.Clear(); }
            editor.SetPosition(track.Measures.Count - 1, 0, 0);
            var before = track.Measures.Count;
            editor.TryHandleKey(Key.Right, ModifierKeys.None);   // the new bar copies the meter
            var presses = 1;
            while (track.Measures.Count < before + 2 && presses++ < 12) editor.TryHandleKey(Key.Right, ModifierKeys.None);
            Check($"{num}/{den}: an empty bar takes one press (took {presses})", presses == 2 && track.Measures.Count == before + 2);
        }
    }

    private static void TestRightFromOverfullLastBar()
    {
        foreach (var (count, den) in new[] { (18, 16), (20, 16), (40, 32), (33, 32), (130, 16) })
        {
            var editor = RightAppendEditor(out var track, true);
            var last = track.Measures.Count - 1;
            var cells = track.Measures[last].Cells;
            cells.Clear();
            for (var i = 0; i < count; i++)
                cells.Add(new TabCell { DurationDenominator = den, Notes = { new TabNote { StringIndex = 0, Fret = 20, MidiValue = 84 } } });
            for (var start = 0; start < 2; start++)
            {
                var before = track.Measures.Count;
                editor.SetPosition(last, start == 0 ? count - 1 : 0, 0);
                var presses = 0;
                while (track.Measures.Count == before && presses++ < count + 4) editor.TryHandleKey(Key.Right, ModifierKeys.None);
                Check($"overfull {count}x1/{den} from {(start == 0 ? "last note" : "first")}: Right appends (took {presses})", track.Measures.Count == before + 1);
                Eq("cursor in the new bar", before, editor.SelectedMeasure);
                if (track.Measures.Count > before) track.Measures.RemoveAt(before);
            }
            editor.SetPosition(last, count - 1, 0);
            var n = track.Measures.Count;
            editor.TryHandleKey(Key.Right, ModifierKeys.Control);
            Eq($"overfull {count}x1/{den}: Ctrl+Right appends", n + 1, track.Measures.Count);
        }
    }

    private static void TestRightFromOverfullMidBar()
    {
        var editor = RightAppendEditor(out var track, true);
        var cells = track.Measures[1].Cells;
        cells.Clear();
        for (var i = 0; i < 20; i++) cells.Add(new TabCell { DurationDenominator = 16, Notes = { new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 } } });
        editor.SetPosition(1, 0, 0);
        var presses = 0;
        while (editor.SelectedMeasure == 1 && presses++ < 30) editor.TryHandleKey(Key.Right, ModifierKeys.None);
        Eq("Right leaves an overfull middle bar into the next", 2, editor.SelectedMeasure);
        Eq("on its first cell", 0, editor.SelectedCell);
        Eq("no bar appended", 4, track.Measures.Count);
        editor.SetPosition(1, 19, 0);
        editor.TryHandleKey(Key.Right, ModifierKeys.Control);
        Eq("Ctrl+Right moves a bar", 2, editor.SelectedMeasure);
    }
}
