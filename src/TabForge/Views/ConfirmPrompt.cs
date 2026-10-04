using System.Windows;

namespace TabForge.Views;

// Owns: a themed question with a destructive action button and a Cancel default (Enter and Esc cancel).
// Tests: TestBarDeleteGuards (answers through DialogHost.Capture).
internal static class ConfirmPrompt
{
    /// <summary>True when the user chose <paramref name="yesText"/>; <paramref name="withUndoHint"/> appends the live Undo key.</summary>
    public static bool Ask(Window? owner, string title, string text, string yesText, bool withUndoHint)
    {
        if (withUndoHint)
        {
            var undoKey = Services.HotkeyCatalog.DisplayAll(TooltipShortcuts.Hotkeys, "Edit.Undo");
            text += undoKey.Length == 0 ? " You can undo this." : $" You can undo this with {undoKey}.";
        }
        var dialog = new ThemedConfirmDialog(title, text, yesToolTip: yesText, noToolTip: "Leave things as they are", showCancel: false,
            yesText: yesText, noText: "Cancel", defaultIsNo: true);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        return DialogHost.ShowModal(dialog) == true && dialog.Result == MessageBoxResult.Yes;
    }
}
