using System.Diagnostics;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.Core.Midi;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;

namespace TabForge.AudioEngine;

// Owns: one engine run's per-slot state (song transports, clip specs, mixes, arms, MIDI input routes and processors, loaded chains) and
//   the command handlers that change it, on the engine main thread.
// Does not own: the process side (pipe, shared block, command reader and watchdog, in EngineHost), and the mixing and plug-in hosting
//   (MixEngine, TrackChain).
// Tests: no dedicated test class.
/// <summary>
/// The engine's per-session state and its command handlers, one instance per engine run. <see cref="EngineHost.Run"/> creates it;
/// the headless harness uses the one created with the process. <see cref="EngineHost"/> keeps only the process and IPC side (pipe,
/// shared block, command reader, watchdog, main loop); it parses each command on the reader thread and posts the matching handler
/// here. Unless noted, every member belongs to the engine main thread (checked with <see cref="AssertMain"/> in Debug builds).
/// </summary>
internal sealed partial class EngineSession
{
    /// <summary>This session log and main-thread queue / thread flags: created with the session, handed to everything the session builds (two sessions share neither).</summary>
    public readonly EngineLogWriter Log;
    public readonly EngineThreads Threads;

    public EngineSession(EngineLogWriter log)
    {
        Log = log;
        Threads = new EngineThreads(log);
        Editors = PluginEditorHosts.Create(log);
        Editors.ProcessCalls = plugin => { foreach (var chain in Loaded.Values) if (chain.ProcessCallsOf(plugin) is var n and >= 0) return n; return -1; };
        Retired = new RetireQueue(log);
    }

    /// <summary>One transport per song owner (position, tempo, bar map), shared by every MixEngine this process creates (written by the command reader thread). Allocated once, never grown.</summary>
    public readonly SongTransport[] Transports = CreateTransports();
    /// <summary>Owner 0: the transport of a sender that names no owner, and the one plug-ins see.</summary>
    public SongTransport Transport => Transports[0];
    /// <summary>Slots whose General MIDI synth is switched off; handed to every MixEngine the session creates, so it survives a device restart.</summary>
    public readonly bool[] SynthOff = new bool[MixEngine.TotalSlots];
    /// <summary>The programs, controllers and bend the songs set per slot; carried to every new mix so a device restart keeps the instruments.</summary>
    public readonly ChannelStateMemory ChannelState = new();
    private static SongTransport[] CreateTransports() { var t = new SongTransport[SongOwners.Max]; for (var i = 0; i < t.Length; i++) t[i] = new SongTransport(); return t; }
    /// <summary>Per-slot owner of the song whose position drives the slot's clips, kept across chain rebuilds.</summary>
    public readonly Dictionary<int, int> ClipOwners = new();
    /// <summary>The plug-in editor windows of this run (main thread only); closed with the session.</summary>
    public readonly IPluginEditorHost Editors;
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
    /// <summary>The linear input gain of a slot (live input), kept here so a rebuilt chain gets it again; absent: unity.</summary>
    public readonly Dictionary<int, float> InputGains = new();
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
    public readonly RetireQueue Retired;

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
    internal void AssertMain() => Debug.Assert(Threads.IsMainThread, "engine session state touched off the engine main thread");

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
            Log.Write($"saved state did not arrive for {path}; loaded with its default settings");
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

    /// <summary>The tuner's analysis window in ms (any thread sets it; the main thread's tuner tick reads it).</summary>
    public volatile int TunerWindowMs = Audio.PitchDetector.DefaultWindowMs;

    public void SetInputGain(int slot, float gain)
    {
        AssertMain();
        InputGains[slot] = gain;
        if (Loaded.TryGetValue(slot, out var c)) c.InputGain = gain;
    }

    public void SetPluginBypass(int slot, int index, bool enabled)
    {
        AssertMain();
        if (Loaded.TryGetValue(slot, out var c)) c.SetPluginBypass(index, !enabled);
    }

    public void SetSynth(int slot, bool on)
    {
        AssertMain();
        Volatile.Write(ref SynthOff[slot], !on);
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

    /// <summary>Starts or stops the take. <paramref name="songSecAtRead"/> is the song second the host read at the record click (the clip-start anchor); null reads the mixer's own position for <paramref name="owner"/> when capture begins.</summary>
    public void Record(bool start, string folder, Dictionary<int, string> names, double offsetMs, int owner = 0, double? songSecAtRead = null)
    {
        AssertMain();
        if (start) StartRecording(folder, names, offsetMs, owner, songSecAtRead); else StopRecording();
    }

    public void CloseEditor(int slot, int index)
    {
        AssertMain();
        if (PluginAt(slot, index) is { } p) Editors.CloseFor(p);
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
        catch (FormatException) { Log.Write("preset state was not valid base64"); }
        finally { LeaveCall(entered); }
    }

    /// <summary>The live master safety limiter (off by default); survives a device change.</summary>
    public bool LiveLimiter { get; private set; }

    /// <summary>The master output tap (off until TabForge asks); outlives device changes.</summary>
    public readonly Mixing.MasterTap Tap = new();

    /// <summary>The sound bank choice shared by this session's General MIDI synths.</summary>
    public readonly Synth.GmBank SoundBank = new();

    /// <summary>Selects the General MIDI synths' sound bank (a .sf2 path, empty = Windows GM); an unreadable file keeps the bank as it was.</summary>
    public void SetSoundBank(string path)
    {
        AssertMain();
        var ok = SoundBank.Select(path, out var error);
        Log.Write(ok ? $"sound bank: {(path.Length == 0 ? "Windows GM" : path)}" : $"sound bank not changed: {error}");
    }

    public void SetLiveLimiter(bool on)
    {
        AssertMain();
        LiveLimiter = on;
        if (Mix is { } mix) mix.LiveLimiter = on;
        Log.Write($"live safety limiter {(on ? "on" : "off")}");
    }

    public void SetWindowsPathOffset(float db)
    {
        AssertMain();
        _pathOffsetGain = Gain.FromDb(db);
        Log.Write($"Windows audio path offset {db:0.00} dB");
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
            Log.Write("routing graph has a cycle; the tracks in it render in slot order");
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
                catch (Exception ex) { Log.Write($"MIDI processors could not be built: {ex.Message}"); }
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
                Log.Write($"non-finite audio (NaN / infinity) from {(index >= 0 ? path : "the track's own sound")} on slot {slot}: " +
                                (index >= 0 ? "output discarded, the plug-in is skipped until it is switched on again" : "block silenced"));
                Send(EngineEvent.PluginMisbehaved, w => { w.Write(slot); w.Write(index); w.WriteString(path); });
                sent++;
            }
        }
        return sent;
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
        catch (Exception ex) { Log.Write($"shutdown: recorder: {ex.Message}"); }
        try { Input?.Dispose(); } catch (Exception ex) { Log.Write($"shutdown: input: {ex.Message}"); }
        try { Editors.CloseAll(); } catch (Exception ex) { Log.Write($"shutdown: editors: {ex.Message}"); }
        try { StopOutput(); } catch (Exception ex) { Log.Write($"shutdown: player: {ex.Message}"); }
        foreach (var chain in Loaded.Values) { try { chain.Dispose(); } catch (Exception ex) { Log.Write($"dispose failed: {ex.Message}"); } }
        Loaded.Clear();
        Retired.DisposeAll();
    }
}
