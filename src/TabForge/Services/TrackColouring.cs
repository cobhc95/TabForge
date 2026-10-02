using TabForge.Models;

namespace TabForge.Services;

// Owns: track colours by mixer group.
// Does not own: the colour settings storage and the track list drawing.
// Tests: TestClips.
/// <summary>
/// Track colours by group: every track of a mixer group (guitars, basses, keys, drums, other...) gets that group's
/// colour. The group colours are the user's (Settings: Appearance), with sensible defaults; colouring a range of
/// tracks by hand is done by the caller with <see cref="SetColour"/>.
/// </summary>
public static class TrackColouring
{
    /// <summary>Defaults: guitars red, basses yellow, drums blue, keys purple, the rest teal (from the track palette).</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [MixerGroups.Guitars] = "#B8403A",
        [MixerGroups.Basses] = "#A89A2E",
        [MixerGroups.Drums] = "#3A6FB8",
        [MixerGroups.Keys] = "#7A4FB0",
        [MixerGroups.Other] = "#2E8A7E",
    };

    /// <summary>The colour for a group: the user's choice, else the default, else teal.</summary>
    public static string ColourOf(string group, IReadOnlyDictionary<string, string>? chosen) =>
        chosen is not null && chosen.TryGetValue(group, out var hex) && SettingsColor.IsValid(hex) ? hex
        : Defaults.TryGetValue(group, out var fallback) ? fallback : "#2E8A7E";

    /// <summary>Colours every track by its mixer group; returns how many tracks changed.</summary>
    public static int ByGroup(SongProject project, IReadOnlyDictionary<string, string>? chosen)
    {
        var changed = 0;
        foreach (var track in project.Tracks)
            changed += SetColour(track, ColourOf(MixerGroups.GroupOf(project, track), chosen)) ? 1 : 0;
        return changed;
    }

    /// <summary>Colours the tracks of one group; returns how many changed.</summary>
    public static int Group(SongProject project, string group, string hex) =>
        project.Tracks.Where(t => MixerGroups.GroupOf(project, t) == group).Count(t => SetColour(t, hex));

    public static bool SetColour(TrackModel track, string hex)
    {
        if (string.Equals(track.ColorHex, hex, StringComparison.OrdinalIgnoreCase)) return false;
        track.ColorHex = hex;
        return true;
    }
}
