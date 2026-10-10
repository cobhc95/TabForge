using System.IO;

namespace TabForge.Services;

// Owns: the media approval scope and state of one document.
// Does not own: path classification and the settings file.
// Tests: TestClips, TestDocumentContext.
/// <summary>
/// The context every media operation (waveform reading, clip playback sync, drop measuring, the linked-audio review) is given explicitly:
/// whose media it is, where relative paths resolve, which approvals count, and an operation revision that tells work started earlier
/// whether it is still wanted. One instance belongs to one open document (<c>DocumentSession.Media</c>); nothing looks it up from a
/// window, the focused tab or a global.
///
/// <b>Identity.</b> <see cref="SessionId"/> is made at run time and never stored, so nothing inside a song file can name it.
/// <b>Media base directory</b> (<see cref="BaseDirectory"/>) is where a relative clip path resolves: the folder of the saved file, else the
/// folder a Guitar Pro song was imported from; it is not the writable save path.
/// <b>Approval scope</b> (<see cref="ScopeKey"/>): a saved song is scoped to its canonical path (persisted approvals, as before); an unsaved
/// song is scoped to its own session (kept in memory only, never written under an empty or shared key). Legacy approvals stored with an
/// empty path match nothing.
/// <b>Save As.</b> Approvals of a saved song stay with its old path and are not copied to the new one (the new path is evaluated again).
/// The one transfer: the approvals of an unsaved song's own session become approvals of the path it is first saved to.
/// <b>Revision</b> (<see cref="Revision"/>) changes when the path changes (Save As) or the document closes; work that captured an earlier
/// revision is dropped when it completes.
/// </summary>
public sealed class MediaContext
{
    /// <summary>For code with no document (headless probes): no base directory, no approvals can be given, relative paths are refused.</summary>
    public static MediaContext Anonymous { get; } = new(isAnonymous: true);

    private readonly Func<AudioSettings?> _settings;
    private readonly Action? _persist;
    private readonly object _gate = new();
    private readonly HashSet<string> _sessionFolders = new(StringComparer.OrdinalIgnoreCase);
    private string? _savedPath;
    private string? _sourceDirectory;
    private int _revision;
    private bool _closed;

    /// <param name="settings">The approvals store; default the app-wide settings (<see cref="AppSettingsStore.Shared"/>).</param>
    /// <param name="persist">Called after this context changes persisted approvals itself (the first save of an unsaved song); default marks the shared store changed.</param>
    public MediaContext(Func<AudioSettings?>? settings = null, Action? persist = null)
    {
        _settings = settings ?? (() => AppSettingsStore.Shared.Settings.Audio);
        _persist = persist ?? (settings is null ? () => AppSettingsStore.Shared.MarkChanged(this) : null);
    }

    private MediaContext(bool isAnonymous)
    {
        IsAnonymous = isAnonymous;
        _settings = () => null;
    }

    public bool IsAnonymous { get; }

    /// <summary>Made at run time, never stored in a file: an id inside an untrusted song cannot confer approval.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Canonical path of the file this song was saved to or opened from; null while unsaved.</summary>
    public string? SavedPath { get { lock (_gate) return _savedPath; } }

    public bool IsSaved => SavedPath is not null;

    /// <summary>The folder a Guitar Pro (or other non-native) song was imported from; used while the song has no saved path.</summary>
    public string? SourceDirectory { get { lock (_gate) return _sourceDirectory; } }

    /// <summary>The approval scope: the canonical saved path, or <c>session:&lt;id&gt;</c> for an unsaved song.</summary>
    public string ScopeKey { get { lock (_gate) return _savedPath ?? (IsAnonymous ? "anonymous" : "session:" + SessionId); } }

    /// <summary>Where a relative media path resolves (null: nowhere, relative paths are refused).</summary>
    public string? BaseDirectory { get { lock (_gate) return MediaAccess.FolderOf(_savedPath) ?? _sourceDirectory; } }

    public int Revision => Volatile.Read(ref _revision);
    public bool IsClosed { get { lock (_gate) return _closed; } }

    /// <summary>The approvals store (null when none is available: nothing is approved).</summary>
    public AudioSettings? Settings { get { try { return _settings(); } catch (Exception) { return null; } } } // Not logged: approvals getter: nothing approved is the safe default

    /// <summary>True while work that captured <paramref name="revision"/> is still wanted (the song is open and its path did not change).</summary>
    public bool IsCurrent(int revision) { lock (_gate) return !_closed && _revision == revision; }

    /// <summary>The song was saved to (or opened from) <paramref name="path"/>; null: it is unsaved. Changing the path starts a new revision.</summary>
    public void SetSavedPath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : MediaPathPolicy.Normalize(path);
        List<string>? transfer = null;
        lock (_gate)
        {
            if (string.Equals(_savedPath, normalized, StringComparison.OrdinalIgnoreCase)) return;
            var wasUnsaved = _savedPath is null;
            _savedPath = normalized;
            Interlocked.Increment(ref _revision);
            // The first save of an unsaved song: its own session's approvals become approvals of that path. Never from one saved path to another.
            if (wasUnsaved && normalized is not null && _sessionFolders.Count > 0) transfer = _sessionFolders.ToList();
            _sessionFolders.Clear();
        }
        if (transfer is not null && Settings?.ApprovedMedia is { } list)
        {
            var key = normalized!;
            lock (list)
                foreach (var folder in transfer)
                    if (list.Count < 512 && !list.Any(a => string.Equals(a.Project, key, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Folder, folder, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new MediaApproval { Project = key, Folder = folder });
            _persist?.Invoke();
        }
        MediaAccess.RaiseChanged();   // what waited for or relied on the old scope is evaluated again
    }

    public void SetSourceDirectory(string? directory)
    {
        lock (_gate)
        {
            var normalized = string.IsNullOrWhiteSpace(directory) ? null : MediaPathPolicy.Normalize(directory);
            if (string.Equals(_sourceDirectory, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _sourceDirectory = normalized;
            Interlocked.Increment(ref _revision);
        }
    }

    /// <summary>The document closed or was replaced: queued work for it is dropped and its session approvals end.</summary>
    public void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            Interlocked.Increment(ref _revision);
            _sessionFolders.Clear();
        }
    }

    // ---- session approvals (unsaved songs): memory only ----

    internal bool IsSessionFolderApproved(string fullPath)
    {
        lock (_gate)
        {
            if (_closed || _savedPath is not null) return false;
            return _sessionFolders.Any(folder => MediaPathPolicy.IsInside(fullPath, folder));
        }
    }

    internal bool AddSessionFolder(string folder)
    {
        lock (_gate)
        {
            if (_closed || _savedPath is not null || IsAnonymous || _sessionFolders.Count >= 512) return false;
            return _sessionFolders.Add(folder);
        }
    }

    internal bool RemoveSessionFolder(string folder) { lock (_gate) return _sessionFolders.Remove(folder); }

    /// <summary>The folders this unsaved song's session was allowed to read (for the approvals window).</summary>
    public IReadOnlyList<string> SessionFolders() { lock (_gate) return _sessionFolders.ToList(); }
}
