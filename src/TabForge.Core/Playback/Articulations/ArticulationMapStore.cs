using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Playback;

// Owns: where one track's articulation map is kept: in its playing instrument plug-in slot, or on the track itself when the track has
//   no instrument slot (a built-in-sound track whose map is for another program).
// Does not own: the map form (ArticulationMap), the presets (ArticulationPresets) or the export choices (ArticulationExportOptions).
// Tests: MidiExportArticulationTests, MidiProcessingWindowTests.
/// <summary>Reads and writes a track's articulation map, with the track-level fallback for tracks without an instrument plug-in.</summary>
public static class ArticulationMapStore
{
    /// <summary>The map shown by the editor: the slot's map when it has rules, else the track's own saved map.</summary>
    public static ArticulationMap MapOf(TrackModel track, PluginSlot? slot)
    {
        if (slot?.ArticulationBindings is { Count: > 0 } slotBindings) return ArticulationMap.FromBindings(slotBindings);
        return TrackMapOf(track);
    }

    /// <summary>The map saved on the track itself (empty when there is none).</summary>
    public static ArticulationMap TrackMapOf(TrackModel track) => ArticulationMap.FromBindings(track.ArticulationBindings);

    /// <summary>Stores the map where the editor edits it: the slot when there is one, else the track. A slot edit clears the track fallback.</summary>
    public static void Store(TrackModel track, PluginSlot? slot, ArticulationMap map)
    {
        var bindings = map.ToBindings();
        if (slot is not null)
        {
            slot.ArticulationBindings = bindings;   // a new dictionary: the compile reading the old one is never disturbed
            track.ArticulationBindings = null;
            return;
        }
        track.ArticulationBindings = map.IsEmpty ? null : bindings;
    }
}
