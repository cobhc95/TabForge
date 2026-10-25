using TabForge.Models;

namespace TabForge.Services;

// Owns: the one sentence the instrument picker shows when the chosen General MIDI sound is not what the track plays through (a VST instrument
//   plays it, or its GM sound is off).
// Does not own: the route rules (MixerGroups), the picker window that shows it.
// Tests: TabForge.Avalonia.Tests.InstrumentChoiceTests.
public static class InstrumentRouteNote
{
    /// <summary>The note for <paramref name="track"/>, or null when the instrument is what is heard.</summary>
    public static string? For(TrackModel track)
    {
        if (track.IsAudio) return null;
        var plugin = track.Rig.Plugins.FirstOrDefault(p => p.Plays && !p.Unavailable && p.Type == TabForge.Plugins.PluginSlotType.Instrument);
        if (MixerGroups.InstrumentPlays(track) && plugin is not null)
            return $"This track plays through {plugin.Name}; the GM instrument is used only when the plug-in is off.";
        return MixerGroups.IsSilentRoute(track) ? "This track's GM sound is off; the instrument is heard only when it is on." : null;
    }
}
