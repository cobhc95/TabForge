namespace TabForge.Documents;

/// <summary>What the person answered for one document with unsaved changes.</summary>
public enum DiscardAnswer { Close, Keep, SaveFirst }

/// <summary>The outcome of asking about every unsaved document of a closing window.</summary>
/// <param name="Cancel">The person cancelled at some point: every document and the window stay exactly as they were.</param>
/// <param name="Save">Documents to save before the window closes.</param>
/// <param name="Discard">Documents the person chose not to save (the close that follows the saves does not ask about them again).</param>
public sealed record ClosePlan(bool Cancel, IReadOnlyList<DocumentSession> Save, IReadOnlyList<DocumentSession> Discard)
{
    public static readonly ClosePlan Cancelled = new(true, Array.Empty<DocumentSession>(), Array.Empty<DocumentSession>());
}

// Owns: closing a window that holds several songs: which songs need a question, how answers are collected and the save
//     sequence that follows.
// Does not own: the save mechanics (DocumentSaveFlow) and the dialogs.
// Tests: TestDocumentOperations, TestArchitectureLayering.
/// <summary>
/// Closing a window that holds several songs: which of them need a question, how the answers are collected, and the save sequence that follows. The
/// document list, the already-answered set and the questions are arguments; there is no window and no "current" document in here.
/// </summary>
public static class DocumentCloseFlow
{
    /// <summary>
    /// Asks <paramref name="ask"/> once for every document with unsaved changes (except those already answered "don't save"), in tab order, and
    /// collects all the answers before anything happens: a Cancel at any point is a plan that does nothing. <paramref name="ask"/> gets the document
    /// and how many documents are being asked about (so a lone document can be asked in the short form).
    /// </summary>
    public static ClosePlan Plan(IEnumerable<DocumentSession> documents, ISet<DocumentSession> alreadyDiscarded, Func<DocumentSession, int, DiscardAnswer?> ask)
    {
        var dirty = documents.Where(d => d.HasUnsavedChanges && !alreadyDiscarded.Contains(d)).ToList();
        var save = new List<DocumentSession>();
        var discard = new List<DocumentSession>();
        foreach (var document in dirty)
        {
            switch (ask(document, dirty.Count))
            {
                case null or DiscardAnswer.Keep: return ClosePlan.Cancelled;
                case DiscardAnswer.SaveFirst: save.Add(document); break;
                default: discard.Add(document); break;
            }
        }
        return new ClosePlan(false, save, discard);
    }

    /// <summary>
    /// Saves the documents in order. Stops at the first one that did not save (or is still unsaved afterwards): the window then stays open and
    /// every answer is asked again at the next close. True when all of them are saved and clean.
    /// </summary>
    public static async Task<bool> SaveAllAsync(IEnumerable<DocumentSession> documents, Func<DocumentSession, Task<bool>> save)
    {
        foreach (var document in documents)
            if (!await save(document) || document.HasUnsavedChanges) return false;
        return true;
    }
}
