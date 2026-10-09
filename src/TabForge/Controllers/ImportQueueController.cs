using TabForge.Documents;

namespace TabForge.Controllers;

/// <summary>What the import queue controller needs from its window.</summary>
internal interface IImportQueueHost
{
    /// <summary>The window closed: a result arriving later is never applied.</summary>
    event EventHandler Closed;
    /// <summary>The pending imports changed: shows (or hides) the status line and the Cancel button.</summary>
    void ShowPendingImports(IReadOnlyList<ScoreImportJob> pending);
    /// <summary>The protected import process could not start: asks whether to open the file inside TabForge (default No).</summary>
    bool ConfirmImportInProcess(ScoreImportJob job, string reason);
}

// Owns: the window's background score-import queue (created on first use) and its wiring to the status line.
// Does not own: opening the finished song (MainWindow.Import.cs), the queue itself (ScoreImportQueue) and the status controls.
// Tests: TestGuitarProImportWorker, TestGuitarProImportContainment (full-suite).
internal sealed class ImportQueueController
{
    private readonly IImportQueueHost _host;
    private readonly DocumentController _documents;
    private ScoreImportQueue? _queue;

    public ImportQueueController(IImportQueueHost host, DocumentController documents) { _host = host; _documents = documents; }

    /// <summary>The queue, built on first use.</summary>
    public ScoreImportQueue Queue
    {
        get
        {
            if (_queue is not null) return _queue;
            var queue = _queue = ScoreImportQueue.WithImportWorker(_documents);
            queue.Changed += () => _host.ShowPendingImports(queue.Pending);
            queue.ConfirmInProcess = _host.ConfirmImportInProcess;
            _host.Closed += (_, _) => queue.CancelAll();
            return queue;
        }
    }

    /// <summary>Imports running or waiting (0 before the first import).</summary>
    public int PendingCount => _queue?.Pending.Count ?? 0;

    public void CancelAll() => _queue?.CancelAll();
}
