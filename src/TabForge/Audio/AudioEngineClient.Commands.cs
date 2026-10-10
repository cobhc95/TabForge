using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: the commands sent to the engine: transport position and song transports per document, the master output tap, recording, editor windows, programs, pitch, state, offline render and live MIDI writes.
// Does not own: the engine process and pipe (AudioEngineClient.cs) or the graph sync (AudioEngineClient.Sync.cs).
// Tests: TestMasterTapProtocol.

/// <summary>Commands sent to the engine: transport position, recording, editor windows, programs, pitch, state, offline render and live MIDI writes.</summary>
public sealed partial class AudioEngineClient : IDisposable
{
    /// <summary>Switches the engine's master output tap (everything audible, as <see cref="MasterAudio"/> chunks). Off by default and off costs the engine nothing.</summary>
    public void SetMasterTap(bool on) { if (IsRunning) Send(EngineCommand.SetMasterTap, new SetMasterTapMessage(on).Write); }

    private void ReadMasterAudio(BinaryReader r)
    {
        var rate = r.ReadInt32(); var first = r.ReadInt64(); var frames = r.ReadInt32();
        if (frames is < 1 or > MasterTapLimits.MaxChunkFrames || rate is < 8000 or > 384000) return;
        var bytes = r.ReadBytes(frames * 8);
        if (bytes.Length != frames * 8) return;
        var samples = new float[frames * 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        MasterAudio?.Invoke(rate, first, samples, frames);
    }

    /// <summary>Song position for audio clips (see <see cref="SongClock"/>).</summary>
    public void SetPosition(bool playing, double songSec, long stamp, int ownerId = 0)
    {
        _songIds.Record(ownerId, playing, songSec, stamp);
        if (IsRunning) Send(EngineCommand.SetPosition, new SetPositionMessage(playing, songSec, stamp, ownerId).Write);
    }

    /// <summary>The engine id of a document's song transport, allocated on first use (lowest free id); 0 (shared) when every id is taken. Any thread.</summary>
    public int OwnerIdOf(object owner)
    {
        if (IsAnonymousOwner(owner)) return 0;
        foreach (var dead in _songIds.Sweep()) StopSongTransport(dead);   // a document dropped while it played: its engine transport must not keep playing
        var id = _songIds.IdOf(owner);
        if (id == 0 && _songIds.IsSharing(owner)) System.Diagnostics.Debug.WriteLine($"audio engine: all {SongOwners.Max} song transports are taken; a further song shares transport 0");
        return id;
    }

    /// <summary>Owners that are not documents (a bare project or track list: renders, probes) have no clock and use the shared transport 0 without taking an id.</summary>
    private static bool IsAnonymousOwner(object owner) => owner is SongProject || ReferenceEquals(owner, TracksOnlyOwner);

    private void StopSongTransport(int id)
    {
        if (IsRunning) Send(EngineCommand.SetPosition, new SetPositionMessage(false, 0.0, Stopwatch.GetTimestamp(), id).Write);
    }

    /// <summary>The id <see cref="OwnerIdOf"/> gave a document, or -1 when it has none (never synced, or released). Any thread.</summary>
    public int ExistingOwnerId(object owner) => IsAnonymousOwner(owner) ? 0 : _songIds.Existing(owner);

    /// <summary>True for a document that shares transport 0 because every id was taken: it must never stop that transport.</summary>
    public bool SharesOwnerZero(object owner) => _songIds.IsSharing(owner);

    /// <summary>UI thread: the document is gone. Its song stops in the engine and its id is free for the next document.</summary>
    private void ReleaseOwnerId(object owner)
    {
        if (_songIds.Release(owner, out var id)) StopSongTransport(id);
    }

    /// <summary>After an engine restart: every owner's last position again (the engine starts with every song stopped at 0).</summary>
    public void ResendPositions()
    {
        if (!IsRunning) return;
        foreach (var (id, playing, sec, stamp) in _songIds.Positions()) Send(EngineCommand.SetPosition, new SetPositionMessage(playing, sec, stamp, id).Write);
    }

    /// <summary>Starts recording the armed tracks into <paramref name="folder"/>; false when nothing is armed.</summary>
    public bool StartRecording(IEnumerable<TrackModel> tracks, string folder)
    {
        var armed = tracks.Where(t => t.RecordArm && !AudioInputs.IsMidi(t.AudioInput) && _slots.ContainsKey(t)).ToList();
        if (!IsRunning || armed.Count == 0) return false;
        _recordingTracks = armed.ToDictionary(t => _slots[t]);
        var owner = _slotOwners.TryGetValue(_slots[armed[0]], out var recordingSong) ? Math.Max(0, _songIds.Existing(recordingSong)) : 0;   // the take aligns to the recording song's own position
        // RT-09: the engine lines the take up with the recording offset
        Send(EngineCommand.Record, new RecordMessage(true, folder, armed.Select(t => (_slots[t], t.Name)).ToList(), RecordingOffsetMs, owner).Write);
        return true;
    }

    public void StopRecording()
    {
        if (IsRunning) Send(EngineCommand.Record, RecordMessage.WriteStop);
    }

    /// <summary>The engine slot a track plays through, or -1 (Windows MIDI).</summary>
    public int SlotOf(TrackModel track) => IsRunning && _slots.TryGetValue(track, out var slot) ? slot : -1;

    /// <summary>
    /// Any thread: queues a MIDI message for the engine; false when the ring is full or no engine runs. The ring is single-producer
    /// but the client is app-wide (two playing documents or windows, a render feed, live input), so producers take a lock; none
    /// of them is real-time and an uncontended lock costs ~20 ns. The lock also keeps a block from being disposed mid-write.
    /// </summary>
    public bool Write(in TimedMidi message)
    {
        WrittenForTest?.Invoke(message);
        lock (_writeGate) return Volatile.Read(ref _shared)?.TryWrite(message) ?? false;
    }

    public void Panic() { if (IsRunning) Send(EngineCommand.Panic); }

    /// <summary>All notes off on these slots only (one document's tracks): other documents that play at the same time keep sounding.</summary>
    public void Panic(IReadOnlyCollection<int> slots)
    {
        if (IsRunning && slots.Count > 0) Send(EngineCommand.PanicSlots, new PanicSlotsMessage(slots).Write);
    }

    public void SetTransport(double tempo, bool playing) { if (IsRunning) Send(EngineCommand.SetTransport, w => SetTransportMessage.WriteTempoOnly(w, tempo, playing)); }

    /// <summary>RT-04: tempo plus the song's bar map (time signature, bar start and tempo per performed bar) for plug-in transport.</summary>
    public void SetTransport(double tempo, bool playing, TransportBar[] bars, int ownerId = 0)
    {
        if (IsRunning) Send(EngineCommand.SetTransport, new SetTransportMessage(tempo, playing, bars, ownerId).Write);
    }

    /// <summary>
    /// Opens a plug-in's own window in the engine: <paramref name="docked"/> inside <paramref name="window"/> (a host area
    /// of the FX chain window), else floating, owned by <paramref name="window"/> and optionally always on top.
    /// </summary>
    public bool OpenEditor(TrackModel track, PluginSlot slot, IntPtr window, bool dark, bool docked = false, bool onTop = false)
    {
        if (!TryAddress(track, slot, out var engineSlot, out var index)) return false;
        Send(EngineCommand.OpenEditor, new OpenEditorMessage(engineSlot, index, (long)window, dark, docked, onTop).Write);
        return true;
    }

    public void CloseEditor(TrackModel track, PluginSlot slot)
    {
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.CloseEditor, new CloseEditorMessage(engineSlot, index).Write);
    }

    /// <summary>Asks for the plug-in's programs; the answer arrives as <see cref="ProgramsReceived"/>.</summary>
    public void RequestPrograms(TrackModel track, PluginSlot slot)
    {
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.GetPrograms, new GetProgramsMessage(engineSlot, index).Write);
    }

    public void SetProgram(TrackModel track, PluginSlot slot, int program)
    {
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.SetProgram, new SetProgramMessage(engineSlot, index, program).Write);
        PresetChanged?.Invoke(track, slot);
    }

    /// <summary>The track playing through an engine slot, or null.</summary>
    public TrackModel? TrackAt(int engineSlot) => _slots.FirstOrDefault(kv => kv.Value == engineSlot).Key;

    /// <summary>Sends a track's automatic pitch transposes (chain index, semitones) to the engine.</summary>
    public void SetAutoPitch(TrackModel track, IReadOnlyList<(int Index, int Semitones)> list)
    {
        if (!IsRunning || !_slots.TryGetValue(track, out var engineSlot)) return;
        Send(EngineCommand.SetAutoPitch, new SetAutoPitchMessage(engineSlot, list).Write);
    }

    /// <summary>Loads a saved state (a preset) into the running plug-in without reloading it.</summary>
    public void SetState(TrackModel track, PluginSlot slot, string state)
    {
        if (state.Length > InputLimits.MaxPluginStateChars) return;   // the one state size contract (the engine would refuse the frame)
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.SetState, new SetStateMessage(engineSlot, index, state).Write);
        PresetChanged?.Invoke(track, slot);
    }

    private bool TryAddress(TrackModel track, PluginSlot slot, out int engineSlot, out int index)
    {
        index = track.Rig.Plugins.IndexOf(slot);
        return IsRunning & _slots.TryGetValue(track, out engineSlot) & index >= 0;
    }

    /// <summary>
    /// Renders offline through the running engine: it stops its audio device, renders faster than realtime on parallel workers,
    /// writes the WAV files named in <paramref name="spec"/> and reopens the device. Call on the UI thread while the engine runs
    /// (after <see cref="Sync"/>), with the transport stopped. Progress arrives about 10 times a second (on the UI thread).
    /// Cancelling the token cancels the render (files are deleted); failures throw <see cref="RenderException"/>. If a plug-in
    /// crashes the engine, the exception carries the plug-in path when it could be identified (render single-threaded, RenderThreads.One, to pin it).
    /// </summary>
    public Task<RenderResult> RenderAsync(RenderSpec spec, IProgress<RenderProgressInfo>? progress = null, CancellationToken cancel = default)
    {
        _ui ??= SynchronizationContext.Current;
        if (!IsRunning) return Task.FromException<RenderResult>(new RenderException("The audio engine is not running."));
        if (_renderTask is { Task.IsCompleted: false }) return Task.FromException<RenderResult>(new RenderException("A render is already running."));
        var source = new TaskCompletionSource<RenderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _renderTask = source; _renderProgress = progress;
        cancel.Register(() => { if (IsRunning) Send(EngineCommand.RenderCancel); });
        Send(EngineCommand.RenderOffline, w => spec.Write(w));
        return source.Task;
    }

    private void FailRender(RenderException ex)
    {
        var source = _renderTask; _renderTask = null; _renderProgress = null;
        source?.TrySetException(ex);
    }
}
