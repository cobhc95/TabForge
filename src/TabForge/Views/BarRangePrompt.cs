using System.Windows;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the bar-range Delete prompt answered: the action, whether it applies to every track, and whether to remember the action.</summary>
internal readonly record struct BarRangeAnswer(BarRangeAction Action, bool AllTracks, bool Remember);

/// <summary>The per-window details needed to show the delete-bars choice prompt.</summary>
internal interface IBarRangePromptHost
{
    Window Owner { get; }
    AppSettings Settings { get; }
}

// Owns: one window owner's reusable Delete-bars choice window and its per-show selections.
// Does not own: the action, remembered settings or bar edits (BarRangeFlow), nor prompt wording (BarRangePromptText).
// Tests: TestBarRangeGaps (real repeated-modal prompt coverage).
internal sealed class BarRangePrompt : IDisposable
{
    private readonly IBarRangePromptHost _host;
    private ThemedConfirmDialog? _dialog;
    private bool _showing;
    private bool _disposed;

    public BarRangePrompt(IBarRangePromptHost host)
    {
        _host = host;
        host.Owner.Closing += OwnerClosing;
        host.Owner.Closed += OwnerClosed;
    }

    /// <summary>Asks what to do; null on Cancel / Esc. Each owner keeps its own hidden reusable window.</summary>
    public BarRangeAnswer? Ask(string text, BarRangeAction preselect, bool allTracks)
    {
        if (_disposed || _showing || !_host.Owner.IsLoaded) return null;
        _showing = true;
        try
        {
            var actions = Enum.GetValues<BarRangeAction>();
            var labels = Labels(id => HotkeyCatalog.DisplayAll(_host.Settings.Hotkeys, id));
            if (DialogHost.Capture is not null)
            {
                var captured = CreateDialog(_host.Owner, text, labels, preselect, allTracks, reusable: false);
                if (DialogHost.ShowModal(captured) != true || captured.Result != MessageBoxResult.Yes) return null;
                return new BarRangeAnswer(actions[captured.SelectedChoice], captured.SelectedScope == 0, captured.RememberChoice);
            }
            _dialog ??= CreateDialog(_host.Owner, text, labels, preselect, allTracks, reusable: true);
            var dialog = _dialog;
            dialog.ResetReusable(text, labels, Array.IndexOf(actions, preselect), allTracks ? 0 : 1);
            DialogHost.ShowModal(dialog);    // Hide ends WPF's modal loop with false; Result is the logical answer.
            if (_disposed || !ReferenceEquals(dialog, _dialog) || dialog.Result != MessageBoxResult.Yes) return null;
            return new BarRangeAnswer(actions[dialog.SelectedChoice], dialog.SelectedScope == 0, dialog.RememberChoice);
        }
        finally { _showing = false; }
    }

    /// <summary>One-shot prompt retained for diagnostic capture tools that adopt the dialog instead of showing it.</summary>
    public static BarRangeAnswer? Ask(Window? owner, string text, BarRangeAction preselect, bool allTracks, Func<string, string> shortcut)
    {
        var actions = Enum.GetValues<BarRangeAction>();
        var dialog = CreateDialog(owner, text, Labels(shortcut), preselect, allTracks, reusable: false);
        if (DialogHost.ShowModal(dialog) != true || dialog.Result != MessageBoxResult.Yes) return null;
        return new BarRangeAnswer(actions[dialog.SelectedChoice], dialog.SelectedScope == 0, dialog.RememberChoice);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Owner.Closing -= OwnerClosing;
        _host.Owner.Closed -= OwnerClosed;
        ClosePrompt();
    }

    private void OwnerClosing(object? sender, System.ComponentModel.CancelEventArgs e) => ClosePrompt();

    private void OwnerClosed(object? sender, EventArgs e) => Dispose();

    private void ClosePrompt()
    {
        var dialog = _dialog;
        _dialog = null;
        dialog?.CloseReusable();
    }

    private static ThemedConfirmDialog CreateDialog(Window? owner, string text, IReadOnlyList<string> labels,
        BarRangeAction preselect, bool allTracks, bool reusable)
    {
        var actions = Enum.GetValues<BarRangeAction>();
        var dialog = new ThemedConfirmDialog(hideOnAnswer: reusable, title: "Delete bars", message: text,
            yesToolTip: "Do the selected option", noToolTip: "Leave things as they are", showCancel: false,
            rememberText: "Remember my answer", yesText: "OK", noText: "Cancel", choices: labels, defaultChoice: Array.IndexOf(actions, preselect),
            scopes: new[] { "All tracks", "This track" }, defaultScope: allTracks ? 0 : 1);
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
            dialog.ShowActivated = owner.ShowActivated;
        }
        return dialog;
    }

    private static List<string> Labels(Func<string, string> shortcut) =>
        Enum.GetValues<BarRangeAction>().Select(action =>
            shortcut(BarRangePromptText.CommandOf(action)) is { Length: > 0 } key
                ? $"{BarRangePromptText.Label(action)} ({key})"
                : BarRangePromptText.Label(action)).ToList();
}
