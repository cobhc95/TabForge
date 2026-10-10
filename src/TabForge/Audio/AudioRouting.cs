using TabForge.Audio.Contracts;
using TabForge.Models;

namespace TabForge.Audio;

/// <summary>The one place that brings the engine in line with a song and points its MIDI channels at the engine.</summary>
public static class AudioRouting
{
    /// <param name="owner">The document that owns the engine now (R-10); null: the project itself.</param>
    public static void Apply(SongProject project, RoutedMidiOutput? routing, AudioEngineClient engine, Services.PluginSettings settings, int masterPercent = 100, object? owner = null,
        Services.MediaContext? media = null, ICollection<string>? skippedPlugins = null)
    {
        engine.RefreshAvailability(project.Tracks, settings, skippedPlugins);   // instruments the engine cannot play (quarantined, untrusted, missing, failed to load) count as not playing
        foreach (var t in project.Tracks) MixerGroups.ApplyAutoGm(t, engine.Mixer);   // GM sound follows whether a VST instrument plays
        engine.Sync(project.Tracks, settings, Levels(project, masterPercent), project, owner, media, skippedPlugins);
        if (routing is null) return;
        var channels = Playback.ChannelAllocator.Assign(project);
        var routes = Enumerable.Repeat(-1, 16).ToArray();
        var effectChannels = Playback.ChannelAllocator.AssignEffect(project, channels);
        for (var i = 0; i < project.Tracks.Count && i < channels.Length; i++)
            if (MixerGroups.MidiInEngine(project.Tracks[i], engine.Mixer) && engine.SlotOf(project.Tracks[i]) is var slot && slot >= 0 && channels[i] is >= 0 and < 16)
            {
                routes[channels[i]] = slot; // only MIDI that plays through plug-ins goes to the engine
                if (effectChannels[i] is >= 0 and < 16) routes[effectChannels[i]] = slot;   // the track's bent notes (effect channel) follow it
            }
        routing.SetRoutes(routes, engine.IsRunning && project.Tracks.Any(t => engine.SlotOf(t) >= 0));
        // RT-04: with the song's bar map, plug-ins get the real meter, bar start and ppq position (arpeggiators, synced LFOs, gates).
        TransportBar[] bars;
        try { bars = SongClock.TransportBars(project); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException) { bars = Array.Empty<TransportBar>(); } // Not logged: layout probe: no bars are drawn
        engine.SetTransport(project.Tempo, false, bars, engine.OwnerIdOf(owner ?? project));
    }

    /// <summary>Per-track engine level and pan: the mute/solo rule and group levels, with the same master scaling as the CC7 the playback sends (PlaybackEngine.MasterScaled).</summary>
    internal static Func<TrackModel, (int Volume, int Pan)> Levels(SongProject project, int masterPercent) => t =>
    {
        var level = Math.Clamp((int)Math.Round(MixerGroups.Volume(project, t) * Math.Clamp(masterPercent, 0, 100) / 100.0), 0, 127);
        return (EngineLevel(MixerGroups.IsAudible(project, t), level), MixerGroups.Pan(project, t));
    };

    /// <summary>Mute/solo fast path: sends only the loaded tracks' levels to the running engine (no chain, clip or route sync).</summary>
    public static void ApplyLevelsNow(SongProject project, AudioEngineClient engine, int masterPercent) =>
        engine.SetTrackLevelsNow(project.Tracks, Levels(project, masterPercent));

    /// <summary>
    /// The level sent to the engine. Level 0 is the engine's mute gate (no MIDI volume message lifts it), so it is reserved for a track the
    /// mute/solo rule silences; an audible track with its fader at 0 sends 1 (about -84 dB) so a Mix Table volume change can still bring it in.
    /// </summary>
    internal static int EngineLevel(bool audible, int level) => audible ? Math.Max(1, level) : 0;
}
