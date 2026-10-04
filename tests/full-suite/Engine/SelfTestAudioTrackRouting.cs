using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Audio track routing (owner Q1): MIDI on an audio track is silent unless an enabled instrument plug-in is in its chain, the General MIDI synth
/// never starts for it, it takes part in mute/solo like any track, and it changes neither the channels of the other tracks nor the metronome device.
/// Synthetic songs only, no engine.
/// </summary>
public static partial class SelfTest
{
    private static TrackModel AudioWithInstrument(SongProject song)
    {
        var t = new TrackController().CreateTrack(song, TrackKind.Audio);
        t.SoundSource = SoundSources.Plugins;
        t.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = "", Type = PluginSlotType.Instrument, Format = "VST3" });
        return t;
    }

    private static void TestAudioTrackRouting()
    {
        var mixer = new MixerOptions { PlayAllThroughEngine = true };
        var song = new SongProject { Tempo = 120 };
        var tracks = new TrackController();
        var guitar = tracks.CreateTrack(song, TrackKind.Guitar);
        song.Tracks.Add(guitar);
        var audio = tracks.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(audio);
        audio.AudioClips.Add(new AudioClip { Name = "midi", SourceLengthSec = 2, FileLengthSec = 2, Notes = new List<ClipNote> { new(0, 0.5, 60, 100) } });
        guitar.MidiOutputDeviceId = 5; audio.MidiOutputDeviceId = 7;
        var groupsBefore = song.Mixer.Groups.Count;

        // Silent without an instrument plug-in, whatever the global setting; never the GM synth.
        foreach (var playAll in new[] { true, false })
        {
            mixer.PlayAllThroughEngine = playAll;
            Check($"audio routing: no instrument plug-in is Silent (play all {playAll})", MixerGroups.RouteOf(audio, mixer) == TrackRoute.Silent && MixerGroups.IsSilentRoute(audio));
            Check($"audio routing: no GM synth is started for it (play all {playAll})", !MixerGroups.MidiInEngine(audio, mixer) && !MixerGroups.GmSounds(audio, mixer));
        }
        audio.MidiSound = true;   // even a ticked GM sound never sounds on an audio track
        Check("audio routing: a ticked GM sound is ignored", MixerGroups.RouteOf(audio, mixer) == TrackRoute.Silent && !MixerGroups.GmSounds(audio, mixer));
        audio.Rig.Plugins.Add(new PluginSlot { Name = "Delay", Path = "", Type = PluginSlotType.Effect, Format = "VST3" });
        audio.SoundSource = SoundSources.Plugins;
        Check("audio routing: effects only stay Silent and start no GM synth", MixerGroups.RouteOf(audio, mixer) == TrackRoute.Silent && !MixerGroups.MidiInEngine(audio, mixer));
        Check("audio routing: a clip-only track still gets an engine slot for its audio (UsesEngineAudio unchanged)", MixerGroups.UsesEngineAudio(new TrackModel { Kind = TrackKind.Audio, RecordArm = true }));
        audio.Rig.Plugins.Clear(); audio.SoundSource = SoundSources.Midi; audio.MidiSound = false;
        var auto = new MixerOptions { AutoGmSound = true };
        Check("audio routing: auto GM never ticks GM on an audio track", !MixerGroups.ApplyAutoGm(audio, auto) && !audio.MidiSound);

        // Instrument plug-in: plays through it, GM still off.
        var synth = AudioWithInstrument(song);
        synth.MidiSound = true;
        Check("audio routing: an enabled instrument plug-in plays (Instrument route, in the engine)", MixerGroups.RouteOf(synth, mixer) == TrackRoute.Instrument && MixerGroups.MidiInEngine(synth, mixer) && !MixerGroups.IsSilentRoute(synth));
        Check("audio routing: with an instrument the GM synth is still not used", !MixerGroups.GmSounds(synth, mixer));
        synth.Rig.Plugins[0].Enabled = false;
        Check("audio routing: a bypassed instrument is Silent again", MixerGroups.RouteOf(synth, mixer) == TrackRoute.Silent);
        synth.Rig.Plugins[0].Enabled = true; synth.Rig.Plugins[0].Unavailable = true;
        Check("audio routing: an unavailable (quarantined) instrument is Silent", MixerGroups.RouteOf(synth, mixer) == TrackRoute.Silent);

        // Family and groups: Audio family, existing group dictionaries untouched.
        Check("audio routing: audio tracks are in the Audio family and group", MixerGroups.Family(audio) == MixerGroups.Audio && MixerGroups.GroupOf(song, audio) == MixerGroups.Audio);
        song.Mixer.Grouping = MixerGrouping.Compact;
        Check("audio routing: Compact grouping also keeps audio tracks in Audio", MixerGroups.GroupOf(song, audio) == MixerGroups.Audio && MixerGroups.GroupOf(song, guitar) == MixerGroups.Guitars);
        song.Mixer.Grouping = MixerGrouping.ByInstrument;
        Check("audio routing: reading the Audio group stores nothing and the group names are unchanged",
            song.Mixer.Groups.Count == groupsBefore && !MixerGroups.Names(MixerGrouping.ByInstrument).Contains(MixerGroups.Audio) && MixerGroups.AllNames(MixerGrouping.ByInstrument).Contains(MixerGroups.Audio));
        Check("audio routing: the Audio group has a bus slot", MixerBuses.SlotOf(MixerGroups.Audio) >= 0);

        // Mute / solo truth table with an audio track (any track, same rule).
        var bad = new List<string>();
        for (var bits = 0; bits < 16; bits++)
        {
            for (var i = 0; i < 2; i++) { song.Tracks[i].Mute = (bits & (1 << i)) != 0; song.Tracks[i].Solo = (bits & (1 << (i + 2))) != 0; }
            var anySolo = song.Tracks.Take(2).Any(t => t.Solo);
            var othersSolo = song.Tracks.Skip(2).Any(t => t.Solo);
            for (var i = 0; i < 2; i++)
            {
                var expected = anySolo ? song.Tracks[i].Solo : !song.Tracks[i].Mute;
                if (MixerGroups.IsAudible(song, song.Tracks[i]) != expected) bad.Add($"bits {bits} track {i}");
            }
            _ = othersSolo;
        }
        Check("audio routing: mute/solo truth table holds for a guitar and an audio track (" + (bad.Count == 0 ? "16 combinations" : string.Join(", ", bad)) + ")", bad.Count == 0);
        foreach (var t in song.Tracks) { t.Mute = false; t.Solo = false; }
        audio.Solo = true;
        Check("audio routing: soloed audio track silences the guitar, the mute fast-path mask agrees", !MixerGroups.IsAudible(song, guitar) && PlaybackEngine.AudibleMask(song) is { } mask && mask[1] && !mask[0]);
        audio.Solo = false;

        // Compiler: no notes, no channel, metronome device from a real instrument.
        song.Tracks.Remove(synth);
        guitar.Measures = TemplateFactory.Measures(1); audio.Measures = TemplateFactory.Measures(1);
        Beat(song, 0, 0, 0, 4, 64);
        var plain = new SongProject { Tempo = 120 };
        plain.Tracks.Add(CloneForCompile(guitar));
        var channelsPlain = ChannelAllocator.Assign(plain);
        var channels = ChannelAllocator.Assign(song);
        Check("audio routing: no channel for a silent audio track; the guitar's channel is unchanged", channels[1] == -1 && channels[0] == channelsPlain[0]);
        var timeline = new ScoreToMidiCompiler(song, new PlaybackOptions { Metronome = true }).Build();
        Check("audio routing: nothing is emitted for a silent audio track (clip notes included)", timeline.Events.All(e => e.TrackIndex != 1) && timeline.Events.Any(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0x90));
        Check("audio routing: the metronome device is never the audio track's", timeline.Events.Where(e => e.IsMetronome).All(e => e.DeviceId == 5) && timeline.Events.Any(e => e.IsMetronome));
        song.Tracks.Insert(0, audio); song.Tracks.RemoveAt(2);   // audio first: still the guitar's device
        var audioFirst = new ScoreToMidiCompiler(song, new PlaybackOptions { Metronome = true, RespectMuteSolo = false }).Build();
        Check("audio routing: audio track first in the list: metronome still follows a notation track", audioFirst.Events.Where(e => e.IsMetronome).All(e => e.DeviceId == 5));
        song.Tracks.RemoveAt(0); song.Tracks.Add(audio);

        // With an instrument plug-in its MIDI clips are emitted, on a channel no notated track uses.
        audio.SoundSource = SoundSources.Plugins;
        audio.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = "", Type = PluginSlotType.Instrument, Format = "VST3" });
        var withInst = ChannelAllocator.Assign(song);
        Check("audio routing: an instrument-played audio track gets a free channel and the guitar keeps its own", withInst[1] >= 0 && withInst[1] != withInst[0] && withInst[1] != ChannelAllocator.PercussionChannel && withInst[0] == channelsPlain[0]);
        var played = new ScoreToMidiCompiler(song, new PlaybackOptions()).Build();
        var clipOn = played.Events.Where(e => e.TrackIndex == 1 && (e.Status & 0xF0) == 0x90).ToList();
        Check("audio routing: with an instrument plug-in the MIDI clip's note plays (Q1)", clipOn.Count == 1 && clipOn[0].Data1 == 60 && clipOn[0].Channel == withInst[1]);
        var viaAll = new ScoreToMidiCompiler(song, new PlaybackOptions { RespectMuteSolo = false }).Build();
        audio.Rig.Plugins.Clear(); audio.SoundSource = SoundSources.Midi;
        var viaAllSilent = new ScoreToMidiCompiler(song, new PlaybackOptions { RespectMuteSolo = false }).Build();
        Check("audio routing: without it the clip stays silent even with mute/solo ignored", viaAll.Events.Any(e => e.TrackIndex == 1) && viaAllSilent.Events.All(e => e.TrackIndex != 1));

        // Auto chains never put an instrument chain on an audio track.
        var settings = new PluginSettings();
        settings.AutoChains.Add(new AutoChain { Key = AutoChains.KeyOf(audio), Plugins = { new PluginSlot { Name = "Synth", Type = PluginSlotType.Instrument, Format = "VST3" } } });
        Check("audio routing: auto chains skip audio tracks", !AutoChains.Apply(settings, audio) && audio.Rig.Plugins.Count == 0);
    }

    private static TrackModel CloneForCompile(TrackModel source)
    {
        var copy = new TrackModel { Name = source.Name, Kind = source.Kind, MidiChannel = source.MidiChannel, MidiOutputDeviceId = source.MidiOutputDeviceId, Measures = TemplateFactory.Measures(1) };
        return copy;
    }

    // Live MIDI input on an audio track (Q1): only through its enabled instrument plug-in (engine slot), never Windows MIDI; instrument tracks unchanged.
    private sealed class LiveCountingOutput : IMidiOutput
    {
        public int Sent;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices => Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) => Sent++;
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static void TestAudioTrackLiveMidi()
    {
        var song = new SongProject { Tempo = 120 };
        var tracks = new TrackController();
        var guitar = tracks.CreateTrack(song, TrackKind.Guitar);
        song.Tracks.Add(guitar);
        var audio = tracks.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(audio);
        foreach (var t in song.Tracks) { t.AudioInput = AudioInputs.Midi; t.RecordArm = true; t.MonitorInput = true; }

        var channels = ChannelAllocator.Assign(song);
        Check("audio live MIDI: an armed, monitoring audio track without an instrument plug-in plays nothing", channels[1] == -1 && !RecordingController.MonitorsLive(audio, channels[1]));
        Check("audio live MIDI: an armed, monitoring instrument track still plays live", RecordingController.MonitorsLive(guitar, channels[0]));

        audio.SoundSource = SoundSources.Plugins;
        audio.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = "", Type = PluginSlotType.Instrument, Format = "VST3" });
        channels = ChannelAllocator.Assign(song);
        Check("audio live MIDI: with an enabled instrument plug-in the armed audio track plays live", channels[1] >= 0 && RecordingController.MonitorsLive(audio, channels[1]));
        audio.MonitorInput = false;
        Check("audio live MIDI: monitoring off: nothing live", !RecordingController.MonitorsLive(audio, channels[1]));
        audio.MonitorInput = true; audio.RecordArm = false;
        Check("audio live MIDI: unarmed: nothing live", !RecordingController.MonitorsLive(audio, channels[1]));
        audio.RecordArm = true; audio.Rig.Plugins[0].Enabled = false;
        Check("audio live MIDI: a bypassed instrument plug-in: nothing live", !RecordingController.MonitorsLive(audio, ChannelAllocator.Assign(song)[1]));
        audio.Rig.Plugins[0].Enabled = true;

        // The routed output: the audio track's live note goes to its engine slot or nowhere; an instrument track still falls back to Windows MIDI.
        var inner = new LiveCountingOutput();
        var engine = AudioEngineClient.Instance;
        var routed = new RoutedMidiOutput(inner, engine);
        var routes = Enumerable.Repeat(-1, 16).ToArray();
        routes[channels[1]] = 5;
        routed.SetRoutes(routes);
        if (!engine.IsRunning)
        {
            var sentToEngine = routed.SendLiveEngineOnly(0x90 | channels[1], 60, 100) | routed.SendLiveEngineOnly(0x80 | channels[1], 60, 0);
            Check("audio live MIDI: engine not running: the audio track's note-on/off are dropped, never sent to Windows MIDI", !sentToEngine && inner.Sent == 0);
        }
        Check("audio live MIDI: an unrouted channel is dropped too", !routed.SendLiveEngineOnly(0x90 | channels[0], 60, 100) && inner.Sent == 0);
        routed.SendLive(0, 0x90 | channels[0], 60, 100); routed.SendLive(0, 0x80 | channels[0], 60, 0);
        Check("audio live MIDI: an instrument track's live notes still reach Windows MIDI", inner.Sent == 2);
    }
}
