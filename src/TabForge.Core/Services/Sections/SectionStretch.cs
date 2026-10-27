using TabForge.Models;

namespace TabForge.Services;

/// <summary>What stretching a section past the next one does (the answers of the prompt and, but for Ask, the stored values of EditingSettings.SectionStretch).</summary>
public enum SectionStretchMode { AddEmpty, RepeatLast, AddRepeatSign, Stop }

public static class SectionStretchChoice
{
    public const string Ask = "Ask", AddEmpty = "AddEmpty", RepeatLast = "RepeatLast", RepeatSign = "RepeatSign", Stop = "Stop";

    /// <summary>The Preferences labels, in the order of <see cref="Values"/>.</summary>
    public static string[] Labels => new[] { "Ask each time", "Add empty bars", "Repeat last bars", "Add repeat sign", "Stop at the next section" };
    public static string[] Values => new[] { Ask, AddEmpty, RepeatLast, RepeatSign, Stop };

    /// <summary>The stored value for any text: an unknown or missing value asks.</summary>
    public static string Normalize(string? stored) => stored is AddEmpty or RepeatLast or RepeatSign or Stop ? stored : Ask;

    public static string ToLabel(string? stored) => Labels[Array.IndexOf(Values, Normalize(stored))];

    public static string FromLabel(string label) { var i = Array.IndexOf(Labels, label); return i < 0 ? Ask : Values[i]; }

    /// <summary>The answer without asking, when the setting remembers one; null when it asks.</summary>
    public static SectionStretchMode? Remembered(string? stored) => Normalize(stored) switch
    {
        AddEmpty => SectionStretchMode.AddEmpty,
        RepeatLast => SectionStretchMode.RepeatLast,
        RepeatSign => SectionStretchMode.AddRepeatSign,
        Stop => SectionStretchMode.Stop,
        _ => null,
    };

    /// <summary>The value Remember stores for an answer.</summary>
    public static string Store(SectionStretchMode mode) => mode switch
    {
        SectionStretchMode.AddEmpty => AddEmpty,
        SectionStretchMode.RepeatLast => RepeatLast,
        SectionStretchMode.AddRepeatSign => RepeatSign,
        _ => Stop,
    };
}

/// <summary>A section being stretched: its first bar, the bar its right edge can reach without bars being added (the next section's start, or the song end) and that next section (null for the last one).</summary>
public sealed record SectionStretchPlan(MarkerModel Section, MarkerModel? Next, int Start, int NextStart);

// Owns: the rule of "Stretch a section" (dragging a section's right edge past the next section): the bars the pointer asks for beyond the bump, the plan for a section, the repeat-sign conflict check and the
//   one edit for each choice (add empty bars / repeat the last bars / add a repeat sign / stop at the next section) as steps of ONE model edit; the new bars go in through the shared BarInsert, so every track gets
//   them at the same index and sections, markers, tempo, repeats and clips after the point move later.
// Does not own: the drag, the preview and the prompt (TimelineInputController.SectionStretch, TimelineView.SectionStretch, SectionStretchPromptWindow), the undo step (DocumentEdits) or the words (SectionStretchPromptPresenter).
// Tests: TabForge.Avalonia.Tests.SectionStretchTests.
/// <summary>Pure model code: no UI, no undo.</summary>
public static class SectionStretch
{
    /// <summary>The resistance (px) the edge holds at the next section's start before it keeps going.</summary>
    public const double BumpPx = 12;

    /// <summary>The section's plan, or null when it is unknown or the song has no bars.</summary>
    public static SectionStretchPlan? PlanFor(SongProject project, MarkerModel marker)
    {
        var sorted = SectionLayout.Sorted(project);
        var index = sorted.IndexOf(marker);
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (index < 0 || barCount <= 0) return null;
        var start = Math.Clamp(marker.MeasureIndex, 0, barCount);
        var next = index + 1 < sorted.Count ? sorted[index + 1] : null;
        return new SectionStretchPlan(marker, next, start, next is null ? barCount : Math.Clamp(next.MeasureIndex, start, barCount));
    }

    /// <summary>How many whole bars (snapped to the nearest bar line) the pointer asks for: it is <paramref name="pointerX"/> past <paramref name="edgeX"/> (the next section's start) plus the bump,
    /// each new bar <paramref name="barWidth"/> wide; never more than <paramref name="room"/> and 0 inside the bump.</summary>
    public static int BarsFor(double pointerX, double edgeX, double barWidth, int room) =>
        barWidth <= 0 || room < 1 || double.IsNaN(pointerX) ? 0 : (int)Math.Clamp(Math.Floor((pointerX - edgeX - BumpPx) / barWidth + 0.5), 0, room);

    /// <summary>How many more bars the song can take.</summary>
    public static int Room(SongProject project) => Math.Max(0, InputLimits.MaxMeasuresPerTrack - BarRangeEditor.MaxMeasures(project));

    /// <summary>The bars a stretch by <paramref name="bars"/> copies or marks: the section's last min(bars, its length), as (first bar, count).</summary>
    public static (int First, int Count) TailOf(SectionStretchPlan plan, int bars)
    {
        var count = Math.Clamp(Math.Min(bars, plan.NextStart - plan.Start), 1, Math.Max(1, plan.NextStart));
        return (plan.NextStart - count, count);
    }

    /// <summary>Why a repeat sign cannot go round the section's last bars (they hold repeat signs or endings, or sit inside a repeat); null when it can.</summary>
    public static string? RepeatSignConflict(SongProject project, SectionStretchPlan plan, int bars)
    {
        var (first, count) = TailOf(plan, bars);
        var last = first + count - 1;
        var range = first == last ? $"Bar {first + 1}" : $"Bars {first + 1}–{last + 1}";
        var open = false;
        for (var bar = 0; bar <= last; bar++)
        {
            bool start = false, end = false, ending = false;
            foreach (var track in project.Tracks)
            {
                if (bar >= track.Measures.Count) continue;
                var m = track.Measures[bar];
                start |= m.RepeatStart;
                end |= m.RepeatEnd;
                ending |= m.AlternateEnding > 0 || m.AlternateEndingMask != 0;
            }
            if (bar >= first && (start || end || ending)) return $"{range} already {(first == last ? "has" : "have")} a repeat sign.";
            if (start) open = true;
            if (end) open = false;
        }
        return open ? $"{range} {(first == last ? "is" : "are")} inside an existing repeat." : null;
    }

    /// <summary>
    /// Applies the stretch of <paramref name="marker"/> by <paramref name="bars"/> as steps of one edit: the section's end first reaches the next section's start (closing any gap), then Stop leaves it there,
    /// AddEmpty inserts empty bars at that point on every track, RepeatLast inserts copies of the section's last min(bars, length) bars (cycling when the section is shorter), AddRepeatSign adds no bars and
    /// puts a repeat open and close (2 plays) round those last bars. <paramref name="map"/> is the old-to-new bar map when bars went in. False when nothing changed (or the repeat sign conflicts).
    /// Audio and MIDI clips are not copied by RepeatLast; the clips after the section move later with their bars.
    /// </summary>
    public static bool Apply(SongProject project, MarkerModel marker, SectionStretchMode mode, int bars, out int[]? map)
    {
        map = null;
        if (PlanFor(project, marker) is not { } plan) return false;
        var changed = SectionLayout.ResizeClamped(project, marker, true, plan.NextStart);
        if (mode == SectionStretchMode.Stop || bars < 1) return changed;
        var (first, count) = TailOf(plan, bars);
        switch (mode)
        {
            case SectionStretchMode.AddEmpty:
                map = BarInsert.Empty(project, plan.NextStart, bars, fillRests: false);
                break;
            case SectionStretchMode.RepeatLast:
                var source = BarRangeEditor.Capture(project, first, plan.NextStart - 1);
                var copies = source.Select(track => Enumerable.Range(0, bars).Select(k =>
                {
                    var copy = ProjectService.CloneMeasure(track[k % count]);
                    copy.SectionName = "";
                    copy.Anacrusis = false;
                    return copy;
                }).ToList()).ToList();
                map = BarInsert.Bars(project, plan.NextStart, copies, fillRests: false);
                break;
            case SectionStretchMode.AddRepeatSign:
                if (RepeatSignConflict(project, plan, bars) is not null) return changed;
                foreach (var track in project.Tracks)
                {
                    if (plan.NextStart > track.Measures.Count) continue;
                    track.Measures[first].RepeatStart = true;
                    var close = track.Measures[plan.NextStart - 1];
                    close.RepeatEnd = true;
                    close.RepeatCount = 2;
                }
                return true;
        }
        return map is not null || changed;
    }
}
