using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Documents;

/// <summary>
/// One open song. Holds everything that is per-document so several songs can be
/// open at once: the score, the file, undo history and the editor's own state.
/// </summary>
public sealed class DocumentSession
{
    private byte[]? _cleanContentHash;
    private ProjectState? _cleanState;

    // The routed output sends plug-in tracks to the audio engine (a plain engine here bypassed it: no MIDI reached plug-ins).
    public DocumentSession() => Playback = new DocumentPlaybackState();

    public DocumentSession(TabForge.Playback.PlaybackEngine playbackEngine)
    {
        Playback = new DocumentPlaybackState(playbackEngine);
    }

    public SongProject Project { get; set; } = TemplateFactory.Blank();
    public string? Path { get; set; }
    public UndoController Undo { get; } = new();
    public bool IsNew { get; set; } = true;

    // Editor state, restored when the user switches back to this tab.
    public int CursorBar { get; set; }
    public int CursorCell { get; set; }
    public int CursorString { get; set; }
    public int ActiveVoiceIndex { get; set; }
    public int TrackIndex { get; set; }
    public NotationMode Notation { get; set; } = NotationMode.TabAndStaff;
    public double ZoomFactor { get; set; } = 1.0;     // new documents open at 100%; 0 = fit width
    public bool ContinuousScoreView { get; set; } = true;
    /// <summary>Score as one line scrolling right instead of wrapped lines scrolling down.</summary>
    public bool HorizontalScoreView { get; set; }
    public bool DarkPaper { get; set; } = true;
    public int DurationDenominator { get; set; } = 4;
    public int DurationDots { get; set; }
    public bool DurationTriplet { get; set; }
    public int TupletNumerator { get; set; }
    public int TupletDenominator { get; set; }
    public DocumentPlaybackState Playback { get; }

    // Transport/practice state of this song, restored when its tab becomes active again.
    public bool LoopEnabled { get; set; }
    public int LoopStartBar { get; set; }
    public int LoopEndBar { get; set; } = 3;
    public int LoopStartCell { get; set; }
    public int LoopEndCell { get; set; } = -1;
    /// <summary>Bar ranges skipped during playback ("Skip area during playback").</summary>
    public List<(int Start, int End)> SkipRanges { get; } = new();
    /// <summary>
    /// Plug-ins (full paths) the user chose to disable after a very slow load: skipped for this song while it stays open.
    /// Unlike quarantine this is not saved and does not affect other songs.
    /// </summary>
    public HashSet<string> SkippedPlugins { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Semitone shift applied per string (high to low) by the global tuning tool, relative to how the song was opened.</summary>
    public int[] TuningShift { get; } = new int[6];

    /// <summary>
    /// True only when persisted project content differs from its clean baseline. The legacy dirty
    /// flag remains a fast hint that a write path ran; comparing content filters no-op UI commits and
    /// edits that were subsequently undone back to the saved state.
    /// </summary>
    public bool IsDirty => Project.IsDirty && HasUnsavedChanges;

    /// <summary>Content-accurate close-prompt check, including changes missed by a legacy dirty flag.</summary>
    public bool HasUnsavedChanges => _cleanContentHash is null
        ? Project.IsDirty
        : !CryptographicOperations.FixedTimeEquals(_cleanContentHash, ContentHash());

    /// <summary>Marks the current project content as saved/loaded (<paramref name="contentHash"/>, when given, is that content's hash).</summary>
    public void MarkClean(byte[]? contentHash = null)
    {
        Project.IsDirty = false;
        _cleanContentHash = contentHash?.ToArray() ?? ContentHash();
        _cleanState = Undo.Snapshot(Project).State;
    }

    /// <summary>
    /// The file was written but some content could not be captured (plug-in states): keep the song marked unsaved so the user is asked
    /// again, even though the model matches what was written.
    /// </summary>
    public void MarkIncomplete()
    {
        Project.IsDirty = true;
        _cleanContentHash = null;
        _cleanState = null;
    }

    /// <summary>
    /// Undo/redo: true when the restored <paramref name="snapshot"/> is exactly the saved content (same answer as
    /// <see cref="HasUnsavedChanges"/> being false, without hashing the whole song).
    /// </summary>
    public bool IsCleanContent(UndoSnapshot snapshot) =>
        _cleanContentHash is not null && (_cleanState is { } clean ? clean.ContentEquals(snapshot.State) : !HasUnsavedChanges);

    // Streams the compact in-memory serialization straight into SHA-256 (no multi-MB JSON string per check).
    private byte[] ContentHash() => ProjectService.ContentHash(Project);

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Path)) return System.IO.Path.GetFileName(Path)!;
            return string.IsNullOrWhiteSpace(Project.Title) ? "Untitled" : Project.Title;
        }
    }

    public string Tooltip => string.IsNullOrWhiteSpace(Path) ? $"{DisplayName} (unsaved)" : Path!;

    public static DocumentSession FromProject(SongProject project, string? path) =>
        new() { Project = project, Path = path, IsNew = path is null };

    public static DocumentSession Blank()
    {
        var p = TemplateFactory.Blank();
        var session = new DocumentSession { Project = p, IsNew = true };
        session.MarkClean();
        return session;
    }

    /// <summary>The document is gone for good (tab closed, replaced or its window closed): playback stops and its engine chains are unloaded now, not parked.</summary>
    public void DisposePlayback()
    {
        Playback.Dispose();
        if (Playback.Routing is not null) Audio.AudioEngineClient.Instance.ReleaseOwner(this);
    }
}

/// <summary>Owns the open documents and which one is active.</summary>
public sealed class DocumentManager
{
    private readonly List<DocumentSession> _documents = new();
    private int _activeIndex = -1;

    public event EventHandler? Changed;
    public event EventHandler? ActiveChanged;

    public IReadOnlyList<DocumentSession> Documents => _documents;

    public int ActiveIndex => _activeIndex;

    public DocumentSession Active
    {
        get
        {
            if (_documents.Count == 0) _documents.Add(DocumentSession.Blank());
            if (_activeIndex < 0 || _activeIndex >= _documents.Count) _activeIndex = _documents.Count - 1;
            return _documents[_activeIndex];
        }
    }

    public DocumentSession Add(DocumentSession session, bool activate = true)
    {
        _documents.Add(session);
        if (activate) _activeIndex = _documents.Count - 1;
        RaiseChanged(activeMoved: activate);
        return session;
    }

    public DocumentSession AddNew() => Add(DocumentSession.Blank());

    public void Activate(int index)
    {
        if (index < 0 || index >= _documents.Count || index == _activeIndex) return;
        _activeIndex = index;
        RaiseChanged(activeMoved: true);
    }

    public void Activate(DocumentSession session)
    {
        var i = _documents.IndexOf(session);
        if (i >= 0) Activate(i);
    }

    public bool Close(int index)
    {
        if (index < 0 || index >= _documents.Count) return false;
        var wasActive = index == _activeIndex;
        _documents.RemoveAt(index);
        if (_documents.Count == 0) _documents.Add(DocumentSession.Blank());
        if (wasActive) _activeIndex = Math.Clamp(index, 0, _documents.Count - 1);
        else if (index < _activeIndex) _activeIndex--;
        RaiseChanged(activeMoved: true);
        return true;
    }

    public bool Move(int from, int to)
    {
        if (from < 0 || from >= _documents.Count) return false;
        to = Math.Clamp(to, 0, _documents.Count - 1);
        if (from == to) return false;
        var doc = _documents[from];
        _documents.RemoveAt(from);
        _documents.Insert(to, doc);
        if (_activeIndex == from) _activeIndex = to;
        else if (from < _activeIndex && to >= _activeIndex) _activeIndex--;
        else if (from > _activeIndex && to <= _activeIndex) _activeIndex++;
        RaiseChanged(activeMoved: true);
        return true;
    }

    public DocumentSession? FindByPath(string path) =>
        _documents.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Path) &&
                                       string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Removes a document and hands it back to the caller (used when a tab is dragged into another
    /// window or dropped out into a new one). Unlike <see cref="Close"/> this may leave the manager
    /// empty; the caller decides what happens next.
    /// </summary>
    public DocumentSession? Detach(int index)
    {
        if (index < 0 || index >= _documents.Count) return null;
        var doc = _documents[index];
        _documents.RemoveAt(index);
        if (_activeIndex >= _documents.Count) _activeIndex = _documents.Count - 1;
        RaiseChanged(activeMoved: true);
        return doc;
    }

    /// <summary>Inserts an already-built document (a tab dragged in from another window).</summary>
    public DocumentSession Insert(DocumentSession doc, int index)
    {
        index = Math.Clamp(index, 0, _documents.Count);
        _documents.Insert(index, doc);
        _activeIndex = index;
        RaiseChanged(activeMoved: true);
        return doc;
    }

    /// <summary>Replaces a tab while keeping its position and making the replacement active.</summary>
    public bool Replace(int index, DocumentSession session)
    {
        if (index < 0 || index >= _documents.Count) return false;
        _documents[index] = session;
        _activeIndex = index;
        RaiseChanged(activeMoved: true);
        return true;
    }

    /// <summary>Replaces the startup document set with one requested file.</summary>
    public void ReplaceAll(DocumentSession session)
    {
        _documents.Clear();
        _documents.Add(session);
        _activeIndex = 0;
        RaiseChanged(activeMoved: true);
    }

    /// <summary>Duplicates a document as an unsaved copy right after the original.</summary>
    public DocumentSession? Duplicate(int index)
    {
        if (index < 0 || index >= _documents.Count) return null;
        var src = _documents[index];
        var project = ProjectService.Restore(ProjectService.Snapshot(src.Project));
        project.Title = string.IsNullOrWhiteSpace(project.Title) ? "Untitled copy" : project.Title + " copy";
        project.IsDirty = true;
        var copy = DocumentSession.FromProject(project, null);
        copy.CursorBar = src.CursorBar;
        copy.CursorCell = src.CursorCell;
        copy.CursorString = src.CursorString;
        copy.TrackIndex = src.TrackIndex;
        copy.Notation = src.Notation;
        copy.ZoomFactor = src.ZoomFactor;
        copy.ContinuousScoreView = src.ContinuousScoreView;
        copy.DarkPaper = src.DarkPaper;
        copy.DurationDenominator = src.DurationDenominator;
        return Insert(copy, index + 1);
    }

    /// <summary>Index of a document, or -1.</summary>
    public int IndexOf(DocumentSession doc) => _documents.IndexOf(doc);

    private void RaiseChanged(bool activeMoved)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (activeMoved) ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
