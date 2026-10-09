using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

/// <summary>Keeps the engine's graph in step with the documents: slots, ownership and warm parking, wiring, MIDI routes and processors, gain, bypass and audio clips.</summary>
public sealed partial class AudioEngineClient : IDisposable
{
    /// <param name="project">The song (group buses, master chain and the routing graph); null: tracks only.</param>
    /// <param name="owner">
    /// The document these tracks belong to (R-10: the engine has one explicit owner, the active document). Null: <paramref name="project"/>.
    /// Chains of an earlier owner are parked (kept loaded, silent) for <see cref="WarmIdle"/>, so switching back reloads nothing.
    /// </param>
    /// <param name="media">The owning document's media context (base directory, approval scope): every linked audio clip of these tracks is judged with it. Null: no document (headless probes), nothing remote is allowed.</param>
    /// <param name="skippedPlugins">The owning document's plug-ins skipped for now (R-06 "Disable it" after a very slow load): never saved, never added to.</param>
    public void Sync(IEnumerable<TrackModel> tracks, PluginSettings settings, Func<TrackModel, (int Volume, int Pan)>? mix = null, SongProject? project = null, object? owner = null,
        MediaContext? media = null, ICollection<string>? skippedPlugins = null)
    {
        if (Rendering) return;
        _ui ??= SynchronizationContext.Current;
        RecordingOffsetMs = settings.RecordingOffsetMs;
        owner ??= (object?)project ?? TracksOnlyOwner;
        OwnerIdOf(owner);
        ICollection<string> quarantined = Quarantine?.Invoke() ?? Array.Empty<string>();
        if (skippedPlugins is { Count: > 0 } skipped)
            quarantined = quarantined.Concat(skipped).ToHashSet(StringComparer.OrdinalIgnoreCase);   // a copy: the quarantine list itself is not changed
        // Tracks whose MIDI plays through plug-ins, or that have audio (clips, or an armed input).
        var wanted = tracks.Where(t => MixerGroups.MidiInEngine(t, Mixer) || MixerGroups.UsesEngineAudio(t)).ToList();
        var config = new EngineConfig(settings.Driver, settings.Device, settings.SampleRate, settings.BufferSize, settings.SeparateProcessPerPlugin, settings.InputDevice, settings.AsioInputChannel, settings.AsioOutputChannel, settings.AsioInputLastChannel, settings.AsioInputsEnabled, settings.FollowWindowsVolume, settings.AsioOutputLastChannel);
        // Multi-tab playback: tracks of other documents that are still playing stay live (never parked or removed here).
        var liveElsewhere = IsRunning ? _slots.Where(kv => !kv.Key.IsBus && !wanted.Contains(kv.Key) && PlaysElsewhere(kv.Value, owner)).Select(kv => kv.Key).ToList() : new List<TrackModel>();

        if (wanted.Count == 0 && liveElsewhere.Count == 0 && (!IsRunning || WarmIdle <= TimeSpan.Zero))
        {
            if (IsRunning) Stop();
            _slots.Clear();
            _sentChains.Clear();
            _sentChainIds.Clear();
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
        if (project is not null && !ownIdle) wanted.AddRange(SongRigs.Running(project, null, SongRigKind.Bus | SongRigKind.Master).Select(r => r.AsTrack()));
        // Monitor FX (speaker calibration): after the master, live output only; the app-wide chain or the song's own. Global slot: wanted while any document's tracks are live.
        if (project is not null && !idle) wanted.AddRange(SongRigs.Running(project, settings.MonitorFx, SongRigKind.Monitor).Select(r => r.AsTrack()));
        if (!ownIdle && _config != config) { _config = config; Send(EngineCommand.Configure, w => w.Write(config)); }
        if (!ownIdle) SyncWindowsPathOffset(config);
        if (EngineProcessId is int enginePid && _limiterSent != (enginePid, settings.LiveLimiter))
        {
            _limiterSent = (enginePid, settings.LiveLimiter);
            var liveLimiter = settings.LiveLimiter;
            Send(EngineCommand.SetLiveLimiter, w => w.Write(liveLimiter));
        }
        var now = WarmClock();
        foreach (var parked in _parkedSince.Keys.Where(s => !_slots.ContainsValue(s)).ToList()) _parkedSince.Remove(parked);   // a crash cleared the slots

        AdoptReplacedTracks(wanted, liveElsewhere, owner);
        CurrentOwner = owner;
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
            var useSynth = !track.IsBus && MixerGroups.MidiInEngine(track, Mixer);
            var key = $"{useSynth}|{track.Name}|{(track.BusSlot == MixerBuses.MonitorSlot ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(track.Bus) : 0)}|" + string.Join("|", specs.Select(s => $"{s.Id}:{s.Path}:{s.Skip}:{s.Wet}:{s.IsInstrument}:{s.Pins}"));   // OutputDb and Enabled are not part of the key: SetPluginGain / SetPluginEnabled change them live
            if (!(_sentChains.TryGetValue(slot, out var sent) && sent == key))
            {
                _sentChains[slot] = key;
                _sentChainIds[slot] = specs.Select(s => s.Id).ToArray();
                _chainRequests[slot] = _chainRequests.GetValueOrDefault(slot) + 1;
                var name = track.Name;
                ChainLoadProtocol.Send((c, p) => Send(c, p), slot, useSynth, name, specs, Interlocked.Increment(ref _nextChainLoad));   // spec first, then one frame per state
                _sentBypass[slot] = string.Join(",", specs.Select(s => s.Enabled));   // the new chain starts with these
                _sentGain[slot] = string.Join(",", specs.Select(s => s.OutputDb.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            }
            SyncBypass(track, slot);
            SyncGain(track, slot);
            if (useSynth) SyncSynth(slot, MixerGroups.GmSounds(track, Mixer));
            if (!track.IsBus) SyncAudio(track, slot, mix?.Invoke(track) ?? (track.Mute ? 0 : track.Volume, track.Pan), media);
        }
        foreach (var track in wanted) { if (!track.IsBus) SyncMidiRoute(track, wanted, project); SyncMidiProcessors(track); SyncWiring(track); }
        SyncGraph(wanted, project, liveElsewhere);
        _idleSince = idle ? _idleSince ?? now : null;
        ScheduleWarmCheck();
    }

    /// <summary>
    /// A track is the same engine track as long as its id is: an undo or a reload builds new <see cref="TrackModel"/> objects with the same ids,
    /// and each takes over the slot of the object it replaces (same owner, same <see cref="TrackModel.Id"/>; a bus stand-in by its fixed slot), so
    /// the chain is compared and kept instead of unloaded and loaded again.
    /// </summary>
    private void AdoptReplacedTracks(List<TrackModel> wanted, List<TrackModel> liveElsewhere, object owner)
    {
        var fresh = wanted.Where(t => !_slots.ContainsKey(t)).ToList();
        if (fresh.Count == 0) return;
        var stale = _slots.Keys.Where(t => !wanted.Contains(t) && !liveElsewhere.Contains(t)).ToList();
        foreach (var track in fresh)
        {
            var old = stale.FirstOrDefault(o => track.IsBus
                ? o.IsBus && o.BusSlot == track.BusSlot
                : !o.IsBus && o.Id == track.Id && _slotOwners.TryGetValue(_slots[o], out var slotOwner) && ReferenceEquals(slotOwner, owner));
            if (old is null) continue;
            stale.Remove(old);
            var slot = _slots[old];
            _slots.Remove(old);
            _slots[track] = slot;
        }
    }

    private bool PlaysElsewhere(int slot, object owner)
        => _slotOwners.TryGetValue(slot, out var slotOwner) && !ReferenceEquals(slotOwner, owner) && IsOwnerPlaying?.Invoke(slotOwner) == true;

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
        _sentChainIds.Remove(slot);
        _sentAudio.Remove(slot);
        _audioContext.Remove(slot);
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
        var ownerId = _slotOwners.TryGetValue(slot, out var parkedOwner) ? OwnerIdOf(parkedOwner) : 0;
        Send(EngineCommand.SetClips, w => { w.Write(slot); w.Write(new List<ClipSpec>()); w.Write(ownerId); });
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
        foreach (var track in _slots.Where(kv => !kv.Key.IsBus && _slotOwners.TryGetValue(kv.Value, out var o) && ReferenceEquals(o, owner)).Select(kv => kv.Key).ToList())
            RemoveSlot(track);
        foreach (var slot in _slotOwners.Where(kv => ReferenceEquals(kv.Value, owner)).Select(kv => kv.Key).ToList())
            _slotOwners.Remove(slot);
        // No track of any document left (the closed one was the last, e.g. its window closed and no other window syncs): its bus,
        // master and monitor chains go too and the warm period starts, as a Sync without engine tracks would do, instead of the
        // closed song's bus chains staying loaded and unowned until some later Sync.
        if (IsRunning && !_slots.Keys.Any(t => !t.IsBus))
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

    /// <summary>The plug-in at chain index <paramref name="index"/> failed to load, per the engine's acknowledgement of the current request (an older or missing acknowledgement says nothing).</summary>
    internal static bool InstrumentFailed(ChainAck? ack, int currentGeneration, int index) =>
        ack is not null && ack.Generation == currentGeneration && ack.Plugins.Any(r => r.Index == index && r.Status != PluginLoadStatus.Loaded);

    /// <summary>
    /// Marks each track's plug-ins the engine cannot play (<see cref="PluginSlot.Unavailable"/>): untrusted, missing, quarantined (a crash) or
    /// skipped for this song, or reported as not loaded by the engine. Cheap (no engine traffic); returns true when any flag changed, so the
    /// caller re-applies the automatic GM sound. Run before <see cref="MixerGroups.ApplyAutoGm"/>.
    /// </summary>
    public bool RefreshAvailability(IEnumerable<TrackModel> tracks, PluginSettings settings, ICollection<string>? skippedPlugins = null)
    {
        var changed = false;
        ICollection<string> quarantined = Quarantine?.Invoke() ?? Array.Empty<string>();
        if (skippedPlugins is { Count: > 0 } skipped) quarantined = quarantined.Concat(skipped).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            if (track.IsBus || track.Rig.Plugins.Count == 0) continue;
            var slotKnown = _slots.TryGetValue(track, out var slot);
            ChainAck? ack = null;
            if (slotKnown) _lastAcks.TryGetValue(slot, out ack);
            var generation = slotKnown ? _chainRequests.GetValueOrDefault(slot) : -1;
            var specs = PluginTrust.BuildSpecs(track.Rig.Plugins, quarantined, settings);
            for (var i = 0; i < track.Rig.Plugins.Count; i++)
            {
                var p = track.Rig.Plugins[i];
                var unavailable = specs[i].Skip || InstrumentFailed(ack, generation, i);
                if (p.Unavailable != unavailable) { p.Unavailable = unavailable; changed = true; }
            }
        }
        return changed;
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

    /// <summary>Sends each loaded track's level at once (a mute/solo toggle is heard within a block); the full <see cref="Sync"/> that follows reconciles.</summary>
    public void SetTrackLevelsNow(IEnumerable<TrackModel> tracks, Func<TrackModel, (int Volume, int Pan)> mix)
    {
        if (!IsRunning) return;
        foreach (var track in tracks)
        {
            if (track.IsBus || !_slots.TryGetValue(track, out var slot)) continue;
            var (volume, pan) = mix(track);
            Send(EngineCommand.SetTrackMix, w => { w.Write(slot); w.Write(volume); w.Write(pan); });
        }
    }

    private void SyncAudio(TrackModel track, int slot, (int Volume, int Pan) mix, MediaContext? media)
    {
        media ??= MediaContext.Anonymous;
        _audioContext[slot] = (track, media, mix);
        // Linked audio goes through the media policy with the owning document's context: device paths are refused, network / removable
        // folders wait for that document's approval (a clip that is not allowed is simply not sent, so the engine never opens it); the engine
        // gets the normalised absolute path. This runs on the control thread; the engine's audio callback never evaluates paths.
        var clips = new List<ClipSpec>();
        foreach (var c in track.AudioClips)
        {
            if (c.IsMidi || !ClipLanes.Audible(track, c)) continue;
            // UI thread: no file-system call. A clip whose classification is still being resolved is left out now and judged again when MediaAccess.Resolved arrives (RefreshClips).
            if (MediaAccess.EvaluateNoWait(c.File, media) is not { Allowed: true } access) continue;
            // A clip dragged past the end of its media loops: it plays as one engine clip per pass (fades only on the first start and the last end).
            var pieces = TabForge.Services.ClipLoop.Pieces(c);
            for (var i = 0; i < pieces.Count; i++)
                clips.Add(new ClipSpec(access.Verdict.FullPath, pieces[i].StartSec, pieces[i].OffsetSec, pieces[i].SourceLengthSec, c.GainDb, c.Pitch, c.Speed,
                    i == 0 ? c.FadeInSec : 0, i == pieces.Count - 1 ? c.FadeOutSec : 0));
        }
        var armMode = Array.IndexOf(AudioInputs.Audio, track.AudioInput);
        var armed = track.RecordArm && armMode >= 0;   // MIDI input is recorded by the editor, not the engine
        var ownerId = _slotOwners.TryGetValue(slot, out var slotOwner) ? OwnerIdOf(slotOwner) : 0;
        var key = $"{ownerId}|{mix.Volume}|{mix.Pan}|{armed}|{armMode}|{track.MonitorInput}|" + string.Join("|", clips.Select(c => $"{c.File}@{c.StartSec:0.###}+{c.OffsetSec:0.###}/{c.SourceLengthSec:0.###}/{c.GainDb:0.##}/{c.Pitch:0.##}/{c.Speed:0.###}/{c.FadeInSec:0.###}/{c.FadeOutSec:0.###}"));
        if (_sentAudio.TryGetValue(slot, out var sent) && sent == key) return;
        _sentAudio[slot] = key;
        Send(EngineCommand.SetTrackMix, w => { w.Write(slot); w.Write(mix.Volume); w.Write(mix.Pan); });
        ClipsSentForTest?.Invoke(slot, clips);
        Send(EngineCommand.SetClips, w => { w.Write(slot); w.Write(clips); w.Write(ownerId); });
        Send(EngineCommand.SetArm, w => { w.Write(slot); w.Write(armed); w.Write(Math.Max(0, armMode)); w.Write(track.MonitorInput); });
    }
}
