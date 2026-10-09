using TabForge.Models;

namespace TabForge.Views.Score;

// Owns: the rule for which duration a beat under the cursor stands for: the remembered writing duration (the editor's current duration, last
//     picked or written) on an empty slot or a placeholder rest (a rest with no note after it in its bar: the rest fill's stand-in for an empty
//     beat), the beat's own length on a note or a written rest (a rest followed by a note keeps its length, as in GP5).
// Does not own: the stored value (the editor's CurrentDuration*), writing the note, or rest filling.
// Tests: TestWritingDuration, TestEntryDurationWorkflow, TestGp5EntryStories.
internal static class WritingDuration
{
    /// <summary>True when typing a fret onto <paramref name="cell"/> of <paramref name="bar"/> writes the remembered duration (an empty slot or a placeholder rest); false when the beat keeps its own length.</summary>
    public static bool Applies(IReadOnlyList<TabCell> bar, TabCell cell)
    {
        if (cell.Notes.Count > 0) return false;
        if (!cell.IsRest) return true;
        var at = -1;
        for (var i = 0; i < bar.Count; i++) if (ReferenceEquals(bar[i], cell)) { at = i; break; }
        for (var i = at + 1; at >= 0 && i < bar.Count; i++) if (bar[i].Notes.Count > 0) return false;
        return true;
    }

    /// <summary>True when the duration tools show the writing duration for a lone cursor on <paramref name="cell"/> (no selection, the beat stands for the writing duration).</summary>
    public static bool ToolsShowWriting(IReadOnlyList<TabCell> bar, TabCell cell, bool hasSelection) => !hasSelection && Applies(bar, cell);
}
