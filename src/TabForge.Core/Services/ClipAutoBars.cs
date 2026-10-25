using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

/// <summary>The bars the song grew by to hold a clip: how many trail the song, the bar count right after they were added (they only count while the song still has that many bars), and the tracks whose Fine the growth moved (null: none; only that Fine goes back).</summary>
public readonly record struct AutoAddedBars(int Count, int EndBars, Guid[]? FineTracks = null);

/// <summary>What <see cref="ClipAutoBars.Fit"/> did: bars added, bars removed, and whether the growth was refused because the song ends in a jump.</summary>
public readonly record struct AutoFitResult(int Added, int Removed, bool EndsInJump);

// Owns: growing the song with empty bars so a clip (or pasted material) fits, remembering which bars that added, and removing those again when nothing reaches them;
//   a Fine on the old last bar moves to the new last bar so the added bars play (and moves back with them).
// Does not own: the undo step (the caller's DocumentEdits.Run), appending bars (BarGrid), what makes a trailing bar needed (TrailingBars), measuring the song (SongExtent).
// Tests: ClipAutoBarsTests.
/// <summary>
/// Auto bars: a clip that ends after the song grows it by whole empty bars (<see cref="SongExtent.EnsureCovers"/>); a later edit that leaves the bars it added
/// empty and unreached gives them back. Bars the user made are never removed here. The record is kept in memory only (a reload makes them normal bars).
/// </summary>
public static class ClipAutoBars
{
    /// <summary>The notice for a song whose playing order ends before its last bar (Fine, D.S. al Fine, a coda jump): more bars would never play.</summary>
    public const string JumpNotice = "Song ends with a jump; add bars manually to extend it";

    /// <summary>Grows the song so every clip fits, then removes auto-added bars nothing reaches. Call inside the edit's undo step.</summary>
    public static AutoFitResult Fit(SongProject project, int headroomBars = 0)
    {
        var grown = GrowToClips(project, headroomBars);
        return grown with { Removed = Shrink(project) };
    }

    /// <summary>Appends the bars the clips need (plus <paramref name="headroomBars"/> when it grows) and records them as auto-added. Refused when the song ends in a jump.</summary>
    public static AutoFitResult GrowToClips(SongProject project, int headroomBars = 0)
    {
        var (songEnd, _) = SongExtent.Measure(project);
        if (SongExtent.ClipsEnd(project) <= songEnd + 1e-6) return default;
        if (EndsInJump(project)) return new AutoFitResult(0, 0, true);
        var before = BarRangeEditor.MaxMeasures(project);
        SongExtent.EnsureCoversClips(project, headroomBars);
        return Record(project, before, CarryFine(project, before));
    }

    /// <summary>Appends empty bars so the song has at least <paramref name="bars"/> (for pasted material) and records them as auto-added. Refused when the song ends in a jump.</summary>
    public static AutoFitResult GrowToBars(SongProject project, int bars)
    {
        var before = BarRangeEditor.MaxMeasures(project);
        if (project.Tracks.Count == 0 || bars <= before) return default;
        if (EndsInJump(project)) return new AutoFitResult(0, 0, true);
        var room = Math.Min(InputLimits.MaxMeasuresPerTrack, InputLimits.MaxTotalMeasures / project.Tracks.Count) - before;
        var add = Math.Min(bars - before, room);
        if (add <= 0) return default;
        BarGrid.AppendBars(project, add);
        project.MarkTimelineChanged();
        return Record(project, before, CarryFine(project, before));
    }

    /// <summary>Removes the auto-added bars at the end that hold nothing and that no clip reaches; returns how many went.</summary>
    public static int Shrink(SongProject project)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var auto = Current(project, count);
        if (auto == 0) return 0;
        // A Fine the growth carried to the last bar is not content of its own: it goes back with the bars (and stays put when none go); a Fine the user added stays.
        var moved = project.AutoBars.FineTracks?.Intersect(project.Tracks.Select(track => track.Id)).ToArray();   // a track deleted since is no longer one to restore
        var fine = moved is null ? [] : TakeFine(project, count - 1, moved);
        var remove = Math.Min(auto, count - TrailingBars.NeededBars(project));
        if (remove > 0) BarRangeEditor.Remove(project, count - remove, count - 1);
        PutFine(fine, count - Math.Max(0, remove) - 1);
        if (remove > 0) project.MarkTimelineChanged();
        if (remove <= 0) return 0;
        project.AutoBars = new AutoAddedBars(auto - remove, count - remove, moved);
        return remove;
    }

    /// <summary>
    /// A Fine on the song's old last bar that the performed order really ends at stops the D.S./D.C. pass there, so the bars just appended after it would never play: the Fine moves to the new
    /// last bar (the song still ends at its Fine, now after the added bars). Nothing moves when the added bars play anyway or the order ends elsewhere (a coda jump); other markers never move.
    /// Returns the tracks whose Fine moved.
    /// </summary>
    private static Guid[] CarryFine(SongProject project, int barsBefore)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var performed = SongExtent.Timing(project).Performed;
        if (count <= barsBefore || barsBefore <= 0 || performed.Length == 0 || performed[^1].Bar != barsBefore - 1) return [];
        var fine = TakeFine(project, barsBefore - 1);
        if (fine.Count == 0) return [];
        PutFine(fine, count - 1);
        project.MarkTimelineChanged();
        return fine.Select(track => track.Id).ToArray();
    }

    /// <summary>Removes the Fine from bar <paramref name="bar"/> of the <paramref name="only"/> tracks (null: every track); returns the tracks that had one.</summary>
    private static List<TrackModel> TakeFine(SongProject project, int bar, Guid[]? only = null)
    {
        var had = new List<TrackModel>();
        foreach (var track in project.Tracks)
        {
            if (bar < 0 || bar >= track.Measures.Count || (only is not null && !only.Contains(track.Id))) continue;
            var measure = track.Measures[bar];
            var tokens = Tokens(measure);
            if (tokens.RemoveAll(IsFine) == 0) continue;
            measure.Directions = string.Join(", ", tokens);
            had.Add(track);
        }
        return had;
    }

    private static void PutFine(List<TrackModel> tracks, int bar)
    {
        foreach (var track in tracks)
        {
            if (bar < 0 || bar >= track.Measures.Count) continue;
            var measure = track.Measures[bar];
            if (Tokens(measure).Any(IsFine)) continue;   // a bar holds one Fine
            measure.Directions = string.IsNullOrWhiteSpace(measure.Directions) ? "Fine" : measure.Directions + ", Fine";
        }
    }

    private static List<string> Tokens(MeasureModel measure) => (measure.Directions ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

    private static bool IsFine(string token) => PlaybackOrder.DirectionTokens(token) is [var name] && string.Equals(name, "Fine", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the performed order stops before the last written bar, so a bar appended after it would never play.</summary>
    public static bool EndsInJump(SongProject project)
    {
        var performed = SongExtent.Timing(project).Performed;
        return performed.Length > 0 && performed[^1].Bar < BarRangeEditor.MaxMeasures(project) - 1;
    }

    private static int Current(SongProject project, int count) => project.AutoBars.EndBars == count ? project.AutoBars.Count : 0;

    private static AutoFitResult Record(SongProject project, int barsBefore, Guid[] carried)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var added = count - barsBefore;
        if (added > 0) project.AutoBars = new AutoAddedBars(Current(project, barsBefore) + added, count, carried.Length > 0 ? carried : Current(project, barsBefore) > 0 ? project.AutoBars.FineTracks : null);
        return new AutoFitResult(Math.Max(0, added), 0, false);
    }
}
