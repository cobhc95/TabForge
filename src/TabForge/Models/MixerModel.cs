using System.Text.Json.Serialization;

namespace TabForge.Models;

/// <summary>
/// The song's mixer: how tracks are grouped and each group's level, pan and pitch. Plain data saved in
/// .tforge; playback reads it through <see cref="MixerGroups"/>. Guitar Pro files cannot hold it, so exporting
/// bakes the group values into each track's volume, pan and transpose.
/// </summary>
public sealed class MixerSettings
{
    /// <summary>How tracks are grouped: see <see cref="MixerGrouping"/>.</summary>
    public string Grouping { get; set; } = MixerGrouping.ByInstrument;
    /// <summary>Group name → its controls. Missing groups use the defaults.</summary>
    public Dictionary<string, MixerGroupLevels> Groups { get; set; } = new();
    /// <summary>The main track list shows these groups as boxes (collapsible, dragged as a whole).</summary>
    public bool ShowGroupsInTrackList { get; set; }
    /// <summary>Groups collapsed in the track list (their tracks' rows are hidden).</summary>
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>Group name → its effects bus (the group's tracks sum into it before the master). Missing: no bus effects.</summary>
    public Dictionary<string, BusChain> Buses { get; set; } = new();
    /// <summary>The master effects chain, applied to the whole mix.</summary>
    public BusChain Master { get; set; } = new();
    /// <summary>Master pan offset added to every track's pan (-64…63), like the master volume scales every track's level.</summary>
    public int MasterPan { get; set; }
    /// <summary>
    /// Monitoring: true (default) = the app-wide monitor chain (app settings) is used for this song; false = this song's own
    /// <see cref="MonitorFx"/>. Monitor chains are heard live only, never rendered or exported.
    /// </summary>
    public bool MonitorUseGlobal { get; set; } = true;
    /// <summary>This song's own monitor chain (used when <see cref="MonitorUseGlobal"/> is off). Empty by default.</summary>
    public BusChain MonitorFx { get; set; } = new();

    [JsonIgnore]
    public bool IsDefault => Groups.Values.All(g => g.IsDefault) && !ShowGroupsInTrackList && CollapsedGroups.Count == 0
        && Buses.Values.All(b => b.IsDefault) && Master.IsDefault && MasterPan == 0 && MonitorUseGlobal && (MonitorFx?.IsDefault ?? true);

    /// <summary>The group's bus chain (created on first use).</summary>
    public BusChain Bus(string group)
    {
        if (!Buses.TryGetValue(group, out var bus)) Buses[group] = bus = new BusChain();
        return bus;
    }

    public MixerGroupLevels Levels(string group) =>
        Groups.TryGetValue(group, out var levels) ? levels : MixerGroupLevels.Neutral;

    public MixerGroupLevels Edit(string group)
    {
        if (!Groups.TryGetValue(group, out var levels)) Groups[group] = levels = new MixerGroupLevels();
        return levels;
    }
}

/// <summary>One group's controls, applied on top of each member track's own settings.</summary>
public sealed class MixerGroupLevels
{
    internal static readonly MixerGroupLevels Neutral = new();

    /// <summary>Group level in percent of each track's own volume (0–200, 100 = unchanged).</summary>
    public int Volume { get; set; } = 100;
    /// <summary>Pan offset added to each track's pan (-64…63).</summary>
    public int Pan { get; set; }
    /// <summary>Semitones added to each track's transpose (-24…24).</summary>
    public int Pitch { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }

    [JsonIgnore]
    public bool IsDefault => Volume == 100 && Pan == 0 && Pitch == 0 && !Mute && !Solo;
}

/// <summary>A group bus or the master: an effects chain (power on / off) the summed audio passes through at unity.</summary>
public sealed class BusChain
{
    /// <summary>Power: off bypasses the chain (the plug-ins are unloaded; the audio passes straight on).</summary>
    public bool On { get; set; } = true;
    public TabForge.Plugins.RigPreset Rig { get; set; } = new();

    [JsonIgnore] public bool IsDefault => On && Rig.Plugins.Count == 0;
    /// <summary>Runtime stand-in track the FX window and the engine client work with (see <see cref="MixerBuses"/>).</summary>
    [JsonIgnore] internal TrackModel? Proxy { get; set; }
}

/// <summary>
/// Group buses and the master as the rest of the app sees them: stand-in <see cref="TrackModel"/>s (not in the song's track list)
/// whose Rig is the bus chain and whose SoundSource is its power, with a fixed engine slot. Levels: group volume / pan and the
/// master volume / pan stay in each track's own level (one place, no double gain); the bus and master chains run at unity.
/// </summary>
public static class MixerBuses
{
    public const int BusBase = 256, MasterSlot = 288, MonitorSlot = 289;
    private static readonly string[] Known =
        { MixerGroups.Guitars, MixerGroups.Basses, MixerGroups.Keys, MixerGroups.Drums, MixerGroups.Other, MixerGroups.Everything, MixerGroups.AllButGuitarsAndBass, MixerGroups.Audio };

    public static int SlotOf(string group) { var i = Array.IndexOf(Known, group); return i < 0 ? -1 : BusBase + i; }

    public static TrackModel BusTrack(SongProject project, string group) => Proxy(project.Mixer.Bus(group), $"{group} bus", SlotOf(group));

    public static TrackModel MasterTrack(SongProject project) => Proxy(project.Mixer.Master, "Master", MasterSlot);

    /// <summary>
    /// The monitor chain in effect (live output only, after the master; never rendered): the app-wide chain, or the song's own when
    /// the song opted out. Stand-in track in <see cref="MonitorSlot"/>.
    /// </summary>
    public static TrackModel MonitorTrack(SongProject project, BusChain global) =>
        Proxy(project.Mixer.MonitorUseGlobal ? global : project.Mixer.MonitorFx ??= new BusChain(), "Monitor", MonitorSlot);

    /// <summary>The monitor chain in effect for this song.</summary>
    public static BusChain MonitorChain(SongProject project, BusChain global) => project.Mixer.MonitorUseGlobal ? global : project.Mixer.MonitorFx ??= new BusChain();

    /// <summary>True when the stand-in is the monitor chain (its FX window shows the "Use for all projects" box).</summary>
    public static bool IsMonitor(TrackModel track) => track.BusSlot == MonitorSlot;

    /// <summary>The monitor stand-in the engine runs (chain on with plug-ins), or null.</summary>
    public static TrackModel? ActiveMonitor(SongProject project, BusChain global) =>
        MonitorChain(project, global) is { On: true } c && c.Rig.Plugins.Count > 0 ? MonitorTrack(project, global) : null;

    private static TrackModel Proxy(BusChain bus, string name, int slot)
    {
        var t = bus.Proxy ??= new TrackModel { Name = name, InstrumentName = "Effects bus", Kind = TrackKind.Guitar };
        t.Name = name; t.Rig = bus.Rig; t.Bus = bus; t.BusSlot = slot; t.MidiSound = false;
        t.SoundSource = bus.On ? SoundSources.Plugins : SoundSources.Midi;
        return t;
    }

    /// <summary>Bus power from the mixer / track list: the bus and its stand-in (an open FX window) agree.</summary>
    public static void SetOn(BusChain bus, bool on)
    {
        bus.On = on;
        if (bus.Proxy is { } t) t.SoundSource = on ? SoundSources.Plugins : SoundSources.Midi;
    }

    /// <summary>The FX window changed a stand-in: its power goes back to the bus.</summary>
    public static void SyncBack(TrackModel proxy)
    {
        if (proxy.Bus is { } bus) bus.On = proxy.SoundSource == SoundSources.Plugins;
    }

    /// <summary>True when <paramref name="track"/> is a current stand-in of this song (FX windows of stale ones close).</summary>
    public static bool IsCurrent(SongProject project, TrackModel track) =>
        track.Bus is { } bus && (ReferenceEquals(bus, project.Mixer.Master) || ReferenceEquals(bus, project.Mixer.MonitorFx) || project.Mixer.Buses.Values.Any(b => ReferenceEquals(b, bus)));

    /// <summary>Stand-ins the engine runs: buses of groups that have tracks, switched on, with plug-ins; then the master.</summary>
    public static List<TrackModel> Active(SongProject project)
    {
        var list = new List<TrackModel>();
        foreach (var group in MixerGroups.AllNames(project.Mixer.Grouping))
            if (project.Mixer.Buses.TryGetValue(group, out var bus) && bus.On && bus.Rig.Plugins.Count > 0 && SlotOf(group) >= 0
                && project.Tracks.Any(t => MixerGroups.GroupOf(project, t) == group))
                list.Add(BusTrack(project, group));
        if (project.Mixer.Master is { On: true } master && master.Rig.Plugins.Count > 0) list.Add(MasterTrack(project));
        return list;
    }
}

/// <summary>Track-to-track links (sidechain sources, MIDI-output forwards) by stable track id, and cycle checks for the wiring UI.</summary>
public static class RoutingLinks
{
    /// <summary>Edges source → destination (the engine renders sources first).</summary>
    public static List<(string From, string To)> Edges(IEnumerable<TrackModel> tracks)
    {
        var edges = new List<(string, string)>();
        foreach (var t in tracks)
        {
            var id = t.Id.ToString("N");
            foreach (var p in t.Rig.Plugins)
            {
                if (!string.IsNullOrEmpty(p.SidechainTrackId) && p.SidechainTrackId != id) edges.Add((p.SidechainTrackId, id));
                if (!string.IsNullOrEmpty(p.MidiOutTrackId) && p.MidiOutTrackId != id) edges.Add((id, p.MidiOutTrackId));
            }
        }
        return edges;
    }

    /// <summary>True when the links plus one more edge <paramref name="from"/> → <paramref name="to"/> would form a cycle.</summary>
    public static bool WouldCycle(IEnumerable<TrackModel> tracks, string from, string to)
    {
        if (from == to) return true;
        var edges = Edges(tracks);
        // A cycle appears when "from" is already reachable from "to".
        var seen = new HashSet<string> { to };
        var stack = new Stack<string>(); stack.Push(to);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == from) return true;
            foreach (var (a, b) in edges) if (a == n && seen.Add(b)) stack.Push(b);
        }
        return false;
    }

    /// <summary>
    /// Model boundary check for songs loaded from disk (or edited outside the wiring window): clears any sidechain /
    /// MIDI-forward link that closes a loop, one at a time, until the link graph is acyclic. Returns what was cleared.
    /// </summary>
    public static List<string> BreakCycles(IList<TrackModel> tracks)
    {
        var cleared = new List<string>();
        for (var guard = 0; guard < 1024; guard++)
        {
            var broke = false;
            foreach (var t in tracks)
            {
                var id = t.Id.ToString("N");
                foreach (var p in t.Rig.Plugins)
                {
                    if (!string.IsNullOrEmpty(p.MidiOutTrackId) && ClosesLoop(tracks, p, forward: true, id, p.MidiOutTrackId))
                    { cleared.Add($"{t.Name}: MIDI forward"); p.MidiOutTrackId = ""; broke = true; break; }
                    if (!string.IsNullOrEmpty(p.SidechainTrackId) && ClosesLoop(tracks, p, forward: false, p.SidechainTrackId, id))
                    { cleared.Add($"{t.Name}: sidechain"); p.SidechainTrackId = ""; broke = true; break; }
                }
                if (broke) break;
            }
            if (!broke) return cleared;
        }
        return cleared;
    }

    private static bool ClosesLoop(IList<TrackModel> tracks, TabForge.Plugins.PluginSlot slot, bool forward, string from, string to)
    {
        // Test the edge against the graph without itself.
        var keep = forward ? slot.MidiOutTrackId : slot.SidechainTrackId;
        if (forward) slot.MidiOutTrackId = ""; else slot.SidechainTrackId = "";
        try { return WouldCycle(tracks, from, to); }
        finally { if (forward) slot.MidiOutTrackId = keep; else slot.SidechainTrackId = keep; }
    }
}

public static class MixerGrouping
{
    /// <summary>Guitars, Basses, Keys, Drums, Other.</summary>
    public const string ByInstrument = "By instrument";
    /// <summary>Guitars, Basses, Drums, and everything else together.</summary>
    public const string Compact = "Compact";
    /// <summary>Every track in one group.</summary>
    public const string None = "No groups";
    public static readonly string[] All = { ByInstrument, Compact, None };
}

/// <summary>Where a track's MIDI is played.</summary>
public static class SoundSources
{
    /// <summary>The Windows General MIDI synthesiser (or the track's chosen MIDI output).</summary>
    public const string Midi = "MIDI";
    /// <summary>The track's plug-in chain: a VST instrument followed by effects.</summary>
    public const string Plugins = "Plug-ins";
    public static readonly string[] All = { Midi, Plugins };
}

/// <summary>How a track is actually heard (see <see cref="MixerGroups.RouteOf"/>).</summary>
public enum TrackRoute
{
    /// <summary>Windows MIDI, exactly as without plug-ins (the audio engine is not involved).</summary>
    WindowsMidi,
    /// <summary>The track's VST instrument (plus its General MIDI sound when switched on), then its effects.</summary>
    Instrument,
    /// <summary>No VST instrument but effects: the General MIDI sound is rendered by the engine so the effects can shape it.</summary>
    MidiThroughEffects,
    /// <summary>The chain is on but produces nothing (no instrument, MIDI sound off).</summary>
    Silent,
}

/// <summary>Group membership and the effective level / pan / pitch a track plays at.</summary>
public static class MixerGroups
{
    public const string Guitars = "Guitars";
    public const string Basses = "Basses";
    public const string Keys = "Keys";
    public const string Drums = "Drums";
    public const string Other = "Other";
    public const string Everything = "All tracks";
    public const string AllButGuitarsAndBass = "Other instruments";
    /// <summary>The family of audio tracks. Not in <see cref="Names"/> (so "Move to" menus and existing songs' group lists are unchanged); a group level is only stored once the user edits it.</summary>
    public const string Audio = "Audio";

    /// <summary><see cref="Names"/> plus the Audio family (unless the grouping is "All tracks"): every group a track can be in.</summary>
    public static IReadOnlyList<string> AllNames(string grouping) =>
        grouping == MixerGrouping.None ? Names(grouping) : Names(grouping).Append(Audio).ToList();

    /// <summary>The groups a grouping mode can produce, in display order.</summary>
    public static IReadOnlyList<string> Names(string grouping) => grouping switch
    {
        MixerGrouping.Compact => new[] { Guitars, Basses, Drums, AllButGuitarsAndBass },
        MixerGrouping.None => new[] { Everything },
        _ => new[] { Guitars, Basses, Keys, Drums, Other },
    };

    /// <summary>The group a track belongs to (its own choice first, else by instrument).</summary>
    public static string GroupOf(SongProject project, TrackModel track)
    {
        var grouping = project.Mixer.Grouping;
        if (grouping == MixerGrouping.None) return Everything;
        if (!string.IsNullOrWhiteSpace(track.MixerGroup) && AllNames(grouping).Contains(track.MixerGroup)) return track.MixerGroup;
        var family = Family(track);
        if (family == Audio) return Audio;
        if (grouping == MixerGrouping.Compact)
            return family is Guitars or Basses or Drums ? family : AllButGuitarsAndBass;
        return family;
    }

    /// <summary>Instrument family from the track type, GM program and name.</summary>
    public static string Family(TrackModel track)
    {
        if (track.IsAudio) return Audio;
        if (track.Kind == TrackKind.Drums || track.MidiChannel == 9) return Drums;
        var program = track.MidiProgram;
        var name = " " + System.Text.RegularExpressions.Regex.Replace((track.InstrumentName ?? "").ToLowerInvariant(), "[^a-z]+", " ");
        if (track.Kind == TrackKind.Bass || program is >= 32 and <= 39 || name.Contains(" bass ") && !name.Contains("contrabass")) return Basses;
        if (track.Kind == TrackKind.Guitar || program is >= 24 and <= 31 || name.Contains(" guitar")) return Guitars;
        // Keyboard-like: pianos, chromatic percussion, organs, synth leads / pads / keys.
        if (track.Kind == TrackKind.Keys || program is >= 0 and <= 23 or >= 80 and <= 95
            || name.Contains(" piano") || name.Contains(" organ") || name.Contains(" synth") || name.Contains(" keys")) return Keys;
        return Other;
    }

    /// <summary>The group's controls for a track.</summary>
    public static MixerGroupLevels LevelsFor(SongProject project, TrackModel track) =>
        project.Mixer.Levels(GroupOf(project, track));

    /// <summary>Track volume (0–127) after its group's level.</summary>
    public static int Volume(SongProject project, TrackModel track) =>
        Math.Clamp((int)Math.Round(track.Volume * LevelsFor(project, track).Volume / 100.0), 0, 127);

    /// <summary>Track pan (0–127) after its group's pan offset.</summary>
    public static int Pan(SongProject project, TrackModel track) =>
        Math.Clamp(track.Pan + LevelsFor(project, track).Pan + project.Mixer.MasterPan, 0, 127);

    /// <summary>Track transpose in semitones after its group's pitch.</summary>
    public static int Transpose(SongProject project, TrackModel track) =>
        Math.Clamp(track.Transpose + LevelsFor(project, track).Pitch, -48, 48);

    /// <summary>The group's part of the audibility rule: with any group soloed only soloed groups play (a muted, soloed group plays); otherwise a muted group is silent.</summary>
    public static bool GroupSilences(SongProject project, TrackModel track)
    {
        var levels = LevelsFor(project, track);
        var anySolo = project.Mixer.Groups.Where(g => g.Value.Solo).Select(g => g.Key).ToHashSet();
        return anySolo.Count > 0 ? !anySolo.Contains(GroupOf(project, track)) : levels.Mute;
    }

    /// <summary>
    /// THE mute/solo rule, shared by MIDI scheduling, the audio clip path (the level sent to the engine), the mixer and offline render.
    /// With any track soloed only soloed tracks play (a track both muted and soloed plays); otherwise a muted track is silent.
    /// Groups follow the same rule one level up. Mute covers the whole track: every lane and clip.
    /// </summary>
    public static bool IsAudible(SongProject project, TrackModel track)
    {
        var trackPlays = project.Tracks.Any(t => t.Solo) ? track.Solo : !track.Mute;
        return trackPlays && !GroupSilences(project, track);
    }

    /// <summary>
    /// The one rule for how a track sounds. The engine's built-in General MIDI synth runs ONLY for
    /// <see cref="TrackRoute.MidiThroughEffects"/> (chain on, no VST instrument, at least one enabled effect,
    /// MIDI sound on); every other track without an instrument stays on Windows MIDI, so nothing extra runs.
    /// </summary>
    public static TrackRoute RouteOf(TrackModel track, MixerOptions options)
    {
        // An audio track has no notes of its own: its MIDI clips sound only through an enabled instrument plug-in, never the GM synth.
        if (track.IsAudio) return InstrumentPlays(track) ? TrackRoute.Instrument : TrackRoute.Silent;
        var playAll = options.PlayAllThroughEngine;
        // The user unticked both "Through chain" and "GM sound" on a track with plug-ins: silence is what they asked for.
        if (IsSilentRoute(track)) return TrackRoute.Silent;
        if (track.SoundSource != SoundSources.Plugins) return playAll ? TrackRoute.MidiThroughEffects : TrackRoute.WindowsMidi;
        var plugins = track.Rig.Plugins.Where(p => p.Enabled).ToList();
        if (plugins.Any(p => p.Type == TabForge.Plugins.PluginSlotType.Instrument && !p.Unavailable)) return TrackRoute.Instrument;
        return plugins.Count > 0 || playAll ? TrackRoute.MidiThroughEffects : TrackRoute.WindowsMidi;
    }

    /// <summary>
    /// The track is silent whatever the playback routing is (no <see cref="MixerOptions"/> needed): both "Through chain" and "GM sound" are
    /// unticked on a track with plug-ins, or its chain is on with no instrument playing and no GM sound.
    /// </summary>
    public static bool IsSilentRoute(TrackModel track)
    {
        if (track.IsAudio) return !InstrumentPlays(track);
        if (track.SoundSource != SoundSources.Plugins) return track.MidiSoundManualOff && !track.MidiSound && track.Rig.Plugins.Count > 0;
        var instrumentPlays = track.Rig.Plugins.Any(p => p.Enabled && p.Type == TabForge.Plugins.PluginSlotType.Instrument && !p.Unavailable);
        return !instrumentPlays && !track.MidiSound;
    }

    /// <summary>True when the song uses anything a Guitar Pro file cannot store (plug-ins or audio).</summary>
    public static bool UsesPlugins(SongProject project) =>
        project.Tracks.Any(t => t.SoundSource == SoundSources.Plugins || t.Rig.Plugins.Count > 0 || t.AudioClips.Count > 0 || t.Lanes.Count > 0);

    /// <summary>The track needs the audio engine for audio (clips to play, or its input monitored).</summary>
    public static bool UsesEngineAudio(TrackModel track) =>
        (track.RecordArm && !AudioInputs.IsMidi(track.AudioInput)) || track.AudioClips.Any(c => !c.IsMidi && !c.Muted);

    /// <summary>The track's MIDI plays through the engine (VST instrument or effects on the General MIDI sound).</summary>
    /// A track with plug-ins stays in the engine whatever its route (chain off, everything bypassed, silent): the plug-ins stay
    /// loaded with their editors open, and its General MIDI sound plays on the engine's synth in time with the plug-in tracks.
    public static bool MidiInEngine(TrackModel track, MixerOptions options) =>
        track.IsAudio ? InstrumentPlays(track) :
        RouteOf(track, options) is TrackRoute.Instrument or TrackRoute.MidiThroughEffects || (!track.IsBus && track.Rig.Plugins.Count > 0);

    /// <summary>The track's General MIDI sound is heard (for a track in the engine: its GM synth is on).</summary>
    public static bool GmSounds(TrackModel track, MixerOptions options) => !track.IsAudio && RouteOf(track, options) switch
    {
        TrackRoute.Instrument => track.MidiSound,
        TrackRoute.Silent => false,
        _ => true,
    };

    /// <summary>A VST instrument really plays this track: chain on, the instrument not bypassed, and the engine can run it (not crashed, quarantined, untrusted or missing).</summary>
    public static bool InstrumentPlays(TrackModel track) =>
        track.SoundSource == SoundSources.Plugins
        && track.Rig.Plugins.Any(p => p.Enabled && !p.Unavailable && p.Type == TabForge.Plugins.PluginSlotType.Instrument);

    /// <summary>
    /// Keeps "GM sound" in line with reality: when no VST instrument plays the track (chain off, instrument bypassed or removed) GM is
    /// ticked, and when an instrument plays again an automatic tick is taken back. A manual untick is respected (then silence is correct).
    /// Returns true when it changed the track.
    /// </summary>
    public static bool ApplyAutoGm(TrackModel track, MixerOptions options)
    {
        if (!options.AutoGmSound || track.IsBus || track.IsAudio) return false;
        // A track with the chain on but nothing left in it (the last plug-in removed) is silent without GM too; a plain track is not touched.
        if (track.Rig.Plugins.Count == 0 && track.SoundSource != SoundSources.Plugins) return false;
        var instrumentPlays = InstrumentPlays(track);
        if (!instrumentPlays && !track.MidiSound && !track.MidiSoundManualOff) { track.MidiSound = true; track.MidiSoundAuto = true; return true; }
        if (instrumentPlays && track.MidiSound && track.MidiSoundAuto) { track.MidiSound = false; track.MidiSoundAuto = false; return true; }
        return false;
    }

    /// <summary>
    /// True when the song has TabForge audio data Guitar Pro does not understand: plug-ins, FX, mixer groups or
    /// moved tracks. Only then does saving as .gp offer the choices; a plain song saves as .gp as before.
    /// </summary>
    public static bool HasAudioData(SongProject project) =>
        UsesPlugins(project) || !project.Mixer.IsDefault || project.Mixer.Grouping != MixerGrouping.ByInstrument
        || project.Tracks.Any(t => t.MixerGroup is not null);
}
