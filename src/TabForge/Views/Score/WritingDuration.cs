using TabForge.Models;

namespace TabForge.Views.Score;

// Owns: the rule for which duration a beat under the cursor stands for: the remembered writing duration (the editor's current duration, last
//     picked or written) on a rest or empty slot, the note's own length on a note.
// Does not own: the stored value (the editor's CurrentDuration*), writing the note, or rest filling.
// Tests: TestWritingDuration.
internal static class WritingDuration
{
    /// <summary>True when typing a fret onto <paramref name="cell"/> writes the remembered duration (a rest or an empty slot); false when the note keeps its own length.</summary>
    public static bool Applies(TabCell cell) => cell.Notes.Count == 0;

    /// <summary>True when the duration tools show the writing duration for a lone cursor on <paramref name="cell"/> (a rest or empty slot with no selection).</summary>
    public static bool ToolsShowWriting(TabCell cell, bool hasSelection) => !hasSelection && cell.Notes.Count == 0;
}
