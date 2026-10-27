using TabForge.Models;

namespace TabForge.Services;

// Owns: Delete on bars selected on the timeline: the two modes (leave the bars empty, or remove them and close the gap), what each does to the clips
//   (untouched, or split at the range edges with the later parts moved left; removing bars always splits), the refusal to remove a range that cuts a
//   repeat in two, and keeping the later bars' time signature when the song's own first bar goes.
// Does not own: the bar and clip surgery itself (BarRangeGaps), the question (BarRangePromptPresenter), the undo step and the selection (the caller).
// Tests: TimelineRangeDeleteTests.
/// <summary>What Delete does to the selected bars: <see cref="LeaveEmpty"/> keeps every bar in place (clips untouched), <see cref="Remove"/> closes the gap (clips split and moved).</summary>
public enum TimelineDeleteMode { LeaveEmpty, Remove }

/// <summary>The outcome of one timeline delete: whether the song changed, or why nothing was done.</summary>
public readonly record struct TimelineRangeResult(bool Changed, string? Refusal = null);

public static class TimelineRangeEdit
{
    /// <summary>
    /// Deletes bars first..last on every track (<paramref name="track"/> &lt; 0) or on one track. Pure model code: the caller wraps it in one undo step
    /// and discards the step when <see cref="TimelineRangeResult.Changed"/> is false. Tracks that hold only clips are kept.
    /// </summary>
    public static TimelineRangeResult Apply(SongProject project, int first, int last, TimelineDeleteMode mode, int track, bool fillRests)
    {
        if (mode == TimelineDeleteMode.LeaveEmpty) return new(BarRangeGaps.Clear(project, first, last, track, fillRests));
        if (RepeatRefusal(project, first, last) is { } refusal) return new(false, refusal);
        if (track < 0) KeepLaterMeter(project, first, last);
        return new(BarRangeGaps.Remove(project, first, last, track) is not null);
    }

    /// <summary>
    /// The one path for every bar-range edit (timeline menus, Delete flow and the score's Range.* hotkeys): Clear and Remove go through <see cref="Apply(SongProject,int,int,TimelineDeleteMode,int,bool)"/>,
    /// the inserts add an empty gap before or after the range. Same contract: the caller wraps it in one undo step.
    /// </summary>
    public static TimelineRangeResult Apply(SongProject project, int first, int last, BarRangeAction action, int track, bool fillRests) => action switch
    {
        BarRangeAction.Clear => Apply(project, first, last, TimelineDeleteMode.LeaveEmpty, track, fillRests),
        BarRangeAction.Remove => Apply(project, first, last, TimelineDeleteMode.Remove, track, fillRests),
        _ => new(BarRangeGaps.InsertGap(project, first, last, before: action == BarRangeAction.InsertBefore, track, fillRests) is not null),
    };

    /// <summary>The notice for removing bars first..last when that cuts a repeat region (|: inside the range and :| outside it, or the other way round); null when it does not.</summary>
    public static string? RepeatRefusal(SongProject project, int first, int last)
    {
        if (project.Tracks.Count == 0) return null;
        var measures = project.Tracks[0].Measures;   // the repeat marks are read from the first track, as playback does
        var open = 0;
        for (var bar = 0; bar < measures.Count; bar++)
        {
            if (measures[bar].RepeatEnd)
            {
                if (Within(open, first, last) != Within(bar, first, last))
                    return $"Bars {first + 1}-{last + 1} cut through the repeat from bar {open + 1} to bar {bar + 1}. Select the whole repeat, or leave the bars empty instead.";
                open = bar + 1;
            }
            if (measures[bar].RepeatStart) open = bar;
        }
        return null;
    }

    private static bool Within(int bar, int first, int last) => bar >= first && bar <= last;

    /// <summary>Removing the first bar changes the song's own time signature (bar 1's): the new bar 1 keeps its meter and the bars that relied on the old one keep theirs.</summary>
    private static void KeepLaterMeter(SongProject project, int first, int last)
    {
        if (first > 0 || last + 1 >= BarRangeEditor.MaxMeasures(project)) return;
        var next = BarSignatures.TimeAt(project, last + 1);
        foreach (var track in project.Tracks)
            for (var bar = last + 1; bar < track.Measures.Count; bar++)
            {
                track.Measures[bar].TimeSigNum ??= project.TimeSignatureNumerator;
                track.Measures[bar].TimeSigDenom ??= project.TimeSignatureDenominator;
            }
        project.TimeSignatureNumerator = next.Num;
        project.TimeSignatureDenominator = next.Den;
    }
}
