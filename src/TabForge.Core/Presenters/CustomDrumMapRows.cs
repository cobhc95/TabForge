using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the rows of the track's custom drum map window (one per GM percussion sound 27-87), the choices each cell offers
//   (TAB line 1-8, staff position -4..10, notehead) and the row label text.
// Does not own: the window (Avalonia TrackDrumMapWindow) or applying the map (DrumMaps.Apply).
// Tests: TabForge.Avalonia.Tests.TrackDrumMapWindowTests.
public static class CustomDrumMapRows
{
    public const int FirstTabLine = 1, LastTabLine = 8, LowestStaffStep = -4, HighestStaffStep = 10, LabelMaxLength = 4;
    public static readonly IReadOnlyList<string> Heads = new[] { "normal", "x", "circle", "diamond" };

    /// <summary>Copies of the track's current mapping, one row per sound.</summary>
    public static List<DrumMapEntry> FromTrack(TrackModel track) => Sounds().Select(m => DrumMaps.For(track, m).Clone()).ToList();

    /// <summary>Every row as the preset writes it (the window's "Reset from").</summary>
    public static List<DrumMapEntry> FromPreset(string preset) => Sounds().Select(m => DrumMaps.Default(preset, m)).ToList();

    public static string SoundLabel(int midi) => $"{midi} - {PercussionKeyMapRules.Names[midi - PercussionKeyMapRules.FirstNote]}";

    public static int ClampStaffStep(int step) => Math.Clamp(step, LowestStaffStep, HighestStaffStep);

    private static IEnumerable<int> Sounds() => Enumerable.Range(PercussionKeyMapRules.FirstNote, PercussionKeyMapRules.Names.Count);
}
