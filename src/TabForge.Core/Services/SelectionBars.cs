using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Services;

// Owns: which bars (and tracks) a bar command acts on now: the loop region when one is set (every track), else the score selection's bars, else the bar
//   span of the selected clips on those clips' tracks. Every former consumer of the old timeline range (copy/cut/delete/insert bars, paste into range,
//   export range, playback start, the bar menu label) asks this one rule.
// Does not own: the selection or the loop (SelectionModel), the edits themselves (TimelineRangeEdit, RangeCapture, RangePaste).
// Tests: no dedicated test class.

/// <summary>Bars <see cref="Start"/>…<see cref="End"/> on <see cref="Tracks"/> (null: every track); <see cref="FromLoop"/> when the loop region gave them.</summary>
public sealed record BarSpan(int Start, int End, IReadOnlyList<int>? Tracks, bool FromLoop)
{
    /// <summary>The first track the span covers (0 for every track).</summary>
    public int FirstTrack => Tracks is { Count: > 0 } t ? t[0] : 0;
}

/// <summary>The bars a bar command acts on.</summary>
public static class SelectionBars
{
    /// <summary>The loop region if set, else the white selection's bar span (score bars, or the selected clips' bars on their tracks); null when neither exists.</summary>
    public static BarSpan? For(SelectionModel selection, SongProject project)
    {
        // disabled 25 Oct: bar commands never use the loop, only the current selection (was: loop region first)
        if (selection.BarRange is { } score) return new BarSpan(score.Start, score.End, null, false);
        return ClipSpan(selection, project);
    }

    /// <summary>The bars the selected clips touch and their tracks (in track order), or null without clips.</summary>
    public static BarSpan? ClipSpan(SelectionModel selection, SongProject project)
    {
        if (selection.ClipCount == 0) return null;
        int first = int.MaxValue, last = -1;
        var tracks = new SortedSet<int>();
        foreach (var (track, clip) in selection.Clips)
        {
            var index = project.Tracks.IndexOf(track);
            if (index < 0) continue;
            tracks.Add(index);
            first = Math.Min(first, AudioClipTimelineRules.BarAtSeconds(project, clip.StartSec).Bar);
            var (endBar, fraction) = AudioClipTimelineRules.BarAtSeconds(project, clip.EndSec);
            last = Math.Max(last, fraction > 0 || endBar == 0 ? endBar : endBar - 1);   // a clip ending on a bar line does not touch the next bar
        }
        if (tracks.Count == 0) return null;
        last = Math.Max(first, last);
        return new BarSpan(first, last, tracks.ToList(), false);
    }
}
