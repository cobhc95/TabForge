using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;

namespace TabForge.AudioEngine;

// Owns: the engine session's track chains: load, build, remove, retire and clips, split from EngineSession.cs.
// Does not own: the other parts of the engine session (EngineSession).
internal sealed partial class EngineSession
{
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
        var live = CollectLivePlugins(slot, previous, out var wasParked);
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
                if (reused is not null) { nowParked[spec.Id] = reused; kept.Add(reused); Editors.CloseFor(reused); }
                continue;
            }
            try
            {
                IPluginInstance plugin;
                if (reused is not null) { plugin = reused; kept.Add(reused); }
                else plugin = LoadPlugin(slot, index, spec, maxBlock, ref created);
                var gain = spec.OutputDb <= -59.9 ? 0f : (float)Gain.FromDb(spec.OutputDb);
                if (spec.IsInstrument && instrumentIndex < 0) instrumentIndex = index;
                var stage = new TrackChain.Effect(plugin, spec.Wet / 100f, index, spec.Pins, gain, spec.IsInstrument) { Bypass = !spec.Enabled };
                if (Wirings.TryGetValue((slot, spec.Id), out var flags)) TrackChain.ApplyWiring(stage, flags);
                effects.Add(stage);
                if (spec.Id.Length > 0) byId[spec.Id] = plugin;
            }
            catch (Exception ex)
            {
                Shared!.LeaveMain();
                if (ex is PluginChangedException) blockedChanged.Add(index);
                // A plug-in created here that did not make it into the chain has no other owner (never reached the audio thread).
                if (created is not null) { try { created.Dispose(); } catch (Exception dex) { Log.Write($"dispose failed: {dex.Message}"); } }
                Log.Write($"plug-in failed to load: {spec.Path}: {LoadFailureText(spec, ex)}");
                Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(spec.Path); w.WriteString(LoadFailureText(spec, ex)); });
            }
        }
        if (nowParked.Count > 0) Parked[slot] = nowParked;
        // Previously bypassed plug-ins that are gone now: nobody else owns them.
        foreach (var gone in wasParked.Where(p => !kept.Contains(p))) RetirePlugin(gone);
        var synth = useMidiSynth ? CreateSynth(slot, previous, kept, maxBlock) : null;
        var chain = new TrackChain(slot, instrumentIndex, synth, effects, maxBlock);
        foreach (var (id, p) in byId) chain.ById[id] = p;
        if (Mixes.TryGetValue(slot, out var mixed)) chain.SetMix(mixed.Volume, mixed.Pan);
        if (ClipSpecs.TryGetValue(slot, out var clipSpecs)) ApplyClips(chain, clipSpecs, ClipOwners.GetValueOrDefault(slot));
        chain.ArmMode = Arms.TryGetValue(slot, out var armMode) ? armMode : -1;
        chain.Monitor = !Unmonitored.Contains(slot);
        chain.InputGain = InputGains.GetValueOrDefault(slot, 1f);
        Loaded[slot] = chain;
        if (previous is not null) foreach (var p in kept) previous.Kept.Add(p);
        var old = Mix!.SetChain(slot, chain);
        Retire(old);
        ApplyRoute(slot);
        chain.MidiLog.Watching = LogWatch.Contains(slot);
        ApplyMidiProcs(slot);
        ApplyGraph();
        SendChainLoaded(slot, chain, specs, blockedChanged, useMidiSynth, synth);
    }

    /// <summary>The plug-ins already running for the slot, by chain id: those of the loaded chain and those parked (bypassed ones kept loaded).</summary>
    private Dictionary<string, IPluginInstance> CollectLivePlugins(int slot, TrackChain? previous, out List<IPluginInstance> wasParked)
    {
        var live = new Dictionary<string, IPluginInstance>();
        if (previous is not null) foreach (var (id, p) in previous.ById) live[id] = p;
        wasParked = new List<IPluginInstance>();
        if (Parked.Remove(slot, out var parked)) foreach (var (id, p) in parked) { live[id] = p; wasParked.Add(p); }
        return live;
    }

    /// <summary>Loads one plug-in of a chain (hash check, saved state, load time in the log); a state it rejects is dropped and reported. <paramref name="created"/> is set as soon as the instance exists, so a failure later can still dispose it.</summary>
    private IPluginInstance LoadPlugin(int slot, int index, PluginSpec spec, int maxBlock, ref IPluginInstance? created)
    {
        IPluginInstance plugin;
        // Identity check for a real load only (a reused instance is already mapped): hash the file through a handle that
        // denies writers and keep it open until the plug-in is created. Before EnterMain: hashing is not a plug-in call.
        using var identityHold = TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(spec.Path) ? null : PluginIdentity.Hold(spec);   // a built-in is engine code, no file
        byte[]? savedState = null;
        string? stateProblem = null;
        try { savedState = spec.State is { Length: > 0 } state ? Convert.FromBase64String(state) : null; }
        catch (FormatException) { stateProblem = "its saved settings were not valid"; }
        // A crash or hang while loading is attributed too (kind Load: 90 s). Not for an isolated or bridged load: both are
        // bounded waits on the plug-in's own process, and a failure there costs only that plug-in.
        Shared!.EnterMain(slot, index, spec.Path, PluginCallKind.Load, blame: !Config.SeparateProcessPerPlugin && !spec.Isolate && !Isolation.Vst32Bridge.IsBridged(spec));
        var loadTimer = Stopwatch.StartNew();
        long createdMs;
        // A state the plug-in rejects disposes that instance (no leaked instance / process); the plug-in then loads with its defaults and it is reported.
        try { plugin = created = PluginLoading.CreateWithState(() => Create(spec, maxBlock, slot, index), savedState, Log, out createdMs); }
        catch (Exception sex) when (savedState is not null)
        {
            stateProblem = $"its saved settings were rejected ({sex.GetBaseException().Message})";
            plugin = created = PluginLoading.CreateWithState(() => Create(spec, maxBlock, slot, index), null, Log, out createdMs);
        }
        Shared!.LeaveMain();
        if (stateProblem is not null)
        {
            Log.Write($"plug-in state not applied: {spec.Path}: {stateProblem}");
            Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(spec.Path); w.WriteString($"The plug-in runs with its default settings: {stateProblem}."); });
        }
        Log.Write($"plug-in loaded: {System.IO.Path.GetFileName(spec.Path)} in {loadTimer.ElapsedMilliseconds} ms (create {createdMs} ms, state {loadTimer.ElapsedMilliseconds - createdMs} ms)");
        if (plugin is Vst2Plugin vst2) vst2.Edited += () => Send(EngineEvent.StateChanged, w => { w.Write(slot); w.Write(index); });
        if (plugin is Builtin.BuiltinEffect builtin)
        {
            builtin.Edited += () => Send(EngineEvent.StateChanged, w => { w.Write(slot); w.Write(index); });
            builtin.Problem += (file, why) => Threads.Post(() => Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(file); w.WriteString(why); }));
        }
        return plugin;
    }

    /// <summary>The General MIDI synth of the chain: the one it already has, else a new one (a failure is reported and leaves the chain without it).</summary>
    private IPluginInstance? CreateSynth(int slot, TrackChain? previous, HashSet<IPluginInstance> kept, int maxBlock)
    {
        IPluginInstance? synth = null;
        if (previous?.MidiSynth is { } oldSynth) { synth = oldSynth; kept.Add(oldSynth); }
        else
        {
            try { synth = new GmSynth(SampleRate, maxBlock, SoundBank); }
            catch (Exception ex) { Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(-1); w.WriteString("General MIDI synth"); w.WriteString(ex.Message); }); }
        }
        return synth;
    }

    /// <summary>Tells TabForge how each plug-in of the chain fared (loaded, skipped, blocked, failed).</summary>
    private void SendChainLoaded(int slot, TrackChain chain, List<PluginSpec> specs, HashSet<int> blockedChanged, bool useMidiSynth, IPluginInstance? synth)
    {
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
        Editors.CloseFor(plugin);
        Retired.Add(plugin, Mix?.Epoch);
    }

    private IPluginInstance Create(PluginSpec spec, int maxBlock, int slot, int index)
    {
        if (PluginFactory is { } factory) return factory(spec, SampleRate, maxBlock);   // headless tests only
#if TABFORGE_TEST_HOOKS
        // Self-test hook: the managed test effect, optionally slow in effOpen, in a real engine process. Never without the hooks.
        if (EngineHost.TestHooks && spec.Path == Vst2Plugin.TestEffect.PathName)
            return Vst2Plugin.TestEffect.Create(SampleRate, maxBlock, Threads,
                int.TryParse(Environment.GetEnvironmentVariable("TABFORGE_TEST_OPEN_DELAY_MS"), out var delay) ? Math.Clamp(delay, 0, 60_000) : 0);
#endif
        // TabForge's own effects (C#, no file) always run here, never in a plug-in process.
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(spec.Path)) return Builtin.BuiltinEffectFactory.Create(spec.Path, SampleRate, maxBlock);
        Isolation.RemotePlugin Remote(PluginHostLaunch? launch = null)
        {
            // A plug-in in its own process (the existing 64-bit isolation or a bitness helper): a crash there loses only this plug-in.
            var remote = launch is null
                ? new Isolation.RemotePlugin(spec, SampleRate, maxBlock, Threads)
                : new Isolation.RemotePlugin(spec, SampleRate, maxBlock, Threads, launch);
            // The last byte distinguishes a bridge helper (its own process may stop for bridge reasons). The UI names the
            // helper, never the plug-in, but treats it as that plug-in's crash for quarantine; the engine itself is still up,
            // so this never counts toward the engine-wide crash limit.
            var bridged = launch is not null;
            remote.Crashed += r => { Editors.CloseFor(r); Send(EngineEvent.PluginCrashed, w => { w.Write(slot); w.Write(index); w.WriteString(r.Path); w.Write(bridged); w.WriteString(r.CrashReportPath ?? ""); }); };
            remote.Edited += () => Send(EngineEvent.StateChanged, w => { w.Write(slot); w.Write(index); });
            // Both isolated paths (64-bit host and a bridge helper) send one MsgEditorClosed when the user closes the editor.
            remote.EditorClosed += () => Send(EngineEvent.EditorClosed, _ => { });
            return remote;
        }
        // A 32-bit VST2 plug-in and a 32-bit-only VST3 always go through the 32-bit helper, and a 64-bit Windows VST2
        // through the 64-bit helper under Wine, whether or not "run each plug-in separately" is on. A missing helper throws
        // a readable failure here; the chain keeps playing dry without it. Windows 64-bit VST2 and 64-bit VST3 never take
        // this path (they stay native).
        var route = Isolation.Vst32Bridge.Route(spec, Config.SeparateProcessPerPlugin);
        if (route is Isolation.Vst32Bridge.PluginRoute.Bridged32 or Isolation.Vst32Bridge.PluginRoute.Bridged64)
            return Remote(Isolation.Vst32Bridge.Launch(spec));
        if (route == Isolation.Vst32Bridge.PluginRoute.Isolated) return Remote();
        if (Plugins.PluginFactory.IsVst3(spec.Path, spec.Format) && !Vst3Plugin.Available) throw new DllNotFoundException();
        return Plugins.PluginFactory.Create(spec.Path, spec.Format, spec.IsInstrument, SampleRate, maxBlock, Threads);
    }

    /// <summary>Why a plug-in did not load, in words for the user: a VST3 plug-in whose helper library is missing says so instead of the raw "Dll was not found." (P2-07: per-OS shim name.)</summary>
    internal static string LoadFailureText(PluginSpec spec, Exception ex) =>
        ex.GetBaseException() is DllNotFoundException && Plugins.PluginFactory.IsVst3(spec.Path, spec.Format)
            ? $"TabForge's VST3 helper ({ShimFileName}) is missing or can't load."
            : ex.GetBaseException().Message;

    /// <summary>The VST3 shim file of this OS (mirrors Vst3Plugin.ShimFile without loading it).</summary>
    internal static string ShimFileName => OperatingSystem.IsWindows() ? "tfvst3.dll" : OperatingSystem.IsMacOS() ? "libtfvst3.dylib" : "libtfvst3.so";

    public void RemoveChain(int slot)
    {
        AssertMain();
        if (Defer(() => RemoveChain(slot))) return;
        Mixes.Remove(slot);
        ClipSpecs.Remove(slot); ClipOwners.Remove(slot);
        InputGains.Remove(slot);
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
        foreach (var plugin in chain.Plugins) if (!chain.Kept.Contains(plugin)) Editors.CloseFor(plugin);
        Retired.Add(chain, Mix?.Epoch);
    }

    private void ApplyClips(TrackChain chain, List<ClipSpec> specs, int owner)
    {
        AssertMain();
        chain.ClipOwner = owner;
        // A drop sends the whole list again: a player whose spec did not change keeps its open file and filled ring (no dropout under the playhead, no reopen per drop);
        // only new or changed specs get a player, and the players that left are retired.
        var old = chain.Clips;
        var leaving = old.ToList();
        var players = new List<Audio.ClipPlayer>(specs.Count);
        var fresh = new List<Audio.ClipPlayer>();
        foreach (var spec in specs.Where(s => File.Exists(s.File)))
        {
            var kept = leaving.FirstOrDefault(p => !p.Failed && p.Spec == spec);
            if (kept is not null) { leaving.Remove(kept); players.Add(kept); continue; }
            var made = new Audio.ClipPlayer(spec, SampleRate, Log);
            fresh.Add(made);
            players.Add(made);
        }
        Audio.DiskStreamer.Register(fresh);
        chain.Clips = players.ToArray();
        Audio.DiskStreamer.Unregister(leaving);
        foreach (var p in leaving) Retired.Add(p, Mix?.Epoch);
    }
}
