using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

// Owns: the settings a new song takes: its notation (the one the person set in Preferences, else tab and staff) and whether its track
//   list shows groups (Settings > Timeline & sections > Track list).
// Does not own: the settings file (AppSettingsStore), where the new tab goes (DocumentRoutingDecision), the song's creation (SongDocument).
// Tests: TabForge.Avalonia.Tests.MainWindowNewTabTests.
public static class NewSongDefaults
{
    /// <summary>The notation a new song starts with.</summary>
    public static NotationMode Notation(AppSettings settings) =>
        settings.NotationPreferenceSet ? NotationNames.Normalize(settings.Notation) : NotationMode.TabAndStaff;

    /// <summary>Gives a new song the notation and track-list grouping the settings choose.</summary>
    public static void Apply(SongDocument song, AppSettings settings)
    {
        song.Notation = Notation(settings);
        song.Project.Mixer.ShowGroupsInTrackList = settings.Timeline.ShowGroupsInNewSongs;
    }
}
