using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

/// <summary>The clips that lie fully inside a section, each with where it starts in bars from the section start.</summary>
public sealed record SectionClipSet(int Start, List<SectionClipSet.Entry> Entries)
{
    /// <summary>One clip; <see cref="RelBar"/> is whole bars from the section start plus the fraction of the bar (bar and beat position, not seconds).</summary>
    public sealed record Entry(int TrackIndex, AudioClip Clip, double RelBar);

    public static SectionClipSet None => new(0, new List<Entry>());
    public bool IsEmpty => Entries.Count == 0;
}

// Owns: which clips travel with a section and where they land, measured in bars and beats so tempo changes are honoured.
// Does not own: the bar edits (SectionReorderService, BarRangeEditor), undo (DocumentEdits) and growing the song (SongExtent).
// Tests: TestSectionClips.
/// <summary>
/// Clips lying fully inside a section move or copy with it. A clip crossing a section edge stays where it is. The position is taken from the bar
/// and the fraction of the bar the clip starts in, and the new time is read from the bars of the song after the edit, so a different tempo at the new
/// place gives the clip the same bar and beat. Bar times are those of the first pass through each bar.
/// </summary>
public static class SectionClips
{
    private static Dictionary<int, ScoreBar> FirstPass(SongProject project)
    {
        var map = new Dictionary<int, ScoreBar>();
        foreach (var bar in MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true }).Bars)
            map.TryAdd(bar.Bar, bar);
        return map;
    }

    /// <summary>The clips of every track that lie fully inside bars [start, end). The entries hold the clips themselves; copy them with <see cref="Cloned"/>.</summary>
    public static SectionClipSet Capture(SongProject project, int start, int end)
    {
        var entries = new List<SectionClipSet.Entry>();
        if (end <= start || !project.Tracks.Any(t => t.AudioClips.Count > 0)) return new SectionClipSet(start, entries);
        var bars = FirstPass(project);
        if (!bars.TryGetValue(start, out var first) || !bars.TryGetValue(end - 1, out var last)) return new SectionClipSet(start, entries);
        for (var t = 0; t < project.Tracks.Count; t++)
            foreach (var clip in project.Tracks[t].AudioClips)
            {
                var (from, to) = (clip.StartSec * 1000, clip.EndSec * 1000);
                if (from < first.StartMs - 0.5 || to > last.EndMs + 0.5) continue;   // crossing the section's edge: it stays
                for (var bar = start; bar < end; bar++)
                {
                    if (!bars.TryGetValue(bar, out var b) || from >= b.EndMs) continue;
                    entries.Add(new SectionClipSet.Entry(t, clip, bar - start + (b.EndMs > b.StartMs ? b.SlotFraction(Math.Max(from, b.StartMs)) : 0)));
                    break;
                }
            }
        return new SectionClipSet(start, entries);
    }

    /// <summary>The same set with a copy of every clip (new identities).</summary>
    public static SectionClipSet Cloned(SectionClipSet set) =>
        new(set.Start, set.Entries.Select(e => e with { Clip = e.Clip.Clone() }).ToList());

    /// <summary>
    /// Puts the set's clips on the section that now starts at bar <paramref name="newStart"/>; with <paramref name="add"/> the clips are added to their
    /// tracks (copies), otherwise they are the clips already there (moves). Returns the latest end of the placed clips in seconds, or 0 when none.
    /// </summary>
    public static double Place(SongProject project, SectionClipSet set, int newStart, bool add)
    {
        if (set.IsEmpty) return 0;
        var bars = FirstPass(project);
        var end = 0.0;
        foreach (var entry in set.Entries)
        {
            var whole = (int)Math.Floor(entry.RelBar);
            if (!bars.TryGetValue(newStart + whole, out var bar) || entry.TrackIndex >= project.Tracks.Count) continue;
            entry.Clip.StartSec = bar.MsAtFraction(Math.Clamp(entry.RelBar - whole, 0, 1)) / 1000;
            if (add)
            {
                project.Tracks[entry.TrackIndex].AudioClips.Add(entry.Clip);
                ClipLanes.Ensure(project.Tracks[entry.TrackIndex], entry.Clip.Lane + 1);
            }
            end = Math.Max(end, entry.Clip.EndSec);
        }
        return end;
    }
}
