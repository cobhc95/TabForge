using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>Where Insert > Bar > Several bars puts the new bars.</summary>
public enum InsertBarsPlace { BeforeSelected, AfterSelected, AtEnd }

/// <summary>What the Several bars dialog answered: how many bars and where.</summary>
public sealed record InsertBarsAnswer(int Count, InsertBarsPlace Place);

/// <summary>
/// The Insert several bars dialog as plain data: its wording, what a typed count means and the bar a place resolves to.
/// Owns: the wording, the count rules (a whole number of 1 or more, at most 64 and at most what the song can still hold) and the insertion bar.
/// Does not own: building or showing the dialog (InsertBarsWindow in Avalonia) or the insert itself (BarStructureEdits.InsertBars).
/// Tests: TabForge.Avalonia.Tests.InsertMenuTests.
/// </summary>
public static class InsertBarsPresenter
{
    public const string Title = "Insert several bars";
    public const string CountLabel = "How many bars:";
    public const string DefaultCount = "4";
    public const string BeforeText = "Before the selected bar";
    public const string AfterText = "After the selected bar";
    public const string AtEndText = "At the end of the song";
    public const int MaxCount = 64;

    /// <summary>The count typed, clamped to <see cref="MaxCount"/> and to the room left under the song limit; null for anything but a whole number of 1 or more, or when the song is full.</summary>
    public static int? Count(string? typed, int songBars)
    {
        if (typed is null || !int.TryParse(typed.Trim(), out var count) || count < 1) return null;
        var room = InputLimits.MaxMeasuresPerTrack - songBars;
        return room < 1 ? null : Math.Min(Math.Min(count, MaxCount), room);
    }

    /// <summary>The bar the new bars go in front of: the first selected bar, the bar after the last selected bar, or the song end. Without a selection the cursor bar stands in for it.</summary>
    public static int InsertionBar(InsertBarsPlace place, int cursorBar, (int First, int Last)? selected, int songBars)
    {
        var (first, last) = selected ?? (cursorBar, cursorBar);
        return place switch
        {
            InsertBarsPlace.BeforeSelected => Math.Clamp(first, 0, songBars),
            InsertBarsPlace.AfterSelected => Math.Clamp(last + 1, 0, songBars),
            _ => songBars,
        };
    }
}
