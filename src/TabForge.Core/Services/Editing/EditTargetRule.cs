using TabForge.Models;

namespace TabForge.Services;

// Owns: which focus target the edit keys (Copy, Cut, Delete, Paste) act on when the timeline highlight and a selection both exist: the "Last clicked / Highlight always wins /
//   Selection always wins" setting, its words, and the pure rule that turns the last-clicked focus into the effective one.
// Does not own: the focus itself (SelectionModel.Focus, set by the last click), the highlight (BarHighlight) or the routing table (EditRouting).
// Tests: no dedicated test class.
public enum EditTargetMode { LastClicked, HighlightWins, SelectionWins }

/// <summary>The edit target setting and the rule that applies it.</summary>
public static class EditTargetRule
{
    public static IReadOnlyList<string> Choices { get; } = new[] { "Last clicked", "Highlight always wins", "Selection always wins" };

    public static string ToChoice(EditTargetMode mode) => Choices[(int)mode];

    /// <summary>The mode a stored setting text names; anything unknown is the default, Last clicked.</summary>
    public static EditTargetMode FromChoice(string? choice)
    {
        var index = choice is null ? -1 : Choices.ToList().IndexOf(choice);
        return index < 0 ? EditTargetMode.LastClicked : (EditTargetMode)index;
    }

    /// <summary>
    /// The focus target the edit keys use. Last clicked: <paramref name="focus"/> as it is. Highlight wins: the highlight whenever one exists (clips and track rows keep their own focus).
    /// Selection wins: a highlight focus gives way to the score selection or the timeline range when one exists.
    /// </summary>
    public static FocusTarget Effective(EditTargetMode mode, FocusTarget focus, SelectionModel selection, BarHighlight highlight)
    {
        switch (mode)
        {
            case EditTargetMode.HighlightWins when highlight.Has && focus is FocusTarget.None or FocusTarget.ScoreSelection or FocusTarget.Loop:
                return FocusTarget.BarHighlight;
            case EditTargetMode.SelectionWins when focus == FocusTarget.BarHighlight:
                return selection.HasScoreSelection ? FocusTarget.ScoreSelection : selection.HasLoop ? FocusTarget.Loop : focus;
            default:
                return focus;
        }
    }
}
