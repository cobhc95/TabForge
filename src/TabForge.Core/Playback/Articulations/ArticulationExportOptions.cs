using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Playback;

// Owns: what a MIDI file export does with articulation maps: the per-track map (or preset) override for one export only, whether techniques
//   without a rule keep their own sound or are left out, and the counts of the techniques each choice met. Any track can carry an override,
//   including a built-in-sound track whose map is for another program.
// Does not own: the map and its rules (ArticulationMap, ArticulationRule), the presets (ArticulationPresets) or the export file (MidiExportService).
// Tests: MidiTechniqueExportTests.
/// <summary>The articulation choices of one MIDI export. Null everywhere else, so playback and the default export are unchanged.</summary>
public sealed class ArticulationExportOptions
{
    /// <summary>A map picked in the export dialog for a track index; it replaces the track's slot map for this export and never edits the track.</summary>
    public Dictionary<int, ArticulationMap> Overrides { get; } = new();

    /// <summary>True: a technique without a rule emits nothing of its own. False (the default): it plays as before.</summary>
    public bool SkipUnmapped { get; set; }

    /// <summary>False: the map is not applied at all (the As-played choice with technique markers ticked); the file is the plain export plus markers.</summary>
    public bool MapsEnabled { get; set; } = true;

    /// <summary>True: every note that carries techniques also gets a small TF1 marker meta event so TabForge can read them back on import.</summary>
    public bool KeepTechniques { get; set; }

    /// <summary>Techniques that a map rule covered (occurrences, not distinct ids).</summary>
    public int Mapped { get; internal set; }
    /// <summary>Technique occurrences without a rule that kept their own sound.</summary>
    public int Kept { get; internal set; }
    /// <summary>Technique occurrences without a rule that were left out.</summary>
    public int Skipped { get; internal set; }

    /// <summary>
    /// The map attached to the track's instrument plug-in slot (or, for a track that now has a slot, the map saved on the track while it had
    /// none); null when no instrument slot plays the track. A built-in-sound track's own map is never a default: the export must pick it.
    /// </summary>
    public static ArticulationMap? DefaultMapOf(TrackModel track)
    {
        var slot = InstrumentSlotOf(track);
        if (slot is null) return null;
        if (slot.ArticulationBindings is { Count: > 0 } bindings)
        {
            var map = ArticulationMap.FromBindings(bindings);
            return map.IsEmpty ? null : map;
        }
        var fallback = ArticulationMapStore.TrackMapOf(track);
        return fallback.IsEmpty ? null : fallback;
    }

    /// <summary>The instrument plug-in slot that plays the track, or null for a non-plug-in / audio track.</summary>
    public static PluginSlot? InstrumentSlotOf(TrackModel track)
    {
        if (track.IsAudio || track.SoundSource != SoundSources.Plugins) return null;
        return track.Rig.Plugins.FirstOrDefault(p => p.Plays && !p.Unavailable && p.Type == PluginSlotType.Instrument);
    }
}
