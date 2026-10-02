using TabForge.Plugins;

namespace TabForge.Models;

/// <summary>Which kind of owner a <see cref="SongRig"/> belongs to. A flags value so a query can name several kinds.</summary>
[Flags]
public enum SongRigKind
{
    None = 0,
    /// <summary>A track of the song.</summary>
    Track = 1,
    /// <summary>A group bus (the group's tracks sum into it before the master).</summary>
    Bus = 2,
    /// <summary>The master chain.</summary>
    Master = 4,
    /// <summary>A monitor chain (live output only, after the master).</summary>
    Monitor = 8,
    /// <summary>The effects chains that are not tracks: buses, master and monitor.</summary>
    Chains = Bus | Master | Monitor,
    Every = Track | Chains,
}

/// <summary>One rig of the song and the thing that owns it.</summary>
public sealed class SongRig
{
    private readonly SongProject _project;

    internal SongRig(SongProject project, SongRigKind kind, string owner, RigPreset rig, TrackModel? track, BusChain? chain, string? group, bool runs)
    {
        _project = project;
        Kind = kind;
        Owner = owner;
        Rig = rig;
        Track = track;
        Chain = chain;
        Group = group;
        Runs = runs;
    }

    public SongRigKind Kind { get; }
    /// <summary>The name a message shows: the track's name, "&lt;group&gt; bus", "Master" or "Monitor".</summary>
    public string Owner { get; }
    public RigPreset Rig { get; }
    /// <summary>The song's track for a track rig; null for the chains.</summary>
    public TrackModel? Track { get; }
    /// <summary>The bus, master or monitor chain; null for a track.</summary>
    public BusChain? Chain { get; }
    /// <summary>The group a bus belongs to; null otherwise.</summary>
    public string? Group { get; }
    /// <summary>
    /// True when the audio engine can be asked to run this rig: every track; a bus that is switched on, has plug-ins and belongs to a group with
    /// tracks; the master when it is on and has plug-ins; the monitor chain in effect when it is on and has plug-ins.
    /// </summary>
    public bool Runs { get; }

    /// <summary>The track the engine and the FX windows work with: the song's own track, or the stand-in of the bus, master or monitor chain (see <see cref="MixerBuses"/>).</summary>
    public TrackModel AsTrack() => Kind switch
    {
        SongRigKind.Track => Track!,
        SongRigKind.Bus => MixerBuses.BusTrack(_project, Group!),
        SongRigKind.Master => MixerBuses.MasterTrack(_project),
        _ => MixerBuses.MonitorTrack(_project, Chain!),
    };
}

/// <summary>
/// The song's rigs, enumerated one way for everyone who needs them: saving, plug-in trust, the audio data file and the engine sync.
/// <see cref="All"/> is every rig the song stores, whether or not it is switched on or reached by audio; <see cref="Running"/> is the part the
/// audio engine runs, in the order it loads them.
/// </summary>
public static class SongRigs
{
    /// <summary>
    /// Every rig of the song: its tracks in list order, then each stored group bus in the mixer's storage order (switched off, without plug-ins,
    /// or without tracks included), the master, and a monitor chain. Owners without a rig (a missing bus, master or monitor chain) are left out.
    /// The monitor chain is the song's own (<see cref="MixerSettings.MonitorFx"/>), whether or not the song uses it; with
    /// <paramref name="globalMonitor"/> given, the chain in effect instead: the app-wide chain when the song uses it, else the song's own.
    /// </summary>
    public static IEnumerable<SongRig> All(SongProject project, BusChain? globalMonitor = null)
    {
        foreach (var track in project.Tracks)
            if (track.Rig is { } rig) yield return new SongRig(project, SongRigKind.Track, track.Name, rig, track, null, null, true);
        if (project.Mixer is not { } mixer) yield break;
        if (mixer.Buses is not null)
            foreach (var (group, bus) in mixer.Buses)
                if (bus?.Rig is { } rig)
                    yield return new SongRig(project, SongRigKind.Bus, $"{group} bus", rig, null, bus, group,
                        IsOn(bus) && MixerBuses.SlotOf(group) >= 0 && project.Tracks.Any(t => MixerGroups.GroupOf(project, t) == group));
        if (mixer.Master?.Rig is { } masterRig)
            yield return new SongRig(project, SongRigKind.Master, "Master", masterRig, null, mixer.Master, null, IsOn(mixer.Master));
        var inEffectGlobal = globalMonitor is not null && mixer.MonitorUseGlobal;
        var monitor = inEffectGlobal ? globalMonitor : mixer.MonitorFx;
        if (monitor?.Rig is { } monitorRig)
            yield return new SongRig(project, SongRigKind.Monitor, "Monitor", monitorRig, null, monitor, null, (inEffectGlobal || !mixer.MonitorUseGlobal) && IsOn(monitor));
    }

    /// <summary>
    /// The rigs of <paramref name="kinds"/> the engine runs (<see cref="SongRig.Runs"/>), in loading order: tracks, buses in the order of the
    /// song's grouping, the master, the monitor chain in effect (<paramref name="globalMonitor"/> is the app-wide chain; null: the song's own only).
    /// </summary>
    public static IEnumerable<SongRig> Running(SongProject project, BusChain? globalMonitor, SongRigKind kinds = SongRigKind.Every)
    {
        var groups = MixerGroups.AllNames(project.Mixer?.Grouping ?? MixerGrouping.ByInstrument).ToList();
        return All(project, globalMonitor).Where(r => r.Runs && (r.Kind & kinds) != 0)
            .OrderBy(r => r.Kind).ThenBy(r => r.Group is null ? 0 : groups.IndexOf(r.Group));
    }

    private static bool IsOn(BusChain chain) => chain.On && chain.Rig.Plugins is { Count: > 0 };
}
