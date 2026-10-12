using System.Windows.Media.Imaging;
using TabForge.Diagnostics;
using TabForge.Presets;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>The yellow edit highlight is drawn when stopped and absent while the song plays.</summary>
    private static void TestScoreCursorBox()
    {
        var rethrow = TabEditorControl.RethrowRenderFailures;
        TabEditorControl.RethrowRenderFailures = true;
        try
        {
            var project = DemoSongFactory.Create();
            var stopped = CursorPixels(project, "edit");
            var playing = CursorPixels(project, "play");
            Check("cursor box: drawn when stopped", stopped > 60, $"{stopped} cursor pixels");
            Check("cursor box: not drawn while playing", playing == 0, $"{playing} cursor pixels");

            var editor = ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, "edit");
            var host = (Views.Score.IScoreRenderHost)editor;
            var moved = editor.TryHandleKey(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);
            Check("cursor box: hover follows a keyboard cursor move", moved && host.HoverMeasure == host.SelectedMeasure && host.HoverCell == host.SelectedCell,
                $"moved {moved}, cursor {host.SelectedMeasure}/{host.SelectedCell}, hover {host.HoverMeasure}/{host.HoverCell}");
        }
        finally { TabEditorControl.RethrowRenderFailures = rethrow; }
    }

    /// <summary>The cursor and hover outline share one box: its centre is the fret's centre, for a sixteenth, a quarter and an empty cell.</summary>
    private static void TestCursorBoxCentreOnFret()
    {
        var bar = Presets.TemplateFactory.Measures(1)[0];
        bar.Cells[0].DurationDenominator = 16;
        bar.Cells[0].Notes.Add(new Models.TabNote { StringIndex = 0, Fret = 12, MidiValue = 67 });
        bar.Cells[8].DurationDenominator = 4;
        bar.Cells[8].Notes.Add(new Models.TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        var warp = Views.MeasureWarp.Build(new double[] { 0, 8 }, 16);
        // Cell 1 is an empty slot that is not a beat: the box snaps to beat 0, so its fret is beat 0's.
        foreach (var width in new[] { 400.0, 600.0 })
            foreach (var (cell, beat) in new[] { (0, 0), (1, 0), (8, 8) })
            {
                var box = Views.Score.ScoreRenderer.CursorRect(bar, bar.Cells, cell, 100, warp, width, 10, 16);
                var fret = 100 + warp.CenterFraction(Views.Score.ScoreRenderer.CellStartSlots(bar, beat, bar.Cells)) * width;
                Check($"cursor box centred on the fret (cell {cell}, width {width})", Math.Abs(box.X + box.Width / 2 - fret) <= 0.5, $"{box} fret {fret}");
            }

        // The hover outline spans the note's duration: an eighth is wider than a sixteenth, and both keep the cursor box's left edge.
        var eighth = Presets.TemplateFactory.Measures(1)[0];
        eighth.Cells[0].DurationDenominator = 8;
        eighth.Cells[0].Notes.Add(new Models.TabNote { StringIndex = 0, Fret = 12, MidiValue = 67 });
        var sixteenthWarp = Views.MeasureWarp.Build(new double[] { 0, 8 }, 16);
        var eighthOutline = Views.Score.ScoreRenderer.HoverRect(eighth, eighth.Cells, 0, 100, sixteenthWarp, 400, 10, 16);
        var sixteenthOutline = Views.Score.ScoreRenderer.HoverRect(bar, bar.Cells, 0, 100, sixteenthWarp, 400, 10, 16);
        var cursorBox = Views.Score.ScoreRenderer.CursorRect(bar, bar.Cells, 0, 100, sixteenthWarp, 400, 10, 16);
        Check("hover outline: an eighth is wider than a sixteenth", eighthOutline.Width > sixteenthOutline.Width,
            $"eighth {eighthOutline.Width}, sixteenth {sixteenthOutline.Width}");
        Check("hover outline: left edge on the cursor box", Math.Abs(eighthOutline.X - cursorBox.X) <= 0.5 && Math.Abs(sixteenthOutline.X - cursorBox.X) <= 0.5,
            $"cursor {cursorBox.X}, eighth {eighthOutline.X}, sixteenth {sixteenthOutline.X}");
    }

    /// <summary>The hover outline is drawn while the song plays, and a hover change repaints the system that holds the beat: the result equals a fresh drawing.</summary>
    private static void TestHoverBoxDuringPlayback()
    {
        var rethrow = TabEditorControl.RethrowRenderFailures;
        TabEditorControl.RethrowRenderFailures = true;
        try
        {
            var project = DemoSongFactory.Create();
            var editor = ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, "play");
            var playing = Pixels(ScoreRenderIdentity.Render(editor));
            SetHover(editor, 0, 0);
            var hovered = Pixels(ScoreRenderIdentity.Render(editor));
            var fresh = ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, "play");
            SetHover(fresh, 0, 0);
            var expected = Pixels(ScoreRenderIdentity.Render(fresh));
            var changed = 0;
            for (var i = 0; i < hovered.Length && i < playing.Length; i += 4)
                if (hovered[i] != playing[i] || hovered[i + 1] != playing[i + 1] || hovered[i + 2] != playing[i + 2]) changed++;
            Check("hover box while playing: the outline is drawn", changed > 20, $"{changed} pixels changed");
            Check("hover box while playing: the retained systems take the outline too", hovered.AsSpan().SequenceEqual(expected), "differs from a fresh drawing");
        }
        finally { TabEditorControl.RethrowRenderFailures = rethrow; }
    }

    private static void SetHover(TabEditorControl editor, int measure, int cell)
    {
        var sel = typeof(TabEditorControl).GetField("_sel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
        sel.GetType().GetProperty("HoverMeasure")!.SetValue(sel, measure);
        sel.GetType().GetProperty("HoverCell")!.SetValue(sel, cell);
        ((Views.Score.IScorePageHost)editor).RepaintKeepingDrawings();
    }

    // The pixels the cursor adds: the same state rendered with and without the cursor.
    private static int CursorPixels(Models.SongProject project, string state)
    {
        var with = Pixels(ScoreRenderIdentity.Render(ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, state)));
        var hidden = ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, state);
        hidden.HideCursor = true;
        var without = Pixels(ScoreRenderIdentity.Render(hidden));
        var n = 0;
        for (var i = 0; i < with.Length && i < without.Length; i += 4)
            if (with[i] != without[i] || with[i + 1] != without[i + 1] || with[i + 2] != without[i + 2]) n++;
        return n;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var data = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(data, stride, 0);
        return data;
    }
}
