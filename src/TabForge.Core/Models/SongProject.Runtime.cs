using System.Linq;
using System.Text.Json.Serialization;

namespace TabForge.Models;

// Owns: a SongProject's runtime dirty state, timeline revision, edit batching and extent-measure cache.
// Does not own: serialized song fields, notation or timeline compilation.
// Tests: TestSongExtent and timeline invalidation tests.
// Cache slot: holds the SongExtentMeasureCache; its key, ceiling and thread are stated in that file.
public sealed partial class SongProject
{
    private bool _isDirty;
    [JsonIgnore]
    public bool IsDirty
    {
        get => _isDirty;
        set { if (value) { _contentRevision++; Interlocked.Increment(ref _editVersion); } if (_isDirty == value) return; _isDirty = value; DisplayStateChanged?.Invoke(this, EventArgs.Empty); }
    }

    private int _contentRevision;
    private int _editVersion;
    /// <summary>
    /// Bumped on every edit pipeline step that can change saved content (each <see cref="IsDirty"/> mark and each timeline
    /// invalidation). Keys the document's cached unsaved-changes hash so repeated <c>HasUnsavedChanges</c> checks reuse it
    /// instead of re-serializing the song. Not saved; read on the UI thread.
    /// </summary>
    [JsonIgnore]
    public int EditVersion => Volatile.Read(ref _editVersion);

    /// <summary>Records a content change made outside <see cref="IsDirty"/> / <see cref="MarkTimelineChanged"/> (additive; existing callers unchanged).</summary>
    public void BumpEditVersion() => Interlocked.Increment(ref _editVersion);
    /// <summary>
    /// Changes with every edit (each one marks the song changed, also when it already was) and with every timeline change. Views that cache
    /// what bars contain (the timeline's bar cells) and the playback song copy (PlaybackSeekRebuild.TakeSnapshot) key on it, so no edit path can
    /// leave them stale. Invariant: every model mutation goes through DocumentEdits, or sets <see cref="IsDirty"/>, or calls
    /// <see cref="MarkTimelineChanged"/>; a field written without one of these is invisible to every cache keyed here. Not saved; read on the UI thread.
    /// </summary>
    [JsonIgnore]
    public int ContentRevision => _contentRevision + TimelineRevision;

    private int _timelineRevision;
    /// <summary>
    /// Changes whenever something that affects timing may have changed (tempos and ramps, time signatures, repeats, endings,
    /// directions, bars inserted/deleted/moved, undo/redo). Timing caches (TimelineSongClock) key on it. Not saved; read from any thread.
    /// </summary>
    [JsonIgnore]
    public int TimelineRevision => Volatile.Read(ref _timelineRevision);

    /// <summary>The trailing bars the song grew by to hold a clip, so a later edit can take them back (ClipAutoBars). Memory only: a reload makes them normal bars.</summary>
    [JsonIgnore]
    public Services.AutoAddedBars AutoBars { get; set; }

    private SongExtentMeasureCache? _songExtentMeasureCache;
    [JsonIgnore]
    internal SongExtentMeasureCache SongExtentMeasures => _songExtentMeasureCache ??= new SongExtentMeasureCache();

    /// <summary>Invalidates every timing cache built for this project. Called by each edit ending (editor, window, arrangement) and by undo/redo.</summary>
    public void MarkTimelineChanged()
    {
        if (_timelineBatchDepth > 0) { _timelineBatchMarked = true; return; }
        Interlocked.Increment(ref _timelineRevision);
        Interlocked.Increment(ref _editVersion);
        TimelineMarked?.Invoke(this);
    }

    /// <summary>
    /// Raised after the timeline revision changed (outside a batch), on the thread that marked it. Edits mark on the thread that owns the song,
    /// so a subscriber can take an immutable copy of the song here and hand that copy to other threads instead of letting them read the live song.
    /// </summary>
    public event Action<SongProject>? TimelineMarked;

    private int _timelineBatchDepth;
    private bool _timelineBatchMarked;

    /// <summary>
    /// One logical edit, however many model steps mark the timeline inside it (a bar grid change marks itself, the edit marks too): the marks made
    /// until the returned scope is disposed count as one, applied when the outermost scope ends. A mid-edit state is therefore never published under a
    /// new revision. UI thread only (edits are).
    /// </summary>
    public IDisposable BeginTimelineBatch()
    {
        _timelineBatchDepth++;
        return new TimelineBatch(this);
    }

    private sealed class TimelineBatch : IDisposable
    {
        private SongProject? _project;
        public TimelineBatch(SongProject project) => _project = project;

        public void Dispose()
        {
            var project = Interlocked.Exchange(ref _project, null);
            if (project is null || --project._timelineBatchDepth > 0) return;
            if (!project._timelineBatchMarked) return;
            project._timelineBatchMarked = false;
            Interlocked.Increment(ref project._timelineRevision);
            Interlocked.Increment(ref project._editVersion);
            project.TimelineMarked?.Invoke(project);
        }
    }

    /// <summary>A shallow copy without the tracks added from startup templates (what is written to a .tforge file).</summary>
    public SongProject WithoutStartupTracks()
    {
        var copy = (SongProject)MemberwiseClone();
        copy.DisplayStateChanged = null;
        copy.TimelineMarked = null;
        copy._songExtentMeasureCache = null;
        copy.Tracks = Tracks.Where(t => t.StartupTemplateId is null).ToList();
        return copy;
    }
}
