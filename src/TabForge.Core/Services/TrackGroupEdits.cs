using TabForge.Models;

namespace TabForge.Services;

// Owns: the pure song edits on a group of track rows (the track list's multi-selection): set mute/solo/arm, move volume or pan by one delta or reset them, remove the tracks, move them as a block.
// Does not own: which rows are selected (TrackSelection), the undo step, the confirmation question, the pointer gestures.
// Tests: TrackGroupEditsTests.

/// <summary>The two-state flags of a track row.</summary>
public enum TrackFlag { Mute, Solo, Arm }

/// <summary>The two track row sliders; both are stored as MIDI 0-127 on the track.</summary>
public enum TrackLevel { Volume, Pan }

/// <summary>Edits that act on several track rows at once; every method returns whether the song changed.</summary>
public static class TrackGroupEdits
{
    public const int LevelMaximum = MidiLimits.Max;

    public static bool SetFlag(SongProject project, IEnumerable<int> rows, TrackFlag flag, bool on)
    {
        var changed = false;
        foreach (var row in rows)
        {
            var track = project.Tracks[row];
            if (Get(track, flag) == on) continue;
            Set(track, flag, on);
            changed = true;
        }
        return changed;
    }

    public static bool Get(TrackModel track, TrackFlag flag) => flag switch { TrackFlag.Mute => track.Mute, TrackFlag.Solo => track.Solo, _ => track.RecordArm };

    private static void Set(TrackModel track, TrackFlag flag, bool on)
    {
        if (flag == TrackFlag.Mute) track.Mute = on;
        else if (flag == TrackFlag.Solo) track.Solo = on;
        else track.RecordArm = on;
    }

    /// <summary>Moves the level of every row by <paramref name="delta"/>, each clamped to 0-127 on its own.</summary>
    public static bool ShiftLevel(SongProject project, IEnumerable<int> rows, TrackLevel level, int delta) =>
        SetLevels(project, rows, level, current => Math.Clamp(current + delta, 0, LevelMaximum));

    /// <summary>Sets the level of every row to <paramref name="value"/> (MIDI 0-127).</summary>
    public static bool ResetLevel(SongProject project, IEnumerable<int> rows, TrackLevel level, int value) =>
        SetLevels(project, rows, level, _ => value);

    private static bool SetLevels(SongProject project, IEnumerable<int> rows, TrackLevel level, Func<int, int> next)
    {
        var changed = false;
        foreach (var row in rows)
        {
            var track = project.Tracks[row];
            var current = level == TrackLevel.Volume ? track.Volume : track.Pan;
            var value = next(current);
            if (value == current) continue;
            if (level == TrackLevel.Volume) track.Volume = value; else track.Pan = value;
            changed = true;
        }
        return changed;
    }

    /// <summary>Removes the tracks at <paramref name="rows"/>; refused (false) when no track would be left.</summary>
    public static bool Remove(SongProject project, IReadOnlyCollection<int> rows)
    {
        var doomed = rows.Where(r => r >= 0 && r < project.Tracks.Count).Distinct().Select(r => project.Tracks[r]).ToHashSet();
        if (doomed.Count == 0 || doomed.Count >= project.Tracks.Count) return false;
        project.Tracks.RemoveAll(doomed.Contains);
        return true;
    }

    /// <summary>Moves the tracks at <paramref name="rows"/> together, keeping their order, so the track at <paramref name="pressed"/> ends at <paramref name="drop"/> (clamped to what fits).</summary>
    public static bool MoveBlock(SongProject project, IReadOnlyList<int> rows, int pressed, int drop)
    {
        var block = rows.Distinct().OrderBy(r => r).ToList();
        var before = block.Count(r => r < pressed);
        if (block.Count == 0 || !block.Contains(pressed)) return false;
        var moving = block.Select(r => project.Tracks[r]).ToList();
        var rest = project.Tracks.Where(t => !moving.Contains(t)).ToList();
        var start = Math.Clamp(drop - before, 0, rest.Count);
        var order = rest.Take(start).Concat(moving).Concat(rest.Skip(start)).ToList();
        if (order.SequenceEqual(project.Tracks)) return false;
        project.Tracks.Clear();
        project.Tracks.AddRange(order);
        return true;
    }
}
