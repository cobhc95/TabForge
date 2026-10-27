// Owns: the words of the "Clips overlap" prompt (title, body, the two option cards, remember box and buttons).
// Does not own: finding or moving the clips (ClipLaneOverlap), the decision and the stored setting (ClipOverlapPolicy, EditingSettings.ClipOverlap) or the window (Views/ClipOverlapPromptWindow).
// Tests: TabForge.Avalonia.Tests.ClipLaneOverlapTests.
using TabForge.Documents;

namespace TabForge.Presenters;

public static class ClipOverlapPromptPresenter
{
    public const string Title = "Clips overlap";
    public const string MoveHint = "It keeps its timing; only its lane changes.";
    public const string KeepTitle = "Keep them overlapping";
    public const string KeepHint = "Both clips stay on the lane and play over each other.";
    public const string RememberText = "Remember my choice";
    public const string RememberHint = "(change in Settings › Editing)";
    public const string CancelText = "Cancel";
    public const string OkText = "OK";
    public const string CancelToolTip = "Undo the edit that made the clips overlap";
    public const string OkToolTip = "Apply the chosen option";

    /// <summary>The question text: the edited clip now overlaps the existing one on a lane.</summary>
    public static string Body(ClipOverlapQuestion question) => $"'{question.Edited}' now overlaps '{question.Existing}' on lane {question.Lane}.";

    /// <summary>The first card: the existing clip moves to a free lane.</summary>
    public static string MoveTitle(ClipOverlapQuestion question) => $"Move '{question.Existing}' to a free lane";

    /// <summary>The value Remember stores for an answer.</summary>
    public static string Store(ClipOverlapAnswer answer) => answer.Move ? ClipOverlapChoice.Move : ClipOverlapChoice.Keep;
}
