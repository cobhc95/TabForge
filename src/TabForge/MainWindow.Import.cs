using System.IO;
using System.Windows;
using TabForge.Documents;

namespace TabForge;

// MainWindow, background Guitar Pro import (audit A5-07): the parse runs off the UI thread with a status "Importing <name>…"
// and a Cancel button in the status bar; the song is opened on the UI thread only when the import succeeded.
public partial class MainWindow
{
    private ScoreImportQueue? _imports;

    private ScoreImportQueue Imports
    {
        get
        {
            if (_imports is not null) return _imports;
            _imports = ScoreImportQueue.WithImportWorker(_documentController);
            _imports.Changed += RefreshImportStatus;
            _imports.ConfirmInProcess = ConfirmImportInProcess;
            Closed += (_, _) => _imports.CancelAll();   // a result arriving after the window closed is never applied
            return _imports;
        }
    }

    /// <summary>
    /// Opens a score: a .tforge synchronously; a Guitar Pro file in the background. <paramref name="opened"/> runs on the UI
    /// thread after the song was loaded (LoadProject returned <paramref name="opened"/>'s argument: false = the user kept the tab).
    /// Returns the job for a background import, null when the open already finished.
    /// </summary>
    private ScoreImportJob? OpenScore(string path, bool replaceCurrent, bool replaceAll, Action<OpenedScore, bool>? opened = null, bool background = true)
    {
        // A .tforge opens at once, unless imports are still running: then it queues behind them so tabs keep the requested order.
        // background = false: the synchronous open for probe / tour launches.
        if (!background || (!ScoreImportQueue.RunsInBackground(path) && (_imports?.Pending.Count ?? 0) == 0))
        {
            OpenedScore result;
            try { result = _documentController.Open(path); }
            catch (Exception ex) { ReportOpenFailed(path, ex); return null; }
            var loaded = LoadProject(result.Project, result.SessionPath, true, replaceCurrent, replaceAll);
            opened?.Invoke(result, loaded);
            return null;
        }

        return Imports.Start(path,
            (_, result) =>
            {
                bool loaded;
                try { loaded = LoadProject(result.Project, result.SessionPath, true, replaceCurrent, replaceAll); }
                catch (Exception ex) { ReportOpenFailed(path, ex); return; }
                opened?.Invoke(result, loaded);
            },
            (job, error) =>
            {
                if (error is null) StatusText.Text = $"Import of {job.Name} cancelled";
                else ReportOpenFailed(path, error);
            });
    }

    /// <summary>A6-03: the protected import could not start; opening in this process needs the user's yes for this file (default No).</summary>
    private bool ConfirmImportInProcess(ScoreImportJob job, string reason) =>
        IsLoaded && MessageBox.Show(this,
            $"The protected import process could not start ({reason}).\n\nOpening {job.Name} inside TabForge is less protected against damaged files. Open it anyway?",
            "Open without protection?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ReportOpenFailed(string path, Exception ex)
    {
        StatusText.Text = "Open failed";
        MessageBox.Show(this, $"Could not open {Path.GetFileName(path)}.\n\n{ex.Message}", "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void RefreshImportStatus()
    {
        var pending = _imports?.Pending ?? Array.Empty<ScoreImportJob>();
        CancelImportButton.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (pending.Count == 0) { StatusText.ToolTip = "Last action or current state"; return; }
        StatusText.Text = pending.Count == 1 ? $"Importing {pending[0].Name}…" : $"Importing {pending.Count} files…";
        StatusText.ToolTip = string.Join("\n", pending.Select(job => $"Importing {job.Name}"));
        CancelImportButton.ToolTip = pending.Count == 1 ? $"Cancel importing {pending[0].Name}" : $"Cancel the {pending.Count} imports in progress";
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e) => _imports?.CancelAll();
}
