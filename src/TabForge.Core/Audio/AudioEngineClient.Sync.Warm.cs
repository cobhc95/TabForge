using TabForge.Audio.Contracts;

namespace TabForge.Audio;

// Owns: the warm lifecycle of the engine's chains (parking a slot's audio, the warm-idle timer and expiry, releasing a closed document's owner), split from AudioEngineClient.Sync.cs.
// Does not own: the graph sync itself (AudioEngineClient.Sync.cs).
// Tests: no dedicated test class.
public sealed partial class AudioEngineClient
{
    /// <summary>A parked slot is silent: level 0, no clips, not armed. Its chain (and any open editor) stays as it is.</summary>
    private void ParkAudio(int slot)
    {
        _sentAudio[slot] = "parked";   // the owner's next Sync of this track sends its real level, clips and arm again
        _sentTrackMix.Remove(slot);
        Send(EngineCommand.SetTrackMix, new SetTrackMixMessage(slot, 0, 64).Write);
        var ownerId = _slotOwners.TryGetValue(slot, out var parkedOwner) ? OwnerIdOf(parkedOwner) : 0;
        Send(EngineCommand.SetClips, new SetClipsMessage(slot, new List<ClipSpec>(), ownerId).Write);
        Send(EngineCommand.SetArm, new SetArmMessage(slot, false, 0, false).Write);
    }

    private void ScheduleWarmCheck()
    {
        DateTime? due = _idleSince is { } idle ? idle + WarmIdle : null;
        foreach (var since in _parkedSince.Values) if (due is null || since + WarmIdle < due) due = since + WarmIdle;
        if (due is null || !IsRunning) { _warmTimer?.Change(Timeout.Infinite, Timeout.Infinite); return; }
        var wait = due.Value - WarmClock();
        var ms = (long)Math.Clamp(wait.TotalMilliseconds + 50, 50, int.MaxValue - 1);
        _warmTimer ??= new Timer(_ => RaiseOnUi(ExpireWarm), null, Timeout.Infinite, Timeout.Infinite);
        _warmTimer.Change(ms, Timeout.Infinite);
    }

    /// <summary>UI thread: drops chains parked longer than <see cref="WarmIdle"/>; stops the engine after that long without engine tracks.</summary>
    private void ExpireWarm()
    {
        if (!IsRunning) { ResetWarmState(); return; }
        var now = WarmClock();
        if (_idleSince is { } idle && now - idle >= WarmIdle) { Stop(); return; }
        foreach (var track in _slots.Where(kv => _parkedSince.TryGetValue(kv.Value, out var since) && now - since >= WarmIdle).Select(kv => kv.Key).ToList())
            RemoveSlot(track);
        ScheduleWarmCheck();
    }

    /// <summary>
    /// UI thread: a document closed. Its tracks' chains are removed now instead of being parked for <see cref="WarmIdle"/>: a closed tab
    /// never comes back (reopening the file builds new tracks), so a parked chain would only cost memory and CPU until it expired
    /// (one large sampler per closed song). Shared bus slots stay for the next owner's Sync; the document is no longer referenced.
    /// The removal is posted, not done inline: chain windows of the closed tracks close later in the same close (tab switch, owned
    /// windows of a closing window) and a startup track's window asks the engine for its plug-in states as it closes; that request
    /// must reach the engine before the chain is removed, or the startup template keeps stale states.
    /// </summary>
    public void ReleaseOwner(object owner) => RaiseOnUi(() => ReleaseOwnerNow(owner));

    private void ReleaseOwnerNow(object owner)
    {
        if (ReferenceEquals(CurrentOwner, owner)) CurrentOwner = null;
        ReleaseOwnerId(owner);
        foreach (var track in _slots.Where(kv => !kv.Key.IsBus && !LiveInput.Owns(kv.Key) && !Click.Owns(kv.Key) && _slotOwners.TryGetValue(kv.Value, out var o) && ReferenceEquals(o, owner)).Select(kv => kv.Key).ToList())
            RemoveSlot(track);
        foreach (var slot in _slotOwners.Where(kv => ReferenceEquals(kv.Value, owner)).Select(kv => kv.Key).ToList())
            _slotOwners.Remove(slot);
        // No track of any document left (the closed one was the last, e.g. its window closed and no other window syncs): its bus,
        // master and monitor chains go too and the warm period starts, as a Sync without engine tracks would do, instead of the
        // closed song's bus chains staying loaded and unowned until some later Sync.
        if (IsRunning && !_slots.Keys.Any(t => !t.IsBus && !Click.Owns(t)))
        {
            foreach (var bus in _slots.Keys.ToList()) RemoveSlot(bus);
            _idleSince ??= WarmClock();
        }
        ScheduleWarmCheck();
    }

    private void ResetWarmState()
    {
        _slotOwners.Clear();
        _parkedSince.Clear();
        _idleSince = null;
        _warmTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
