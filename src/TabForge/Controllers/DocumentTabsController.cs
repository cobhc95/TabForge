using TabForge.Documents;

namespace TabForge.Controllers;

/// <summary>What <see cref="DocumentTabsController"/> needs from its window.</summary>
internal interface IDocumentTabsHost
{
    DocumentManager Documents { get; }
    TabSettings TabSettings { get; }
    /// <summary>True while a save runs: a tab with unsaved changes does not close then.</summary>
    bool IsSaving { get; }
    /// <summary>Stores the view state of the song on show.</summary>
    void CaptureDocumentState();
    void RefreshTabs();
    void Activate(DocumentSession session, bool focusTabSelection, bool applyPlaybackSwitchPolicy);
    /// <summary>The save-changes question for one document (Close when it has none).</summary>
    DiscardAnswer AskDiscard(DocumentSession doc);
    Task<bool> SaveDocumentAsync(DocumentSession doc);
    /// <summary>A window close waits for operations begun here (a save, a multi-tab close).</summary>
    void BeginDocumentOperation();
    void EndDocumentOperation();
    void CloseWindow();
    void SetStatus(string text);
}

// Tab commands of one window: switch, duplicate, reorder and the closes (one, others, to the right), each asking about unsaved changes.
// Owns: the tab close sequences (the question, the awaited save, finding the tab again by reference afterwards) and tab switching, duplicating and moving.
// Does not own: the save itself (DocumentSaveFlow), where a new song opens (DocumentPlacement), moving tabs between windows (TabTransferController).
// Tests: TestEssentialCloseTabUnsaved, TestEssentialSecondFileAndTabs, TestDocumentOperations, TestTabUi, TestDocumentTabsController.
internal sealed class DocumentTabsController
{
    private readonly IDocumentTabsHost _host;

    public DocumentTabsController(IDocumentTabsHost host) => _host = host;

    private DocumentManager Documents => _host.Documents;

    public void ActivateAt(int index)
    {
        if (index < 0 || index >= Documents.Documents.Count || index == Documents.ActiveIndex) return;
        _host.Activate(Documents.Documents[index], focusTabSelection: false, applyPlaybackSwitchPolicy: true);
    }

    public void Duplicate(int index)
    {
        _host.CaptureDocumentState();
        var copy = Documents.Duplicate(index);
        if (copy is null) return;
        _host.Activate(copy, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
        _host.SetStatus($"Duplicated {copy.DisplayName}");
    }

    public void Move(int from, int to)
    {
        if (!Documents.Move(from, to)) return;
        _host.CaptureDocumentState();
        _host.RefreshTabs();
    }

    /// <summary>Closes one tab. "Save" awaits the save (no nested dispatcher frame); the tab is found again by reference afterwards.</summary>
    public async Task CloseAsync(int index)
    {
        if (index < 0 || index >= Documents.Documents.Count) return;
        _host.CaptureDocumentState();
        var doc = Documents.Documents[index];
        if (!await ConfirmDiscardAsync(doc)) return;
        index = Documents.IndexOf(doc);
        if (index < 0) return;   // it went away while saving
        if (Documents.Documents.Count == 1 && _host.TabSettings.LastTabClosed == LastTabActions.CloseWindow)
        {
            doc.DisposePlayback();
            Documents.Detach(index);
            _host.CloseWindow();
            return;
        }
        Documents.Close(index);
        doc.DisposePlayback();
        Reactivate();
        _host.SetStatus($"Closed {doc.DisplayName}");
    }

    public async Task CloseOthersAsync(int keep)
    {
        if (keep < 0 || keep >= Documents.Documents.Count) return;
        _host.CaptureDocumentState();
        var kept = Documents.Documents[keep];
        var others = Documents.Documents.Where(d => !ReferenceEquals(d, kept)).ToArray();
        await CloseManyAsync(others);
        var at = Documents.IndexOf(kept);
        Reactivate(at < 0 ? Documents.ActiveIndex : at);
        _host.SetStatus("Closed other tabs");
    }

    public async Task CloseToTheRightAsync(int from)
    {
        _host.CaptureDocumentState();
        await CloseManyAsync(Documents.Documents.Skip(from + 1).ToArray());
        Reactivate(Documents.ActiveIndex);
        _host.SetStatus("Closed tabs to the right");
    }

    /// <summary>Shows the document at <paramref name="index"/> (clamped) as a plain switch: no focus move, no playback policy.</summary>
    private void Reactivate(int? index = null) =>
        _host.Activate(index is { } i ? Documents.Documents[Math.Clamp(i, 0, Documents.Documents.Count - 1)] : Documents.Active, focusTabSelection: false, applyPlaybackSwitchPolicy: false);

    /// <summary>Closes <paramref name="targets"/> last-first, asking for each; one operation, so a window close waits for all of it.</summary>
    private async Task CloseManyAsync(IReadOnlyList<DocumentSession> targets)
    {
        _host.BeginDocumentOperation();
        try
        {
            for (var k = targets.Count - 1; k >= 0; k--)
            {
                var doc = targets[k];
                if (!await ConfirmDiscardAsync(doc)) continue;
                var i = Documents.IndexOf(doc);
                if (i < 0) continue;
                Documents.Close(i);
                doc.DisposePlayback();
            }
        }
        finally { _host.EndDocumentOperation(); }
    }

    /// <summary>Asks before a tab closes; "Save" saves it with a real await. True when the document may close now.</summary>
    public async Task<bool> ConfirmDiscardAsync(DocumentSession doc)
    {
        if (_host.IsSaving && doc.HasUnsavedChanges)
        {
            _host.SetStatus("Saving… close that tab when the save has finished");
            return false;
        }
        switch (_host.AskDiscard(doc))
        {
            case DiscardAnswer.Keep: return false;
            case DiscardAnswer.Close: return true;
        }
        return await _host.SaveDocumentAsync(doc) && !doc.HasUnsavedChanges;
    }

    /// <summary>"Save" when a song replaces this tab: the new song opened beside it; this tab closes once its save succeeded.</summary>
    public async Task SaveThenCloseAsync(DocumentSession doc)
    {
        if (!await _host.SaveDocumentAsync(doc) || doc.HasUnsavedChanges) return;   // not saved: the tab stays open with its changes
        var index = Documents.IndexOf(doc);
        if (index < 0 || Documents.Documents.Count <= 1) return;
        Documents.Close(index);
        doc.DisposePlayback();
        Reactivate();
    }
}
