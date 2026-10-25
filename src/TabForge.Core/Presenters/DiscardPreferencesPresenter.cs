namespace TabForge.Presenters;

/// <summary>One line of the discard question: its text, and for a colour change the two colours shown as swatches (the text then ends at the colon).</summary>
public sealed record DiscardLine(string Text, string? OldColour = null, string? NewColour = null, string? ToolTip = null);

/// <summary>The discard question: the lines to show (at most <see cref="DiscardPreferencesPresenter.MaxLines"/>) and the "and N more" line when some were cut.</summary>
public sealed record DiscardSummary(IReadOnlyList<DiscardLine> Lines, string? MoreText);

/// <summary>
/// The "Unsaved changes" question of the Preferences window: its texts and the plain-language list of what changed.
/// Owns: the wording, the buttons, folding the values a changed theme itself sets into one "Theme" line, and the line cap.
/// Does not own: finding the changes (PreferencesPagePresenter.Changes), the preset values (the catalogue row of the theme) or the window.
/// Tests: no dedicated test class.
/// </summary>
public static class DiscardPreferencesPresenter
{
    public const string Title = "Unsaved changes";
    public const string Message = "You changed some preferences but haven't applied them. Discard these changes?";
    public const string DiscardLabel = "Discard changes";
    public const string KeepLabel = "Keep editing";
    public const string ThemeKey = "appearance.thememode";
    public const int MaxLines = 8;
    private const string NoShownChanges = "Other settings not shown on these pages were changed";

    /// <summary>
    /// The lines for <paramref name="changes"/>. <paramref name="presetRows"/> are the settings as they would be with only the new theme picked:
    /// a change that merely equals the value the theme sets is folded into the "Theme: old → new" line; any other change keeps its own line.
    /// </summary>
    public static DiscardSummary Summarize(IReadOnlyList<PreferencesPagePresenter.PreferenceChange> changes, IEnumerable<IPreferenceRow> presetRows)
    {
        var theme = changes.FirstOrDefault(change => change.Key == ThemeKey);
        var presetValues = theme is null ? new Dictionary<string, string>() : presetRows.ToDictionary(row => row.Key, row => PreferencesPagePresenter.ShowValue(row.Value));
        var lines = new List<DiscardLine>();
        if (theme is not null) lines.Add(new DiscardLine($"Theme: {theme.OldText} → {theme.NewText}"));
        foreach (var change in changes)
        {
            if (change == theme || (presetValues.TryGetValue(change.Key, out var preset) && preset == change.NewText)) continue;
            lines.Add(Line(change));
        }
        if (lines.Count == 0) lines.Add(new DiscardLine(NoShownChanges));
        var more = lines.Count - MaxLines;
        return new DiscardSummary(lines.Take(MaxLines).ToList(), more > 0 ? $"…and {more} more change{(more == 1 ? "" : "s")}" : null);
    }

    private static DiscardLine Line(PreferencesPagePresenter.PreferenceChange change)
    {
        var label = $"{change.Page} · {change.Setting}:";
        return change.IsColour
            ? new DiscardLine(label, change.OldText, change.NewText, $"{change.OldText} → {change.NewText}")
            : new DiscardLine($"{label} {change.OldText} → {change.NewText}");
    }
}
