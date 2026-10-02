using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// The song's rigs (tracks, group buses, master, monitor chain) as the save, trust, sync and audio-data-file paths enumerate them. The pins use one
/// project with every rig kind, plug-ins on each, and a bus dictionary whose order differs from the group order.
/// </summary>
public static partial class SelfTest
{
    private const string RigsPluginDir = @"D:\tf-songrigs-pin\";

    private static PluginSlot RigsPlugin(string name, int stateMb = 0, string? sidechain = null, string? midiFrom = null)
    {
        var slot = new PluginSlot { Name = "P-" + name, Path = RigsPluginDir + name + ".dll" };
        if (stateMb > 0) slot.State = new string((char)65, stateMb * 1024 * 1024);
        if (sidechain is not null) slot.SidechainTrackId = sidechain;
        if (midiFrom is not null) slot.MidiIn = new PluginMidiIn { Source = PluginMidiIn.OtherTrack, TrackId = midiFrom };
        return slot;
    }

    /// <summary>
    /// Tracks Lead (Guitars), Bass (Basses), Pad (Keys). Buses added in the order Basses, Guitars, Drums, Keys: Basses is switched off, Guitars is
    /// active, Drums has no tracks, Keys has no plug-ins. The master is active. The song's own monitor chain has a plug-in and the song uses the app-wide chain.
    /// </summary>
    private static SongProject RigsProject(bool withLinks)
    {
        var project = new SongProject();
        var ghost = Guid.NewGuid().ToString("N");
        TrackModel Track(string name, TrackKind kind, int stateMb) =>
            new() { Name = name, Kind = kind, Rig = { Plugins = { RigsPlugin(name, stateMb, withLinks ? ghost : null, withLinks ? ghost : null) } } };
        project.Tracks.Add(Track("Lead", TrackKind.Guitar, 4));
        project.Tracks.Add(Track("Bass", TrackKind.Bass, 2));
        project.Tracks.Add(Track("Pad", TrackKind.Keys, 1));
        void Bus(BusChain chain, string name, int stateMb) =>
            chain.Rig.Plugins.Add(RigsPlugin(name, stateMb, withLinks ? ghost : null, withLinks ? ghost : null));
        Bus(project.Mixer.Bus(MixerGroups.Basses), "BassesBus", 6);
        project.Mixer.Bus(MixerGroups.Basses).On = false;
        Bus(project.Mixer.Bus(MixerGroups.Guitars), "GuitarsBus", 3);
        Bus(project.Mixer.Bus(MixerGroups.Drums), "DrumsBus", 5);
        _ = project.Mixer.Bus(MixerGroups.Keys);
        Bus(project.Mixer.Master, "Master", 7);
        Bus(project.Mixer.MonitorFx, "Monitor", 8);
        return project;
    }

    private static BusChain RigsGlobalMonitor(bool withPlugin)
    {
        var chain = new BusChain();
        if (withPlugin) chain.Rig.Plugins.Add(RigsPlugin("GlobalMonitor"));
        return chain;
    }

    /// <summary>The old audio-data-file enumeration (largest states, relinking): tracks, then each stored bus in dictionary order, the master, the song's monitor chain.</summary>
    private static List<(string Owner, RigPreset Rig)> LegacyDataRigs(SongProject project)
    {
        var list = new List<(string, RigPreset)>();
        void Rig(RigPreset? rig, string owner) { if (rig is not null) list.Add((owner, rig)); }
        foreach (var t in project.Tracks) Rig(t.Rig, t.Name);
        if (project.Mixer is { } mixer)
        {
            if (mixer.Buses is not null) foreach (var (group, bus) in mixer.Buses) Rig(bus?.Rig, $"{group} bus");
            Rig(mixer.Master?.Rig, "Master");
            Rig(mixer.MonitorFx?.Rig, "Monitor");
        }
        return list;
    }

    /// <summary>The old trust enumeration: tracks, then the buses and master the engine runs.</summary>
    private static List<RigPreset> LegacyTrustRigs(SongProject project) =>
        project.Tracks.Concat(MixerBuses.Active(project)).Distinct().Select(t => t.Rig).ToList();

    /// <summary>The old save-time enumeration (the states the engine is asked for): tracks, running buses and master, then the monitor chain in effect.</summary>
    private static List<TrackModel> LegacySyncAndSave(SongProject project, BusChain globalMonitor) =>
        project.Tracks.Concat(MixerBuses.Active(project))
            .Concat(MixerBuses.ActiveMonitor(project, globalMonitor) is { } monitorFx ? new[] { monitorFx } : Array.Empty<TrackModel>()).ToList();

    private static string RigsNames(IEnumerable<TrackModel> tracks) => string.Join(", ", tracks.Select(t => t.Name));

    private static void TestSongRigs()
    {
        Section("Song rigs");
        RigsPinCurrentEnumerations();
        RigsMatchTheEnumerations();
        RigsEdgeCases();
    }

    private static void RigsPinCurrentEnumerations()
    {
        // Audio data file: every stored rig, in storage order.
        var project = RigsProject(withLinks: false);
        Eq("Song rigs: the audio data file lists tracks, stored buses (dictionary order), master, monitor",
            "Lead, Bass, Pad, Basses bus, Guitars bus, Drums bus, Keys bus, Master, Monitor",
            string.Join(", ", LegacyDataRigs(project).Select(r => r.Owner)));
        Eq("Song rigs: the largest saved states come from every rig kind, inactive and monitor included",
            "P-Monitor on Monitor (8 MB), P-Master on Master (7 MB), P-BassesBus on Basses bus (6 MB), P-DrumsBus on Drums bus (5 MB), P-Lead on Lead (4 MB)",
            string.Join(", ", AudioDataFile.LargestPluginStates(project)));

        var linked = RigsProject(withLinks: true);
        var notices = new List<string>();
        AudioDataFile.RelinkRouting(linked, new Dictionary<string, string>(), new HashSet<string>(), notices);
        Check("Song rigs: relinking clears the links in every rig kind",
            LegacyDataRigs(linked).SelectMany(r => r.Rig.Plugins).All(p => p.SidechainTrackId == "" && p.MidiOutTrackId is null or "" && p.MidiIn.Source == PluginMidiIn.None),
            string.Join("; ", LegacyDataRigs(linked).SelectMany(r => r.Rig.Plugins).Select(p => $"{p.Name}:{p.SidechainTrackId}/{p.MidiIn.Source}")));
        Eq("Song rigs: the relink notice names the owners in storage order",
            "16 routing links could not be matched to a track and were cleared (Lead: sidechain, Lead: MIDI input, Bass: sidechain, Bass: MIDI input)",
            notices.FirstOrDefault(n => n.Contains("could not be matched")) ?? "");

        // Trust: every rig of the song, switched on or not, the monitor chain included (the tracks and the buses and master the engine runs, plus the rest).
        var settings = new PluginSettings();
        Eq("Song rigs: the trust check covers every rig: tracks, every bus (off, empty of tracks), master and the song's monitor chain",
            "Lead, Bass, Pad, BassesBus, GuitarsBus, DrumsBus, Master, Monitor",
            string.Join(", ", PluginTrust.UntrustedDetails(project, settings).Select(u => System.IO.Path.GetFileNameWithoutExtension(u.Path))));

        // Sync and save: tracks, running buses and master, then the monitor chain in effect.
        var withGlobal = RigsGlobalMonitor(true);
        Eq("Song rigs: sync/save with the app-wide monitor chain in effect", "Lead, Bass, Pad, Guitars bus, Master, Monitor", RigsNames(LegacySyncAndSave(project, withGlobal)));
        Check("Song rigs: the monitor in effect is the app-wide chain", ReferenceEquals(LegacySyncAndSave(project, withGlobal).Last().Rig, withGlobal.Rig));
        Eq("Song rigs: sync/save with an empty app-wide monitor chain has no monitor", "Lead, Bass, Pad, Guitars bus, Master", RigsNames(LegacySyncAndSave(project, RigsGlobalMonitor(false))));
        project.Mixer.MonitorUseGlobal = false;
        var own = LegacySyncAndSave(project, RigsGlobalMonitor(false));
        Eq("Song rigs: sync/save with the song's own monitor chain in effect", "Lead, Bass, Pad, Guitars bus, Master, Monitor", RigsNames(own));
        Check("Song rigs: the monitor in effect is the song's own chain", ReferenceEquals(own.Last().Rig, project.Mixer.MonitorFx.Rig));
        project.Mixer.MonitorFx.Rig.Plugins.Clear();
        Eq("Song rigs: sync/save with an empty own monitor chain has no monitor", "Lead, Bass, Pad, Guitars bus, Master", RigsNames(LegacySyncAndSave(project, withGlobal)));
    }

    /// <summary>The enumerator gives the same sets, in the same order, as the four enumerations it replaced.</summary>
    private static void RigsMatchTheEnumerations()
    {
        var project = RigsProject(withLinks: false);
        var legacy = LegacyDataRigs(project);
        var all = SongRigs.All(project).ToList();
        Check("Song rigs: All lists the same owners and rigs, in the same order, as the audio data file's old enumeration",
            all.Select(r => r.Owner).SequenceEqual(legacy.Select(l => l.Owner)) && all.Select(r => r.Rig).SequenceEqual(legacy.Select(l => l.Rig), ReferenceEqualityComparer.Instance),
            string.Join(", ", all.Select(r => r.Owner)));
        var trustOld = LegacyTrustRigs(project);
        Check("Song rigs: every rig the trust check used to see is still in All, in the same relative order",
            trustOld.SequenceEqual(all.Select(r => r.Rig).Where(rig => trustOld.Contains(rig)), ReferenceEqualityComparer.Instance));
        Eq("Song rigs: the rigs the trust check now also sees are the inactive buses, the inactive master and the monitor chain",
            "BassesBus, DrumsBus, Monitor", string.Join(", ", all.Select(r => r.Rig).Where(rig => !trustOld.Contains(rig) && rig.Plugins.Count > 0).Select(rig => rig.Plugins[0].Name[2..])));
        Eq("Song rigs: Runs marks tracks, the running bus and master only (the song's own monitor chain is dormant while the app-wide one is used)",
            "Lead, Bass, Pad, Guitars bus, Master", string.Join(", ", all.Where(r => r.Runs).Select(r => r.Owner)));

        // Sync and save: the same stand-ins, in the same order, for each monitor situation, and with the buses in the grouping's order.
        foreach (var (useGlobal, appWide, ownPlugin) in new[] { (true, true, true), (true, false, true), (false, false, true), (false, true, false), (false, false, false) })
        {
            project.Mixer.MonitorUseGlobal = useGlobal;
            project.Mixer.MonitorFx.Rig.Plugins.Clear();
            if (ownPlugin) project.Mixer.MonitorFx.Rig.Plugins.Add(RigsPlugin("Monitor"));
            var globalChain = RigsGlobalMonitor(appWide);
            foreach (var bassesOn in new[] { false, true })
            {
                project.Mixer.Bus(MixerGroups.Basses).On = bassesOn;
                var expected = LegacySyncAndSave(project, globalChain);
                var actual = SongRigs.Running(project, globalChain).Select(r => r.AsTrack()).ToList();
                Check($"Song rigs: save collects the old set and order (song uses the app-wide chain: {useGlobal}, app-wide plug-in: {appWide}, own plug-in: {ownPlugin}, Basses bus on: {bassesOn})",
                    actual.SequenceEqual(expected, ReferenceEqualityComparer.Instance), $"{RigsNames(actual)} / {RigsNames(expected)}");
                var buses = SongRigs.Running(project, null, SongRigKind.Bus | SongRigKind.Master).Select(r => r.AsTrack()).ToList();
                Check($"Song rigs: the engine sync's buses and master are the old ones, in order (Basses bus on: {bassesOn})",
                    buses.SequenceEqual(MixerBuses.Active(project), ReferenceEqualityComparer.Instance), $"{RigsNames(buses)} / {RigsNames(MixerBuses.Active(project))}");
                var monitor = SongRigs.Running(project, globalChain, SongRigKind.Monitor).Select(r => r.AsTrack()).SingleOrDefault();
                Check("Song rigs: the engine sync's monitor is the old one", ReferenceEquals(monitor, MixerBuses.ActiveMonitor(project, globalChain)));
            }
        }
    }

    private static void RigsEdgeCases()
    {
        var project = RigsProject(withLinks: false);
        Check("Song rigs: the monitor in All is the song's own chain, flagged dormant while the song uses the app-wide one",
            SongRigs.All(project).Single(r => r.Kind == SongRigKind.Monitor) is { Runs: false } own && ReferenceEquals(own.Chain, project.Mixer.MonitorFx));
        var global = RigsGlobalMonitor(true);
        Check("Song rigs: with the app-wide chain given and used, All has that chain, running",
            SongRigs.All(project, global).Single(r => r.Kind == SongRigKind.Monitor) is { Runs: true } inEffect && ReferenceEquals(inEffect.Chain, global));

        project.Mixer.Buses[MixerGroups.Keys] = null!;
        project.Mixer.Buses["Custom"] = new BusChain { Rig = null! };
        project.Mixer.Buses["Stray"] = new BusChain { Rig = { Plugins = { RigsPlugin("Stray") } } };
        project.Tracks.Add(new TrackModel { Name = "NoRig", Rig = null! });
        var all = SongRigs.All(project).ToList();
        Eq("Song rigs: a missing bus, a bus without a rig and a track without a rig are skipped; a bus of an unknown group is listed but never runs",
            "Lead, Bass, Pad, Basses bus, Guitars bus, Drums bus, Stray bus, Master, Monitor", string.Join(", ", all.Select(r => r.Owner)));
        Check("Song rigs: the unknown group's bus does not run", !all.Single(r => r.Group == "Stray").Runs);
        project.Mixer.Buses.Clear();
        project.Mixer.Master = null!;
        project.Mixer.MonitorFx = null!;
        Eq("Song rigs: a mixer with no chains lists the tracks only", "Lead, Bass, Pad", string.Join(", ", SongRigs.All(project).Select(r => r.Owner)));
    }
}
