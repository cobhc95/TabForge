using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

// Owns: which audio and MIDI clips lie under bars that are about to be deleted, and the sentence that tells the user.
// Does not own: the bar removal or the dialog.
// Tests: TestBarDeleteGuards.
internal static class ClipDeleteImpact
{
    /// <summary>The clips (per track) that overlap the song time of bars [Start, End] of any of <paramref name="barRanges"/> (inclusive); null when none do.</summary>
    public static IReadOnlyList<(string Track, int Clips)>? Find(SongProject project, IReadOnlyList<(int Start, int End)> barRanges)
    {
        if (barRanges.Count == 0 || !project.Tracks.Any(t => t.AudioClips.Count > 0)) return null;
        var bars = new Dictionary<int, ScoreBar>();
        foreach (var bar in MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true }).Bars)
            bars.TryAdd(bar.Bar, bar);
        var spans = new List<(double From, double To)>();
        foreach (var (start, end) in barRanges)
            if (bars.TryGetValue(start, out var first) && bars.TryGetValue(end, out var last)) spans.Add((first.StartMs / 1000.0, last.EndMs / 1000.0));
        var result = new List<(string, int)>();
        foreach (var track in project.Tracks)
        {
            var count = track.AudioClips.Count(c => spans.Any(s => c.Overlaps(s.From, s.To)));
            if (count > 0) result.Add((track.Name, count));
        }
        return result.Count == 0 ? null : result;
    }

    /// <summary>The sentence for a bar-range remove: clips inside go, clips across an edge are cut, later clips move earlier.</summary>
    public static string DescribeRange(IReadOnlyList<(string Track, int Clips)> impact)
    {
        var total = impact.Sum(i => i.Clips);
        var parts = string.Join(", ", impact.Select(i => $"{i.Clips} on {i.Track}"));
        return $"{total} clip{(total == 1 ? "" : "s")} ({parts}) lie under these bars: removing the bars deletes the clips inside them and cuts the clips that cross an edge; clips after them move earlier.";
    }

    /// <summary>The warning text for <see cref="Find"/>'s result.</summary>
    public static string Describe(IReadOnlyList<(string Track, int Clips)> impact)
    {
        var total = impact.Sum(i => i.Clips);
        var parts = string.Join(", ", impact.Select(i => $"{i.Clips} on {i.Track}"));
        return $"{total} clip{(total == 1 ? "" : "s")} ({parts}) overlap the bars being deleted. Clips keep their place in time and do not move with the bars, "
            + "so they will no longer line up with the music after the cut.";
    }
}
