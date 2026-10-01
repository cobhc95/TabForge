using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

/// <summary>
/// The built-in full demo song "Ashen Meridian" (CC0), written in code from docs/DEMO_SONG_PLAN.md: 10 tracks, 144 bars
/// (154 performed), every representable technique and structure feature. `TabForge.exe --write-demo-song &lt;out.gp&gt;` saves it.
/// Split into partial files by owner (plan 6.1): Skeleton (structure, tracks, mix), Drums, Rhythm (guitars L/R, bass, sub),
/// LeadKeys (lead, harmony, clean, pad, piano) and the shared Helpers.
/// </summary>
internal static partial class FullDemoSongFactory
{
    /// <summary>Mix-table changes that had to move to another beat in the last Create() (for the self-test log).</summary>
    internal static List<string> MixPlacementNotes { get; } = new();

    public static SongProject Create()
    {
        var song = BuildSkeleton();
        BuildDrums(song);
        BuildRhythm(song);
        BuildLeadAndKeys(song);
        Finalise(song);
        return song;
    }

    /// <summary>Plan 6.1 "Finalise": rests for unfilled cells, legato links, the mix table, tie origins, the drum map, validation.</summary>
    private static void Finalise(SongProject song)
    {
        NormaliseCells(song);
        foreach (var t in song.Tracks) SpreadKeys(t);
        foreach (var t in song.Tracks) LinkLegato(t);
        MixPlacementNotes.Clear();
        foreach (var (name, bar, slot, mix) in MixTable())
            if (AttachMix(Track(song, name), bar, slot, mix) is { } moved) MixPlacementNotes.Add(moved);
        foreach (var t in song.Tracks) GuitarProImporter.LinkTieOrigins(t);
        DrumMaps.Apply(Track(song, Drums), DrumMaps.Custom);
        song.IsDirty = false;
        ProjectValidator.Validate(song);
    }
}
