using TabForge.Models;
using TabForge.Platform.Theme;

namespace TabForge.Services;

// Owns: track colours by mixer group.
// Does not own: the colour settings storage and the track list drawing.
// Tests: TimelineClipScaleTests.
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
        [MixerGroups.Guitars] = NamedColourSwatches.HexOf("Red"),
        [MixerGroups.Basses] = NamedColourSwatches.HexOf("Yellow"),
        [MixerGroups.Drums] = NamedColourSwatches.HexOf("Blue"),
        [MixerGroups.Keys] = NamedColourSwatches.HexOf("Purple"),
        [MixerGroups.Other] = NamedColourSwatches.HexOf("Teal"),
    };

    /// <summary>The colour for a group: the user's choice, else the default, else teal.</summary>
    public static string ColourOf(string group, IReadOnlyDictionary<string, string>? chosen) =>
        chosen is not null && chosen.TryGetValue(group, out var hex) && SettingsColor.IsValid(hex) ? hex
        : Defaults.TryGetValue(group, out var fallback) ? fallback : Defaults[MixerGroups.Other];

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

    /// <summary>The default colour by instrument: drums blue, basses yellow, guitars red, everything else green.</summary>
    public static string InstrumentDefault(TrackModel track) => NamedColourSwatches.HexOf(MixerRules.FamilyOf(track) switch
    {
        MixerRules.Drums => "Blue", MixerRules.Bass => "Yellow", MixerRules.Guitar => "Red", _ => "Green"
    });

    /// <summary>A track still on its default colour takes the colour of its (new) instrument.</summary>
    public static void FollowInstrument(TrackModel track)
    {
        if (track.ColorFollowsInstrument && !track.IsAudio) track.ColorHex = InstrumentDefault(track);
    }

    public static bool SetColour(TrackModel track, string hex)
    {
        track.ColorFollowsInstrument = false;
        if (string.Equals(track.ColorHex, hex, StringComparison.OrdinalIgnoreCase)) return false;
        track.ColorHex = hex;
        return true;
    }
}
