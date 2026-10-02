namespace TabForge.Documents;

public enum PlacementKind
{
    /// <summary>Opened as a new tab.</summary>
    Added,
    /// <summary>Replaced the target tab (its playback is released).</summary>
    Replaced,
    /// <summary>Replaced every open tab (startup / file association launch).</summary>
    ReplacedAll,
    /// <summary>The target has unsaved changes and the person wants them saved: the new song opened beside it; the target closes once its save succeeded.</summary>
    OpenedBesideToSave,
    /// <summary>The person kept the target tab: nothing was opened.</summary>
    Kept,
}

/// <param name="Document">The document that was opened (null when <see cref="PlacementKind.Kept"/>).</param>
/// <param name="Target">The tab that was replaced / must be saved first (null otherwise).</param>
public sealed record PlacementResult(PlacementKind Kind, DocumentSession? Document, DocumentSession? Target);

/// <summary>
/// Where an opened song goes among a window's tabs. The tab a "replace this tab" open applies to is chosen when the open <i>starts</i> and passed in
/// as <c>Target</c>: a background import that finishes after the person switched tabs (or after that tab moved to another window) never replaces
/// whichever tab is displayed at completion; a target that is no longer in this window means the song opens beside the others. No window, no
/// dialogs: the question about unsaved changes is an argument.
/// </summary>
public static class DocumentPlacement
{
    /// <param name="documents">This window's tabs.</param>
    /// <param name="opened">The new document (already prepared: media base folder, notation, zoom, clean state, empty history).</param>
    /// <param name="replaceTarget">The tab to replace (null: open as a new tab).</param>
    /// <param name="replaceAll">Replace every tab (refused, opening beside, while any tab plays).</param>
    /// <param name="saveInProgress">A save is running in this window: the song opens beside instead of replacing.</param>
    /// <param name="askDiscard">What to do about the target's unsaved changes (only asked when it has any).</param>
    /// <param name="beforeReplaceAll">Runs before the tabs are replaced (the playback-on-tab-switch policy needs the old tabs still open).</param>
    /// <param name="beforeSaveFirst">Runs before the new song is inserted beside a target that will be saved (the window copies its lyrics box into the target).</param>
    public static PlacementResult Place(DocumentManager documents, DocumentSession opened, DocumentSession? replaceTarget, bool replaceAll, bool saveInProgress,
        Func<DocumentSession, DiscardAnswer> askDiscard, Action<DocumentSession>? beforeReplaceAll = null, Action<DocumentSession>? beforeSaveFirst = null)
    {
        if (replaceAll && documents.Documents.Any(d => d.Playback.Engine.IsPlaying))
        {
            replaceTarget = null;   // a sounding tab is never replaced
            replaceAll = false;
        }
        if (replaceAll)
        {
            var previous = documents.Documents.ToArray();
            beforeReplaceAll?.Invoke(opened);
            documents.ReplaceAll(opened);
            foreach (var closed in previous) closed.DisposePlayback();
            return new PlacementResult(PlacementKind.ReplacedAll, opened, null);
        }
        // A target that left this window (a tab moved elsewhere while the song was being read) is not replaced from here.
        var index = replaceTarget is null ? -1 : documents.IndexOf(replaceTarget);
        if (index >= 0 && !saveInProgress)
        {
            var target = documents.Documents[index];
            switch (askDiscard(target))
            {
                case DiscardAnswer.Keep:
                    return new PlacementResult(PlacementKind.Kept, null, target);
                case DiscardAnswer.SaveFirst:
                    // No waiting here (no nested dispatcher frame): the new song opens beside the tab; the caller starts the tab's save and
                    // closes the tab once its save succeeded (it stays open, with its changes, when it did not).
                    beforeSaveFirst?.Invoke(target);
                    documents.Insert(opened, index + 1);
                    return new PlacementResult(PlacementKind.OpenedBesideToSave, opened, target);
            }
            documents.Replace(index, opened);
            target.DisposePlayback();
            return new PlacementResult(PlacementKind.Replaced, opened, target);
        }
        documents.Add(opened);
        return new PlacementResult(PlacementKind.Added, opened, null);
    }
}
