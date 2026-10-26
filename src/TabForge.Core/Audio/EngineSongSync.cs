using TabForge.Models;

namespace TabForge.Audio;

// Owns: bringing the engine's chains, clips and levels in line with one song (availability, auto GM sound, live input master, the sync),
//   the part of the routing every host and the render share.
// Does not own: the MIDI routes and the transport bars a host's playback adds (AvaloniaEngineRouting), the render (RenderJob).
// Tests: TabForge.Avalonia.Tests.RenderParityTests.
/// <summary>The engine sync of a song, without MIDI routes: what File > Render applies before it renders, in either app.</summary>
public static class EngineSongSync
{
    /// <param name="liveSolo">False (a render): the live input's solo does not gate the song; it is for monitoring only.</param>
    /// <param name="owner">The document that owns the engine now; null: the project itself.</param>
    public static void Apply(SongProject project, AudioEngineClient engine, Services.PluginSettings settings, int masterPercent, object? owner = null,
        Services.MediaContext? media = null, ICollection<string>? skippedPlugins = null, bool liveSolo = true)
    {
        engine.RefreshAvailability(project.Tracks.Append(engine.LiveInput.Track), settings, skippedPlugins);   // instruments the engine cannot play (quarantined, untrusted, missing, failed to load) count as not playing
        foreach (var t in project.Tracks) MixerGroups.ApplyAutoGm(t, engine.Mixer);   // GM sound follows whether a VST instrument plays
        engine.LiveInput.SetMaster(masterPercent, project.Mixer.MasterPan);
        engine.Sync(project.Tracks, settings, EngineMixLevels.Levels(project, masterPercent, liveSolo && engine.LiveInput.Solo), project, owner, media, skippedPlugins);
    }
}
