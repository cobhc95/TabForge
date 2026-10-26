using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Transport;

// Owns: the engine sync of the Avalonia playback, built on the Core engine APIs: the plug-in availability,
//     the GM sound, the master level, the track levels, each MIDI channel's route to its engine slot and the transport tempo; the live level push
//     after a fader, pan, mute or solo change; and the audio and plug-in settings every sync reads (the master percent is the shared
//     AudioSettings.MasterVolume, which the transport's master knob writes and the settings file saves).
// Does not own: the sessions and their engines (AvaloniaPlaybackSessions), the settings file (MainWindowSettings), the bar map of the
//     meter (the song clock is not in the app, so the transport carries no bars yet).
// Tests: TabForge.Avalonia.Tests.AvaloniaEngineRoutingTests.
// Threads: UI thread only.
public sealed class AvaloniaEngineRouting
{
    /// <summary>The master percent before settings are attached: full level.</summary>
    public const int FullMasterPercent = 100;

    private AudioSettings? _audio;
    private PluginSettings? _plugins;

    /// <summary>Attaches the shared settings objects; every sync from now on reads them.</summary>
    public void Attach(AudioSettings audio, PluginSettings plugins)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
    }

    /// <summary>The master percent every sync and live level push uses (0-100).</summary>
    public int MasterPercent => _audio?.MasterVolume ?? FullMasterPercent;

    /// <summary>The plug-in settings the sync uses; empty defaults before settings are attached.</summary>
    public PluginSettings Plugins => _plugins ?? new PluginSettings();

    /// <summary>Stores the master percent (clamped to 0-100) in the shared audio settings, so the next sync and the saved file use it.</summary>
    public void SetMasterPercent(int percent)
    {
        if (_audio is null) throw new InvalidOperationException("Attach the audio settings before setting the master volume.");
        _audio.MasterVolume = Math.Clamp(percent, 0, 100);
    }

    /// <summary>Brings the engine in line with the document; the routes change only when a MIDI output is given.</summary>
    /// <param name="owner">The document that owns the engine now.</param>
    /// <param name="skippedPlugins">The owning document's plug-ins skipped for now ("Disable it" after a very slow load); null: none.</param>
    public static void Apply(SongProject project, RoutedMidiOutput? routing, AudioEngineClient engine, PluginSettings settings, int masterPercent, object owner,
        ICollection<string>? skippedPlugins = null)
    {
        ApplyMixerOptions(settings, engine.Mixer);
        RoutedMidiOutput.WindowsMidiLatencyMs = settings.WindowsMidiLatencyMs;
        engine.RefreshAvailability(project.Tracks.Append(engine.LiveInput.Track), settings, skippedPlugins);   // instruments the engine cannot play count as not playing
        foreach (var track in project.Tracks) MixerGroups.ApplyAutoGm(track, engine.Mixer);
        engine.LiveInput.SetMaster(masterPercent, project.Mixer.MasterPan);
        engine.Sync(project.Tracks, settings, EngineMixLevels.Levels(project, masterPercent, engine.LiveInput.Solo), project, owner, skippedPlugins: skippedPlugins);
        if (routing is null) return;
        var routes = RoutesFor(project, engine);
        routing.SetRoutes(routes, engine.IsRunning && project.Tracks.Any(t => engine.SlotOf(t) >= 0));
        engine.SetTransport(project.Tempo, false, PerformedTransportBars.From(project), engine.OwnerIdOf(owner));
    }

    /// <summary>Copies the sound options the sync copies (auto GM sound, play-all-through-engine) into the engine mixer, as the settings say
    /// for every song: with play-all on, tracks without plug-ins play on the engine's General MIDI synth, as in the app, not on Windows MIDI.</summary>
    public static void ApplyMixerOptions(PluginSettings settings, MixerOptions mixer)
    {
        mixer.AutoGmSound = settings.AutoGmSound;
        mixer.PlayAllThroughEngine = settings.PlayAllThroughEngine;
    }

    /// <summary>Sends only the loaded tracks' levels to the running engine (no chain, clip or route sync), for live fader, pan, mute and solo changes.</summary>
    public static void ApplyLevelsNow(SongProject project, AudioEngineClient engine, int masterPercent) =>
        engine.SetTrackLevelsNow(project.Tracks, EngineMixLevels.Levels(project, masterPercent, engine.LiveInput.Solo));

    // The engine slot of each MIDI channel: only MIDI that plays through plug-ins goes to the engine; a track's bent-note effect channel follows it.
    private static int[] RoutesFor(SongProject project, AudioEngineClient engine)
    {
        var routes = Enumerable.Repeat(-1, 16).ToArray();
        var channels = ChannelAllocator.Assign(project);
        var effectChannels = ChannelAllocator.AssignEffect(project, channels);
        for (var i = 0; i < project.Tracks.Count && i < channels.Length; i++)
        {
            var track = project.Tracks[i];
            var slot = engine.SlotOf(track);
            if (!MixerGroups.MidiInEngine(track, engine.Mixer) || slot < 0 || channels[i] is < 0 or >= 16) continue;
            routes[channels[i]] = slot;
            if (effectChannels[i] is >= 0 and < 16) routes[effectChannels[i]] = slot;
        }
        return routes;
    }
}
