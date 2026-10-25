// Owns: the Delete-bars choice prompt's title, buttons, option labels (with each action's shortcut), scope row, defaults and the answer
//       its picked indices stand for.
// Does not own: the action names and stored settings (BarRangePromptText), the window (Views/BarRangePrompt, BarRangePromptWindow) or the edits (BarRangeFlow).
// Tests: no dedicated test class.
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>What the bar-range Delete prompt answered: the action, whether it applies to every track, and whether to remember the action.</summary>
public readonly record struct BarRangeAnswer(BarRangeAction Action, bool AllTracks, bool Remember);

public static class BarRangePromptPresenter
{
    public const string Title = "Delete bars";
    public const string OkText = "OK";
    public const string CancelText = "Cancel";
    public const string OkToolTip = "Do the selected option";
    public const string CancelToolTip = "Leave things as they are";
    public const string RememberText = "Remember my choice";
    public static IReadOnlyList<string> Scopes { get; } = new[] { "All tracks", "This track" };

    /// <summary>The clip choices under the bar options: Leave goes with "Leave empty bars", "Split and move" with "Remove bars" (the other is disabled).</summary>
    public static IReadOnlyList<string> ClipLabels { get; } = new[] { "Leave", "Split and move" };
    public const string ClipHeading = "Clips";

    private static IReadOnlyList<BarRangeAction> Actions => BarRangePromptText.DeleteActions;

    /// <summary>True when the picked bar option removes the bars, so clips are split and moved and cannot be left.</summary>
    public static bool SplitsClips(int choice) => Actions[ScoreEditorGeometry.ClampIndex(choice, Actions.Count)] == BarRangeAction.Remove;

    /// <summary>One label per action, in enum order, each followed by its shortcut when it has one.</summary>
    public static List<string> Labels(Func<string, string> shortcut) =>
        Actions.Select(action => shortcut(BarRangePromptText.CommandOf(action)) is { Length: > 0 } key
            ? $"{BarRangePromptText.Label(action)} ({key})"
            : BarRangePromptText.Label(action)).ToList();

    public static int DefaultChoice(BarRangeAction preselect) => Math.Max(0, Actions.ToList().IndexOf(preselect));

    public static int DefaultScope(bool allTracks) => allTracks ? 0 : 1;

    /// <summary>The answer for the picked option and scope (0 = all tracks).</summary>
    public static BarRangeAnswer Answer(int choice, int scope, bool remember) =>
        new(Actions[ScoreEditorGeometry.ClampIndex(choice, Actions.Count)], scope == 0, remember);
}
