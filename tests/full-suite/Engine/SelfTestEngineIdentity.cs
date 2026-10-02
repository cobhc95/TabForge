using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;

namespace TabForge;

/// <summary>
/// The engine knows a track by its id, not by the object that holds it (an undo builds new objects): the chain and its plug-in instances
/// stay loaded; a plug-in edit marks only the song that owns the slot; a state reply names its plug-in by id. Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    private static TrackModel EngineIdentityCopy(TrackModel source)
    {
        var copy = new TrackModel { Id = source.Id, Name = source.Name, SoundSource = source.SoundSource };
        foreach (var p in source.Rig.Plugins)
            copy.Rig.Plugins.Add(new PluginSlot { Id = p.Id, Name = p.Name, Path = p.Path, Format = p.Format, Type = p.Type, Enabled = p.Enabled, State = p.State });
        return copy;
    }

    private static void TestEngineSlotsFollowTrackIdentity()
    {
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;   // these cases describe the engine-off routing
        try
        {
            client.AttachFakeForTest();
            var settings = new Services.PluginSettings();
            var sent = new List<EngineCommand>();
            client.SentForTest = c => sent.Add(c);
            var song = TemplateFactory.Blank();
            var track = song.Tracks[0];
            track.SoundSource = SoundSources.Plugins;
            track.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
            track.Rig.Plugins.Add(new PluginSlot { Name = "Delay", Path = @"C:\NoSuch\Delay.vst3", Format = "VST3", Type = PluginSlotType.Effect });
            var owner = new object();
            client.Sync(song.Tracks, settings, null, song, owner);
            var slot = client.SlotOf(track);
            var loads = client.ChainLoadsSentForTest;

            // Undo: the same track, built again (new objects, same ids). The slot and its chain stay.
            var rebuilt = EngineIdentityCopy(track);
            sent.Clear();
            client.Sync(new[] { rebuilt }, settings, null, song, owner);
            Check("engine identity: a track rebuilt with the same id keeps its engine slot (the replaced object no longer has one)",
                slot >= 0 && client.SlotOf(rebuilt) == slot && client.SlotOf(track) == -1, $"slot {slot} -> {client.SlotOf(rebuilt)}, old object {client.SlotOf(track)}");
            Check("engine identity: no chain is loaded or removed for it (the plug-in instances and their state stay in the engine)",
                client.ChainLoadsSentForTest == loads && !sent.Contains(EngineCommand.RemoveTrack) && !sent.Contains(EngineCommand.LoadChain), $"loads {loads} -> {client.ChainLoadsSentForTest}, commands {string.Join(",", sent.Distinct())}");

            // Another song's track with the same id is another engine track.
            var other = new object();
            var twin = EngineIdentityCopy(rebuilt);
            client.Sync(new[] { twin }, settings, null, song, other);
            Check("engine identity: a track of another song with the same id gets its own slot", client.SlotOf(twin) >= 0 && client.SlotOf(twin) != slot, $"{client.SlotOf(twin)} vs {slot}");

            // A group bus rebuilt by an undo (a new chain object, so a new stand-in track) keeps its fixed slot and chain.
            var busSong = TemplateFactory.Blank();
            busSong.Tracks[0].SoundSource = SoundSources.Plugins;
            busSong.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
            var group = MixerGroups.GroupOf(busSong, busSong.Tracks[0]);
            var busPlugin = new PluginSlot { Name = "Bus", Path = @"C:\NoSuch\Bus.vst3", Format = "VST3", Type = PluginSlotType.Effect };
            busSong.Mixer.Bus(group).Rig.Plugins.Add(busPlugin);
            var busOwner = new object();
            client.Sync(busSong.Tracks, settings, null, busSong, busOwner);
            var busSlot = MixerBuses.SlotOf(group);
            var busLoads = client.ChainLoadsSentForTest;
            busSong.Mixer.Buses[group] = new BusChain { On = true, Rig = { Plugins = { new PluginSlot { Id = busPlugin.Id, Name = "Bus", Path = busPlugin.Path, Format = "VST3", Type = PluginSlotType.Effect } } } };
            var busTrackAfter = busSong.Tracks.Select(EngineIdentityCopy).ToList();
            sent.Clear();
            client.Sync(busTrackAfter, settings, null, busSong, busOwner);
            Check("engine identity: a group bus rebuilt with the same plug-ins keeps its slot and chain (nothing is loaded or removed)",
                busSlot >= 0 && client.ChainLoadsSentForTest == busLoads && !sent.Contains(EngineCommand.RemoveTrack) && !sent.Contains(EngineCommand.LoadChain),
                $"bus slot {busSlot}, loads {busLoads} -> {client.ChainLoadsSentForTest}, commands {string.Join(",", sent.Distinct())}");

            // A real change still reloads: the chain with a plug-in inserted is a different chain.
            var changed = EngineIdentityCopy(twin);
            changed.Rig.Plugins.Insert(0, new PluginSlot { Name = "Reverb", Path = @"C:\NoSuch\Reverb.vst3", Format = "VST3", Type = PluginSlotType.Effect });
            var before = client.ChainLoadsSentForTest;
            var twinSlot = client.SlotOf(twin);
            client.Sync(new[] { changed }, settings, null, song, other);
            Check("engine identity: inserting a plug-in loads the new chain once, in the same slot", client.ChainLoadsSentForTest == before + 1 && client.SlotOf(changed) == twinSlot,
                $"loads +{client.ChainLoadsSentForTest - before}, slot {twinSlot} -> {client.SlotOf(changed)}");
        }
        finally
        {
            client.SentForTest = null;
            client.Stop();
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll;
        }
    }

    private static void TestEnginePluginEditAndStatesByIdentity()
    {
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;   // these cases describe the engine-off routing
        try
        {
            client.AttachFakeForTest();
            var settings = new Services.PluginSettings();
            SongProject Song(string name)
            {
                var s = TemplateFactory.Blank();
                s.Tracks[0].SoundSource = SoundSources.Plugins;
                s.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
                s.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = name + "2", Path = $@"C:\NoSuch\{name}2.vst3", Format = "VST3", Type = PluginSlotType.Effect });
                return s;
            }
            var songA = Song("EditA"); var songB = Song("EditB");
            object ownerA = new(), ownerB = new();
            client.Sync(songA.Tracks, settings, null, songA, ownerA);
            client.Sync(songB.Tracks, settings, null, songB, ownerB);
            var edited = new List<object?>();
            client.PluginEdited += edited.Add;
            client.OnPluginEdited(client.SlotOf(songA.Tracks[0]));
            client.OnPluginEdited(client.SlotOf(songB.Tracks[0]));
            client.OnPluginEdited(-5);
            PumpUi();
            Check("engine identity: a plug-in edit names the song that owns its slot (and no song for an unknown slot)",
                edited.Count == 3 && ReferenceEquals(edited[0], ownerA) && ReferenceEquals(edited[1], ownerB) && edited[2] is null, string.Join(",", edited.Select(e => e is null ? "none" : ReferenceEquals(e, ownerA) ? "A" : "B")));
            Check("engine identity: the engine's current owner is the song that synced last", ReferenceEquals(client.CurrentOwner, ownerB));
            client.PluginEdited -= edited.Add;

            // States by plug-in id: the model gained a plug-in at the front while the states were read.
            var track = songB.Tracks[0];
            var request = client.BeginStateRequest(client.SlotOf(track));
            var ids = request.PluginIds;
            lock (request)
            {
                request.Expected = 2;
                request.Replies[0] = (PluginStateStatus.Captured, "c3RhdGUw");
                request.Replies[1] = (PluginStateStatus.Captured, "c3RhdGUx");
                request.Received = 2;
            }
            var first = track.Rig.Plugins[0]; var second = track.Rig.Plugins[1];
            track.Rig.Plugins.Insert(0, new PluginSlot { Name = "Late", State = "bGF0ZQ==" });
            var results = AudioEngineClient.ApplyStates(track, request);
            Check("engine identity: states are written to the plug-ins they came from, by id, when the chain changed meanwhile (a plug-in inserted at the front is left alone)",
                ids is { Length: 2 } && first.State == "c3RhdGUw" && second.State == "c3RhdGUx" && track.Rig.Plugins[0].State == "bGF0ZQ=="
                && results.Count == 3 && results[0].Outcome == PluginStateOutcome.Unchanged, $"{first.State}, {second.State}, {track.Rig.Plugins[0].State}");
        }
        finally { client.Stop(); TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll; }
    }
}
