namespace TabForge.Models;

/// <summary>
/// The one place that reorders tracks and groups. The song's track list order is the arrangement order; the Mixer shows the same
/// order (groups by first appearance, tracks in track order). Dropping, nudging or moving a group in either view calls these,
/// so both views always describe the same order and grouping.
/// </summary>
public static class TrackOrdering
{
    /// <summary>Groups in the order they first appear in the track list, each with its tracks in track order.</summary>
    public static List<(string Group, List<TrackModel> Tracks)> Layout(SongProject project)
    {
        var layout = new List<(string Group, List<TrackModel> Tracks)>();
        foreach (var track in project.Tracks)
        {
            var group = MixerGroups.GroupOf(project, track);
            var entry = layout.FindIndex(l => l.Group == group);
            if (entry < 0) layout.Add((group, new List<TrackModel> { track }));
            else layout[entry].Tracks.Add(track);
        }
        return layout;
    }

    /// <summary>Puts a track in a group: its own choice is cleared when the group is the one its instrument would give it.</summary>
    public static void AssignGroup(SongProject project, TrackModel track, string group)
    {
        track.MixerGroup = null;
        if (MixerGroups.GroupOf(project, track) != group) track.MixerGroup = group;
    }

    private static bool Apply(SongProject project, List<(string Group, List<TrackModel> Tracks)> layout, List<TrackModel> before)
    {
        var order = layout.SelectMany(l => l.Tracks).ToList();
        if (order.SequenceEqual(before)) return false;
        project.Tracks.Clear();
        project.Tracks.AddRange(order);
        return true;
    }

    /// <summary>
    /// Moves a track to <paramref name="index"/> within <paramref name="group"/> (other groups keep their place). The track
    /// joins the group when it was in another one. Returns true when the order or the group changed.
    /// </summary>
    public static bool MoveTrackToGroup(SongProject project, TrackModel track, string group, int index)
    {
        var before = project.Tracks.ToList();
        var oldChoice = track.MixerGroup;
        var layout = Layout(project);
        var from = layout.FindIndex(l => l.Tracks.Contains(track));
        var to = layout.FindIndex(l => l.Group == group);
        if (from < 0 || to < 0) return false;
        layout[from].Tracks.Remove(track);
        var target = layout[to].Tracks;
        target.Insert(Math.Clamp(index, 0, target.Count), track);
        if (from != to) AssignGroup(project, track, group);
        return Apply(project, layout, before) || oldChoice != track.MixerGroup;
    }

    /// <summary>Moves a whole group to position <paramref name="index"/> among the groups (0 = first).</summary>
    public static bool MoveGroup(SongProject project, string group, int index)
    {
        var before = project.Tracks.ToList();
        var layout = Layout(project);
        var from = layout.FindIndex(l => l.Group == group);
        if (from < 0) return false;
        var entry = layout[from];
        layout.RemoveAt(from);
        layout.Insert(Math.Clamp(index, 0, layout.Count), entry);
        return Apply(project, layout, before);
    }

    /// <summary>Track-list group drag: moves <paramref name="count"/> tracks starting at <paramref name="start"/> before track index <paramref name="before"/>.</summary>
    public static bool MoveRun(SongProject project, int start, int count, int before)
    {
        if (start < 0 || count <= 0 || start + count > project.Tracks.Count) return false;
        var moving = project.Tracks.GetRange(start, count);
        project.Tracks.RemoveRange(start, count);
        var insert = before > start ? before - count : before;
        project.Tracks.InsertRange(Math.Clamp(insert, 0, project.Tracks.Count), moving);
        return insert != start;
    }

    /// <summary>Moves a track one place up (-1) or down (+1) in the Mixer's order; past the end of its group it joins the neighbouring group.</summary>
    public static bool Nudge(SongProject project, TrackModel track, int direction)
    {
        var layout = Layout(project);
        var g = layout.FindIndex(l => l.Tracks.Contains(track));
        if (g < 0 || direction == 0) return false;
        var tracks = layout[g].Tracks;
        var i = tracks.IndexOf(track);
        if (direction < 0 && i > 0) return MoveTrackToGroup(project, track, layout[g].Group, i - 1);
        if (direction > 0 && i < tracks.Count - 1) return MoveTrackToGroup(project, track, layout[g].Group, i + 1);
        if (direction < 0 && g > 0) return MoveTrackToGroup(project, track, layout[g - 1].Group, layout[g - 1].Tracks.Count);
        if (direction > 0 && g < layout.Count - 1) return MoveTrackToGroup(project, track, layout[g + 1].Group, 0);
        return false;
    }

    /// <summary>Moves a whole group one place up (-1) or down (+1).</summary>
    public static bool NudgeGroup(SongProject project, string group, int direction)
    {
        var layout = Layout(project);
        var g = layout.FindIndex(l => l.Group == group);
        if (g < 0) return false;
        var target = g + Math.Sign(direction);
        return target >= 0 && target < layout.Count && MoveGroup(project, group, target);
    }
}
