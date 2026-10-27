// Owns: the words of the "Stretch <Section> by N bars?" prompt (title, muted line, the four option cards, the repeat-sign reason, remember box and buttons) and the answer it gives.
// Does not own: the edit (SectionStretch), the stored setting (EditingSettings.SectionStretch) or the window (Views/SectionStretchPromptWindow).
// Tests: TabForge.Avalonia.Tests.SectionStretchTests.
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>What the prompt answered: the chosen option and whether to remember it.</summary>
public readonly record struct SectionStretchAnswer(SectionStretchMode Mode, bool Remember);

/// <summary>What the prompt shows: the section, the bars it has now and is asked to gain, the next section's name (null for the last section) and why a repeat sign is not possible (null when it is).</summary>
public sealed record SectionStretchQuestion(string Section, int Bars, int Added, string? Next, string? SignConflict, int TailBars);

public static class SectionStretchPromptPresenter
{
    public const string RememberText = "Remember my choice";
    public const string RememberHint = "(Settings › Editing › Sections)";
    public const string CancelText = "Cancel";
    public const string StretchText = "Stretch";
    public const string CancelToolTip = "Leave the section as it was";
    public const string StretchToolTip = "Stretch the section with the chosen option";
    public const string AddHint = "New empty bars at the end of the section on every track.";
    public const string SignTitle = "Add a repeat sign instead";
    public const string StopHint = "Nothing changes.";

    /// <summary>The question for dragging <paramref name="marker"/>'s end <paramref name="added"/> bars past the next section.</summary>
    public static SectionStretchQuestion Question(SongProject project, SectionStretchPlan plan, int added)
    {
        var (_, tail) = SectionStretch.TailOf(plan, added);
        return new SectionStretchQuestion(NameOf(plan.Section), plan.NextStart - plan.Start, added, plan.Next is null ? null : NameOf(plan.Next),
            SectionStretch.RepeatSignConflict(project, plan, added), tail);
    }

    private static string NameOf(MarkerModel marker) => string.IsNullOrWhiteSpace(marker.Title) ? "Section" : marker.Title;

    private static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";

    public static string Title(SectionStretchQuestion q) => $"Stretch {q.Section} by {Bars(q.Added)}?";

    public static string Body(SectionStretchQuestion q) =>
        $"{q.Section} goes from {q.Bars} to {Bars(q.Bars + q.Added)}" + (q.Next is null ? "." : $"; {q.Next} and everything after moves {Bars(q.Added)} later.");

    public static string AddTitle(SectionStretchQuestion q) => $"Add {(q.Added == 1 ? "1 empty bar" : $"{q.Added} empty bars")}";

    public static string RepeatTitle(SectionStretchQuestion q) => $"Repeat {q.Section}'s last bars to fill";

    public static string RepeatHint(SectionStretchQuestion q) =>
        q.TailBars < q.Added ? $"Copies all {Bars(q.TailBars)} of {q.Section} again and again until {Bars(q.Added)} are filled." : $"Copies the last {Bars(q.TailBars)} of {q.Section} into the new space.";

    public static string SignHint(SectionStretchQuestion q) => $"No new bars: the last {Bars(q.TailBars)} of {q.Section} play twice.";

    public static string StopTitle(SectionStretchQuestion q) => q.Next is null ? "Stop at the end of the song" : $"Stop at {q.Next}";
}

/// <summary>What the backward prompt answered: the chosen option and whether to remember it.</summary>
public readonly record struct SectionStretchBackAnswer(SectionStretchBackMode Mode, bool Remember);

/// <summary>What the backward prompt shows: the section, its bars now and the bars asked for, the previous section's name (null for the first section) and the bars it can give.</summary>
public sealed record SectionStretchBackQuestion(string Section, int Bars, int Added, string? Previous, int Takeable);

// Owns: the words of the "Stretch <Section> back by N bars?" prompt (dragging a section's start past the previous section).
public static class SectionStretchBackPromptPresenter
{
    public const string AddHint = "New empty bars at the start of the section on every track; everything from its old start moves later.";
    public const string StopHint = SectionStretchPromptPresenter.StopHint;

    public static SectionStretchBackQuestion Question(SectionStretchBackPlan plan, int added) =>
        new(NameOf(plan.Section), plan.End - plan.Start, added, plan.Previous is null ? null : NameOf(plan.Previous), plan.Takeable);

    private static string NameOf(MarkerModel marker) => string.IsNullOrWhiteSpace(marker.Title) ? "Section" : marker.Title;

    private static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";

    public static string Title(SectionStretchBackQuestion q) => $"Stretch {q.Section} back by {Bars(q.Added)}?";

    public static string Body(SectionStretchBackQuestion q) => $"{q.Section} goes from {q.Bars} to {Bars(q.Bars + q.Added)}, starting {Bars(q.Added)} earlier.";

    public static string AddTitle(SectionStretchBackQuestion q) => $"Add {(q.Added == 1 ? "1 empty bar" : $"{q.Added} empty bars")} at its start";

    public static string RepeatTitle(SectionStretchBackQuestion q) => "Repeat its first bars as a lead-in";

    public static string RepeatHint(SectionStretchBackQuestion q) => q.Bars < q.Added
        ? $"Copies all {Bars(q.Bars)} of {q.Section} again and again in front of it until {Bars(q.Added)} are filled."
        : $"Copies the first {Bars(q.Added)} of {q.Section} in front of it.";

    public static string TakeTitle(SectionStretchBackQuestion q) => $"Take {Bars(Math.Max(1, Math.Min(q.Added, q.Takeable)))} from {q.Previous ?? "the previous section"}";

    public static string TakeHint(SectionStretchBackQuestion q) => $"No new bars: the last bars of {q.Previous ?? "the previous section"} become part of {q.Section}.";

    /// <summary>Why the previous section cannot give bars; null when it can.</summary>
    public static string? TakeConflict(SectionStretchBackQuestion q) =>
        q.Previous is null ? $"No section comes before {q.Section}." : q.Takeable < 1 ? $"{q.Previous} would be left with no bars." : null;

    public static string StopTitle(SectionStretchBackQuestion q) => q.Previous is null ? "Stop at the start of the song" : $"Stop at {q.Previous}";
}
