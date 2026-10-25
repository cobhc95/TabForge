// Owns: the status bar text, tooltip and Cancel button text shown while background score imports run, and the status text after an import was cancelled.
// Does not own: the import queue (ImportQueueController), the status bar and Cancel button (MainWindow.Import.cs), or the Cancel button's shortcut bracket (Views/TooltipShortcuts).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>
/// What the status bar and the Cancel button show for the pending imports. <see cref="StatusText"/> and <see cref="CancelText"/> are null when
/// the window keeps its current text (nothing is pending).
/// </summary>
public sealed record ImportStatus(bool CancelVisible, string? StatusText, string StatusTooltip, string? CancelText);

/// <summary>
/// Import status texts, from the names of the pending imports in queue order.
/// Thread: UI thread only (the import queue calls its host on the UI thread).
/// </summary>
public static class ImportStatusPresenter
{
    /// <summary>The status bar tooltip when no import is pending.</summary>
    public const string IdleTooltip = "Last action or current state";

    /// <summary>The status for the imports in progress; one name is shown by name, several by count.</summary>
    public static ImportStatus ForPending(IReadOnlyList<string> pendingNames)
    {
        if (pendingNames.Count == 0) return new ImportStatus(false, null, IdleTooltip, null);
        var tooltip = string.Join("\n", pendingNames.Select(name => $"Importing {name}"));
        if (pendingNames.Count == 1)
            return new ImportStatus(true, $"Importing {pendingNames[0]}…", tooltip, $"Cancel importing {pendingNames[0]}");
        return new ImportStatus(true, $"Importing {pendingNames.Count} files…", tooltip, $"Cancel the {pendingNames.Count} imports in progress");
    }

    /// <summary>The status text after the user cancelled an import.</summary>
    public static string CancelledStatus(string name) => $"Import of {name} cancelled";
}
