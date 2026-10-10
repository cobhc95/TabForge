using System.IO;
using System.Windows;
using TabForge.Controllers;
using TabForge.Documents;

namespace TabForge;

// MainWindow, background score import: the parse runs off the UI thread with a status "Importing <name>…"
// and a Cancel button in the status bar; the song is opened on the UI thread only when the import succeeded.
// Owns: background score import: the parse runs off the UI thread, with its status text and Cancel; the song opens on the UI thread only after a successful import.
// Does not own: the import queue itself.
// Tests: listed in docs/feature-map/import-and-export.md.
public partial class MainWindow : IImportQueueHost
{
    private ImportQueueController? _importQueue;
    private ImportQueueController Imports => _importQueue ??= new ImportQueueController(this, _documentController);

    /// <summary>
    /// Opens a score: a .tforge synchronously; a Guitar Pro file in the background. <paramref name="opened"/> runs on the UI
    /// thread after the song was loaded (LoadProject returned <paramref name="opened"/>'s argument: false = the user kept the tab).
    /// Returns the job for a background import, null when the open already finished.
    /// </summary>
    private ScoreImportJob? OpenScore(string path, bool replaceCurrent, bool replaceAll, Action<OpenedScore, bool>? opened = null, bool background = true)
    {
        // The tab this open replaces is chosen now, not when a background import completes (by then another tab may be displayed, or this one may
        // have moved to another window): see DocumentPlacement.
        var replaceTarget = replaceCurrent && _documents.Documents.Count > 0 ? Doc : null;
        // A .tforge opens at once, unless imports are still running: then it queues behind them so tabs keep the requested order.
        // background = false: the synchronous open for probe / tour launches.
        if (!background || (!ScoreImportQueue.RunsInBackground(path) && Imports.PendingCount == 0))
        {
            OpenedScore result;
            try { result = _documentController.Open(path); }
            catch (Exception ex) { ReportOpenFailed(path, ex); return null; }
            var loaded = LoadProject(result.Project, result.SessionPath, true, replaceTarget, replaceAll, result.SourcePath);
            opened?.Invoke(result, loaded);
            return null;
        }

        return Imports.Queue.Start(path,
            (_, result) =>
            {
                bool loaded;
                try { loaded = LoadProject(result.Project, result.SessionPath, true, replaceTarget, replaceAll, result.SourcePath); }
                catch (Exception ex) { ReportOpenFailed(path, ex); return; }
                opened?.Invoke(result, loaded);
            },
            (job, error) =>
            {
                if (error is null) StatusText.Text = $"Import of {job.Name} cancelled";
                else ReportOpenFailed(path, error);
            });
    }

    /// <summary>The protected import could not start; opening in this process needs the user's yes for this file (default No).</summary>
    bool IImportQueueHost.ConfirmImportInProcess(ScoreImportJob job, string reason) =>
        IsLoaded && MessageBox.Show(this,
            $"The protected import process could not start ({reason}).\n\nOpening {job.Name} inside TabForge is less protected against damaged files. Open it anyway?",
            "Open without protection?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ReportOpenFailed(string path, Exception ex)
    {
        StatusText.Text = "Open failed";
        TabForge.Views.DialogHost.ShowError(this, $"Could not open {Path.GetFileName(path)}.\n\n{ex.Message}", "Open failed");
    }

    void IImportQueueHost.ShowPendingImports(IReadOnlyList<ScoreImportJob> pending)
    {
        CancelImportButton.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (pending.Count == 0) { StatusText.ToolTip = "Last action or current state"; return; }
        StatusText.Text = pending.Count == 1 ? $"Importing {pending[0].Name}…" : $"Importing {pending.Count} files…";
        StatusText.ToolTip = string.Join("\n", pending.Select(job => $"Importing {job.Name}"));
        Views.TooltipShortcuts.SetText(CancelImportButton, pending.Count == 1 ? $"Cancel importing {pending[0].Name}" : $"Cancel the {pending.Count} imports in progress");   // keeps the key bracket, and a rebind cannot revert it
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e) => _importQueue?.CancelAll();
}
