using TabForge.Documents;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

// Owns: the bar and note-value menu commands that need no clipboard or dialog (Bar.Insert, Bar.Delete, Bar.Duplicate, Note.Whole..Note.SixtyFourth)
//   as edits of one document: which bars they touch from the cursor and range, and where the cursor lands afterwards.
// Does not own: the bar edits themselves (BarStructureEdits), the note value rules (ScoreEditCommands.SetDuration), the repeat-count question, undo (DocumentEdits).
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests.
/// <summary>The bar commands of the Bar and Note menus as one-undo-step edits of a document, from the cursor bar and the selected bars.</summary>
public static class ScoreBarCommands
{
    /// <summary>The note value (1, 2, 4 .. 64) a Note.Whole..Note.SixtyFourth command sets, or null for any other id.</summary>
    public static int? Denominator(string id) => id switch
    {
        "Note.Whole" => 1, "Note.Half" => 2, "Note.Quarter" => 4, "Note.Eighth" => 8,
        "Note.Sixteenth" => 16, "Note.ThirtySecond" => 32, "Note.SixtyFourth" => 64,
        _ => null,
    };

    /// <summary>The status line when Bar.Delete cannot run (the last bar stays), or null when it can.</summary>
    public static string? DeleteRefusal(IEditableDocument document) =>
        BarRangeEditor.MaxMeasures(document.Project) <= 1 ? "Cannot delete the last bar" : null;

    /// <summary>The question Bar.Delete asks before it deletes the bar at <paramref name="measureIndex"/> (0-based) from every track.</summary>
    public static string ConfirmDeleteText(int measureIndex) => $"Delete bar {measureIndex + 1} from every track?";

    /// <summary>
    /// Runs Bar.Insert (an empty bar before the cursor bar, unless bars are selected), Bar.Delete (the cursor bar; the last bar stays) or Bar.Duplicate
    /// (the selected bars, else the cursor bar, copied after themselves). <paramref name="cursorBar"/> becomes the bar the cursor belongs on. False when
    /// the id is another command or the edit changed nothing.
    /// </summary>
    public static bool TryRun(string id, IEditableDocument document, ref int cursorBar, (int First, int Last)? selectedBars, bool fillRests)
    {
        var bars = BarRangeEditor.MaxMeasures(document.Project);
        if (bars == 0) return false;
        var bar = Math.Clamp(cursorBar, 0, bars - 1);
        switch (id)
        {
            case "Bar.Insert":
                if (selectedBars is not null || !BarStructureEdits.InsertBar(document, bar, fillRests).Changed) return false;
                cursorBar = bar;
                return true;
            case "Bar.Delete":
                if (!BarStructureEdits.DeleteBar(document, bar, -1, allTracks: true).Changed) return false;
                cursorBar = Math.Max(0, bar - 1);
                return true;
            case "Bar.Duplicate":
                var (first, last) = Range(selectedBars, bar, bars);
                if (BarStructureEdits.DuplicateBars(document, first, last).Value is null) return false;
                cursorBar = Math.Min(last + 1, BarRangeEditor.MaxMeasures(document.Project) - 1);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Repeats the selected bars (else the cursor bar) <paramref name="times"/> times after themselves (Edit.RepeatSelection). False when nothing changed.</summary>
    public static bool Repeat(IEditableDocument document, int cursorBar, (int First, int Last)? selectedBars, int times)
    {
        var bars = BarRangeEditor.MaxMeasures(document.Project);
        if (bars == 0) return false;
        var (first, last) = Range(selectedBars, Math.Clamp(cursorBar, 0, bars - 1), bars);
        return BarStructureEdits.RepeatRange(document, first, last, times).Changed;
    }

    private static (int First, int Last) Range((int First, int Last)? selected, int cursor, int bars) =>
        selected is { } s ? (Math.Clamp(Math.Min(s.First, s.Last), 0, bars - 1), Math.Clamp(Math.Max(s.First, s.Last), 0, bars - 1)) : (cursor, cursor);
}
