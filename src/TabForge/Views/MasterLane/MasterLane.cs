using System.Runtime.CompilerServices;
using TabForge.Models;

namespace TabForge.Views;

// Owns: the master track lane's numbers and rules: its label, default colour and opacity range, which songs show it, and the
//   one-row offset it puts above track 1 (read by the row geometry).
// Does not own: drawing it (TrackTimeline.MasterLane.cs), its track-list row and menu item (ArrangementPanel.MasterLane.cs).
// Tests: TestMasterLane.
/// <summary>The optional overview lane above track 1: one thin line per track, filled where that track plays. Display only.</summary>
internal static class MasterLane
{
    public const string Label = "Master";
    public const string MenuHeader = "Show master track";
    public const string CommandId = "View.MasterTrack";
    public const string SettingKey = "timeline.mastertrack";
    public const string DefaultColour = "#8B939F";
    public const int DefaultOpacityPercent = 25, MinOpacityPercent = 5, MaxOpacityPercent = 50;

    // View state per shown song (never saved in the song), the same way the panel keeps its row height per song.
    private static readonly ConditionalWeakTable<SongProject, object> ShownSongs = new();
    private static readonly object Mark = new(), CompactMark = new();

    public static bool IsShown(SongProject? project) => project is { Tracks.Count: > 0 } && ShownSongs.TryGetValue(project, out _);

    /// <summary>Compact mode: the lane is shown at twice its height and no track row takes space.</summary>
    public static bool IsCompact(SongProject? project) => project is { Tracks.Count: > 0 } && ShownSongs.TryGetValue(project, out var mark) && ReferenceEquals(mark, CompactMark);

    public static void SetShown(SongProject project, bool shown, bool compact = false)
    {
        if (shown || compact) ShownSongs.AddOrUpdate(project, compact ? CompactMark : Mark); else ShownSongs.Remove(project);
    }

    /// <summary>Rows the lane adds above track 1 (0, 1, or 2 in compact mode).</summary>
    public static int Rows(SongProject? project) => !IsShown(project) ? 0 : IsCompact(project) ? 2 : 1;

    /// <summary>Height the lane takes above track 1: one track row (two in compact mode), or 0 when hidden.</summary>
    public static double OffsetOf(SongProject? project) => Rows(project) * ArrangementPanel.RowHeightFor(project);
}
