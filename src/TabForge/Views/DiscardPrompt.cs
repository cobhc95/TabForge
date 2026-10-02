using System.Windows;

namespace TabForge.Views;

/// <summary>
/// The window that decides whether the "discard unapplied changes?" question is asked and remembers "Don't ask me again": the main
/// window a settings-style dialog belongs to (found through the dialog's owner chain, never through a static callback).
/// </summary>
internal interface IDiscardPromptHost
{
    /// <summary>Whether to warn (General > Safety).</summary>
    bool WarnOnDiscard { get; }
    /// <summary>Turns the warning off and saves.</summary>
    void DisableDiscardWarning();
}

/// <summary>
/// Shared "discard unapplied changes?" confirmation for settings-style windows (Preferences, Track
/// properties, Project settings). Honours the General → Safety setting and its "don't ask again" box.
/// </summary>
public static class DiscardPrompt
{
    /// <summary>The host of <paramref name="dialog"/>: the first window up its owner chain that is one (null for an unowned dialog).</summary>
    internal static IDiscardPromptHost? HostOf(Window? dialog)
    {
        for (var window = dialog; window is not null; window = window.Owner)
            if (window is IDiscardPromptHost host) return host;
        return null;
    }

    /// <summary>True when the window may close (no changes, warning off, or the user confirmed).</summary>
    public static bool Confirm(Window owner, string title, string message, IReadOnlyList<string> changes, string noToolTip)
    {
        var host = HostOf(owner);
        if (changes.Count == 0 || !(host?.WarnOnDiscard ?? true)) return true;
        var prompt = new ThemedConfirmDialog(title, message,
            yesToolTip: "Close without applying", noToolTip: noToolTip,
            details: changes, showCancel: false, rememberText: "Don't ask me again") { Owner = owner };
        var discard = DialogHost.ShowModal(prompt) == true && prompt.Result == MessageBoxResult.Yes;
        if (discard && prompt.RememberChoice) host?.DisableDiscardWarning();
        return discard;
    }
}
