using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

/// <summary>One line of the MIDI log: song time, the message, and which processor stage logged it.</summary>
public readonly record struct MidiLogLine(float Time, byte Status, byte Data1, byte Data2, int Stage);

/// <summary>What happened to one plug-in's state when states were collected before a save.</summary>
public enum PluginStateOutcome { Captured, Unchanged, TimedOut, Failed, TooLarge }

public sealed record PluginStateResult(TrackModel Track, PluginSlot Plugin, PluginStateOutcome Outcome);

/// <summary>Per-plug-in result of <see cref="AudioEngineClient.CollectStates"/>.</summary>
public sealed class StateCollection
{
    public List<PluginStateResult> Results { get; } = new();
    /// <summary>Plug-ins whose current state could not be read (timed out, failed, too large): the song keeps their previous settings.</summary>
    public int IncompleteCount => Results.Count(r => r.Outcome is PluginStateOutcome.TimedOut or PluginStateOutcome.Failed or PluginStateOutcome.TooLarge);
    public bool Complete => IncompleteCount == 0;
    /// <summary>The save warning, or null when every state was read.</summary>
    public string? Warning => Complete ? null
        : $"{IncompleteCount} plug-in state{(IncompleteCount == 1 ? "" : "s")} could not be read ({string.Join(", ", Results.Where(r => r.Outcome is PluginStateOutcome.TimedOut or PluginStateOutcome.Failed or PluginStateOutcome.TooLarge).Select(r => $"{(r.Plugin.Name.Length > 0 ? r.Plugin.Name : Path.GetFileNameWithoutExtension(r.Plugin.Path))}: {Describe(r.Outcome)}").Distinct().Take(4))}); the saved song has their previous settings";

    private static string Describe(PluginStateOutcome outcome) => outcome switch
    {
        PluginStateOutcome.TimedOut => "no answer",
        PluginStateOutcome.TooLarge => $"larger than {PluginStateLimits.MaxRawBytes / 1048576} MiB",
        _ => "failed",
    };
}

/// <summary>
/// TabForge's only link to the audio engine process (TabForge.exe --audio-engine), which hosts plug-ins and the
/// audio device. The engine starts only when some track plays through plug-ins; it has one owner (the active document) and
/// stays warm for <see cref="WarmIdle"/> after the last engine track goes away (R-10), so a song without plug-ins runs as before. If the engine dies (a plug-in crashed), the plug-in that was
/// running is switched off, the engine restarts, and playback carries on; TabForge itself is never affected.
/// </summary>
public sealed class AudioEngineClient : IDisposable
{
    public static AudioEngineClient Instance { get; } = new();

    private readonly object _gate = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private SharedBlock? _shared;
    private bool _stopping;
    private readonly List<DateTime> _crashes = new();
    private readonly Dictionary<TrackModel, int> _slots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, string> _sentChains = new();
    private readonly Dictionary<int, StateRequest> _stateRequests = new();   // by request id
    private EngineConfig? _config;
    private SynchronizationContext? _ui;

    public bool IsRunning { get; private set; }
    /// <summary>Delay from a message's time stamp to it being heard; Windows MIDI is delayed by this too.</summary>
    public long LatencyTicks => _shared?.LatencyTicks ?? 0;
    public double CpuLoad => _shared?.CpuLoad ?? 0;
    /// <summary>The tuner window is open: the engine detects the pitch of the armed input (needs an armed track, the input is only open then).</summary>
    public bool TunerOn { set { if (_shared is { } shared) shared.TunerOn = value; } }
    /// <summary>The engine's latest tuner reading (Hz 0: silence / no pitch) and a counter that changes with each one.</summary>
    public (float Hz, float Clarity, int Seq) TunerReading => _shared?.Tuner ?? default;
    /// <summary>A track's current output level (0..1), for meters; 0 when it does not play through the engine.</summary>
    /// <summary>An armed track's input level (before its effects), 0..1+.</summary>
    public float InputPeakOf(TrackModel track) => _shared is { } shared && _slots.TryGetValue(track, out var slot) ? shared.InputPeak(slot) : 0;
    public float PeakOf(TrackModel track) => _shared is { } shared && _slots.TryGetValue(track, out var slot) ? shared.Peak(slot) : 0;
    public string? DeviceDescription { get; private set; }
    /// <summary>The running output: driver (WASAPI, ASIO...), device, sample rate, buffer and total output latency.</summary>
    public (string Driver, string Device, int Rate, int Buffer, int LatencyMs)? Output { get; private set; }
    /// <summary>The ASIO driver's channel names (as it reports them), from the last start; null until known.</summary>
    public (string[] Inputs, string[] Outputs)? AsioChannels { get; private set; }

    /// <summary>A plug-in crashed the engine and was switched off (its path; raised on the UI thread).</summary>
    public event Action<string>? PluginCrashed;
    /// <summary>
    /// A plug-in call is slow but within its limit (path, what it is doing, seconds so far; raised on the UI thread): show "still
    /// loading". It is not a crash; only if it passes its limit does the engine end and the plug-in get switched off.
    /// </summary>
    public event Action<string, PluginCallKind, int>? PluginSlow;
    /// <summary>A plug-in could not be loaded (path, reason).</summary>
    public event Action<string, string>? PluginFailed;
    /// <summary>
    /// RT-02: a plug-in produced non-finite audio (NaN / infinity) and the engine now skips it until it is switched on again (path; index -1:
    /// the track's own sound, whose block was silenced). Raised on the UI thread, once per occurrence.
    /// </summary>
    public event Action<string, int>? PluginMisbehaved;
    /// <summary>The audio device could not be opened or stopped working.</summary>
    public event Action<string>? DeviceError;
    /// <summary>A plug-in reported a parameter edit (the song has unsaved changes).</summary>
    public event Action? PluginEdited;
    /// <summary>The engine started or stopped.</summary>
    public event Action? StatusChanged;
    /// <summary>A track's chain (plug-ins / General MIDI synth) is ready in the engine: fresh synths still need their program changes.</summary>
    public event Action? ChainLoaded;
    /// <summary>The engine's per-request acknowledgement (slot, generation, every plug-in's outcome); raised just before <see cref="ChainLoaded"/>.</summary>
    public event Action<ChainAck>? ChainAcknowledged;

    private readonly Dictionary<int, int> _chainRequests = new();
    /// <summary>How many LoadChain requests were sent for a slot since the engine started (the engine counts the same way).</summary>
    public int ChainGenerationOf(int slot) => _chainRequests.GetValueOrDefault(slot);
    private readonly Dictionary<int, ChainAck> _lastAcks = new();
    /// <summary>Latest acknowledgement per slot (UI thread), so a waiter can start from what already arrived.</summary>
    public IReadOnlyCollection<ChainAck> LastAcknowledgements => _lastAcks.Values.ToList();
    /// <summary>Every slot that has a chain request in flight or done, with the generation of its latest request.</summary>
    public IReadOnlyList<(int Slot, int Generation)> RequestedChains => _chainRequests.Where(kv => _slots.ContainsValue(kv.Key) && !_parkedSince.ContainsKey(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();   // parked chains of other documents are not the owner's

    /// <summary>Plug-ins switched off after crashing (full paths); owned by the app settings.</summary>
    public Func<ICollection<string>>? Quarantine { get; set; }
    /// <summary>Plug-ins skipped for now only (the active song's "Disable it" after a very slow load); never saved, never added to.</summary>
    public Func<ICollection<string>>? SkipForNow { get; set; }

    /// <summary>
    /// Brings the engine in line with the tracks that need it: starts / stops it, loads changed chains, removes
    /// tracks that no longer play through plug-ins. Call on the UI thread after any chain, source or song change.
    /// </summary>
    /// <param name="mix">The track's effective level and pan (0..127; level 0 when muted); default: its own settings.</param>
    /// <summary>True while File > Render runs: <see cref="Sync"/> does nothing, so the engine is not stopped or reloaded mid-render.</summary>
    public bool Rendering { get; set; }

    /// <param name="project">The song (group buses, master chain and the routing graph); null: tracks only.</param>
    /// <param name="owner">
    /// The document these tracks belong to (R-10: the engine has one explicit owner, the active document). Null: <paramref name="project"/>.
    /// Chains of an earlier owner are parked (kept loaded, silent) for <see cref="WarmIdle"/>, so switching back reloads nothing.
    /// </param>
    public void Sync(IEnumerable<TrackModel> tracks, PluginSettings settings, Func<TrackModel, (int Volume, int Pan)>? mix = null, SongProject? project = null, object? owner = null)
    {
        if (Rendering) return;
        _ui ??= SynchronizationContext.Current;
        RecordingOffsetMs = settings.RecordingOffsetMs;
        owner ??= (object?)project ?? TracksOnlyOwner;
        ICollection<string> quarantined = Quarantine?.Invoke() ?? Array.Empty<string>();
        if (SkipForNow?.Invoke() is { Count: > 0 } skipped)
            quarantined = quarantined.Concat(skipped).ToHashSet(StringComparer.OrdinalIgnoreCase);   // a copy: the quarantine list itself is not changed
        // Tracks whose MIDI plays through plug-ins, or that have audio (clips, or an armed input).
        var wanted = tracks.Where(t => MixerGroups.MidiInEngine(t) || MixerGroups.UsesEngineAudio(t)).ToList();
        var config = new EngineConfig(settings.Driver, settings.Device, settings.SampleRate, settings.BufferSize, settings.SeparateProcessPerPlugin, settings.InputDevice, settings.AsioInputChannel, settings.AsioOutputChannel, settings.AsioInputLastChannel, settings.AsioInputsEnabled, settings.FollowWindowsVolume, settings.AsioOutputLastChannel);
        // Multi-tab playback: tracks of other documents that are still playing stay live (never parked or removed here).
        var liveElsewhere = IsRunning ? _slots.Where(kv => !kv.Key.IsBus && !wanted.Contains(kv.Key) && PlaysElsewhere(kv.Value, owner)).Select(kv => kv.Key).ToList() : new List<TrackModel>();

        if (wanted.Count == 0 && liveElsewhere.Count == 0 && (!IsRunning || WarmIdle <= TimeSpan.Zero))
        {
            if (IsRunning) Stop();
            _slots.Clear();
            _sentChains.Clear();
            _sentRoutes.Clear();
            _sentProcessors.Clear();
            _sentWiring.Clear();
            _sentBypass.Clear();
            _sentGain.Clear();
            _sentSynth.Clear();
            _sentGraph = "";
            ResetWarmState();
            return;
        }
        var ownIdle = wanted.Count == 0;
        var idle = ownIdle && liveElsewhere.Count == 0;   // R-10: the engine stays warm (no Stop) until WarmIdle has passed without engine tracks
        if (!IsRunning && !Start()) return;
        // Group buses and the master chain run only alongside real engine tracks (they never start the engine themselves).
        if (project is not null && !ownIdle) wanted.AddRange(MixerBuses.Active(project));
        // Monitor FX (speaker calibration): after the master, live output only; the app-wide chain or the song's own. Global slot: wanted while any document's tracks are live.
        if (project is not null && !idle && MixerBuses.ActiveMonitor(project, settings.MonitorFx) is { } monitorFx) wanted.Add(monitorFx);
        if (!ownIdle && _config != config) { _config = config; Send(EngineCommand.Configure, w => w.Write(config)); }
        if (!ownIdle) SyncWindowsPathOffset(config);
        var now = WarmClock();
        foreach (var parked in _parkedSince.Keys.Where(s => !_slots.ContainsValue(s)).ToList()) _parkedSince.Remove(parked);   // a crash cleared the slots

        foreach (var gone in _slots.Keys.Where(t => !wanted.Contains(t) && !liveElsewhere.Contains(t)).ToList())
        {
            var slot = _slots[gone];
            // Monitor chain replaced by another one (app-wide <-> the song's own, or another song's): the new chain swaps into the slot, no unload gap.
            if (gone.IsBus && gone.BusSlot == MixerBuses.MonitorSlot && wanted.Any(t => t.IsBus && t.BusSlot == MixerBuses.MonitorSlot)) { _slots.Remove(gone); continue; }
            // Another document's track: parked (loaded, silent) while warm. Buses and the master use fixed slots that every
            // document shares, so they are never parked; the current owner's own removals are real removals.
            if (!gone.IsBus && WarmIdle > TimeSpan.Zero && _slotOwners.TryGetValue(slot, out var slotOwner) && !ReferenceEquals(slotOwner, owner))
            {
                if (!_parkedSince.ContainsKey(slot)) { _parkedSince[slot] = now; ParkAudio(slot); }
                continue;
            }
            RemoveSlot(gone);
        }
        foreach (var track in wanted)
        {
            if (!_slots.TryGetValue(track, out var slot))
            {
                slot = track.IsBus ? track.BusSlot : FreeTrackSlot();
                _slots[track] = slot;
            }
            _slotOwners[slot] = owner;
            _parkedSince.Remove(slot);   // back from parking: its chain is still loaded (the chain key below matches)
            // A track's plug-ins stay loaded while the chain is off (all bypassed): their editors stay open
            // and switching back is instant. Bus chains that are off are not in the engine at all.
            var chainOn = track.SoundSource == SoundSources.Plugins;
            var specs = PluginTrust.BuildSpecs(chainOn || !track.IsBus ? track.Rig.Plugins : new List<PluginSlot>(), quarantined, settings);   // untrusted paths are Skip: never loaded
            if (!chainOn) specs = specs.Select(s => s with { Enabled = false }).ToList();
            // The General MIDI synth exists for every MIDI track in the engine and is switched on / off live (SetSynth): GM
            // taking over from a bypassed instrument or a switched-off chain needs no rebuild, so it stays in time.
            var useSynth = !track.IsBus && MixerGroups.MidiInEngine(track);
            var key = $"{useSynth}|{track.Name}|{(track.BusSlot == MixerBuses.MonitorSlot ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(track.Bus) : 0)}|" + string.Join("|", specs.Select(s => $"{s.Id}:{s.Path}:{s.Skip}:{s.Wet}:{s.IsInstrument}:{s.Pins}"));   // OutputDb and Enabled are not part of the key: SetPluginGain / SetPluginEnabled change them live
            if (!(_sentChains.TryGetValue(slot, out var sent) && sent == key))
            {
                _sentChains[slot] = key;
                _chainRequests[slot] = _chainRequests.GetValueOrDefault(slot) + 1;
                var name = track.Name;
                ChainLoadProtocol.Send((c, p) => Send(c, p), slot, useSynth, name, specs, Interlocked.Increment(ref _nextChainLoad));   // spec first, then one frame per state
                _sentBypass[slot] = string.Join(",", specs.Select(s => s.Enabled));   // the new chain starts with these
                _sentGain[slot] = string.Join(",", specs.Select(s => s.OutputDb.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            }
            SyncBypass(track, slot);
            SyncGain(track, slot);
            if (useSynth) SyncSynth(slot, MixerGroups.GmSounds(track));
            if (!track.IsBus) SyncAudio(track, slot, mix?.Invoke(track) ?? (track.Mute ? 0 : track.Volume, track.Pan));
        }
        foreach (var track in wanted) { if (!track.IsBus) SyncMidiRoute(track, wanted, project); SyncMidiProcessors(track); SyncWiring(track); }
        SyncGraph(wanted, project, liveElsewhere);
        _idleSince = idle ? _idleSince ?? now : null;
        ScheduleWarmCheck();
    }

    /// <summary>
    /// Multi-tab playback: true while a document plays (or is paused). Its tracks keep their own live slots when another document
    /// becomes the owner; they are parked only once it has stopped. Null: every other document is parked (the R-10 behaviour).
    /// </summary>
    public Func<object, bool>? IsOwnerPlaying { get; set; }

    private bool PlaysElsewhere(int slot, object owner)
        => _slotOwners.TryGetValue(slot, out var slotOwner) && !ReferenceEquals(slotOwner, owner) && IsOwnerPlaying?.Invoke(slotOwner) == true;

    // ---- R-10: one owner, warm engine (one engine process is shared by all documents and kept warm while idle) ----

    /// <summary>How long the engine stays running without engine tracks, and how long another document's chains stay parked.</summary>
    public const int DefaultWarmIdleMinutes = 5;
    /// <summary>Zero or less: the old behaviour (a Sync without engine tracks stops the engine at once; nothing is parked).</summary>
    public TimeSpan WarmIdle { get; set; } = TimeSpan.FromMinutes(DefaultWarmIdleMinutes);
    private static readonly object TracksOnlyOwner = new();
    /// <summary>Document that each slot's chain belongs to (the slot key's document part; tracks are keyed by reference).</summary>
    private readonly Dictionary<int, object> _slotOwners = new();
    /// <summary>Slots of documents that are not the owner: loaded, silent, removed once parked longer than <see cref="WarmIdle"/>.</summary>
    private readonly Dictionary<int, DateTime> _parkedSince = new();
    private DateTime? _idleSince;
    private Timer? _warmTimer;
    internal Func<DateTime> WarmClock { get; set; } = () => DateTime.UtcNow;

    private int FreeTrackSlot()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            for (var s = 0; s < MixerBuses.BusBase; s++) if (!_slots.ContainsValue(s)) return s;
            // Full: parked chains of other documents make room first.
            foreach (var parked in _slots.Where(kv => _parkedSince.ContainsKey(kv.Value)).Select(kv => kv.Key).ToList()) RemoveSlot(parked);
        }
        throw new InvalidOperationException("Every engine track slot is in use.");
    }

    private void RemoveSlot(TrackModel gone)
    {
        var slot = _slots[gone];
        _slots.Remove(gone);
        _slotOwners.Remove(slot);
        _parkedSince.Remove(slot);
        _sentChains.Remove(slot);
        _sentAudio.Remove(slot);
        _sentRoutes.Remove(slot);
        _sentProcessors.Remove(slot);
        _sentWiring.Remove(slot);
        _sentBypass.Remove(slot);
        _sentGain.Remove(slot);
        if (_sentSynth.Remove(slot)) Send(EngineCommand.SetSynth, w => { w.Write(slot); w.Write(true); });   // the slot's next track starts with GM on
        Send(EngineCommand.RemoveTrack, w => w.Write(slot));
    }

    /// <summary>A parked slot is silent: level 0, no clips, not armed. Its chain (and any open editor) stays as it is.</summary>
    private void ParkAudio(int slot)
    {
        _sentAudio[slot] = "parked";   // the owner's next Sync of this track sends its real level, clips and arm again
        Send(EngineCommand.SetTrackMix, w => { w.Write(slot); w.Write(0); w.Write(64); });
        Send(EngineCommand.SetClips, w => { w.Write(slot); w.Write(new List<ClipSpec>()); });
        Send(EngineCommand.SetArm, w => { w.Write(slot); w.Write(false); w.Write(0); w.Write(false); });
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

    private void ResetWarmState()
    {
        _slotOwners.Clear();
        _parkedSince.Clear();
        _idleSince = null;
        _warmTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    internal void ExpireWarmForTest() => ExpireWarm();
    internal bool IsParkedForTest(TrackModel track) => _slots.TryGetValue(track, out var slot) && _parkedSince.ContainsKey(slot);
    internal int ChainLoadsSentForTest => Volatile.Read(ref _nextChainLoad);
    /// <summary>Self-test hook: a "running" engine with no process or pipe (every Send is dropped), for UI-side bookkeeping tests.</summary>
    internal void AttachFakeForTest() { _stopping = false; IsRunning = true; }

    private string _sentGraph = "";
    private static float? _windowsPathOffset;
    private static bool _measuringPathOffset;
    /// <summary>Last Windows audio path measurement, for diagnostics.</summary>
    internal static string WindowsPathDetail { get; private set; } = "";
    private int? _pathOffsetSentTo;

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

    /// <summary>
    /// The routing graph (one light message, sent when it changes): each track's group bus (when that bus runs), plug-in sidechain
    /// sources and MIDI-output forwards by stable track id. The engine sorts sources before destinations.
    /// </summary>
    private void SyncGraph(List<TrackModel> wanted, SongProject? project, List<TrackModel>? liveElsewhere = null)
    {
        // Tracks of other documents that still play keep their sidechains and MIDI forwards (their group buses belong to the owner's song).
        var all = liveElsewhere is { Count: > 0 } ? wanted.Concat(liveElsewhere).ToList() : wanted;
        var byId = new Dictionary<string, int>();
        foreach (var t in all) if (!t.IsBus && _slots.TryGetValue(t, out var s)) byId[t.Id.ToString("N")] = s;
        var buses = wanted.Where(t => t.IsBus).Select(t => t.BusSlot).ToHashSet();
        var dests = new List<(int, int)>(); var sides = new List<(int, int, int)>(); var fwds = new List<(int, int, int)>();
        foreach (var t in all)
        {
            if (t.IsBus || !_slots.TryGetValue(t, out var slot)) continue;
            var ownSong = liveElsewhere is null || !liveElsewhere.Contains(t);
            if (ownSong && project is not null && MixerBuses.SlotOf(MixerGroups.GroupOf(project, t)) is var bus && buses.Contains(bus)) dests.Add((slot, bus));
            if (t.SoundSource != SoundSources.Plugins) continue;
            for (var i = 0; i < t.Rig.Plugins.Count; i++)
            {
                var p = t.Rig.Plugins[i];
                if (p.SidechainTrackId is { Length: > 0 } src && byId.TryGetValue(src, out var srcSlot) && srcSlot != slot) sides.Add((slot, i, srcSlot));
                if (p.MidiOutTrackId is { Length: > 0 } dst && byId.TryGetValue(dst, out var dstSlot) && dstSlot != slot) fwds.Add((slot, i, dstSlot));
            }
        }
        var key = string.Join(",", dests) + "|" + string.Join(",", sides) + "|" + string.Join(",", fwds);
        if (key == _sentGraph) return;
        _sentGraph = key;
        Send(EngineCommand.SetGraph, w =>
        {
            w.Write(dests.Count); foreach (var (a, b) in dests) { w.Write(a); w.Write(b); }
            w.Write(sides.Count); foreach (var (a, b, c) in sides) { w.Write(a); w.Write(b); w.Write(c); }
            w.Write(fwds.Count); foreach (var (a, b, c) in fwds) { w.Write(a); w.Write(b); w.Write(c); }
        });
    }

    private readonly Dictionary<int, string> _sentWiring = new();

    /// <summary>Serial-chain options of every plug-in (pass MIDI on, MIDI output on, instrument audio add / replace): light live message, never part of the rebuild key.</summary>
    private void SyncWiring(TrackModel track)
    {
        if (!_slots.TryGetValue(track, out var slot)) return;
        var plugins = track.SoundSource == SoundSources.Plugins ? track.Rig.Plugins : new List<PluginSlot>();
        var flags = plugins.Select(p => (p.PassMidiThrough ? 1 : 0) | (p.MidiOutToNext ? 2 : 0) | (p.InstrumentAudio == "Replace" ? 4 : 0)).ToList();
        var key = string.Join(",", flags);
        var defaults = flags.All(f => f == 3);
        if (_sentWiring.TryGetValue(slot, out var sent) ? sent == key : defaults) { _sentWiring[slot] = key; return; }
        _sentWiring[slot] = key;
        for (var i = 0; i < flags.Count; i++)
        {
            var index = i; var f = flags[i];
            Send(EngineCommand.SetPluginWiring, w => { w.Write(slot); w.Write(index); w.Write(f); });
        }
    }

    private readonly Dictionary<int, string> _sentProcessors = new();

    /// <summary>
    /// Every plug-in's MIDI processors (each list runs right before its plug-in): one light live message for the whole chain when any
    /// list changes (never part of the chain rebuild key). The engine builds new immutable chains and swaps them in; held notes carry over or are released.
    /// </summary>
    private void SyncMidiProcessors(TrackModel track, bool force = false)
    {
        if (!_slots.TryGetValue(track, out var slot)) return;
        var lists = new List<(int Index, List<MidiProcSpec> Specs)>();
        if (track.SoundSource == SoundSources.Plugins)
            for (var i = 0; i < track.Rig.Plugins.Count; i++)
                if (track.Rig.Plugins[i].MidiProcessors is { Count: > 0 } list)
                    lists.Add((i, list.Select(p => new MidiProcSpec(p.Type, p.Enabled, MidiProcessorCatalog.EngineParams(p))).ToList()));
        var key = string.Join("#", lists.Select(l => $"{l.Index}|" + string.Join("|", l.Specs.Select(p => $"{p.Type}:{p.Enabled}:{p.ParamsJson}"))));
        if (!force && (_sentProcessors.TryGetValue(slot, out var sent) ? sent == key : key.Length == 0)) { _sentProcessors[slot] = key; return; }
        _sentProcessors[slot] = key;
        Send(EngineCommand.SetMidiProcessors, w => { w.Write(slot); w.Write(lists.Count); foreach (var (index, specs) in lists) { w.Write(index); w.Write(specs); } });
    }

    /// <summary>Pushes a plug-in's MIDI processor list to the engine now (the MIDI processing window calls it after every edit).</summary>
    public void SetMidiProcessors(TrackModel track, PluginSlot plugin)
    {
        if (IsRunning) SyncMidiProcessors(track);
    }

    /// <summary>Starts / stops the engine sending this track's MIDI log (about 20 times a second while watched).</summary>
    public void WatchMidiLog(TrackModel track, bool watch)
    {
        if (IsRunning && _slots.TryGetValue(track, out var slot)) Send(EngineCommand.SetMidiLogWatch, w => { w.Write(slot); w.Write(watch); });
    }

    /// <summary>MIDI log lines of a watched track (engine slot, lines); raised on the UI thread.</summary>
    public event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived;

    private readonly Dictionary<int, string> _sentRoutes = new();

    /// <summary>
    /// The instrument's MIDI input routing (own / another track / none, channel filter): sent as a light live message
    /// when it changes, never part of the chain rebuild key. Source tracks are found by their stable id.
    /// </summary>
    private void SyncMidiRoute(TrackModel track, List<TrackModel> wanted, SongProject? project = null)
    {
        if (!_slots.TryGetValue(track, out var slot)) return;
        var index = track.SoundSource == SoundSources.Plugins ? track.Rig.Plugins.FindIndex(p => p.Type == PluginSlotType.Instrument) : -1;
        var midiIn = index >= 0 ? track.Rig.Plugins[index].MidiIn ?? new PluginMidiIn() : new PluginMidiIn();
        var source = -1;
        if (midiIn.Source == PluginMidiIn.None) source = -2;
        else if (midiIn.Source == PluginMidiIn.OtherTrack)
        {
            var src = wanted.FirstOrDefault(t => !ReferenceEquals(t, track) && t.Id.ToString("N") == midiIn.TrackId);
            source = src is not null && _slots.TryGetValue(src, out var s) ? s : -2;
        }
        var mask = midiIn.Channel is >= 1 and <= 16 ? 1 << (midiIn.Channel - 1) : 0xFFFF;
        // A filter on the track's own channel also lets its effect channel (bent notes) through.
        if (project is not null && mask != 0xFFFF && project.Tracks.IndexOf(track) is >= 0 and var own)
        {
            var main = Playback.ChannelAllocator.Assign(project);
            var effect = Playback.ChannelAllocator.AssignEffect(project, main);
            if (own < main.Length && mask == 1 << main[own] && effect[own] is >= 0 and < 16) mask |= 1 << effect[own];
        }
        var key = $"{index}|{source}|{mask}";
        if (_sentRoutes.TryGetValue(slot, out var sent) ? sent == key : index < 0 || (source == -1 && mask == 0xFFFF)) { _sentRoutes[slot] = key; return; }
        _sentRoutes[slot] = key;
        if (index >= 0) Send(EngineCommand.SetMidiRoute, w => { w.Write(slot); w.Write(index); w.Write(source); w.Write(mask); });
    }

    /// <summary>A plug-in's output volume while its knob is dragged: one small message, no chain rebuild.</summary>
    public void SetPluginGain(TrackModel track, PluginSlot plugin)
    {
        var index = track.Rig.Plugins.IndexOf(plugin);
        if (IsRunning && index >= 0 && _slots.TryGetValue(track, out var slot))
            Send(EngineCommand.SetPluginGain, w => { w.Write(slot); w.Write(index); w.Write(plugin.OutputDb); });
    }

    /// <summary>Live bypass: the plug-in stays loaded (editor included); the engine just stops processing it.</summary>
    public void SetPluginEnabled(TrackModel track, PluginSlot plugin)
    {
        var index = track.Rig.Plugins.IndexOf(plugin);
        if (IsRunning && index >= 0 && _slots.TryGetValue(track, out var slot)) SyncBypass(track, slot);
    }

    private readonly Dictionary<int, string> _sentBypass = new();
    private readonly Dictionary<int, bool> _sentSynth = new();
    private readonly Dictionary<int, string> _sentGain = new();

    /// <summary>
    /// Every plug-in's Volume (OutputDb), sent live per plug-in that changed: a loaded chain / preset / auto-load chain whose plug-ins match
    /// the running ones (same ids, no rebuild) still gets its saved volumes.
    /// </summary>
    private void SyncGain(TrackModel track, int slot)
    {
        var now = track.Rig.Plugins.Select(p => p.OutputDb.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var key = string.Join(",", now);
        if (_sentGain.TryGetValue(slot, out var sent) && sent == key) return;
        var before = sent?.Split(',');
        _sentGain[slot] = key;
        for (var i = 0; i < now.Count; i++)
        {
            if (before is not null && i < before.Length && before[i] == now[i]) continue;
            var index = i; var db = track.Rig.Plugins[i].OutputDb;
            Send(EngineCommand.SetPluginGain, w => { w.Write(slot); w.Write(index); w.Write(db); });
        }
    }

    /// <summary>Every plug-in's live bypass: its own switch, and all of them while the chain is off (they stay loaded). Sent per plug-in that changed.</summary>
    private void SyncBypass(TrackModel track, int slot)
    {
        var chainOn = track.SoundSource == SoundSources.Plugins;
        var now = track.Rig.Plugins.Select(p => chainOn && p.Enabled).ToList();
        var key = string.Join(",", now);
        if (_sentBypass.TryGetValue(slot, out var sent) && sent == key) return;
        var before = sent?.Split(',');
        _sentBypass[slot] = key;
        for (var i = 0; i < now.Count; i++)
        {
            if (before is not null && i < before.Length && before[i] == now[i].ToString()) continue;
            var index = i; var enabled = now[i];
            Send(EngineCommand.SetPluginBypass, w => { w.Write(slot); w.Write(index); w.Write(enabled); });
        }
    }

    /// <summary>The chain's General MIDI synth on / off (live, no rebuild); sent only when it changes (a new slot starts with it on).</summary>
    private void SyncSynth(int slot, bool on)
    {
        if ((_sentSynth.TryGetValue(slot, out var sent) ? sent : true) == on) return;
        _sentSynth[slot] = on;
        Send(EngineCommand.SetSynth, w => { w.Write(slot); w.Write(on); });
    }

    private readonly Dictionary<int, string> _sentAudio = new();

    /// <summary>Level, clips and arm for one track: sent only when they changed (cheap to call often).</summary>
    private void SyncAudio(TrackModel track, int slot, (int Volume, int Pan) mix)
    {
        var clips = track.AudioClips.Where(c => !c.IsMidi && ClipLanes.Audible(track, c))
            .Select(c => new ClipSpec(c.File, c.StartSec, c.OffsetSec, c.SourceLengthSec, c.GainDb, c.Pitch, c.Speed)).ToList();
        var armMode = Array.IndexOf(AudioInputs.Audio, track.AudioInput);
        var armed = track.RecordArm && armMode >= 0;   // MIDI input is recorded by the editor, not the engine
        var key = $"{mix.Volume}|{mix.Pan}|{armed}|{armMode}|{track.MonitorInput}|" + string.Join("|", clips.Select(c => $"{c.File}@{c.StartSec:0.###}+{c.OffsetSec:0.###}/{c.SourceLengthSec:0.###}/{c.GainDb:0.##}/{c.Pitch:0.##}/{c.Speed:0.###}"));
        if (_sentAudio.TryGetValue(slot, out var sent) && sent == key) return;
        _sentAudio[slot] = key;
        Send(EngineCommand.SetTrackMix, w => { w.Write(slot); w.Write(mix.Volume); w.Write(mix.Pan); });
        Send(EngineCommand.SetClips, w => { w.Write(slot); w.Write(clips); });
        Send(EngineCommand.SetArm, w => { w.Write(slot); w.Write(armed); w.Write(Math.Max(0, armMode)); w.Write(track.MonitorInput); });
    }

    /// <summary>Song position for audio clips (see <see cref="SongClock"/>).</summary>
    public void SetPosition(bool playing, double songSec, long stamp)
    {
        if (IsRunning) Send(EngineCommand.SetPosition, w => { w.Write(playing); w.Write(songSec); w.Write(stamp); });
    }

    /// <summary>Starts recording the armed tracks into <paramref name="folder"/>; false when nothing is armed.</summary>
    public bool StartRecording(IEnumerable<TrackModel> tracks, string folder)
    {
        var armed = tracks.Where(t => t.RecordArm && !AudioInputs.IsMidi(t.AudioInput) && _slots.ContainsKey(t)).ToList();
        if (!IsRunning || armed.Count == 0) return false;
        _recordingTracks = armed.ToDictionary(t => _slots[t]);
        Send(EngineCommand.Record, w =>
        {
            w.Write(true); w.WriteString(folder); w.Write(armed.Count);
            foreach (var t in armed) { w.Write(_slots[t]); w.WriteString(t.Name); }
            w.Write((double)RecordingOffsetMs);   // RT-09: the engine lines the take up with it
        });
        return true;
    }

    /// <summary>Settings > Audio &amp; VST > Recording offset (ms), taken from the settings on every <see cref="Sync"/>.</summary>
    public int RecordingOffsetMs { get; set; }

    public void StopRecording()
    {
        if (IsRunning) Send(EngineCommand.Record, w => { w.Write(false); w.WriteString(""); w.Write(0); });
    }

    private Dictionary<int, TrackModel> _recordingTracks = new();

    /// <summary>A take finished: (track, file, song start seconds, length seconds) on the UI thread.</summary>
    public event Action<TrackModel, string, double, double>? Recorded;
    /// <summary>The audio input could not be opened (message).</summary>
    public event Action<string>? InputError;

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

    private readonly object _writeGate = new();
    /// <summary>The block of an engine that ended; disposed at the next Start (under the write lock), never while a writer holds it.</summary>
    private SharedBlock? _retiredShared;

    public void Panic() { if (IsRunning) Send(EngineCommand.Panic); }

    /// <summary>All notes off on these slots only (one document's tracks): other documents that play at the same time keep sounding.</summary>
    public void Panic(IReadOnlyCollection<int> slots)
    {
        if (IsRunning && slots.Count > 0) Send(EngineCommand.PanicSlots, w => { w.Write(slots.Count); foreach (var s in slots) w.Write(s); });
    }

    /// <summary>Self-test taps: every command sent and every MIDI message written (null in the app).</summary>
    internal Action<EngineCommand>? SentForTest { get; set; }
    internal Action<TimedMidi>? WrittenForTest { get; set; }

    public void SetTransport(double tempo, bool playing) { if (IsRunning) Send(EngineCommand.SetTransport, w => { w.Write(tempo); w.Write(playing); }); }

    /// <summary>RT-04: tempo plus the song's bar map (time signature, bar start and tempo per performed bar) for plug-in transport.</summary>
    public void SetTransport(double tempo, bool playing, TransportBar[] bars)
    {
        if (IsRunning) Send(EngineCommand.SetTransport, w => { w.Write(tempo); w.Write(playing); TransportMap.Write(w, bars); });
    }

    /// <summary>
    /// Opens a plug-in's own window in the engine: <paramref name="docked"/> inside <paramref name="window"/> (a host area
    /// of the FX chain window), else floating, owned by <paramref name="window"/> and optionally always on top.
    /// </summary>
    public bool OpenEditor(TrackModel track, PluginSlot slot, IntPtr window, bool dark, bool docked = false, bool onTop = false)
    {
        if (!TryAddress(track, slot, out var engineSlot, out var index)) return false;
        Send(EngineCommand.OpenEditor, w => { w.Write(engineSlot); w.Write(index); w.Write((long)window); w.Write(dark); w.Write(docked); w.Write(onTop); });
        return true;
    }

    public void CloseEditor(TrackModel track, PluginSlot slot)
    {
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.CloseEditor, w => { w.Write(engineSlot); w.Write(index); });
    }

    /// <summary>Asks for the plug-in's programs; the answer arrives as <see cref="ProgramsReceived"/>.</summary>
    public void RequestPrograms(TrackModel track, PluginSlot slot)
    {
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.GetPrograms, w => { w.Write(engineSlot); w.Write(index); });
    }

    public void SetProgram(TrackModel track, PluginSlot slot, int program)
    {
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.SetProgram, w => { w.Write(engineSlot); w.Write(index); w.Write(program); });
        PresetChanged?.Invoke(track, slot);
    }

    /// <summary>A plug-in's program or preset state was changed from TabForge (automatic pitch matching measures it again).</summary>
    public event Action<TrackModel, PluginSlot>? PresetChanged;
    /// <summary>The automatic pitch matcher (set by the main window).</summary>
    public AutoPitchMatcher? AutoPitch { get; set; }
    /// <summary>An answer to <see cref="MeasurePitch"/>, on the UI thread.</summary>
    public event Action<PitchResult>? PitchMeasured;
    private int _pitchRequest;

    /// <summary>The track playing through an engine slot, or null.</summary>
    public TrackModel? TrackAt(int engineSlot) => _slots.FirstOrDefault(kv => kv.Value == engineSlot).Key;

    /// <summary>Asks the engine to measure an instrument's sounding pitch silently; returns the request id (0: not sent).</summary>
    public int MeasurePitch(TrackModel track, PluginSlot slot, IReadOnlyList<int> notes)
    {
        if (!TryAddress(track, slot, out var engineSlot, out var index)) return 0;
        var id = ++_pitchRequest;
        var channel = Math.Clamp(track.MidiChannel, 0, 15);
        Send(EngineCommand.MeasurePitch, w => { w.Write(engineSlot); w.Write(index); w.Write(id); w.Write(channel); w.Write(notes.Count); foreach (var n in notes) w.Write(n); });
        return id;
    }

    /// <summary>Sends a track's automatic pitch transposes (chain index, semitones) to the engine.</summary>
    public void SetAutoPitch(TrackModel track, IReadOnlyList<(int Index, int Semitones)> list)
    {
        if (!IsRunning || !_slots.TryGetValue(track, out var engineSlot)) return;
        Send(EngineCommand.SetAutoPitch, w => { w.Write(engineSlot); w.Write(list.Count); foreach (var (i, st) in list) { w.Write(i); w.Write(st); } });
    }

    /// <summary>Loads a saved state (a preset) into the running plug-in without reloading it.</summary>
    public void SetState(TrackModel track, PluginSlot slot, string state)
    {
        if (state.Length > InputLimits.MaxPluginStateChars) return;   // the one state size contract (the engine would refuse the frame)
        if (TryAddress(track, slot, out var engineSlot, out var index)) Send(EngineCommand.SetState, w => { w.Write(engineSlot); w.Write(index); w.WriteString(state); });
        PresetChanged?.Invoke(track, slot);
    }

    private bool TryAddress(TrackModel track, PluginSlot slot, out int engineSlot, out int index)
    {
        index = track.Rig.Plugins.IndexOf(slot);
        return IsRunning & _slots.TryGetValue(track, out engineSlot) & index >= 0;
    }

    /// <summary>A plug-in window opened or resized: (engine slot, plug-in index, width, height) on the UI thread.</summary>
    public event Action<int, int, int, int>? EditorSized;
    /// <summary>The user closed a floating plug-in window.</summary>
    public event Action? EditorClosed;
    /// <summary>A plug-in's programs: (engine slot, plug-in index, current, names) on the UI thread.</summary>
    public event Action<int, int, int, IReadOnlyList<string>>? ProgramsReceived;

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

    /// <summary>One outstanding state request: the engine answers one frame per plug-in.</summary>
    internal sealed class StateRequest
    {
        public int Id { get; init; }
        public int Slot { get; init; }
        /// <summary>Chain length the engine reported; -1 until the first frame arrives.</summary>
        public int Expected = -1;
        public int Received;
        public readonly Dictionary<int, (PluginStateStatus Status, string State)> Replies = new();
        public readonly TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private int _nextStateRequest;
    private int _nextChainLoad;

    internal StateRequest BeginStateRequest(int slot)
    {
        var request = new StateRequest { Id = Interlocked.Increment(ref _nextStateRequest), Slot = slot };
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
        for (var i = 0; i < track.Rig.Plugins.Count; i++)
        {
            var plugin = track.Rig.Plugins[i];
            PluginStateOutcome outcome;
            if (expected >= 0 && i >= expected) outcome = PluginStateOutcome.Unchanged;   // not in the live chain: its stored state is its state
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

    private bool Start()
    {
        _stopping = false;
        var session = Guid.NewGuid().ToString("N");
        lock (_writeGate) { _retiredShared?.Dispose(); _retiredShared = null; }
        try
        {
            Volatile.Write(ref _shared, SharedBlock.Create(EngineNames.SharedMemory(session)));
            _pipe = new NamedPipeServerStream(EngineNames.Pipe(session), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("TabForge's own path is unknown.");
            _process = Process.Start(new ProcessStartInfo(exe, $"--audio-engine {session} {Environment.ProcessId}")
            {
                UseShellExecute = false, CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("The audio engine did not start.");
            ChildProcessJob.ForThisProcess.Add(_process);   // R-08: ends with TabForge, however TabForge ends
            _process.EnableRaisingEvents = true;
            // R-08: no blocking wait here. Commands sent until the engine connects are queued in order and flushed on connect
            // (Configure first), so Sync returns at once; the engine answers Ready once its device is open.
            lock (_sendGate) _queued = new List<byte[]>();
            _deafKill = false;
            IsRunning = true;
            _config = null; Output = null;
            var process = _process;
            var pipe = _pipe;
            process.Exited += (_, _) => OnEngineEnded(process);
            _ = ConnectAsync(pipe, process);
            RaiseOnUi(() => StatusChanged?.Invoke());
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or AggregateException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            RaiseOnUi(() => DeviceError?.Invoke($"The audio engine could not start: {ex.GetBaseException().Message}"));
            KillQuietly(_process);
            Cleanup();
            return false;
        }
    }

    /// <summary>Waits (off the UI thread) for the engine to connect: then flushes the queued commands and starts reading and pinging.</summary>
    private async Task ConnectAsync(NamedPipeServerStream pipe, Process process)
    {
        var connected = false;
        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromMilliseconds(ConnectTimeoutMs)).ConfigureAwait(false);
            connected = true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
        if (!connected)
        {
            if (!ReferenceEquals(pipe, _pipe)) return;   // stopped meanwhile
            KillQuietly(process);                         // R-08: the timed-out child does not linger
            RaiseOnUi(() =>
            {
                if (!ReferenceEquals(pipe, _pipe)) return;
                DeviceError?.Invoke("The audio engine could not start: it did not answer.");
                Cleanup();
                StatusChanged?.Invoke();
            });
            return;
        }
        lock (_sendGate)
        {
            if (!ReferenceEquals(pipe, _pipe)) return;
            try { foreach (var frame in _queued ?? new List<byte[]>()) pipe.Write(frame); pipe.Flush(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* the engine is gone; OnEngineEnded handles it */ }
            _queued = null;
        }
        Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp());
        new Thread(() => ReadEvents(pipe)) { IsBackground = true, Name = "TabForge engine events" }.Start();
        var timer = new Timer(_ => CheckLiveness(pipe, process), null, 1000, 1000);
        Interlocked.Exchange(ref _liveness, timer)?.Dispose();
    }

    private const int ConnectTimeoutMs = 8000;
    private readonly object _sendGate = new();
    /// <summary>Frames sent while the engine is still connecting (null once connected).</summary>
    private List<byte[]>? _queued;
    private Timer? _liveness;
    private long _lastAlive;
    private int _pingSeq;
    private volatile bool _deafKill, _awaitingReady;

    /// <summary>
    /// R-05, once a second (timer thread): pings the engine, whose main thread answers. No answer for
    /// <see cref="EngineWatchdog.DeafLimitSec"/> while no plug-in call was in progress, no device was being opened and nothing renders:
    /// the engine is deaf; it is killed and the crash path restarts it, quarantining nothing (see <see cref="OnEngineEnded"/>). Work
    /// that is attributed to a plug-in call is judged by the engine's own watchdog (per-kind limits).
    /// </summary>
    private void CheckLiveness(NamedPipeServerStream pipe, Process process)
    {
        if (!ReferenceEquals(pipe, _pipe) || !IsRunning || _stopping || _deafKill) return;
        var now = Stopwatch.GetTimestamp();
        var busyAttributed = Volatile.Read(ref _shared)?.MainCallRecent(EngineWatchdog.DeafLimitSec) ?? false;
        if (busyAttributed || _awaitingReady || Rendering || _renderTask is { Task.IsCompleted: false }) Volatile.Write(ref _lastAlive, now);
        if (now - Volatile.Read(ref _lastAlive) > EngineWatchdog.DeafLimitSec * Stopwatch.Frequency)
        {
            _deafKill = true;
            Debug.WriteLine($"audio engine: no answer for {EngineWatchdog.DeafLimitSec} s outside plug-in calls; restarting it");
            KillQuietly(process);
            return;
        }
        Send(EngineCommand.Ping, w => w.Write(Interlocked.Increment(ref _pingSeq)));
    }

    private static void KillQuietly(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    /// <summary>Stops the engine now (app exit, or the warm period ended; see <see cref="WarmIdle"/>). A Sync without engine tracks no longer does.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        _stopping = true;
        Send(EngineCommand.Shutdown);
        try { if (_process is { HasExited: false } p && !p.WaitForExit(1500)) p.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        Cleanup();
        RaiseOnUi(() => StatusChanged?.Invoke());
    }

    /// <summary>
    /// Process.Exited (a thread-pool thread): only the breadcrumb is read here (the block stays mapped until the next Start).
    /// Everything else, including Cleanup of the UI-owned dictionaries, runs on the UI thread, and only if that engine is
    /// still the current one (a Stop or a restart in between wins).
    /// </summary>
    private void OnEngineEnded(Process? process)
    {
        if (_stopping || !IsRunning) return;
        // Unexpected exit: which plug-in call was running? Only a crash or an attributed hang blames one. A deaf-engine restart (R-05)
        // or the engine's own unattributed-hang exit (71) quarantines nothing, even if an audio block happened to be in a plug-in.
        var exitCode = ExitCodeOf(process);
        var unattributed = _deafKill || exitCode == EngineWatchdog.ExitUnattributedHang;
        var shared = Volatile.Read(ref _shared);
        var crumb = unattributed ? null : exitCode == EngineWatchdog.ExitAudioHung ? shared?.AudioPluginCall() : shared?.LastPluginCall();
        var crumbPath = crumb?.Path ?? "";
        RaiseOnUi(() =>
        {
            if (_stopping || !IsRunning || !ReferenceEquals(process, _process)) return;
            FailRender(new RenderException("The audio engine stopped while rendering" + (crumbPath.Length > 0 ? $" (in {System.IO.Path.GetFileName(crumbPath)})" : "") + ".", pluginPath: crumbPath));
            Cleanup();
            var path = crumb?.Path;
            if (path is { Length: > 0 })
            {
                var quarantine = Quarantine?.Invoke();
                if (quarantine is not null && !quarantine.Contains(path, StringComparer.OrdinalIgnoreCase)) quarantine.Add(path);
            }
            PluginCrashed?.Invoke(path ?? "");
            _crashes.Add(DateTime.UtcNow);
            _crashes.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(1));
            StatusChanged?.Invoke();
        });
    }

    private static int? ExitCodeOf(Process? process)
    {
        try { return process is { HasExited: true } ? process.ExitCode : null; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>After a crash the app calls Sync again; this says whether restarting is still sensible.</summary>
    public bool TooManyCrashes => _crashes.Count >= 3;

    private void Cleanup()
    {
        IsRunning = false;
        FailRender(new RenderException("The audio engine was stopped."));
        _slots.Clear();
        _sentChains.Clear();
        _chainRequests.Clear();
        _lastAcks.Clear();
        _sentAudio.Clear();
        _sentRoutes.Clear();
        _sentProcessors.Clear();
        _sentWiring.Clear();
        _sentBypass.Clear();
        _sentGain.Clear();
        _sentSynth.Clear();
        _sentGraph = "";
        ResetWarmState();   // Stop, a crash or a failed start: nothing stays parked, no warm timer is left armed
        lock (_gate)
        {
            foreach (var request in _stateRequests.Values) request.Done.TrySetResult(false);   // unanswered plug-ins report as timed out
            _stateRequests.Clear();
        }
        Interlocked.Exchange(ref _liveness, null)?.Dispose();
        lock (_sendGate)
        {
            _queued = null;
            try { _pipe?.Dispose(); } catch (IOException) { }
            _pipe = null;
        }
        _awaitingReady = false;
        // Unpublished, not disposed: a producer (scheduler thread) may have read it just before; see Write / Start.
        lock (_writeGate)
        {
            if (Volatile.Read(ref _shared) is { } shared) { _retiredShared?.Dispose(); _retiredShared = shared; }
            Volatile.Write(ref _shared, null);
        }
        _process?.Dispose();
        _process = null;
        _config = null; Output = null;
    }

    // ---- offline render (RenderProtocol.cs) ----
    private TaskCompletionSource<RenderResult>? _renderTask;
    private IProgress<RenderProgressInfo>? _renderProgress;

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

    private void Send(EngineCommand command, Action<BinaryWriter>? payload = null)
    {
        SentForTest?.Invoke(command);
        if (command == EngineCommand.Configure) _awaitingReady = true;   // opening a device can take seconds: liveness waits for Ready
        lock (_sendGate)
        {
            if (_queued is { } queue)   // still connecting: keep the order, flushed on connect
            {
                using var frame = new MemoryStream();
                Frames.Write(frame, (byte)command, payload);
                queue.Add(frame.ToArray());
                return;
            }
        }
        var pipe = _pipe;
        if (pipe is not { IsConnected: true }) return;
        try { Frames.Write(pipe, (byte)command, payload); }
        catch (IOException) { /* the engine is gone; OnEngineEnded handles it */ }
        catch (ObjectDisposedException) { }
    }

    private void ReadEvents(NamedPipeServerStream pipe)
    {
        try
        {
            while (pipe is { IsConnected: true })
            {
                var frame = Frames.Read(pipe);
                if (frame is null) break;
                var (type, r) = frame.Value;
                switch ((EngineEvent)type)
                {
                    case EngineEvent.Pong: Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp()); break;
                    case EngineEvent.PluginSlow:
                    {
                        r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024); var kind = (PluginCallKind)r.ReadInt32(); var seconds = r.ReadInt32();
                        RaiseOnUi(() => PluginSlow?.Invoke(path, kind, seconds));
                        break;
                    }
                    case EngineEvent.Ready:
                    {
                        _awaitingReady = false;
                        Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp());
                        var rate = r.ReadInt32(); var latency = r.ReadInt32(); var description = r.ReadBoundedString(512);
                        var buffer = r.ReadInt32();
                        var inNames = new string[Math.Clamp(r.ReadInt32(), 0, 128)];
                        for (var i = 0; i < inNames.Length; i++) inNames[i] = r.ReadBoundedString(128);
                        var outNames = new string[Math.Clamp(r.ReadInt32(), 0, 128)];
                        for (var i = 0; i < outNames.Length; i++) outNames[i] = r.ReadBoundedString(128);
                        if (inNames.Length + outNames.Length > 0) AsioChannels = (inNames, outNames);
                        DeviceDescription = $"{description} · {rate} Hz · {latency} ms";
                        if (_config is { } running) Output = (running.Driver, description, rate, buffer, latency);
                        RaiseOnUi(() => StatusChanged?.Invoke());
                        break;
                    }
                    case EngineEvent.ChainLoaded:
                    {
                        var slot = r.ReadInt32(); r.ReadInt32();
                        var generation = r.ReadInt32();
                        var results = new List<PluginLoadResult>();
                        for (int i = 0, n = Math.Clamp(r.ReadInt32(), 0, 512); i < n; i++)
                            results.Add(new PluginLoadResult(r.ReadInt32(), (PluginLoadStatus)r.ReadByte(), r.ReadBoundedString(1024)));
                        var ack = new ChainAck(slot, generation, results);
                        foreach (var changed in results.Where(x => x.Status == PluginLoadStatus.BlockedChanged)) PluginTrust.MarkChanged(changed.Path);   // the file no longer matches its approval: untrusted until approved again
                        RaiseOnUi(() => { _lastAcks[ack.Slot] = ack; ChainAcknowledged?.Invoke(ack); ChainLoaded?.Invoke(); });
                        break;
                    }
                    case EngineEvent.DeviceError: { _awaitingReady = false; var message = r.ReadBoundedString(); RaiseOnUi(() => DeviceError?.Invoke(message)); break; }
                    case EngineEvent.PluginFailed:
                    {
                        r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024); var why = r.ReadBoundedString();
                        RaiseOnUi(() => PluginFailed?.Invoke(path, why));
                        break;
                    }
                    case EngineEvent.PluginMisbehaved:
                    {
                        r.ReadInt32(); var index = r.ReadInt32(); var path = r.ReadBoundedString(1024);
                        RaiseOnUi(() => PluginMisbehaved?.Invoke(path, index));
                        break;
                    }
                    case EngineEvent.PluginState: OnPluginState(r); break;
                    case EngineEvent.PitchMeasured:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var id = r.ReadInt32(); var status = (PitchMatch.Status)r.ReadByte();
                        var transpose = Math.Clamp(r.ReadInt32(), -48, 48); var confidence = r.ReadDouble();
                        var n = Math.Clamp(r.ReadInt32(), 0, 8);
                        var notes = new int[n]; var offsets = new double[n];
                        for (var i = 0; i < n; i++) { notes[i] = r.ReadInt32(); offsets[i] = r.ReadDouble(); }
                        var result = new PitchResult(slot, index, id, status, transpose, confidence, notes, offsets);
                        RaiseOnUi(() => PitchMeasured?.Invoke(result));
                        break;
                    }
                    case EngineEvent.StateChanged: RaiseOnUi(() => PluginEdited?.Invoke()); break;
                    case EngineEvent.Recorded:
                    {
                        var slot = r.ReadInt32(); var path = r.ReadBoundedString(1024); var start = r.ReadDouble(); var length = r.ReadDouble();
                        RaiseOnUi(() => { if (_recordingTracks.TryGetValue(slot, out var track)) Recorded?.Invoke(track, path, start, length); });
                        break;
                    }
                    case EngineEvent.InputError: { var message = r.ReadBoundedString(); RaiseOnUi(() => InputError?.Invoke(message)); break; }
                    case EngineEvent.EditorClosed: RaiseOnUi(() => EditorClosed?.Invoke()); break;
                    case EngineEvent.EditorSize:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var width = Math.Clamp(r.ReadInt32(), 0, 8192); var height = Math.Clamp(r.ReadInt32(), 0, 8192);
                        RaiseOnUi(() => EditorSized?.Invoke(slot, index, width, height));
                        break;
                    }
                    case EngineEvent.Programs:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var current = r.ReadInt32(); var count = Math.Clamp(r.ReadInt32(), 0, 512);
                        var names = new List<string>(count);
                        for (var i = 0; i < count; i++) names.Add(r.ReadBoundedString(256));
                        RaiseOnUi(() => ProgramsReceived?.Invoke(slot, index, current, names));
                        break;
                    }
                    case EngineEvent.MidiLog:
                    {
                        var slot = r.ReadInt32(); var count = Math.Clamp(r.ReadInt32(), 0, 4096);
                        var lines = new MidiLogLine[count];
                        for (var i = 0; i < count; i++) lines[i] = new MidiLogLine(r.ReadSingle(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                        RaiseOnUi(() => MidiLogReceived?.Invoke(slot, lines));
                        break;
                    }
                    case EngineEvent.RenderProgress:
                    {
                        var info = new RenderProgressInfo(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                        var sink = _renderProgress;
                        if (sink is not null) RaiseOnUi(() => sink.Report(info));
                        break;
                    }
                    case EngineEvent.RenderDone:
                    {
                        var result = RenderResult.Read(r);
                        var source = _renderTask; _renderTask = null; _renderProgress = null;
                        source?.TrySetResult(result);
                        break;
                    }
                    case EngineEvent.RenderFailed:
                    {
                        var message = r.ReadBoundedString(); var cancelled = r.ReadBoolean(); var plugin = r.ReadBoundedString(1024);
                        FailRender(new RenderException(message, cancelled, plugin));
                        break;
                    }
                    case EngineEvent.PluginCrashed:
                    {
                        var slot = r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024);
                        RaiseOnUi(() =>
                        {
                            var quarantine = Quarantine?.Invoke();
                            if (quarantine is not null && !quarantine.Contains(path, StringComparer.OrdinalIgnoreCase)) quarantine.Add(path);
                            _sentChains.Remove(slot); // the next Sync reloads this track's chain without it
                            PluginCrashed?.Invoke(path);
                        });
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException) { }
    }

    private void RaiseOnUi(Action action)
    {
        if (_ui is { } context) context.Post(_ => action(), null); else action();
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

    public void Dispose() => Stop();
}
