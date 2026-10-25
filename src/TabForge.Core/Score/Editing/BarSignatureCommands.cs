using TabForge.Documents;
using TabForge.Services;

namespace TabForge.ScoreEditing;

// Owns: the time and key signature commands of the Bar menu and the palette as edits of one document: which bars the answer reaches (the
//   selected bars, else from the cursor bar), and the status line naming them.
// Does not own: the signature rules (BarSignatures), the question dialogs (the hosts) or the redraw after the edit.
// Tests: no dedicated test class.
/// <summary>Applies a signature answer to a document and describes where it reached.</summary>
public static class BarSignatureCommands
{
    /// <summary>The selected bars when the selection spans more than one bar (a signature change then applies to exactly those bars), else null.</summary>
    public static (int First, int Last)? SelectedRange(int barCount, (int Start, int End)? selectedBars)
    {
        if (selectedBars is not { } bars || barCount == 0) return null;
        var (first, last) = (Math.Clamp(Math.Min(bars.Start, bars.End), 0, barCount - 1), Math.Clamp(Math.Max(bars.Start, bars.End), 0, barCount - 1));
        return last > first ? (first, last) : null;
    }

    /// <summary>"for bar 5", "from bar 5 to bar 9" or "from bar 5 to the end" (the bars a signature change reached).</summary>
    public static string Span(int first, int last, int barCount) =>
        last <= first ? $"for bar {first + 1}" : last >= barCount - 1 ? $"from bar {first + 1} to the end" : $"from bar {first + 1} to bar {last + 1}";

    /// <summary>Sets the time signature on the selected range, else from <paramref name="cursorBar"/> (only that bar when <paramref name="onlyThisBar"/>); the status line.</summary>
    public static string SetTime(IEditableDocument document, int cursorBar, (int First, int Last)? range, int numerator, int denominator, bool onlyThisBar)
    {
        var first = range?.First ?? cursorBar;
        var last = first;
        DocumentEdits.Run(document, project =>
        {
            last = range is { } span
                ? BarSignatures.SetTimeRange(project, span.First, span.Last, numerator, denominator)
                : BarSignatures.SetTime(project, first, numerator, denominator, !onlyThisBar);
            return true;
        });
        return $"Time signature {numerator}/{denominator} {Span(first, last, BarRangeEditor.MaxMeasures(document.Project))}";
    }

    /// <summary>Sets the key signature the same way; the status line.</summary>
    public static string SetKey(IEditableDocument document, int cursorBar, (int First, int Last)? range, int signature, bool minor, bool onlyThisBar)
    {
        var first = range?.First ?? cursorBar;
        var last = first;
        DocumentEdits.Run(document, project =>
        {
            last = range is { } span
                ? BarSignatures.SetKeyRange(project, span.First, span.Last, signature, minor)
                : BarSignatures.SetKey(project, first, signature, minor, !onlyThisBar);
            return true;
        });
        return $"Key signature changed {Span(first, last, BarRangeEditor.MaxMeasures(document.Project))}";
    }
}
