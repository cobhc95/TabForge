using TabForge.Models;

namespace TabForge.Services;

/// <summary>What a moved section does with audio or MIDI clips that start before it or end after it (the stored values of EditingSettings.SectionMoveAudio).</summary>
public static class SectionAudioChoice
{
    public const string Ask = "Ask", Split = "Split", Leave = "Leave";

    /// <summary>The Preferences labels, in the order of <see cref="Values"/>.</summary>
    public static string[] Labels => new[] { "Ask each time", "Split and move with the section", "Leave the audio where it is" };
    public static string[] Values => new[] { Ask, Split, Leave };

    /// <summary>The stored value for any text: an unknown or missing value asks.</summary>
    public static string Normalize(string? stored) => stored is Split or Leave ? stored : Ask;

    public static string ToLabel(string? stored) => Labels[Array.IndexOf(Values, Normalize(stored))];

    public static string FromLabel(string label) { var i = Array.IndexOf(Labels, label); return i < 0 ? Ask : Values[i]; }
}

/// <summary>One section move's crossing clips: the section's bars [Start, End) and its span in song seconds (the first pass through those bars), and the clips (audio or MIDI) that overlap the span without lying fully inside it.</summary>
public sealed record SectionAudioCrossing(string SectionTitle, int Start, int End, double StartSec, double EndSec, IReadOnlyList<(int Track, AudioClip Clip)> Clips)
{
    /// <summary>The distinct names of the tracks that hold the crossing clips, in track order.</summary>
    public IReadOnlyList<string> TrackNames(SongProject project) =>
        Clips.Select(c => c.Track).Distinct().OrderBy(i => i).Select(i => project.Tracks[i].Name).ToList();
}

// Owns: finding the clips a section move would cut through (same span as the clips that travel, SectionClips) and splitting them at the section edges
//   so the inside parts travel with the section; one move = splits + bar move, rolled back when the move is refused.
// Does not own: the question to the user (SectionAudioPromptPresenter, the dialog), undo (DocumentEdits) or the carrying of the inside clips (SectionClips).
// Tests: SectionAudioMoveTests.
public static class SectionAudioCrossings
{
    private const double ToleranceMs = 0.5;   // the same tolerance SectionClips.Capture uses to call a clip inside

    /// <summary>The clips crossing the edges of section <paramref name="from"/> (ordinal after sorting the markers by bar), or null when none do. Clips fully inside travel with the section and are not listed.</summary>
    public static SectionAudioCrossing? Find(SongProject project, int from)
    {
        var sorted = SectionLayout.Sorted(project);
        if (from < 0 || from >= sorted.Count || !project.Tracks.Any(t => t.AudioClips.Count > 0)) return null;
        if (!SectionLayout.TryGetBounds(project, sorted[from], out var start, out var end)) return null;
        var bars = SectionClips.FirstPass(project);
        if (!bars.TryGetValue(start, out var first) || !bars.TryGetValue(end - 1, out var last)) return null;
        var clips = new List<(int, AudioClip)>();
        for (var t = 0; t < project.Tracks.Count; t++)
            foreach (var clip in project.Tracks[t].AudioClips)
            {
                var (a, b) = (clip.StartSec * 1000, clip.EndSec * 1000);
                if (a >= last.EndMs - ToleranceMs || b <= first.StartMs + ToleranceMs) continue;   // no overlap
                if (a >= first.StartMs - ToleranceMs && b <= last.EndMs + ToleranceMs) continue;   // fully inside: travels
                clips.Add((t, clip));
            }
        return clips.Count == 0 ? null : new SectionAudioCrossing(sorted[from].Title, start, end, first.StartMs / 1000, last.EndMs / 1000, clips);
    }

    /// <summary>True when moving section <paramref name="from"/> before ordinal <paramref name="insertBefore"/> changes the order (the same checks as the move itself, without locks of other sections).</summary>
    public static bool WouldMove(SongProject project, int from, int insertBefore)
    {
        var sorted = SectionLayout.Sorted(project);
        if (from < 0 || from >= sorted.Count || sorted[from].LockPosition || insertBefore < 0 || insertBefore > sorted.Count) return false;
        return (insertBefore > from ? insertBefore - 1 : insertBefore) != from;
    }

    /// <summary>
    /// Moves section <paramref name="from"/> like <see cref="SectionReorderService.MoveWithClips"/>; with <paramref name="splitCrossing"/> the clips crossing its
    /// edges are first cut at the start and end, so the inside parts travel and the outside parts stay. Without it crossing clips are left alone. When the move is
    /// refused (null) the cuts are undone. Returns the bar mapping.
    /// </summary>
    public static int[]? Move(SongProject project, int from, int insertBefore, bool splitCrossing)
    {
        if (!splitCrossing || Find(project, from) is not { } crossing) return SectionReorderService.MoveWithClips(project, from, insertBefore);
        var tracks = crossing.Clips.Select(c => c.Track).Distinct().ToList();
        var lists = tracks.ToDictionary(t => t, t => project.Tracks[t].AudioClips.ToList());
        var fields = crossing.Clips.ToDictionary(c => c.Clip, c => (c.Clip.SourceLengthSec, c.Clip.FadeOutSec));
        foreach (var (track, clip) in crossing.Clips) Cut(project.Tracks[track], clip, crossing);
        var map = SectionReorderService.MoveWithClips(project, from, insertBefore);
        if (map is not null) return map;
        foreach (var t in tracks) { project.Tracks[t].AudioClips.Clear(); project.Tracks[t].AudioClips.AddRange(lists[t]); }
        foreach (var (clip, (length, fade)) in fields) { clip.SourceLengthSec = length; clip.FadeOutSec = fade; }
        return null;
    }

    // Cuts at the section start (the second part is the inside), then at its end; a cut that would leave a sliver is skipped and that part stays crossing.
    private static void Cut(TrackModel track, AudioClip clip, SectionAudioCrossing crossing)
    {
        var piece = ClipSplitGlue.Split(track, clip, crossing.StartSec) ?? clip;
        ClipSplitGlue.Split(track, piece, crossing.EndSec);
    }
}
