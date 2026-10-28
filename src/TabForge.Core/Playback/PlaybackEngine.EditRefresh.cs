namespace TabForge.Playback;

// Owns: the engine's side of re-scheduling after a score edit: which song copy is playing and the marker that keeps the edit's resume free of a count-in.
// Does not own: the decision and the re-schedule (PlaybackEditRefresh, which holds the live song), what counts as a sound change (PlaybackEditDiff) or the seek itself (PlaybackEngine.Transport.cs).
// Tests: SelfTestPlaybackEditRefresh.

public sealed partial class PlaybackEngine
{
    private long _editSeekId;   // the seek request an edit's refresh made; its resume plays on without a count-in (read by the scheduler)

    /// <summary>
    /// Owner thread. The song copy the engine plays, or the copy a start still compiling reads (<paramref name="startPending"/>); null when stopped
    /// (the next start copies the edited song).
    /// </summary>
    internal SongSnapshot? SnapshotForEdit(out bool startPending)
    {
        var pending = Volatile.Read(ref _pendingStart);
        startPending = pending is not null;
        if (pending is not null) return pending.Snapshot;
        return _running && _options is not null ? Volatile.Read(ref _song)?.Snapshot : null;
    }

    /// <summary>Owner thread. Marks the seek the caller makes next as an edit's: the owner thread numbers seeks, so it is the next id.</summary>
    internal void MarkEditSeek() => Volatile.Write(ref _editSeekId, Volatile.Read(ref _seekRequestId) + 1);
}
