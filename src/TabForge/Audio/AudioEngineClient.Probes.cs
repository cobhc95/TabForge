using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

/// <summary>Diagnostics and probes: plug-in state collection, pitch measurement, the Windows path-offset probe and the hooks the self-tests use.</summary>
public sealed partial class AudioEngineClient : IDisposable
{
    internal void ExpireWarmForTest() => ExpireWarm();
    internal bool IsParkedForTest(TrackModel track) => _slots.TryGetValue(track, out var slot) && _parkedSince.ContainsKey(slot);
    internal int ChainLoadsSentForTest => Volatile.Read(ref _nextChainLoad);
    internal bool IdleForTest => _idleSince is not null;
    /// <summary>Self-test hook: a "running" engine with no process or pipe (every Send is dropped), for UI-side bookkeeping tests.</summary>
    internal void AttachFakeForTest() { _stopping = false; IsRunning = true; }

    /// <summary>Last Windows audio path measurement, for diagnostics.</summary>
    internal static string WindowsPathDetail { get; private set; } = "";
    /// <summary>
    /// ASIO + follow Windows volume: the engine also needs the part of the Windows path's attenuation the volume APIs do not
    /// report (see <see cref="WindowsPathOffset"/>). Measured once per run in the background, sent once per engine process.
    /// </summary>
    private void SyncWindowsPathOffset(EngineConfig config)
    {
        if (config.Driver != AudioDrivers.Asio || !config.FollowWindowsVolume || EngineProcessId is not int pid) return;
        if (_windowsPathOffset is float offset)
        {
            if (_pathOffsetSentTo == pid) return;
            _pathOffsetSentTo = pid;
            Send(EngineCommand.SetWindowsPathOffset, w => w.Write(offset));
            return;
        }
        if (_measuringPathOffset) return;
        _measuringPathOffset = true;
        var ui = _ui;
        Task.Run(() =>
        {
            var (measured, detail) = WindowsPathOffset.Measure();
            WindowsPathDetail = detail;
            void Done() { _windowsPathOffset = measured ?? 0f; _measuringPathOffset = false; if (_config is { } c) SyncWindowsPathOffset(c); }
            if (ui is null) Done(); else ui.Post(_ => Done(), null);
        });
    }

    /// <summary>Self-test taps: every command sent and every MIDI message written (null in the app).</summary>
    internal Action<EngineCommand>? SentForTest { get; set; }
    /// <summary>The last position sent for a song owner id (playing, song seconds), or null when none was sent.</summary>
    internal (bool Playing, double SongSec)? PositionSentForTest(int ownerId) => _songIds.Last(ownerId);
    /// <summary>Gives every transport id back (earlier tests may still hold documents that keep ids taken).</summary>
    internal void ResetSongIdsForTest() => _songIds.Clear();
    /// <summary>Self-test tap: the clip list sent for a slot (the owning document's approved clips).</summary>
    internal Action<int, IReadOnlyList<ClipSpec>>? ClipsSentForTest { get; set; }
    internal Action<TimedMidi>? WrittenForTest { get; set; }

    /// <summary>Asks the engine to measure an instrument's sounding pitch silently; returns the request id (0: not sent).</summary>
    public int MeasurePitch(TrackModel track, PluginSlot slot, IReadOnlyList<int> notes)
    {
        if (!TryAddress(track, slot, out var engineSlot, out var index)) return 0;
        var id = ++_pitchRequest;
        var channel = Math.Clamp(track.MidiChannel, 0, 15);
        Send(EngineCommand.MeasurePitch, w => { w.Write(engineSlot); w.Write(index); w.Write(id); w.Write(channel); w.Write(notes.Count); foreach (var n in notes) w.Write(n); });
        return id;
    }

    /// <summary>
    /// Blocking form of <see cref="CollectStatesAsync"/> for headless probes (never call it on the UI thread: it blocks up to
    /// <paramref name="timeoutMs"/>). Same core, run with blocking waits, so it completes synchronously and cannot deadlock.
    /// </summary>
    public StateCollection CollectStates(IEnumerable<TrackModel> tracks, int timeoutMs = 800) =>
        CollectStatesCore(tracks, timeoutMs, blocking: true).GetAwaiter().GetResult();

    /// <summary>
    /// Copies every plug-in's current state into the song (before saving). Waits at most <paramref name="timeoutMs"/>, awaiting the
    /// engine's replies instead of blocking the calling thread; the states are applied to the song on the caller's context (the UI
    /// thread) once they arrive or the shared deadline passes. The result lists every plug-in with what happened to its state; a save
    /// must not report full success unless it is complete.
    /// </summary>
    public Task<StateCollection> CollectStatesAsync(IEnumerable<TrackModel> tracks, int timeoutMs = 800) =>
        CollectStatesCore(tracks, timeoutMs, blocking: false);

    private async Task<StateCollection> CollectStatesCore(IEnumerable<TrackModel> tracks, int timeoutMs, bool blocking)
    {
        var collection = new StateCollection();
        if (!IsRunning) return collection;
        var pending = new List<(TrackModel Track, StateRequest Request)>();
        foreach (var track in tracks)
        {
            if (!_slots.TryGetValue(track, out var slot)) continue;
            var request = BeginStateRequest(slot);
            Send(EngineCommand.GetStates, w => { w.Write(slot); w.Write(request.Id); });
            pending.Add((track, request));
        }
        var deadline = Stopwatch.GetTimestamp() + timeoutMs * Stopwatch.Frequency / 1000;
        foreach (var (track, request) in pending)
        {
            var left = (int)Math.Max(0, (deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency);
            if (!request.Done.Task.IsCompleted)
            {
                if (blocking) request.Done.Task.Wait(left);   // no await on this path: the sync wrapper never resumes on a captured context
                else await Task.WhenAny(request.Done.Task, Task.Delay(left));
            }
            lock (_gate) _stateRequests.Remove(request.Id);
            collection.Results.AddRange(ApplyStates(track, request));
        }
        return collection;
    }

    internal StateRequest BeginStateRequest(int slot)
    {
        var request = new StateRequest { Id = Interlocked.Increment(ref _nextStateRequest), Slot = slot, PluginIds = _sentChainIds.GetValueOrDefault(slot) };
        lock (_gate) _stateRequests[request.Id] = request;
        return request;
    }

    /// <summary>Reader thread: one <see cref="EngineEvent.PluginState"/> frame (bounded read, routed by request id; late or unknown replies are dropped).</summary>
    internal void OnPluginState(BinaryReader r)
    {
        var slot = r.ReadInt32(); var requestId = r.ReadInt32(); var index = r.ReadInt32();
        var count = Math.Clamp(r.ReadInt32(), 0, InputLimits.MaxPluginsPerTrack);
        var status = (PluginStateStatus)r.ReadByte();
        var state = r.ReadBoundedString(InputLimits.MaxPluginStateChars);
        StateRequest? request;
        lock (_gate) if (!_stateRequests.TryGetValue(requestId, out request) || request.Slot != slot) return;
        lock (request)
        {
            request.Expected = count;
            if (index >= 0 && index < count && request.Replies.TryAdd(index, (status, state))) request.Received++;
            if (request.Received >= request.Expected) request.Done.TrySetResult(true);
        }
    }

    /// <summary>
    /// Writes the captured states into the track's plug-ins and says, per plug-in, what happened. A plug-in without a reply timed out,
    /// a failed or oversized one keeps its previous stored state and is reported; "no state" (not loaded, or none exposed) is not a failure.
    /// </summary>
    internal static List<PluginStateResult> ApplyStates(TrackModel track, StateRequest request)
    {
        var results = new List<PluginStateResult>();
        int expected;
        Dictionary<int, (PluginStateStatus Status, string State)> replies;
        lock (request) { expected = request.Expected; replies = new(request.Replies); }
        for (var position = 0; position < track.Rig.Plugins.Count; position++)
        {
            var plugin = track.Rig.Plugins[position];
            // The reply's index is the plug-in's place in the chain the engine ran; the model may have changed since (an insert or a removal
            // while the states were read), so the plug-in is found by its id there. Without the chain's ids the position is used.
            var i = request.PluginIds is { } ids ? Array.IndexOf(ids, plugin.Id) : position;
            PluginStateOutcome outcome;
            if (i < 0 || (expected >= 0 && i >= expected)) outcome = PluginStateOutcome.Unchanged;   // not in the live chain: its stored state is its state
            else if (!replies.TryGetValue(i, out var reply)) outcome = PluginStateOutcome.TimedOut;
            else outcome = reply.Status switch
            {
                PluginStateStatus.Captured when reply.State.Length is > 0 and <= InputLimits.MaxPluginStateChars => PluginStateOutcome.Captured,
                PluginStateStatus.Captured or PluginStateStatus.NoState => PluginStateOutcome.Unchanged,   // an empty valid state keeps the stored one
                PluginStateStatus.TooLarge => PluginStateOutcome.TooLarge,
                PluginStateStatus.TimedOut => PluginStateOutcome.TimedOut,
                _ => PluginStateOutcome.Failed,
            };
            if (outcome == PluginStateOutcome.Captured) plugin.State = replies[i].State;
            results.Add(new PluginStateResult(track, plugin, outcome));
        }
        return results;
    }

    /// <summary>Test hook (--probe-audio): ends the engine abruptly, as a crashing plug-in would.</summary>
    internal void KillEngineForTest()
    {
        try { _process?.Kill(); } catch (InvalidOperationException) { }
    }

    /// <summary>Test hook: forget earlier crashes (each probe case starts fresh).</summary>
    internal void ResetCrashCountForTest() => _crashes.Clear();

    /// <summary>
    /// Self-test hook (no engine process): publishes <paramref name="shared"/> as a running engine's block, as Start does
    /// (the previously retired block is disposed first), with <paramref name="ui"/> as the UI thread's context and
    /// <paramref name="slots"/> fake sent chains in the UI-owned state.
    /// </summary>
    internal void AttachForTest(SharedBlock shared, SynchronizationContext ui, int slots)
    {
        _ui = ui;
        _stopping = false;
        lock (_writeGate) { _retiredShared?.Dispose(); _retiredShared = null; }
        Volatile.Write(ref _shared, shared);
        for (var i = 0; i < slots; i++) _sentChains[i] = "selftest";
        IsRunning = true;
    }

    /// <summary>Self-test hook: starts a real engine process (as Sync does) with <paramref name="config"/>; returns at once (R-08).</summary>
    internal bool StartForTest(EngineConfig config, SynchronizationContext ui)
    {
        _ui = ui;
        if (!IsRunning && !Start()) return false;
        _config = config;
        Send(EngineCommand.Configure, w => w.Write(config));
        return true;
    }

    /// <summary>Self-test hook: loads a chain on <paramref name="slot"/> directly (no tracks, no trust check: test plug-ins only).</summary>
    internal void LoadChainForTest(int slot, List<PluginSpec> specs)
    {
        _chainRequests[slot] = _chainRequests.GetValueOrDefault(slot) + 1;
        ChainLoadProtocol.Send((c, p) => Send(c, p), slot, false, "selftest", specs, Interlocked.Increment(ref _nextChainLoad));
    }

    /// <summary>Self-test hook: the engine main thread sleeps (honoured only with TABFORGE_ENGINE_TEST_HOOKS=1).</summary>
    internal void SendTestHangForTest(int seconds) => Send(EngineCommand.TestHang, w => w.Write(seconds));
    /// <summary>Self-test hook: when the engine last proved alive (pong / Ready), Stopwatch ticks.</summary>
    internal long LastAliveForTest => Volatile.Read(ref _lastAlive);
    internal bool ConnectedForTest { get { lock (_sendGate) return _pipe is not null && _queued is null; } }

    /// <summary>Self-test hook: the engine process ended unexpectedly (what Process.Exited does), on the calling thread.</summary>
    internal void SimulateEngineExitForTest() => OnEngineEnded(_process);

    /// <summary>Self-test hook: UI-owned state size (the fake sent chains).</summary>
    internal int SentChainCountForTest => _sentChains.Count;
    internal bool HoldsRetiredBlockForTest => _retiredShared is not null;
    internal void DisposeRetiredForTest() { lock (_writeGate) { _retiredShared?.Dispose(); _retiredShared = null; } }

    internal int? EngineProcessId => _process is { HasExited: false } p ? p.Id : null;
}
