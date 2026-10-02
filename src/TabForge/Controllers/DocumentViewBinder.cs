using TabForge.Documents;

namespace TabForge.Controllers;

/// <summary>The surface a document is shown on: the score view with its track list, and the tempo and lyrics boxes above it.</summary>
internal interface IDocumentViewHost
{
    string TempoText { get; }
    string LyricsText { get; }
    /// <summary>Stores what the surface shows (cursor, voice, selected track, view mode, entry state) into the document.</summary>
    void Read(DocumentSession doc);
    /// <summary>Shows the document: its song, stored view state and the boxes.</summary>
    void Show(DocumentSession doc, bool darkPaper);
}

// Owns: binding one window's view surface to its documents: capturing view state on hide, showing a document, committing the
//     tempo and lyrics text boxes.
// Does not own: the document model and the controls' own layout.
// Tests: TestDocumentContext, TestWindowLifetime.
/// <summary>
/// Binds one window's view surface to its documents. <see cref="Capture"/> stores the view state of the document on show into that document,
/// <see cref="Show"/> puts a document on the surface, and the commit methods are the one place where the tempo and lyrics typed in the boxes become
/// edits of a song. A tab switch captures the outgoing document first, so what was typed for a song is committed to that song (never to the one shown next).
/// </summary>
internal sealed class DocumentViewBinder
{
    /// <summary>
    /// What a tab switch does with a tempo or lyrics that was typed and not yet confirmed: true commits it to the song it was typed for as one undo
    /// step; false drops it. The only place this is decided.
    /// </summary>
    internal const bool CommitTypedBoxesOnSwitch = true;

    private readonly IDocumentViewHost _host;

    public DocumentViewBinder(IDocumentViewHost host) => _host = host;

    /// <summary>The document whose view the surface shows (null before the first document is shown).</summary>
    public DocumentSession? Shown => _shown is { } weak && weak.TryGetTarget(out var doc) ? doc : null;
    private WeakReference<DocumentSession>? _shown;   // weak: a window that has closed must not keep its last document alive

    /// <summary>Stores the surface's view state into the document it shows. <paramref name="leaving"/>: the surface is about to show another document, so the boxes are settled too.</summary>
    public void Capture(bool leaving)
    {
        if (Shown is not { } doc) return;
        _host.Read(doc);
        if (leaving && CommitTypedBoxesOnSwitch) { CommitTempo(doc, _host.TempoText); CommitLyrics(doc, _host.LyricsText); }
    }

    /// <summary>Puts the document on the surface.</summary>
    public void Show(DocumentSession doc, bool darkPaper)
    {
        _shown = new WeakReference<DocumentSession>(doc);
        _host.Show(doc, darkPaper);
    }

    /// <summary>The tempo a typed text means: a whole number clamped to 20-400, or <paramref name="current"/> when it is not a number.</summary>
    internal static int ResolveTempoText(string? text, int current) =>
        Math.Clamp(int.TryParse(text?.Trim(), out var bpm) ? bpm : current, 20, 400);

    /// <summary>Makes the typed tempo the song's tempo (one undo step, one timeline invalidation); a song that is playing is recompiled from its position. False when nothing changed.</summary>
    public static bool CommitTempo(DocumentSession doc, string? typed)
    {
        var bpm = ResolveTempoText(typed, doc.Project.Tempo);
        if (bpm == doc.Project.Tempo) return false;
        DocumentEdits.Run(doc, p => { p.Tempo = bpm; return true; });
        if (doc.Playback.Engine.IsPlaying) doc.Playback.Engine.Rebuild(doc.Project);   // the new tempo is heard at once, from the current position
        return true;
    }

    /// <summary>Makes the typed lyrics the song's lyrics (one undo step). False when nothing changed.</summary>
    public static bool CommitLyrics(DocumentSession doc, string? typed)
    {
        var text = typed ?? "";
        if ((doc.Project.Lyrics ?? "") == text) return false;
        DocumentEdits.Run(doc, p => { p.Lyrics = text; return true; });
        return true;
    }
}
