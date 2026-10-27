using TabForge.Models;

namespace TabForge.Services;

/// <summary>What stretching a section's start back past the previous one does (the answers of the prompt and, but for Ask, the stored values of EditingSettings.SectionStretchBack).</summary>
public enum SectionStretchBackMode { AddEmpty, RepeatFirst, TakeFromPrevious, Stop }

public static class SectionStretchBackChoice
{
    public const string Ask = "Ask", AddEmpty = "AddEmpty", RepeatFirst = "RepeatFirst", TakePrevious = "TakePrevious", Stop = "Stop";

    /// <summary>The Preferences labels, in the order of <see cref="Values"/>.</summary>
    public static string[] Labels => new[] { "Ask each time", "Add empty bars", "Repeat first bars", "Take from previous", "Stop" };
    public static string[] Values => new[] { Ask, AddEmpty, RepeatFirst, TakePrevious, Stop };

    /// <summary>The stored value for any text: an unknown or missing value asks.</summary>
    public static string Normalize(string? stored) => stored is AddEmpty or RepeatFirst or TakePrevious or Stop ? stored : Ask;

    public static string ToLabel(string? stored) => Labels[Array.IndexOf(Values, Normalize(stored))];

    public static string FromLabel(string label) { var i = Array.IndexOf(Labels, label); return i < 0 ? Ask : Values[i]; }

    /// <summary>The answer without asking, when the setting remembers one; null when it asks.</summary>
    public static SectionStretchBackMode? Remembered(string? stored) => Normalize(stored) switch
    {
        AddEmpty => SectionStretchBackMode.AddEmpty,
        RepeatFirst => SectionStretchBackMode.RepeatFirst,
        TakePrevious => SectionStretchBackMode.TakeFromPrevious,
        Stop => SectionStretchBackMode.Stop,
        _ => null,
    };

    /// <summary>The value Remember stores for an answer.</summary>
    public static string Store(SectionStretchBackMode mode) => mode switch
    {
        SectionStretchBackMode.AddEmpty => AddEmpty,
        SectionStretchBackMode.RepeatFirst => RepeatFirst,
        SectionStretchBackMode.TakeFromPrevious => TakePrevious,
        _ => Stop,
    };
}

/// <summary>A section whose start is stretched back: its start and end, the boundary its left edge reaches without bars being added (the previous section's end, or bar 0), and that previous section (null for the first) with its start.</summary>
public sealed record SectionStretchBackPlan(MarkerModel Section, MarkerModel? Previous, int Start, int End, int Boundary, int PreviousStart)
{
    /// <summary>How many bars the previous section can give (it keeps one at least).</summary>
    public int Takeable => Previous is null ? 0 : Math.Max(0, Boundary - PreviousStart - 1);
}

// Owns: the rule of stretching a section BACK (dragging its left edge past the previous section): the plan and the one edit for each choice (add empty bars at its start / repeat its first bars as a lead-in /
//   take the previous section's last bars / stop) as steps of ONE model edit. New bars go in through the shared BarInsert at the section's start; the section keeps its start bar and grows, everything from
//   the old start moves later, the previous section is unchanged.
// Does not own: the drag, the preview and the prompt (TimelineInputController.SectionStretch, TimelineView.SectionStretch, SectionStretchPromptWindow), the forward stretch (SectionStretch) or the undo step.
// Tests: TabForge.Avalonia.Tests.SectionStretchTests.
public static class SectionStretchBack
{
    /// <summary>The section's plan, or null when it is unknown or the song has no bars.</summary>
    public static SectionStretchBackPlan? PlanFor(SongProject project, MarkerModel marker)
    {
        var sorted = SectionLayout.Sorted(project);
        var index = sorted.IndexOf(marker);
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (index < 0 || barCount <= 0) return null;
        var start = Math.Clamp(marker.MeasureIndex, 0, barCount);
        var previous = index > 0 ? sorted[index - 1] : null;
        return new SectionStretchBackPlan(marker, previous, start, SectionLayout.End(sorted, index, barCount), index > 0 ? SectionLayout.End(sorted, index - 1, barCount) : 0,
            previous is null ? 0 : Math.Clamp(previous.MeasureIndex, 0, barCount));
    }

    /// <summary>How many whole bars the pointer asks for: it is <paramref name="pointerX"/> before <paramref name="edgeX"/> (the boundary) by more than the bump. See SectionStretch.BarsFor.</summary>
    public static int BarsFor(double pointerX, double edgeX, double barWidth, int room) => SectionStretch.BarsFor(2 * edgeX - pointerX, edgeX, barWidth, room);

    /// <summary>
    /// Applies the backward stretch of <paramref name="marker"/> by <paramref name="bars"/> as steps of one edit: the start first reaches the boundary (closing any gap), then Stop leaves it there,
    /// AddEmpty inserts empty bars at its start on every track, RepeatFirst inserts copies of its first min(bars, length) bars there (cycling), TakeFromPrevious adds no bars and moves the start back over
    /// the previous section's last bars (clamped so that one keeps a bar). <paramref name="map"/> is the old-to-new bar map when bars went in. False when nothing changed.
    /// </summary>
    public static bool Apply(SongProject project, MarkerModel marker, SectionStretchBackMode mode, int bars, out int[]? map)
    {
        map = null;
        if (PlanFor(project, marker) is not { } first) return false;
        var changed = SectionLayout.ResizeClamped(project, marker, false, first.Boundary);
        if (mode == SectionStretchBackMode.Stop || bars < 1 || PlanFor(project, marker) is not { } plan) return changed;
        SectionLayout.Freeze(project);
        var length = plan.End - plan.Start;
        if (mode == SectionStretchBackMode.TakeFromPrevious)
        {
            var take = Math.Min(bars, plan.Takeable);
            if (take < 1) { SectionLayout.Normalize(project); return changed; }
            plan.Previous!.LengthBars = plan.Boundary - plan.PreviousStart - take;
            marker.MeasureIndex = plan.Start - take;
            marker.LengthBars = length + take;
            SectionLayout.Normalize(project);
            return true;
        }
        var kept = project.Markers.Select(m => (Marker: m, Index: m.MeasureIndex, Length: m.LengthBars)).ToList();
        if (mode == SectionStretchBackMode.AddEmpty) map = BarInsert.Empty(project, plan.Start, bars, fillRests: false);
        else
        {
            var count = Math.Max(1, length);
            var source = BarRangeEditor.Capture(project, plan.Start, plan.Start + count - 1);
            var copies = source.Select(track => Enumerable.Range(0, bars).Select(k =>
            {
                var copy = ProjectService.CloneMeasure(track[k % count]);
                copy.SectionName = "";
                copy.Anacrusis = false;
                return copy;
            }).ToList()).ToList();
            map = BarInsert.Bars(project, plan.Start, copies, fillRests: false);
        }
        if (map is null) { SectionLayout.Normalize(project); return changed; }
        // The new bars belong to this section: it keeps its start bar and grows; every other section keeps its length and moves with its bars.
        foreach (var (m, index, len) in kept)
        {
            var own = ReferenceEquals(m, marker);
            m.MeasureIndex = own ? plan.Start : index >= plan.Start ? index + bars : index;
            m.LengthBars = own ? length + bars : len;
        }
        SectionLayout.Normalize(project);
        return true;
    }
}
