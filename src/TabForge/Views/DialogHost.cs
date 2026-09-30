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

    public static bool? ShowModal(Window dialog)
    {
        if (Capture is { } capture) return capture(dialog);
        // Dialogs open centred on the main window (a dialog without an owner would land at the screen's corner).
        if (dialog.Owner is null && Application.Current?.MainWindow is { IsLoaded: true } main && !ReferenceEquals(main, dialog))
        {
            dialog.Owner = main;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return dialog.ShowDialog();
    }
}
