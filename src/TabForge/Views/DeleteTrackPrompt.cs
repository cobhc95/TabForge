using System.Windows;

namespace TabForge.Views;

// Owns: the themed "Delete track?" question (Enter and Esc keep the track).
// Tests: TestTrackRowMenu.
internal static class DeleteTrackPrompt
{
    /// <summary>True when the user chose to delete the track named <paramref name="trackName"/>.</summary>
    public static bool Ask(Window? owner, string trackName)
    {
        var undoKey = Services.HotkeyCatalog.DisplayAll(TooltipShortcuts.Hotkeys, "Edit.Undo");
        var undoHint = undoKey.Length == 0 ? "You can undo this." : $"You can undo this with {undoKey}.";
        var dialog = new ThemedConfirmDialog("Delete track", $"Delete the track '{trackName}'? {undoHint}",
            yesToolTip: "Delete the track", noToolTip: "Keep the track", showCancel: false, yesText: "Delete", noText: "Cancel", defaultIsNo: true);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        return DialogHost.ShowModal(dialog) == true && dialog.Result == MessageBoxResult.Yes;
    }
}
