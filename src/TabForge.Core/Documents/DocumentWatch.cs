using TabForge.Models;

namespace TabForge.Documents;

// Owns: one view's subscription to one document's events (timeline marks, song replaced, undo/redo restored), moved cleanly when the view binds another document.
// Does not own: the events themselves (SongDocument raises them) or what a view does in response.
// Tests: DocumentWatchTests.
/// <summary>
/// A view's listener on a document. Subscribes to the document, not to its current song, so it keeps working after an undo replaces the song.
/// </summary>
public sealed class DocumentWatch : IDisposable
{
    private IEditableDocument? _document;

    /// <summary>Called when the bound document's current song marks its timeline.</summary>
    public Action<SongProject>? Marked { get; set; }
    /// <summary>Called once after the bound document's song was replaced (old, new).</summary>
    public Action<SongProject, SongProject>? ProjectChanged { get; set; }
    /// <summary>Called once after an undo or redo finished on the bound document.</summary>
    public Action? Restored { get; set; }

    /// <summary>Watches <paramref name="document"/> (null: nothing) and stops watching the one watched before; binding the document already watched changes nothing.</summary>
    public void Bind(IEditableDocument? document)
    {
        if (ReferenceEquals(_document, document)) return;
        Dispose();
        _document = document;
        if (document is null) return;
        document.TimelineMarked += OnMarked;
        document.ProjectChanged += OnProjectChanged;
        document.Restored += OnRestored;
    }

    /// <summary>Stops watching.</summary>
    public void Dispose()
    {
        if (_document is not { } document) return;
        document.TimelineMarked -= OnMarked;
        document.ProjectChanged -= OnProjectChanged;
        document.Restored -= OnRestored;
        _document = null;
    }

    private void OnMarked(SongProject project) => Marked?.Invoke(project);
    private void OnProjectChanged(SongProject old, SongProject current) => ProjectChanged?.Invoke(old, current);
    private void OnRestored() => Restored?.Invoke();
}
