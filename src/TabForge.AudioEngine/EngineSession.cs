using System.Diagnostics;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Midi;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;

namespace TabForge.AudioEngine;

/// <summary>
/// The engine's per-session state and its command handlers, one instance per engine run. <see cref="EngineHost.Run"/> creates it;
/// the headless harness uses the one created with the process. <see cref="EngineHost"/> keeps only the process and IPC side (pipe,
/// shared block, command reader, watchdog, main loop); it parses each command on the reader thread and posts the matching handler
/// here. Unless noted, every member belongs to the engine main thread (checked with <see cref="AssertMain"/> in Debug builds).
/// </summary>
internal sealed partial class EngineSession
{
    /// <summary>One transport per song owner (position, tempo, bar map), shared by every MixEngine this process creates (written by the command reader thread). Allocated once, never grown.</summary>
    public readonly SongTransport[] Transports = CreateTransports();
    /// <summary>Owner 0: the transport of a sender that names no owner, and the one plug-ins see.</summary>
    public SongTransport Transport => Transports[0];
    private static SongTransport[] CreateTransports() { var t = new SongTransport[SongOwners.Max]; for (var i = 0; i < t.Length; i++) t[i] = new SongTransport(); return t; }
    /// <summary>Per-slot owner of the song whose position drives the slot's clips, kept across chain rebuilds.</summary>
    public readonly Dictionary<int, int> ClipOwners = new();
    public readonly Dictionary<int, (string Track, bool UseMidiSynth, List<PluginSpec> Specs)> Requested = new();
    public readonly Dictionary<int, TrackChain> Loaded = new();
    /// <summary>Reader thread only: chain loads whose per-plug-in state frames are still arriving.</summary>
    public readonly ChainLoadProtocol.Assembler ChainLoads = new();
    // Per-slot audio state, kept across chain rebuilds.
    public readonly Dictionary<int, (int Volume, int Pan)> Mixes = new();
    public readonly Dictionary<int, List<ClipSpec>> ClipSpecs = new();
    public readonly Dictionary<int, int> Arms = new();
    /// <summary>Armed slots whose input is not played live (recorded and metered only).</summary>
    public readonly HashSet<int> Unmonitored = new();
    /// <summary>Per-slot instrument MIDI input routing (chain index, source slot: -1 own / -2 none, channel mask), kept across chain rebuilds.</summary>
    public readonly Dictionary<int, (int Index, int Source, int Mask)> Routes = new();
    /// <summary>Per-slot MIDI processor lists by plug-in index, kept across chain rebuilds.</summary>
    public readonly Dictionary<int, Dictionary<int, List<MidiProcSpec>>> MidiProcs = new();
    /// <summary>Routing graph from TabForge: track slot → bus slot, sidechains and MIDI forwards by (slot, plug-in index).</summary>
    public readonly Dictionary<int, int> GraphDest = new();
    public readonly Dictionary<(int Slot, int Index), int> Sidechains = new();
    public readonly Dictionary<(int Slot, int Index), int> Forwards = new();
    /// <summary>Serial-chain flags per (slot, plug-in id), kept across chain rebuilds (see EngineCommand.SetPluginWiring).</summary>
    public readonly Dictionary<(int, string), int> Wirings = new();
    /// <summary>Slots whose MIDI log the UI is watching.</summary>
    public readonly HashSet<int> LogWatch = new();
    public readonly List<MidiLogEntry> LogScratch = new();
    public readonly List<(int Index, string Path)> MisbehaveScratch = new();
    /// <summary>Per-slot count of LoadChain requests (the generation reported in ChainLoaded; the client counts the same way).</summary>
    public readonly Dictionary<int, int> ChainGeneration = new();
    /// <summary>Bypassed plug-ins, kept loaded (with their live settings) so switching them back on is instant.</summary>
    public readonly Dictionary<int, Dictionary<string, IPluginInstance>> Parked = new();
    /// <summary>
    /// Retired chains / plug-ins / clip players: disposed on the main loop's thread once the audio callback has
    /// provably let go of them (epoch barrier, not a timer); Shutdown disposes whatever is left.
    /// </summary>
    public readonly RetireQueue Retired = new();

    /// <summary>The live mixer. Written on the main thread (Volatile.Write in Configure); the reader thread reads it for Panic.</summary>
    public MixEngine? Mix;
    public IWavePlayer? Player;
    public EngineConfig Config = new(AudioOutputFactory.WasapiShared, "", 48000, 256, false);
    public int SampleRate = 48000;
    public Audio.InputCapture? Input;
    public Audio.Recorder? Recorder;
    /// <summary>TabForge's process id (its Volume Mixer session is followed on ASIO).</summary>
    public int UiProcessId;
    /// <summary>Headless tests only: replaces plug-in creation (rate, max block).</summary>
    public Func<PluginSpec, double, int, IPluginInstance>? PluginFactory;

    /// <summary>
    /// Debug builds only (the call sites vanish in Release): the handler runs on the engine main thread. Never called from audio-thread code.
    /// </summary>
    [Conditional("DEBUG")]
    internal static void AssertMain() => Debug.Assert(EngineThreads.IsMainThread, "engine session state touched off the engine main thread");

    private static void Send(EngineEvent type, Action<BinaryWriter>? payload = null) => EngineHost.Send(type, payload);
    private static SharedBlock? Shared => EngineHost.Shared;

    // ---- Command handlers (posted by EngineHost.HandleCommand) ----

    /// <summary>ChainCommit: builds the chain, then reports every plug-in whose saved state did not arrive.</summary>
    public void CommitChain(ChainLoadProtocol.Ready ready)
    {
        AssertMain();
        LoadChain(ready.Slot, ready.Track, ready.UseSynth, ready.Specs);
        foreach (var index in ready.Missing)
        {
            var path = ready.Specs[index].Path;
            EngineLog.Write($"saved state did not arrive for {path}; loaded with its default settings");
            Send(EngineEvent.PluginFailed, w => { w.Write(ready.Slot); w.Write(index); w.WriteString(path); w.WriteString("Its saved settings could not be loaded; it runs with its default settings."); });
        }
    }

    public void SetTrackMix(int slot, int volume, int pan)
    {
        AssertMain();
        Mixes[slot] = (volume, pan);
        if (Loaded.TryGetValue(slot, out var c)) c.SetMix(volume, pan);
    }

    public void SetPluginGain(int slot, int index, float gain)
    {
        AssertMain();
        if (Loaded.TryGetValue(slot, out var c)) c.SetPluginGain(index, gain);
    }

    public void SetPluginBypass(int slot, int index, bool enabled)
    {
        AssertMain();
        if (Loaded.TryGetValue(slot, out var c)) c.SetPluginBypass(index, !enabled);
    }

    public void SetSynth(int slot, bool on)
    {
        AssertMain();
        MixEngine.SynthOff[slot] = !on;
        if (Loaded.TryGetValue(slot, out var c)) c.SetSynthOn(on);
    }

    public void SetMidiRoute(int slot, int index, int source, int mask)
    {
        AssertMain();
        Routes[slot] = (index, source, mask);
        ApplyRoute(slot);
    }

    public void SetPluginWiring(int slot, int index, int flags)
    {
        AssertMain();
        if (!Loaded.TryGetValue(slot, out var c)) return;
        var id = c.IdAt(index);
        if (id is not null) Wirings[(slot, id)] = flags;
        c.SetPluginWiring(index, flags);
    }

    public void SetMidiProcessors(int slot, Dictionary<int, List<MidiProcSpec>> lists)
    {
        AssertMain();
        if (lists.Count == 0) MidiProcs.Remove(slot); else MidiProcs[slot] = lists;
        ApplyMidiProcs(slot);
    }

    public void SetGraph(Dictionary<int, int> dests, Dictionary<(int, int), int> sides, Dictionary<(int, int), int> fwds)
    {
        AssertMain();
        GraphDest.Clear(); foreach (var (k, v) in dests) GraphDest[k] = v;
        Sidechains.Clear(); foreach (var (k, v) in sides) Sidechains[k] = v;
        Forwards.Clear(); foreach (var (k, v) in fwds) Forwards[k] = v;
        ApplyGraph();
    }

    public void SetMidiLogWatch(int slot, bool watch)
    {
        AssertMain();
        if (watch) LogWatch.Add(slot); else LogWatch.Remove(slot);
        if (Loaded.TryGetValue(slot, out var c)) c.MidiLog.Watching = watch;
    }

    /// <summary>SetClips command: waits for a running render like every chain change.</summary>
    public void SetClips(int slot, List<ClipSpec> clips, int owner)
    {
        AssertMain();
        void Apply() => StoreClips(slot, clips, owner);
        if (!Defer(Apply)) Apply();
    }

    /// <summary>Stores a track's clips and applies them to its loaded chain (no render check: the headless harness calls it directly).</summary>
    public void StoreClips(int slot, List<ClipSpec> clips, int owner = 0)
    {
        AssertMain();
        ClipSpecs[slot] = clips;
        ClipOwners[slot] = owner;
        if (Loaded.TryGetValue(slot, out var c)) ApplyClips(c, clips, owner);
    }

    public void SetArm(int slot, bool armed, int mode, bool monitor)
    {
        AssertMain();
        if (armed) { Arms[slot] = mode; if (monitor) Unmonitored.Remove(slot); else Unmonitored.Add(slot); }
        else { Arms.Remove(slot); Unmonitored.Remove(slot); }
        ApplyArms();
    }

    public void Record(bool start, string folder, Dictionary<int, string> names, double offsetMs, int owner = 0)
    {
        AssertMain();
        if (start) StartRecording(folder, names, offsetMs, owner); else StopRecording();
    }

    public void CloseEditor(int slot, int index)
    {
        AssertMain();
        if (PluginAt(slot, index) is { } p) EditorWindows.CloseFor(p);
    }

    public void SetProgram(int slot, int index, int program)
    {
        AssertMain();
        if (PluginAt(slot, index) is Vst2Plugin v) { v.SetProgram(program); SendPrograms(slot, index); }
    }

    public void SetState(int slot, int index, string state)
    {
        AssertMain();
        if (PluginAt(slot, index) is not { } p) return;
        var entered = EnterCall(slot, index, p, p.Path, PluginCallKind.SetState);
        try { p.SetState(Convert.FromBase64String(state)); }
        catch (FormatException) { EngineLog.Write("preset state was not valid base64"); }
        finally { LeaveCall(entered); }
    }

    /// <summary>The live master safety limiter (off by default); survives a device change.</summary>
    public bool LiveLimiter { get; private set; }

    public void SetLiveLimiter(bool on)
    {
        AssertMain();
        LiveLimiter = on;
        if (Mix is { } mix) mix.LiveLimiter = on;
        EngineLog.Write($"live safety limiter {(on ? "on" : "off")}");
    }

    public void SetWindowsPathOffset(float db)
    {
        AssertMain();
        _pathOffsetGain = Gain.FromDb(db);
        EngineLog.Write($"Windows audio path offset {db:0.00} dB");
        ApplyFollowedGain();
    }

    // ---- Graph, routing, MIDI ----

    /// <summary>
    /// Engine thread: puts the stored sidechain / forward links on the loaded plug-ins and publishes the render graph: tracks sorted
    /// so every sidechain / MIDI-forward source renders before its destination (a cycle, which the UI rejects, falls back to slot order).
    /// </summary>
    private void ApplyGraph()
    {
        AssertMain();
        if (Mix is null) return;
        foreach (var (slot, chain) in Loaded)
            foreach (var e in chain.Effects)
            {
                e.SideSlot = Sidechains.TryGetValue((slot, e.Index), out var s) && s != slot ? s : -1;
                e.ForwardSlot = Forwards.TryGetValue((slot, e.Index), out var f) && f != slot ? f : -1;
            }
        var tracks = Loaded.Keys.Where(s => s < MixEngine.MaxSlots).OrderBy(s => s).ToList();
        var edges = new List<(int From, int To)>();
        foreach (var ((slot, _), src) in Sidechains) if (src != slot) edges.Add((src, slot));
        foreach (var ((slot, _), dst) in Forwards) if (dst != slot) edges.Add((slot, dst));
        var set = tracks.ToHashSet();
        edges.RemoveAll(e => !set.Contains(e.From) || !set.Contains(e.To));
        var indegree = tracks.ToDictionary(s => s, s => edges.Count(e => e.To == s));
        var order = new List<int>();
        var ready = new SortedSet<int>(tracks.Where(s => indegree[s] == 0));
        while (ready.Count > 0)
        {
            var s = ready.Min; ready.Remove(s); order.Add(s);
            foreach (var e in edges.Where(e => e.From == s)) if (--indegree[e.To] == 0) ready.Add(e.To);
        }
        if (order.Count < tracks.Count)
        {
            EngineLog.Write("routing graph has a cycle; the tracks in it render in slot order");
            order.AddRange(tracks.Where(s => !order.Contains(s)));
        }
        var dest = MixEngine.RenderGraph.NewDest();
        foreach (var (slot, bus) in GraphDest) if (slot is >= 0 and < MixEngine.MaxSlots) dest[slot] = bus;
        var buses = Loaded.Keys.Where(s => s >= MixEngine.BusBase && s < MixEngine.MasterSlot).OrderBy(s => s).ToArray();
        Mix.SetGraph(new MixEngine.RenderGraph(order.ToArray(), dest, buses));
    }

    /// <summary>
    /// Engine thread: builds each plug-in's MIDI processor chain from its stored list and publishes it (a whole new immutable instance
    /// per plug-in; the audio thread swaps it in right before that plug-in, carrying held notes over or releasing them).
    /// </summary>
    private void ApplyMidiProcs(int slot)
    {
        AssertMain();
        if (!Loaded.TryGetValue(slot, out var chain)) return;
        MidiProcs.TryGetValue(slot, out var lists);
        foreach (var e in chain.Effects)
        {
            MidiProcessorChain? built = null;
            if (lists is not null && lists.TryGetValue(e.Index, out var specs))
            {
                try { built = MidiProcessorChain.Create(specs, SampleRate); }
                catch (Exception ex) { EngineLog.Write($"MIDI processors could not be built: {ex.Message}"); }
            }
            e.Processors = built;
        }
    }

    /// <summary>Engine thread, ~20 Hz: sends the MIDI log entries of watched tracks to the UI.</summary>
    public void PumpMidiLog()
    {
        AssertMain();
        foreach (var slot in LogWatch)
        {
            if (!Loaded.TryGetValue(slot, out var chain)) continue;
            LogScratch.Clear();
            if (chain.MidiLog.Read(LogScratch, 256) == 0) continue;
            var entries = LogScratch.ToArray();
            Send(EngineEvent.MidiLog, w =>
            {
                w.Write(slot); w.Write(entries.Length);
                foreach (var e in entries) { w.Write(e.TimeSec); w.Write(e.Status); w.Write(e.Data1); w.Write(e.Data2); w.Write(e.Stage); }
            });
        }
    }

    /// <summary>Engine thread: applies a slot's stored route to its loaded chain and republishes the fan-out table the audio thread reads.</summary>
    private void ApplyRoute(int slot)
    {
        AssertMain();
        if (Loaded.TryGetValue(slot, out var chain) && Routes.TryGetValue(slot, out var route) && chain.InstrumentIndex == route.Index)
            chain.SetRoute(route.Source, route.Mask);
        else if (Loaded.TryGetValue(slot, out chain)) chain.SetRoute(-1, 0xFFFF);
        var lists = new Dictionary<int, List<int>>();
        foreach (var (dest, c) in Loaded)
        {
            if (!Routes.TryGetValue(dest, out var r) || c.InstrumentIndex != r.Index || r.Source < 0 || r.Source == dest) continue;
            if (!lists.TryGetValue(r.Source, out var list)) lists[r.Source] = list = new List<int>();
            list.Add(dest);
        }
        var table = new int[MixEngine.MaxSlots][];
        foreach (var (source, list) in lists) table[source] = list.ToArray();
        Mix?.SetFanout(table);
    }

    private int _misbehaveSeen;

    /// <summary>
    /// Engine main thread: sends <see cref="EngineEvent.PluginMisbehaved"/> once for each plug-in (or track sound) the audio thread
    /// caught producing non-finite audio. One read per loop turn while nothing happened.
    /// </summary>
    public int ReportMisbehaving()
    {
        AssertMain();
        var signal = Volatile.Read(ref TrackChain.MisbehaveSignal);
        if (signal == _misbehaveSeen) return 0;
        _misbehaveSeen = signal;
        var sent = 0;
        foreach (var (slot, chain) in Loaded)
        {
            MisbehaveScratch.Clear();
            chain.CollectMisbehaved(MisbehaveScratch);
            foreach (var (index, path) in MisbehaveScratch)
            {
                EngineLog.Write($"non-finite audio (NaN / infinity) from {(index >= 0 ? path : "the track's own sound")} on slot {slot}: " +
                                (index >= 0 ? "output discarded, the plug-in is skipped until it is switched on again" : "block silenced"));
                Send(EngineEvent.PluginMisbehaved, w => { w.Write(slot); w.Write(index); w.WriteString(path); });
                sent++;
            }
        }
        return sent;
    }

    // ---- Device ----

    /// <summary>Opens the audio device on the current mix and starts it (also reopens it after an offline render).</summary>
    private (int LatencyMs, string Description) OpenOutput()
    {
        AssertMain();
        var (player, latencyMs, description) = AudioOutputFactory.Open(Config, Mix!);
        Player = player;
        _asioBufferFrames = 0;
        if (player is NAudio.Wave.AsioOut asioOut)
        {
            _asioBufferFrames = asioOut.FramesPerBuffer;
            PrepareAsioScratch(_asioBufferFrames, Math.Max(2, AudioOutputFactory.AsioInputChannelsUsed));
            asioOut.AudioAvailable += OnAsioInput;
        }
        Mix!.OutputLatencyTicks = latencyMs * Stopwatch.Frequency / 1000;
        Shared!.LatencyTicks = Mix.DelayTicks;
        Shared.SampleRate = SampleRate;
        Player.PlaybackStopped += (_, e) => { if (e.Exception is { } ex) Send(EngineEvent.DeviceError, w => w.WriteString(ex.Message)); };
        Mix.ResetClock();
        Player.Play();
        FollowWindowsVolume(Config.Driver == AudioOutputFactory.Asio && Config.FollowWindowsVolume);
        return (latencyMs, description);
    }

    public void Configure(EngineConfig config)
    {
        AssertMain();
        if (Defer(() => Configure(config))) return;
        var oldBlock = Math.Clamp(Config.BufferSize, 16, 8192);
        var oldRate = SampleRate;
        Config = config;
        StopOutput();   // from here no audio callback runs until OpenOutput
        try
        {
            SampleRate = AudioOutputFactory.SampleRateFor(config);
            var maxBlock = Math.Clamp(config.BufferSize, 16, 8192);
            // The live plug-ins follow the new rate / block size in place (they keep their settings); see ReconfigureLive.
            if (SampleRate != oldRate || maxBlock != oldBlock) ReconfigureLive(SampleRate, maxBlock);
            var previous = Loaded.Values.ToList();
            Volatile.Write(ref Mix, new MixEngine(Shared!, SampleRate, maxBlock, Transports));   // the reader thread reads it (Panic)
            Mix!.LiveLimiter = LiveLimiter;
            if (config.Driver == AudioOutputFactory.WasapiShared) Mix!.Ceiling = 8f;   // +18 dB: the Windows float mixer clips after its volume
            // Chains depend on the sample rate and block size (their buffers): rebuild every requested chain for the new engine.
            foreach (var (slot, request) in Requested.ToList()) BuildChain(slot, request.Track, request.UseMidiSynth, request.Specs);
            // The fresh mix never held the previous chains (SetChain returned null), and the old device is stopped: nothing can be
            // inside them, so they are disposed now (carried-over plug-ins are in their Kept sets; their clip players leave the disk thread).
            foreach (var chain in previous)
            {
                if (Loaded.ContainsValue(chain)) continue;   // not rebuilt (cannot happen for a requested slot): still in use
                foreach (var plugin in chain.Plugins) if (!chain.Kept.Contains(plugin)) EditorWindows.CloseFor(plugin);
                try { chain.Dispose(); } catch (Exception ex) { EngineLog.Write($"dispose failed: {ex.Message}"); }
            }
            if (Input is not null) { Input.Dispose(); Input = null; }
            ApplyArms();
            var (latencyMs, description) = OpenOutput();
            var bufferFrames = _asioBufferFrames > 0 ? _asioBufferFrames : Math.Clamp(config.BufferSize, 16, 8192);
            var channels = config.Driver == AudioOutputFactory.Asio ? AudioOutputFactory.AsioChannels : (Array.Empty<string>(), Array.Empty<string>());
            Send(EngineEvent.Ready, w =>
            {
                w.Write(SampleRate); w.Write(latencyMs); w.WriteString(description); w.Write(bufferFrames);
                w.Write(channels.Item1.Length); foreach (var n in channels.Item1) w.WriteString(n);
                w.Write(channels.Item2.Length); foreach (var n in channels.Item2) w.WriteString(n);
            });
            EngineLog.Write($"output started: {description}, {SampleRate} Hz, {latencyMs} ms");
        }
        catch (Exception ex)
        {
            EngineLog.Write($"output failed: {ex}");
            Send(EngineEvent.DeviceError, w => w.WriteString(ex.GetBaseException().Message));
        }
    }

    /// <summary>
    /// Engine main thread: the breadcrumb around one plug-in call, with its kind (the watchdog allows each kind its own time). None for
    /// isolated plug-ins (their calls are bounded pipe waits; a hang there is reported through Crashed and costs only that plug-in) and
    /// for the built-in GM synth. Returns whether a breadcrumb was set (pass it to <see cref="LeaveCall"/>).
    /// </summary>
    private static bool EnterCall(int slot, int index, IPluginInstance? plugin, string path, PluginCallKind kind)
    {
        if (plugin is GmSynth || Shared is not { } shared) return false;
        // Isolated: marked as busy for a reason (so TabForge does not take the engine for deaf), never blamed.
        shared.EnterMain(slot, index, path, kind, blame: plugin is not Isolation.RemotePlugin);
        return true;
    }

    private static void LeaveCall(bool entered) { if (entered) Shared?.LeaveMain(); }

    /// <summary>
    /// Configure, after the old device stopped: every live instance (chain plug-ins, bypassed ones, GM synths) switches to the
    /// new rate and block size in place, keeping its live settings, before the chains are rebuilt around them.
    /// One that cannot follow stays loaded but silent and is reported.
    /// </summary>
    private void ReconfigureLive(int sampleRate, int maxBlock)
    {
        AssertMain();
        var done = new HashSet<IPluginInstance>(ReferenceEqualityComparer.Instance);
        void One(int slot, int index, IPluginInstance plugin)
        {
            if (!done.Add(plugin)) return;
            var entered = EnterCall(slot, index, plugin, plugin.Path, PluginCallKind.Load);
            try { plugin.Reconfigure(sampleRate, maxBlock); }
            catch (Exception ex)
            {
                EngineLog.Write($"plug-in could not follow the device change: {plugin.Path}: {ex.GetBaseException().Message}");
                Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(plugin.Path); w.WriteString($"It could not switch to {sampleRate} Hz / {maxBlock} frames: {ex.GetBaseException().Message}"); });
            }
            finally { LeaveCall(entered); }
        }
        foreach (var (slot, chain) in Loaded)
        {
            foreach (var e in chain.Effects) One(slot, e.Index, e.Plugin);
            if (chain.MidiSynth is { } synth) One(slot, -1, synth);
        }
        foreach (var (slot, parked) in Parked) foreach (var p in parked.Values) One(slot, -1, p);
        EngineLog.Write($"device change: {done.Count} live plug-in(s) reconfigured to {sampleRate} Hz / {maxBlock} frames");
    }

    private string AsioInputLabel()
    {
        var names = AudioOutputFactory.AsioChannels.Inputs;
        var first = Math.Clamp(Config.AsioInput, 0, Math.Max(0, names.Length - 1));
        return names.Length == 0 ? "ASIO input" : AudioOutputFactory.AsioInputChannelsUsed > 1 && first + 1 < names.Length ? $"{names[first]} + {names[first + 1]}" : names[first];
    }

    private int _asioBufferFrames;
    private float[] _asioScratch = new float[4096];
    private long _asioScratchMisses;

    /// <summary>Engine thread, when an ASIO driver opens: room for the largest input block it can deliver.</summary>
    private void PrepareAsioScratch(int bufferFrames, int inputChannels)
    {
        var needed = Math.Max(bufferFrames, 8192) * Math.Max(32, inputChannels);
        if (_asioScratch.Length < needed) _asioScratch = new float[needed];
    }

    /// <summary>ASIO driver thread (real time: no allocation, no lock, no main-thread check): the block of input for the channel pair; copied into the input ring.</summary>
    private void OnAsioInput(object? sender, NAudio.Wave.AsioAudioAvailableEventArgs e)
    {
        var input = Input;
        if (input is null) return;
        var needed = e.SamplesPerBuffer * Math.Max(1, e.InputBuffers.Length);
        // Sized when the driver opens (PrepareAsioScratch); a driver that changes its buffer mid-stream is skipped
        // for that block rather than allocating on its real-time thread.
        if (_asioScratch.Length < needed) { Interlocked.Increment(ref _asioScratchMisses); return; }
        var written = e.GetAsInterleavedSamples(_asioScratch);
        input.Feed(_asioScratch.AsSpan(0, written));
    }

    private NAudio.CoreAudioApi.MMDevice? _volumeDevice;

    /// <summary>True while the Windows volume is followed (the main loop then refreshes the Volume Mixer slider about once a second).</summary>
    public bool FollowingWindowsVolume => _volumeDevice is not null;

    /// <summary>
    /// ASIO does not pass through the Windows mixer, so the Windows volume keys do nothing to it. When asked, the Windows
    /// master volume (and mute) of the default playback device becomes a software gain on the engine's output.
    /// </summary>
    private void FollowWindowsVolume(bool on)
    {
        AssertMain();
        try
        {
            if (_volumeDevice is not null)
            {
                _volumeDevice.AudioEndpointVolume.OnVolumeNotification -= OnWindowsVolume;
                _volumeDevice.Dispose();
                _volumeDevice = null;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        _endpointGain = 1f; _sessionGain = 1f; _following = false;
        if (Mix is not null) Mix.MasterGain = 1f;
        if (!on || Mix is null) return;
        try
        {
            _volumeDevice = new NAudio.CoreAudioApi.MMDeviceEnumerator().GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var endpoint = _volumeDevice.AudioEndpointVolume;
            endpoint.OnVolumeNotification += OnWindowsVolume;
            _endpointGain = EndpointGain(endpoint.MasterVolumeLevel, endpoint.Mute);
            _following = true;
            ApplyFollowedGain();
            RefreshSessionVolume();
            EngineLog.Write($"following the Windows volume of {_volumeDevice.FriendlyName}: {endpoint.MasterVolumeLevel:0.00} dB (scalar {endpoint.MasterVolumeLevelScalar:0.000}), gain {_endpointGain:0.000}, path offset {20 * Math.Log10(_pathOffsetGain):0.00} dB");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            EngineLog.Write($"windows volume not available: {ex.Message}");
        }
    }

    private float _endpointGain = 1f, _sessionGain = 1f, _pathOffsetGain = 1f;
    private bool _following;

    /// <summary>
    /// ASIO bypasses the Windows mixer entirely, so both the device's master volume and TabForge's own slider in the
    /// Volume Mixer (the session the built-in Windows MIDI synth plays in) are applied here; otherwise the engine on ASIO
    /// plays far louder than the Windows MIDI tracks whenever TabForge's app slider is below the master.
    /// Main thread, or the COM notification thread (<see cref="OnWindowsVolume"/>).
    /// </summary>
    private void ApplyFollowedGain()
    {
        if (Mix is { } mix && _following) mix.MasterGain = _endpointGain * _sessionGain * _pathOffsetGain;
    }

    /// <summary>Engine thread, about once a second while following: TabForge's app volume in the Windows Volume Mixer.</summary>
    public void RefreshSessionVolume()
    {
        AssertMain();
        if (_volumeDevice is null || UiProcessId == 0) return;
        try
        {
            var manager = _volumeDevice.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            float? found = null;
            for (var i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if ((int)session.GetProcessID != UiProcessId) continue;
                var volume = session.SimpleAudioVolume;
                found = Math.Min(found ?? 1f, SessionGain(volume.Volume, volume.Mute));
            }
            var gain = found ?? 1f;   // TabForge has no session yet (nothing played through Windows): nothing to follow
            if (Math.Abs(gain - _sessionGain) > 1e-4f)
            {
                _sessionGain = gain;
                ApplyFollowedGain();
                EngineLog.Write($"following TabForge's Volume Mixer slider: gain {gain:0.000} (total {_endpointGain * _sessionGain:0.000})");
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { }
    }

    /// <summary>
    /// The endpoint volume Windows really applies is its MasterVolumeLevel in dB (measured on an Audient iD4: loopback level
    /// tracks the reported dB exactly from 0 to -35 dB; the old scalar² curve was up to 5 dB off). The scalar is only the slider.
    /// </summary>
    private static float EndpointGain(float db, bool muted) => muted ? 0f : Gain.FromDb(Math.Clamp(db, -96f, 0f));

    /// <summary>A Volume Mixer session slider is applied as scalar² (measured: 0.5 = -12.04 dB, 0.25 = -24.08 dB).</summary>
    private static float SessionGain(float scalar, bool muted) => muted ? 0f : Math.Clamp(scalar, 0f, 1f) * Math.Clamp(scalar, 0f, 1f);

    /// <summary>COM notification thread.</summary>
    private void OnWindowsVolume(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        float db;
        try { db = _volumeDevice?.AudioEndpointVolume.MasterVolumeLevel ?? 0f; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return; }
        _endpointGain = EndpointGain(db, data.Muted);
        ApplyFollowedGain();
    }

    public void StopOutput()
    {
        AssertMain();
        FollowWindowsVolume(false);
        try { Player?.Stop(); Player?.Dispose(); }
        catch (Exception ex) { EngineLog.Write($"output stop failed: {ex.Message}"); }
        Player = null;
    }

    // ---- Chains ----

    public void LoadChain(int slot, string track, bool useMidiSynth, List<PluginSpec> specs)
    {
        AssertMain();
        if (Defer(() => LoadChain(slot, track, useMidiSynth, specs))) return;
        Requested[slot] = (track, useMidiSynth, specs);
        ChainGeneration[slot] = ChainGeneration.GetValueOrDefault(slot) + 1;
        if (Mix is not null) BuildChain(slot, track, useMidiSynth, specs);
    }

    /// <summary>
    /// (Re)builds a track's chain. Plug-ins already running (same id and file) are carried over as they are, with
    /// their live settings: only new plug-ins are loaded (and get their saved state). So adding, removing,
    /// reordering or bypassing one plug-in never reloads the others (no reset, no stall).
    /// </summary>
    private void BuildChain(int slot, string track, bool useMidiSynth, List<PluginSpec> specs)
    {
        AssertMain();
        var maxBlock = Math.Clamp(Config.BufferSize, 16, 8192);
        Loaded.TryGetValue(slot, out var previous);
        var live = new Dictionary<string, IPluginInstance>();
        if (previous is not null) foreach (var (id, p) in previous.ById) live[id] = p;
        var wasParked = new List<IPluginInstance>();
        if (Parked.Remove(slot, out var parked)) foreach (var (id, p) in parked) { live[id] = p; wasParked.Add(p); }
        var kept = new HashSet<IPluginInstance>(ReferenceEqualityComparer.Instance);
        var nowParked = new Dictionary<string, IPluginInstance>();
        var instrumentIndex = -1;
        var blockedChanged = new HashSet<int>();   // approved plug-ins whose file no longer matches the approval: never loaded
        var effects = new List<TrackChain.Effect>();
        var byId = new Dictionary<string, IPluginInstance>();
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var index = i;
            IPluginInstance? reused = null, created = null;
            if (spec.Id.Length > 0 && live.TryGetValue(spec.Id, out var existing) && existing.Path == spec.Path) { reused = existing; live.Remove(spec.Id); }
            if (spec.Skip)   // quarantined: never loaded (a merely bypassed plug-in is loaded and flagged below)
            {
                if (reused is not null) { nowParked[spec.Id] = reused; kept.Add(reused); }
                continue;
            }
            try
            {
                IPluginInstance plugin;
                if (reused is not null) { plugin = reused; kept.Add(reused); }
                else
                {
                    // Identity check for a real load only (a reused instance is already mapped): hash the file through a handle that
                    // denies writers and keep it open until the plug-in is created. Before EnterMain: hashing is not a plug-in call.
                    using var identityHold = PluginIdentity.Hold(spec);
                    byte[]? savedState = null;
                    string? stateProblem = null;
                    try { savedState = spec.State is { Length: > 0 } state ? Convert.FromBase64String(state) : null; }
                    catch (FormatException) { stateProblem = "its saved settings were not valid"; }
                    // A crash or hang while loading is attributed too (kind Load: 90 s). Not for an isolated load: that is a bounded
                    // wait on the plug-in's own process (15 s), and a failure there costs only that plug-in.
                    Shared!.EnterMain(slot, index, spec.Path, PluginCallKind.Load, blame: !Config.SeparateProcessPerPlugin);
                    var loadTimer = Stopwatch.StartNew();
                    long createdMs;
                    // A state the plug-in rejects disposes that instance (no leaked instance / process); the plug-in then loads with its defaults and it is reported.
                    try { plugin = created = PluginLoading.CreateWithState(() => Create(spec, maxBlock, slot, index), savedState, out createdMs); }
                    catch (Exception sex) when (savedState is not null)
                    {
                        stateProblem = $"its saved settings were rejected ({sex.GetBaseException().Message})";
                        plugin = created = PluginLoading.CreateWithState(() => Create(spec, maxBlock, slot, index), null, out createdMs);
                    }
                    Shared!.LeaveMain();
                    if (stateProblem is not null)
                    {
                        EngineLog.Write($"plug-in state not applied: {spec.Path}: {stateProblem}");
                        Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(spec.Path); w.WriteString($"The plug-in runs with its default settings: {stateProblem}."); });
                    }
                    EngineLog.Write($"plug-in loaded: {System.IO.Path.GetFileName(spec.Path)} in {loadTimer.ElapsedMilliseconds} ms (create {createdMs} ms, state {loadTimer.ElapsedMilliseconds - createdMs} ms)");
                    if (plugin is Vst2Plugin vst2) vst2.Edited += () => Send(EngineEvent.StateChanged, w => { w.Write(slot); w.Write(index); });
                }
                var gain = spec.OutputDb <= -59.9 ? 0f : (float)Gain.FromDb(spec.OutputDb);
                if (spec.IsInstrument && instrumentIndex < 0) instrumentIndex = index;
                var stage = new TrackChain.Effect(plugin, spec.Wet / 100f, index, spec.Pins, gain, spec.IsInstrument) { Bypass = !spec.Enabled };
                if (Wirings.TryGetValue((slot, spec.Id), out var flags)) { stage.PassMidi = (flags & 1) != 0; stage.MidiOutToNext = (flags & 2) != 0; stage.Replace = (flags & 4) != 0; }
                effects.Add(stage);
                if (spec.Id.Length > 0) byId[spec.Id] = plugin;
            }
            catch (Exception ex)
            {
                Shared!.LeaveMain();
                if (ex is PluginChangedException) blockedChanged.Add(index);
                // A plug-in created here that did not make it into the chain has no other owner (never reached the audio thread).
                if (created is not null) { try { created.Dispose(); } catch (Exception dex) { EngineLog.Write($"dispose failed: {dex.Message}"); } }
                EngineLog.Write($"plug-in failed to load: {spec.Path}: {ex.GetBaseException().Message}");
                Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(spec.Path); w.WriteString(ex.GetBaseException().Message); });
            }
        }
        if (nowParked.Count > 0) Parked[slot] = nowParked;
        // Previously bypassed plug-ins that are gone now: nobody else owns them.
        foreach (var gone in wasParked.Where(p => !kept.Contains(p))) RetirePlugin(gone);
        IPluginInstance? synth = null;
        if (useMidiSynth)
        {
            if (previous?.MidiSynth is { } oldSynth) { synth = oldSynth; kept.Add(oldSynth); }
            else
            {
                try { synth = new GmSynth(SampleRate, maxBlock); }
                catch (Exception ex) { Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(-1); w.WriteString("General MIDI synth"); w.WriteString(ex.Message); }); }
            }
        }
        var chain = new TrackChain(slot, instrumentIndex, synth, effects, maxBlock);
        foreach (var (id, p) in byId) chain.ById[id] = p;
        if (Mixes.TryGetValue(slot, out var mixed)) chain.SetMix(mixed.Volume, mixed.Pan);
        if (ClipSpecs.TryGetValue(slot, out var clipSpecs)) ApplyClips(chain, clipSpecs, ClipOwners.GetValueOrDefault(slot));
        chain.ArmMode = Arms.TryGetValue(slot, out var armMode) ? armMode : -1;
        chain.Monitor = !Unmonitored.Contains(slot);
        Loaded[slot] = chain;
        if (previous is not null) foreach (var p in kept) previous.Kept.Add(p);
        var old = Mix!.SetChain(slot, chain);
        Retire(old);
        ApplyRoute(slot);
        chain.MidiLog.Watching = LogWatch.Contains(slot);
        ApplyMidiProcs(slot);
        ApplyGraph();
        var generation = ChainGeneration.GetValueOrDefault(slot);
        var outcomes = new List<(int Index, byte Status, string Path)>();
        for (var i = 0; i < specs.Count; i++)
        {
            var status = specs[i].Skip ? (specs[i].Untrusted ? (byte)PluginLoadStatus.BlockedUntrusted : (byte)PluginLoadStatus.SkippedQuarantined)
                : chain.Effects.Any(e => e.Index == i) ? (byte)PluginLoadStatus.Loaded
                : blockedChanged.Contains(i) ? (byte)PluginLoadStatus.BlockedChanged : (byte)PluginLoadStatus.Failed;
            outcomes.Add((i, status, specs[i].Path));
        }
        if (useMidiSynth) outcomes.Add((-1, synth is null ? (byte)PluginLoadStatus.Failed : (byte)PluginLoadStatus.Loaded, "General MIDI synth"));
        Send(EngineEvent.ChainLoaded, w =>
        {
            w.Write(slot); w.Write(chain.Plugins.Count + (synth is null ? 0 : 1));
            w.Write(generation); w.Write(outcomes.Count);
            foreach (var (index, status, path) in outcomes) { w.Write(index); w.Write(status); w.WriteString(path); }
        });
    }

    /// <summary>Disposes one plug-in once the audio thread can no longer be using it.</summary>
    private void RetirePlugin(IPluginInstance plugin)
    {
        EditorWindows.CloseFor(plugin);
        Retired.Add(plugin, Mix?.Epoch);
    }

    private IPluginInstance Create(PluginSpec spec, int maxBlock, int slot, int index)
    {
        if (PluginFactory is { } factory) return factory(spec, SampleRate, maxBlock);   // headless tests only
        // Self-test hook: the managed test effect, optionally slow in effOpen, in a real engine process. Never without the hooks.
        if (EngineHost.TestHooks && spec.Path == Vst2Plugin.TestEffect.PathName)
            return Vst2Plugin.TestEffect.Create(SampleRate, maxBlock,
                int.TryParse(Environment.GetEnvironmentVariable("TABFORGE_TEST_OPEN_DELAY_MS"), out var delay) ? Math.Clamp(delay, 0, 60_000) : 0);
        if (Config.SeparateProcessPerPlugin)
        {
            // Safest mode: the plug-in runs in its own process; a crash there loses only this plug-in.
            var remote = new Isolation.RemotePlugin(spec, SampleRate, maxBlock);
            remote.Crashed += r => Send(EngineEvent.PluginCrashed, w => { w.Write(slot); w.Write(index); w.WriteString(r.Path); });
            remote.Edited += () => Send(EngineEvent.StateChanged, w => { w.Write(slot); w.Write(index); });
            return remote;
        }
        return Plugins.PluginFactory.Create(spec.Path, spec.Format, spec.IsInstrument, SampleRate, maxBlock);
    }

    public void RemoveChain(int slot)
    {
        AssertMain();
        if (Defer(() => RemoveChain(slot))) return;
        Mixes.Remove(slot);
        ClipSpecs.Remove(slot); ClipOwners.Remove(slot);
        if (Arms.Remove(slot)) ApplyArms();
        Requested.Remove(slot);
        Loaded.Remove(slot);
        if (Parked.Remove(slot, out var parked)) foreach (var p in parked.Values) RetirePlugin(p);
        Retire(Mix?.SetChain(slot, null));
        Routes.Remove(slot);
        MidiProcs.Remove(slot);
        foreach (var key in Wirings.Keys.Where(k => k.Item1 == slot).ToList()) Wirings.Remove(key);
        LogWatch.Remove(slot);
        ApplyRoute(slot);
        ApplyGraph();
    }

    /// <summary>Disposes a replaced chain once the audio thread can no longer be using it.</summary>
    private void Retire(TrackChain? chain)
    {
        if (chain is null) return;
        foreach (var plugin in chain.Plugins) if (!chain.Kept.Contains(plugin)) EditorWindows.CloseFor(plugin);
        Retired.Add(chain, Mix?.Epoch);
    }

    private void ApplyClips(TrackChain chain, List<ClipSpec> specs, int owner)
    {
        AssertMain();
        chain.ClipOwner = owner;
        var players = specs.Where(s => File.Exists(s.File)).Select(s => new Audio.ClipPlayer(s, SampleRate)).ToArray();
        var old = chain.Clips;
        Audio.DiskStreamer.Register(players);
        chain.Clips = players;
        Audio.DiskStreamer.Unregister(old);
        foreach (var p in old) Retired.Add(p, Mix?.Epoch);
    }

    // ---- Input and recording ----

    /// <summary>Opens the audio input while any track is armed (and closes it when none is).</summary>
    private void ApplyArms()
    {
        AssertMain();
        foreach (var (slot, chain) in Loaded) { chain.ArmMode = Arms.TryGetValue(slot, out var mode) ? mode : -1; chain.Monitor = !Unmonitored.Contains(slot); }
        if (Arms.Count > 0 && Input is null && Mix is not null)
        {
            try
            {
                // ASIO: the driver delivers the input in its own callback (no second, competing device).
                Input = Player is NAudio.Wave.AsioOut && _asioBufferFrames > 0
                    // The driver-reported input latency (ASIOGetLatencies, it includes the buffer) when there is one.
                    ? new Audio.InputCapture(AsioInputLabel(), SampleRate, Math.Max(1, AudioOutputFactory.AsioInputChannelsUsed),
                        Math.Max(1, (int)Math.Round((AudioOutputFactory.AsioInputLatencyFrames > 0 ? AudioOutputFactory.AsioInputLatencyFrames : _asioBufferFrames) * 1000.0 / SampleRate)))
                    : new Audio.InputCapture(Config.InputDevice, SampleRate);
                Mix.Input = Input;
                EngineLog.Write($"input opened: {Input.DeviceName}, {Input.SourceRate} Hz" + (Input.SourceRate != SampleRate ? $" (resampled to {SampleRate} Hz)" : "") +
                                $", input latency {Input.LatencyMs} ms ({(AudioOutputFactory.AsioInputLatencyFrames > 0 && Player is NAudio.Wave.AsioOut ? "ASIO driver" : Input.LatencySource)})");
            }
            catch (Exception ex)
            {
                EngineLog.Write($"input failed: {ex}");
                Send(EngineEvent.InputError, w => w.WriteString(ex.GetBaseException().Message));
            }
        }
        else if (Arms.Count == 0 && Input is not null)
        {
            if (Recorder is not null) StopRecording();
            if (Mix is not null) Mix.Input = null;
            Input.Dispose();
            Input = null;
        }
    }

    private void StartRecording(string folder, Dictionary<int, string> names, double offsetMs, int owner)
    {
        AssertMain();
        if (Recorder is not null || Input is null || Mix is null) return;
        var armed = Arms.Where(a => names.ContainsKey(a.Key)).Select(a => (a.Key, names[a.Key], a.Value)).ToList();
        if (armed.Count == 0) return;
        var heardSec = Audio.TakeAlignment.StartSec(Mix.SongSecFor(owner), Mix.DelayTicks / (double)Stopwatch.Frequency, Input.LatencyMs, offsetMs);
        EngineLog.Write($"recording aligned: input latency {Input.LatencyMs} ms, user offset {offsetMs:0.#} ms");
        try
        {
            Recorder = new Audio.Recorder(folder, armed, Math.Max(0, heardSec), SampleRate);
            var recorder = Recorder;
            recorder.Error += msg => Send(EngineEvent.InputError, w => w.WriteString($"Recording problem: {msg}"));
            Input.BlockCaptured = recorder.Enqueue;
            EngineLog.Write($"recording {armed.Count} track(s) into {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Recorder = null;
            Send(EngineEvent.InputError, w => w.WriteString($"Recording could not start: {ex.Message}"));
        }
    }

    /// <summary>Takes still being written after Stop (main thread only); Shutdown finishes them so no file is left without its header.</summary>
    private readonly List<Audio.Recorder> _draining = new();

    /// <param name="wait">True (shutdown): close the files here. False: the disk thread finishes the take and sends the events, so a
    /// slow disk with a long queue never holds the main thread past TabForge's 5 s liveness limit (which would end the engine and lose the take).</param>
    private void StopRecording(bool wait = false)
    {
        AssertMain();
        if (Recorder is null) return;
        if (Input is not null) Input.BlockCaptured = null;
        var recorder = Recorder;
        Recorder = null;
        if (wait) { ReportTakes(recorder, recorder.Finish()); return; }
        _draining.Add(recorder);
        recorder.FinishInBackground(takes =>
        {
            ReportTakes(recorder, takes);
            EngineThreads.Post(() => _draining.Remove(recorder));   // let the queue memory go
        });
    }

    /// <summary>Any thread: the Recorded events, then one loss summary if input was lost.</summary>
    private static void ReportTakes(Audio.Recorder recorder, List<(Audio.Recorder.Take Take, double LengthSec)> takes)
    {
        var summary = recorder.LossSummary;
        if (summary is not null) EngineLog.Write($"recording finished: {summary}");
        foreach (var (take, length) in takes)
            Send(EngineEvent.Recorded, w => { w.Write(take.Slot); w.WriteString(take.Path); w.Write(take.StartSec); w.Write(length); });
        if (summary is not null) Send(EngineEvent.RecordingLoss, w => w.WriteString(summary));
    }

    // ---- Plug-ins: editors, programs, states ----

    private IPluginInstance? PluginAt(int slot, int index)
    {
        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || index < 0 || index >= request.Specs.Count) return null;
        return FindPlugin(chain, request.Specs[index]);
    }

    /// <summary>The loaded instance of one chain slot: by slot Id (two instances of one plug-in share a path), else by path.</summary>
    private static IPluginInstance? FindPlugin(TrackChain chain, PluginSpec spec)
        => spec.Id.Length > 0 && chain.ById.TryGetValue(spec.Id, out var byId) ? byId : chain.Plugins.FirstOrDefault(p => p.Path == spec.Path);

    public void SendPrograms(int slot, int index)
    {
        AssertMain();
        var names = PluginAt(slot, index) is Vst2Plugin v ? v.Programs : Array.Empty<string>();
        var current = PluginAt(slot, index) is Vst2Plugin c ? c.CurrentProgram : -1;
        Send(EngineEvent.Programs, w => { w.Write(slot); w.Write(index); w.Write(current); w.Write(names.Count); foreach (var n in names) w.WriteString(n); });
    }

    public void OpenEditor(int slot, int index, IntPtr owner, bool dark, bool docked = false, bool onTop = false)
    {
        AssertMain();
        EngineLog.Write($"editor requested: slot {slot} plug-in {index}");
        if (owner != IntPtr.Zero && !EditorWindows.IsWindow(owner)) { EngineLog.Write("editor: the parent window no longer exists"); owner = IntPtr.Zero; }
        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || index < 0 || index >= request.Specs.Count)
        {
            EngineLog.Write($"editor: no chain for slot {slot} / plug-in {index}");
            return;
        }
        var path = request.Specs[index].Path;
        var plugin = FindPlugin(chain, request.Specs[index]);
        if (plugin is null) { EngineLog.Write($"editor: {path} is not loaded in slot {slot}"); return; }
        if (plugin is Isolation.RemotePlugin remote) { remote.OpenEditor(owner); return; } // its window lives in its own process (floats)
        var spec = request.Specs[index];
        var name = spec.Name.Length > 0 ? spec.Name : Path.GetFileNameWithoutExtension(path);
        EditorWindows.UserClosed ??= () => Send(EngineEvent.EditorClosed, _ => { });
        void Size(int w, int h) => Send(EngineEvent.EditorSize, x => { x.Write(slot); x.Write(index); x.Write(w); x.Write(h); });
        var entered = EnterCall(slot, index, plugin, path, PluginCallKind.Editor);
        bool shown;
        try { shown = EditorWindows.Show(plugin, $"{name} — {request.Track}", owner, dark, docked, onTop, Size); }
        finally { LeaveCall(entered); }
        if (!shown) EngineLog.Write($"editor: {path} has no editor or its window could not be created");
    }

    /// <summary>
    /// Answers a state request with one <see cref="EngineEvent.PluginState"/> frame per plug-in and an explicit status each, so TabForge
    /// knows exactly which states it has (a failed getter is reported, never replaced by the old stored state). Each GetState call carries
    /// the plug-in breadcrumb, so the main-thread watchdog can end a hung getter and blame that plug-in.
    /// </summary>
    public void SendStates(int slot, int requestId)
    {
        AssertMain();
        void Reply(int index, int count, PluginStateStatus status, string text) =>
            Send(EngineEvent.PluginState, w => { w.Write(slot); w.Write(requestId); w.Write(index); w.Write(count); w.Write((byte)status); w.WriteString(text); });

        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || request.Specs.Count == 0)
        {
            Reply(-1, 0, PluginStateStatus.NoState, "");
            return;
        }
        var count = request.Specs.Count;
        for (var index = 0; index < count; index++)
        {
            var spec = request.Specs[index];
            // By id: two copies of one plug-in each report their own state; bypassed ones too.
            var plugin = chain.ById.GetValueOrDefault(spec.Id)
                ?? (Parked.TryGetValue(slot, out var parked) ? parked.GetValueOrDefault(spec.Id) : null)
                ?? (spec.Id.Length == 0 ? chain.Plugins.FirstOrDefault(p => p.Path == spec.Path) : null);
            var status = PluginStateStatus.NoState;
            var text = "";
            if (plugin is not null)
            {
                var entered = EnterCall(slot, index, plugin, spec.Path, PluginCallKind.GetState);
                try
                {
                    var state = plugin.GetState();
                    if (state is null) status = PluginStateStatus.NoState;
                    else if (state.Length > PluginStateLimits.MaxRawBytes) status = PluginStateStatus.TooLarge;
                    else { status = PluginStateStatus.Captured; text = Convert.ToBase64String(state); }
                }
                catch (InvalidDataException ex) { EngineLog.Write($"get state: {ex.Message}"); status = PluginStateStatus.TooLarge; }
                catch (TimeoutException ex) { EngineLog.Write($"get state timed out: {ex.Message}"); status = PluginStateStatus.TimedOut; }
                catch (Exception ex) { EngineLog.Write($"get state failed: {ex.Message}"); status = PluginStateStatus.Failed; }
                finally { LeaveCall(entered); }
            }
            Reply(index, count, status, text);
        }
    }

    /// <summary>Main thread, at the end of the run: ends a render, recording, input, editors and the device, then disposes every chain and retired object.</summary>
    public void Shutdown()
    {
        AssertMain();
        RenderCancel = true;
        RenderThread?.Join(5000);
        try
        {
            StopRecording(wait: true);
            foreach (var draining in _draining.ToList()) draining.Finish();   // takes stopped earlier and still being written
            _draining.Clear();
        }
        catch (Exception ex) { EngineLog.Write($"shutdown: recorder: {ex.Message}"); }
        try { Input?.Dispose(); } catch (Exception ex) { EngineLog.Write($"shutdown: input: {ex.Message}"); }
        try { EditorWindows.CloseAll(); } catch (Exception ex) { EngineLog.Write($"shutdown: editors: {ex.Message}"); }
        try { StopOutput(); } catch (Exception ex) { EngineLog.Write($"shutdown: player: {ex.Message}"); }
        foreach (var chain in Loaded.Values) { try { chain.Dispose(); } catch (Exception ex) { EngineLog.Write($"dispose failed: {ex.Message}"); } }
        Loaded.Clear();
        Retired.DisposeAll();
    }
}
