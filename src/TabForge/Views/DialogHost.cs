using System.Windows;

namespace TabForge.Views;

/// <summary>
/// Every modal dialog opens through here. Normally it is just ShowDialog(); the screenshot tour
/// (<c>--screenshots</c>) sets <see cref="Capture"/> to photograph each dialog instead of waiting for input.
/// </summary>
public static class DialogHost
{
    /// <summary>When set, receives the fully built dialog instead of it being shown modally.</summary>
    internal static Func<Window, bool?>? Capture { get; set; }

    /// <summary>When set, receives (caption, text) of an error message instead of it being shown (a test of a failed save must not block on a message box).</summary>
    internal static Action<string, string>? MessageCapture { get; set; }

    /// <summary>An error message box over <paramref name="owner"/> (the save and export failure reports).</summary>
    public static void ShowError(Window owner, string text, string caption)
    {
        if (MessageCapture is { } capture) { capture(caption, text); return; }
        MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static bool? ShowModal(Window dialog)
    {
        if (Capture is { } capture) return capture(dialog);
        // Dialogs open centred on the main window (a dialog without an owner would land at the screen's corner).
        if (dialog.Owner is null && Application.Current?.MainWindow is { IsLoaded: true } main && !ReferenceEquals(main, dialog))
        {
            dialog.Owner = main;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        Prepare(dialog);
        return dialog.ShowDialog();
    }

    /// <summary>
    /// Every dialog closes on Esc, however it was opened. WPF's own IsCancel handling needs keyboard focus inside the dialog; a dialog
    /// opened from a shortcut can come up without it (the key that opened it is still down), so Esc went to the window behind it.
    /// The dialog is activated once shown, and an Esc nothing else handled presses its Cancel button.
    /// </summary>
    internal static void Prepare(Window dialog)
    {
        dialog.ContentRendered += (_, _) =>
        {
            if (!dialog.IsActive) dialog.Activate();
            if (dialog.IsKeyboardFocusWithin) return;
            dialog.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape || e.Handled) return;
            if (PressCancel(dialog)) e.Handled = true;
        };
    }

    /// <summary>Clicks the dialog's enabled IsCancel button; false when it has none.</summary>
    internal static bool PressCancel(DependencyObject root)
    {
        if (root is System.Windows.Controls.Button { IsCancel: true, IsEnabled: true, IsVisible: true } button)
        {
            // Invoke (not a raised Click event) so the button's own IsCancel behaviour closes the dialog.
            if (new System.Windows.Automation.Peers.ButtonAutomationPeer(button).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                is System.Windows.Automation.Provider.IInvokeProvider invoke) invoke.Invoke();
            return true;
        }
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (PressCancel(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }
}
