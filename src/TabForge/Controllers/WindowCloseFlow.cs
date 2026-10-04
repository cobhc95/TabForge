using System.Windows.Input;
using TabForge.Documents;

namespace TabForge.Controllers;

/// <summary>What the window close flow needs from its window.</summary>
internal interface IWindowCloseHost
{
    IReadOnlyList<DocumentSession> Documents { get; }
    /// <summary>The song the window shows.</summary>
    DocumentSession Shown { get; }
    bool IsSaving { get; }
    /// <summary>False when closing skips the unsaved-changes questions (probes, scripted runs).</summary>
    bool ConfirmOnClose { get; }
    bool IsClosed { get; }
    /// <summary>The save-changes question for one document.</summary>
    DiscardAnswer AskSaveChanges(string message);
    Task<bool> SaveDocumentAsync(DocumentSession doc);
    /// <summary>Closes the window later, at background priority, unless it already closed.</summary>
    void PostClose(Action closed);
    /// <summary>Runs <paramref name="work"/> on a later UI turn.</summary>
    void Post(Action work);
    /// <summary>The window's cursor (busy while a save runs).</summary>
    Cursor? Cursor { get; set; }
    void SetStatus(string text);
}

// Owns: the window's save / close sequencing: document operations in progress, the deferred close, the window-close questions
//   (DocumentCloseFlow plan, the discard answers), the input gate while a save runs, and degraded mode after an unexpected error.
// Does not own: the save itself (DocumentSaveFlow), the tab close questions, the dialogs (the host shows them).
// Tests: TestDocumentOperations, TestDocumentContext, TestWindowLifetime.
internal sealed class WindowCloseFlow
{
    private readonly IWindowCloseHost _host;
    /// <summary>Saves and multi-tab closes in progress; the window closes only when none is running.</summary>
    private int _documentOperations;
    /// <summary>A close was requested while an operation ran: close when the last one ends.</summary>
    private bool _closeWhenIdle;
    private bool _closeScheduled;
    /// <summary>Documents the user chose to discard in the window-close prompt (so the close that follows the saves of the others does not ask again).</summary>
    private readonly HashSet<DocumentSession> _discardOnClose = new();
    /// <summary>While a save runs the window takes no keyboard or mouse input (no edits, tab closes or commands mid-save); Alt+F4 still reaches Closing, which waits for the save.</summary>
    private int _saveGateDepth;
    private Cursor? _cursorBeforeSave;
    /// <summary>Files the user already chose to write while degraded (overwrite confirmed, or the new file saved as): no second prompt.</summary>
    private readonly HashSet<string> _degradedConfirmedPaths = new(StringComparer.OrdinalIgnoreCase);

    public WindowCloseFlow(IWindowCloseHost host) => _host = host;

    // ---------- degraded mode (after an unexpected error) ----------

    /// <summary>Set by the crash handler: the model may be inconsistent, so overwriting a file is opt-in.</summary>
    public bool Degraded { get; private set; }

    public void MarkDegraded() => Degraded = true;

    public string DegradedTitleSuffix => Degraded ? "  [after an error: restart recommended]" : "";

    /// <summary>True when a degraded save of <paramref name="path"/> still has to ask before overwriting.</summary>
    public bool MustConfirmOverwrite(string path) => Degraded && !_degradedConfirmedPaths.Contains(path);

    public void ConfirmedWhileDegraded(string path)
    {
        if (Degraded) _degradedConfirmedPaths.Add(path);
    }

    // ---------- saving vs. closing (no nested dispatcher frames) ----------

    public bool InputGated => _saveGateDepth > 0;

    public void BeginDocumentOperation() => _documentOperations++;

    public void EndDocumentOperation()
    {
        _documentOperations = Math.Max(0, _documentOperations - 1);
        if (_documentOperations == 0 && _closeWhenIdle) { _closeWhenIdle = false; RequestClose(); }
    }

    /// <summary>Closes the window once no save / close operation runs (Closing then asks as usual).</summary>
    public void RequestClose()
    {
        if (_documentOperations > 0) { _closeWhenIdle = true; return; }
        if (_closeScheduled) return;
        _closeScheduled = true;
        _host.PostClose(() => _closeScheduled = false);
    }

    /// <summary>
    /// Window Closing: it needs a synchronous answer, so it never waits. While a save runs the close is cancelled and retried when the
    /// save ends; "Save" in the prompt cancels it, saves with a real await, then closes again (the prompt is skipped: the song is clean).
    /// </summary>
    public bool ConfirmWindowClose()
    {
        if (_documentOperations > 0 || _host.IsSaving)
        {
            _closeWhenIdle = true;
            _host.SetStatus("Closing when the save has finished…");
            return false;
        }
        if (!_host.ConfirmOnClose) return true;
        // Every document in this window with unsaved changes is asked about, not only the displayed tab. All answers are collected before
        // anything happens: Cancel at any point leaves every document and the window exactly as they were (DocumentCloseFlow).
        var plan = DocumentCloseFlow.Plan(_host.Documents, _discardOnClose, (doc, asked) =>
        {
            var message = asked == 1 && ReferenceEquals(doc, _host.Shown) ? "Save changes to the current project?" : $"Save changes to {doc.DisplayName}?";
            return _host.AskSaveChanges(message);
        });
        if (plan.Cancel) return false;
        foreach (var doc in plan.Discard) _discardOnClose.Add(doc);   // answered "don't save": the close that follows the saves does not ask again
        if (plan.Save.Count == 0) return true;
        _host.Post(() => SaveThenCloseWindow(plan.Save));
        return false;
    }

    private async void SaveThenCloseWindow(IReadOnlyList<DocumentSession> documents)
    {
        if (!await DocumentCloseFlow.SaveAllAsync(documents, _host.SaveDocumentAsync))
        {
            _discardOnClose.Clear();   // a save did not complete: the window stays open and every answer is asked again at the next close
            return;
        }
        if (!_host.IsClosed) RequestClose();
    }

    public void BeginSaveInputGate()
    {
        if (_saveGateDepth++ > 0) return;
        _cursorBeforeSave = _host.Cursor;
        _host.Cursor = Cursors.AppStarting;
    }

    public void EndSaveInputGate()
    {
        if (_saveGateDepth == 0 || --_saveGateDepth > 0) return;
        _host.Cursor = _cursorBeforeSave;
    }
}
