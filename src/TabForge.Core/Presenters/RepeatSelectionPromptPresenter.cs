namespace TabForge.Presenters;

/// <summary>
/// The Repeat selection text prompt as plain data: its title, label and default, and what a typed count means.
/// Owns: the wording and the count rules (a whole number of 1 or more, clamped to 16).
/// Does not own: building or showing the prompt (TextPromptWindow in Avalonia) or the repeat itself (BarStructureEdits).
/// Tests: TabForge.Avalonia.Tests.TextPromptWindowTests.
/// </summary>
public static class RepeatSelectionPromptPresenter
{
    public const string Title = "Repeat selection";
    public const string Label = "How many times should the selected bars be repeated?";
    public const string Default = "1";
    public const int MaxTimes = 16;

    /// <summary>The count typed, clamped to 1-16; null when the user cancelled or typed anything but a whole number of 1 or more.</summary>
    public static int? Times(string? typed) => typed is not null && int.TryParse(typed, out var times) && times >= 1 ? Math.Min(times, MaxTimes) : null;
}
