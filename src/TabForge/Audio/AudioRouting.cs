using TabForge.Audio.Contracts;
using TabForge.Models;

namespace TabForge.Audio;

/// <summary>The one place that brings the engine in line with a song and points its MIDI channels at the engine.</summary>
public static class AudioRouting
{
    /// <param name="owner">The document that owns the engine now (R-10); null: the project itself.</param>
    public static void Apply(SongProject project, RoutedMidiOutput? routing, AudioEngineClient engine, Services.PluginSettings settings, int masterPercent = 100, object? owner = null)
    {
        foreach (var t in project.Tracks) MixerGroups.ApplyAutoGm(t);   // GM sound follows whether a VST instrument plays
        var anySolo = project.Tracks.Any(t => t.Solo);
        engine.Sync(project.Tracks, settings, t =>
        {
            var silent = t.Mute || (anySolo && !t.Solo) || MixerGroups.GroupSilences(project, t);
            // Same master scaling as the CC7 the playback sends (PlaybackEngine.MasterScaled), so both agree on the level.
            var level = Math.Clamp((int)Math.Round(MixerGroups.Volume(project, t) * Math.Clamp(masterPercent, 0, 100) / 100.0), 0, 127);
            return (silent ? 0 : level, MixerGroups.Pan(project, t));
        }, project, owner);
        if (routing is null) return;
        var channels = Playback.ChannelAllocator.Assign(project);
        var routes = Enumerable.Repeat(-1, 16).ToArray();
        var effectChannels = Playback.ChannelAllocator.AssignEffect(project, channels);
        for (var i = 0; i < project.Tracks.Count && i < channels.Length; i++)
            if (MixerGroups.MidiInEngine(project.Tracks[i]) && engine.SlotOf(project.Tracks[i]) is var slot && slot >= 0 && channels[i] is >= 0 and < 16)
            {
                routes[channels[i]] = slot; // only MIDI that plays through plug-ins goes to the engine
                if (effectChannels[i] is >= 0 and < 16) routes[effectChannels[i]] = slot;   // the track's bent notes (effect channel) follow it
            }
        routing.SetRoutes(routes, engine.IsRunning && project.Tracks.Any(t => engine.SlotOf(t) >= 0));
        // RT-04: with the song's bar map, plug-ins get the real meter, bar start and ppq position (arpeggiators, synced LFOs, gates).
        TransportBar[] bars;
        try { bars = SongClock.TransportBars(project); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException) { bars = Array.Empty<TransportBar>(); }
        engine.SetTransport(project.Tempo, false, bars);
    }
}
