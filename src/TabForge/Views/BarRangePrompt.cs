using System.Windows;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the bar-range Delete prompt answered: the action, whether it applies to every track, and whether to remember the action.</summary>
internal readonly record struct BarRangeAnswer(BarRangeAction Action, bool AllTracks, bool Remember);

// Owns: the themed choice prompt Delete opens on bars selected on the timeline (clear, remove, insert a gap before / after; every track or this track).
// Does not own: running the choice (BarRangeFlow) or the words (BarRangePromptText).
// Tests: TestBarRangeGaps (answered through DialogHost.Capture).
internal static class BarRangePrompt
{
    /// <summary>Asks what to do; null on Cancel / Esc. <paramref name="shortcut"/> gives the live key text of an action's command ("" when unbound).</summary>
    public static BarRangeAnswer? Ask(Window? owner, string text, BarRangeAction preselect, bool allTracks, Func<string, string> shortcut)
    {
        var actions = Enum.GetValues<BarRangeAction>();
        var labels = actions.Select(a => shortcut(BarRangePromptText.CommandOf(a)) is { Length: > 0 } key ? $"{BarRangePromptText.Label(a)} ({key})" : BarRangePromptText.Label(a)).ToList();
        var dialog = new ThemedConfirmDialog("Delete bars", text, yesToolTip: "Do the selected option", noToolTip: "Leave things as they are", showCancel: false,
            rememberText: "Remember my answer", yesText: "OK", noText: "Cancel", choices: labels, defaultChoice: Array.IndexOf(actions, preselect),
            scopes: new[] { "All tracks", "This track" }, defaultScope: allTracks ? 0 : 1);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        if (DialogHost.ShowModal(dialog) != true || dialog.Result != MessageBoxResult.Yes) return null;
        return new BarRangeAnswer(actions[dialog.SelectedChoice], dialog.SelectedScope == 0, dialog.RememberChoice);
    }
}
