using TabForge.Diagnostics;
using TabForge.Presets;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Pausing or stopping moves the edit cursor to the playhead's beat and keeps its string; with no playhead the cursor stays.</summary>
    private static void TestPlayheadCursorPlacement()
    {
        var editor = ScoreRenderIdentity.CreateEditor(DemoSongFactory.Create(), 0, 1.0, true, "edit");
        editor.SetPosition(3, 2, 2, seekPlayback: false);
        var beat = editor.SelectedCell;
        editor.SetPosition(0, 0, 2, seekPlayback: false);
        editor.Playback.SetPlayhead(3, beat);
        PlayheadCursorPlacement.MoveCursorToPlayhead(editor);
        Check("playhead cursor: lands on the playhead bar", editor.SelectedMeasure == 3, $"bar {editor.SelectedMeasure}");
        Check("playhead cursor: lands on the playhead beat", editor.SelectedCell == beat, $"cell {editor.SelectedCell}, want {beat}");
        Check("playhead cursor: keeps its string", editor.SelectedString == 2, $"string {editor.SelectedString}");
        editor.Playback.Clear();
        PlayheadCursorPlacement.MoveCursorToPlayhead(editor);
        Check("playhead cursor: no playhead leaves the cursor", editor.SelectedMeasure == 3 && editor.SelectedCell == beat, $"bar {editor.SelectedMeasure}");
    }
}
