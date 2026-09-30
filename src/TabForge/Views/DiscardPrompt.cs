using System.Windows;

namespace TabForge.Views;

/// <summary>
/// Shared "discard unapplied changes?" confirmation for settings-style windows (Preferences, Track
/// properties, Project settings). Honours the General → Safety setting and its "don't ask again" box.
/// </summary>
public static class DiscardPrompt
{
    /// <summary>Whether to warn; wired by the main window to the saved setting.</summary>
    public static Func<bool> IsEnabled { get; set; } = () => true;
    /// <summary>Turns the warning off and saves; wired by the main window.</summary>
    public static Action DisableAndSave { get; set; } = () => { };

    /// <summary>True when the window may close (no changes, warning off, or the user confirmed).</summary>
    public static bool Confirm(Window owner, string title, string message, IReadOnlyList<string> changes, string noToolTip)
    {
        if (changes.Count == 0 || !IsEnabled()) return true;
        var prompt = new ThemedConfirmDialog(title, message,
            yesToolTip: "Close without applying", noToolTip: noToolTip,
            details: changes, showCancel: false, rememberText: "Don't ask me again") { Owner = owner };
        var discard = DialogHost.ShowModal(prompt) == true && prompt.Result == MessageBoxResult.Yes;
        if (discard && prompt.RememberChoice) DisableAndSave();
        return discard;
    }
}
