using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: the track list's row geometry as static functions of the project: the notation and audio-lane heights, group headers, row
//   tops, and the row or group found at a y position.
// Does not own: drawing the rows (ArrangementPanel.TrackRows.cs) and the timeline's own geometry (TimelineGeometry.cs).
// Tests: TestArrangementGeometry.

public sealed partial class ArrangementPanel
{
    // ---------- row geometry: one place for every row position (rows can carry an audio lane) ----------
    public static bool HasAudioLane(TrackModel? track) => LaneCountOf(track) > 0;
    public static int LaneCountOf(TrackModel? track) => track is null ? 0 : ClipLanes.Count(track);
    /// <summary>Height of the notation (bar-cell) part of a row: none for an audio track that has lanes, which is lanes only.</summary>
    public static double NotationHeightOf(SongProject? project, TrackModel? track) =>
        track is { IsAudio: true } && LaneCountOf(track) > 0 ? 0 : RowHeightFor(project);
    public static double RowHeightOf(SongProject? project, TrackModel? track) => NotationHeightOf(project, track) + LaneCountOf(track) * AudioLaneHeight;

    /// <summary>Height of a group's header row in the track list (when groups are shown).</summary>
    public const double GroupHeaderHeight = 24;

    public static bool ShowsGroups(SongProject? project) => project?.Mixer.ShowGroupsInTrackList == true;

    /// <summary>Does a group header sit above this track (the first track of a run of one group)?</summary>
    public static bool StartsGroup(SongProject project, int index) =>
        ShowsGroups(project) && index >= 0 && index < project.Tracks.Count &&
        (index == 0 || MixerGroups.GroupOf(project, project.Tracks[index]) != MixerGroups.GroupOf(project, project.Tracks[index - 1]));

    /// <summary>Tracks of a collapsed group take no space (their group header stays).</summary>
    public static bool IsCollapsed(SongProject? project, int index) =>
        project is not null && ShowsGroups(project) && index >= 0 && index < project.Tracks.Count &&
        project.Mixer.CollapsedGroups.Contains(MixerGroups.GroupOf(project, project.Tracks[index]));

    public static double RowHeight(SongProject? project, int index) =>
        project is null || index < 0 || index >= project.Tracks.Count || IsCollapsed(project, index) ? 0 : RowHeightOf(project, project.Tracks[index]);

    /// <summary>The runs of consecutive tracks in one group: (group, first track, track count).</summary>
    public static List<(string Group, int Start, int Count)> GroupRuns(SongProject project)
    {
        var runs = new List<(string, int, int)>();
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var group = MixerGroups.GroupOf(project, project.Tracks[i]);
            if (runs.Count > 0 && runs[^1].Item1 == group) runs[^1] = (group, runs[^1].Item2, runs[^1].Item3 + 1);
            else runs.Add((group, i, 1));
        }
        return runs;
    }

    /// <summary>Top of a track's row, from the first row (group headers and collapsed groups included).</summary>
    public static double RowTopOf(SongProject? project, int index)
    {
        if (project is null) return index * DefaultTrackRowHeight;
        double y = 0;
        var groups = ShowsGroups(project);
        for (var i = 0; i <= index && i < project.Tracks.Count; i++)
        {
            if (groups && StartsGroup(project, i)) y += GroupHeaderHeight;
            if (i < index) y += RowHeight(project, i);
        }
        return y;
    }

    /// <summary>The track whose row contains <paramref name="y"/> (a group header counts as its first track), or -1.</summary>
    public static int RowIndexAt(SongProject? project, double y)
    {
        if (project is null || project.Tracks.Count == 0 || y < 0) return -1;
        double top = 0;
        var groups = ShowsGroups(project);
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            if (groups && StartsGroup(project, i))
            {
                top += GroupHeaderHeight;
                if (y < top) return i;
            }
            top += RowHeight(project, i);
            if (y < top && RowHeight(project, i) > 0) return i;
        }
        return -1;
    }

    /// <summary>The group header at <paramref name="y"/> (from the first row): the run's first track, or -1.</summary>
    public static int GroupHeaderAt(SongProject? project, double y)
    {
        if (project is null || !ShowsGroups(project)) return -1;
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            if (!StartsGroup(project, i)) continue;
            var top = RowTopOf(project, i) - GroupHeaderHeight;
            if (y >= top && y < top + GroupHeaderHeight) return i;
        }
        return -1;
    }

    public static double RowsHeight(SongProject? project) =>
        project is null || project.Tracks.Count == 0 ? RowHeightFor(project)
        : RowTopOf(project, project.Tracks.Count - 1) + RowHeight(project, project.Tracks.Count - 1);
}
