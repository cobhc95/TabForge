using TabForge.Audio.Contracts;
using TabForge.Models;

namespace TabForge.Audio;

// Owns: what the client does when a command did not reach the engine, or the engine reopened its device: forget everything remembered as "already sent", then send it again.
// Does not own: the send path itself (AudioEngineClient.cs) or the per-kind sync keys (AudioEngineClient.Sync.cs).
// Tests: EngineResyncAfterReconnectTests, SplitMovePlaybackSyncTests.
public sealed partial class AudioEngineClient
{
    /// <summary>UI thread: the engine was brought back in line with the song after a dropped command or a reopened device (the window syncs the shown song again).</summary>
    public event Action? Resynced;

    // Set by any thread when a command could not be delivered (the pipe was down); cleared by the next sync once the link is up, which then forgets every "sent" key.
    private volatile bool _resyncNeeded;
    // Test seam: a pipe-less client whose commands count as delivered (true) or dropped (false).
    private volatile bool _fakeLinkUp;
    // True once the running engine has said Ready: a later Ready means its device was reopened (driver or device change, lost device).
    private volatile bool _readySeen;
    private long _dropLoggedAt = long.MinValue / 2, _resyncLoggedAt = long.MinValue / 2;
    private const long LogEveryMs = 5000;

    /// <summary>Any thread: <paramref name="command"/> was not delivered. The "sent" keys can no longer be trusted, so the next sync with the link up sends everything again. Always returns false.</summary>
    private bool NoteDropped(EngineCommand command, string why)
    {
        if (_stopping || !IsRunning) return false;   // a stopped engine needs nothing; its next start begins empty
        _resyncNeeded = true;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _dropLoggedAt) < LogEveryMs) return false;
        Interlocked.Exchange(ref _dropLoggedAt, now);
        Services.Trace.Error(Services.Trace.Engine, $"engine: command {command} dropped ({why}); everything will be sent again once the engine is reachable");
        return false;
    }

    /// <summary>True while a command would reach the engine (connected, or still queued for the connect).</summary>
    private bool LinkUp
    {
        get
        {
            lock (_sendGate) { if (_queued is not null) return true; }
            var pipe = _pipe;
            return pipe is null ? _fakeLinkUp : pipe.IsConnected;
        }
    }

    /// <summary>
    /// UI thread, at the start of every sync entry: when commands were dropped and the engine can be reached again, every remembered
    /// "sent" key is forgotten, so the sync that follows sends the clips, mixer, chains, routing and graph again. True when it did.
    /// </summary>
    private bool ResyncIfDropped()
    {
        if (!_resyncNeeded || !IsRunning || !LinkUp) return false;
        _resyncNeeded = false;
        InvalidateSentState(includeChains: true);
        LogResync("reconnect");
        return true;
    }

    /// <summary>
    /// UI thread: forgets what was sent. <paramref name="includeChains"/> false keeps the loaded chains (their plug-ins must not reload): the
    /// clips, mix, arm, routing, wiring, bypass, gain, synth and graph keys, and the live settings sent once per engine, are cleared.
    /// </summary>
    private void InvalidateSentState(bool includeChains)
    {
        _sentAudio.Clear(); _sentTrackMix.Clear();
        _sentRoutes.Clear(); _sentProcessors.Clear(); _sentWiring.Clear(); _sentBypass.Clear(); _sentGain.Clear(); _sentSynth.Clear();
        _sentGraph = "";
        _tunerWindowSent = default; _bankSent = default; _limiterSent = default; _pathOffsetSentTo = null;
        lock (_posCoalesceGate) _lastPosSent.Clear();
        if (!includeChains) return;
        _sentChains.Clear(); _sentChainIds.Clear();
        _config = null;   // the device is configured again too
    }

    private void LogResync(string why)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _resyncLoggedAt) < LogEveryMs) return;
        Interlocked.Exchange(ref _resyncLoggedAt, now);
        var clips = _audioContext.Values.Sum(v => v.Track.AudioClips.Count(c => !c.IsMidi));
        Services.Trace.Error(Services.Trace.Engine, $"engine resync after {why}: {clips} clips, {_slots.Count} tracks");
    }

    /// <summary>UI thread: sends the audio of every live slot again (parked ones stay silent) and tells the window to sync the rest.</summary>
    private void ResyncNow(bool positions)
    {
        if (!IsRunning || Rendering) return;
        foreach (var slot in _parkedSince.Keys.Where(s => _slots.ContainsValue(s)).ToList()) ParkAudio(slot);
        RefreshClips();
        if (positions) ResendPositions();
        Resynced?.Invoke();
    }

    /// <summary>Reader thread, on Ready: the first Ready of an engine is the normal start; a later one means the device was reopened, so the audio state is sent again.</summary>
    private void NoteReady()
    {
        var again = _readySeen;
        _readySeen = true;
        if (!again || _ui is null) return;   // no UI context yet (a headless probe): nothing is synced, and the UI-owned state must not be touched from this thread
        RaiseOnUi(() =>
        {
            if (!IsRunning) return;
            InvalidateSentState(includeChains: false);
            LogResync("device reopened");
            ResyncNow(positions: false);
        });
    }

    /// <summary>Test hook: the link to a pipe-less client goes down (commands are dropped) or comes back (what is missing is sent again at once, or with the next sync when <paramref name="resyncAtOnce"/> is false).</summary>
    internal void SetFakeLinkForTest(bool up, bool resyncAtOnce = true)
    {
        _fakeLinkUp = up;
        if (up && resyncAtOnce && ResyncIfDropped()) ResyncNow(positions: true);
    }

    /// <summary>Test hook: the level the engine is known to hold for a slot (null: not sent, or not delivered).</summary>
    internal (int Volume, int Pan)? TrackMixSentForTest(int slot) => _sentTrackMix.TryGetValue(slot, out var held) ? held : null;

    /// <summary>Test hook: the UI context for engine events of a client with no sync yet.</summary>
    internal void SetUiForTest(SynchronizationContext ui) => _ui = ui;

    /// <summary>Test hook: the running engine says Ready (the second one means a reopened device).</summary>
    internal void RaiseReadyForTest() => NoteReady();
}
