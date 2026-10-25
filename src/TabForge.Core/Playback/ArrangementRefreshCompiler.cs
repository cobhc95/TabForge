using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: immutable-input compilation and splicing for a future arrangement refresh.
// Does not own: transport reservation, publication, output, or live project mutation.
// Tests: no dedicated test class.
internal static class ArrangementRefreshCompiler
{
    internal sealed record Plan(ScoreTimeline Timeline, int[] PerformedOrder, PlaybackOptions Options);

    internal static Plan CompileFuture(
        ScoreTimeline current, SongProject source, PlaybackOptions options, int[] baseToCurrentBar,
        ScoreBar activeBar, int? continueAtBar)
    {
        var currentBar = activeBar.Bar >= 0 && activeBar.Bar < baseToCurrentBar.Length
            ? baseToCurrentBar[activeBar.Bar]
            : activeBar.Bar;
        var compileOptions = options.Clone();
        compileOptions.StartBar = 0;
        compileOptions.StartCell = 0;
        compileOptions.StartOccurrence = null;   // a refresh recompiles the whole order from the first performance
        compileOptions.CountIn = false;
        var order = PlaybackOrder.Build(source, compileOptions);
        // The current timeline maps to a contiguous run of the full order (it may begin at a repeat pass or after a
        // jump), so the active bar's order index is that run's start plus the active bar's position in the run.
        int MapBar(int bar) => bar >= 0 && bar < baseToCurrentBar.Length ? baseToCurrentBar[bar] : bar;
        var timelineBars = current.Bars.Select(bar => MapBar(bar.Bar)).Where(bar => bar >= 0).ToList();
        var activeTimelineIndex = current.Bars.FindIndex(bar => Math.Abs(bar.StartMs - activeBar.StartMs) < 0.001);
        var activeTimelinePosition = -1;
        if (activeTimelineIndex >= 0)
            for (var i = 0; i <= activeTimelineIndex; i++)
                if (MapBar(current.Bars[i].Bar) >= 0) activeTimelinePosition++;
        var preferredStart = PlaybackOrder.StartIndex(order, MapBar(options.StartBar), options.StartOccurrence);
        // Only the bars already played identify the run: the future part of the live timeline may come from an older edit.
        var playedBars = activeTimelinePosition >= 0 ? timelineBars.Take(activeTimelinePosition + 1).ToList() : timelineBars;
        var timelineStart = PlaybackOrder.TimelineStartIndex(order, playedBars, preferredStart);
        var removedActiveBar = currentBar < 0;
        var mappedActiveIndex = timelineStart >= 0 && activeTimelinePosition >= 0 ? timelineStart + activeTimelinePosition : -1;
        var activeOrderIndex = removedActiveBar
            ? order.FindIndex(bar => bar >= Math.Max(0, continueAtBar ?? 0)) - 1
            : mappedActiveIndex >= 0 && mappedActiveIndex < order.Count && order[mappedActiveIndex] == currentBar
                ? mappedActiveIndex
                : order.FindIndex(bar => bar > currentBar) - 1;
        var hasContinuation = removedActiveBar
            ? order.Any(bar => bar >= Math.Max(0, continueAtBar ?? 0))
            : order.Any(bar => bar > currentBar);
        if (activeOrderIndex < 0 && order.Count > 0 && !hasContinuation) activeOrderIndex = order.Count - 1;
        var futureOrder = order.Skip(activeOrderIndex + 1).ToArray();

        compileOptions.Metronome = true;
        compileOptions.LiveMetronomeEvents = true;
        var future = futureOrder.Length == 0
            ? new ScoreTimeline()
            : MidiTimelineBuilder.Build(source, compileOptions, futureOrder);
        var revised = SpliceFuture(current, future, activeBar.EndMs, baseToCurrentBar);
        return new Plan(revised, order.ToArray(), compileOptions);
    }

    /// <summary>
    /// Which performance (1 = first) of its bar the live timeline's active bar is in the FULL performed order, counted
    /// from the song start; null when the timeline cannot be matched to the order (a restart then keeps the bar-only start).
    /// </summary>
    internal static int? ActiveOccurrence(ScoreTimeline timeline, SongProject source, PlaybackOptions options, double elapsedMs)
    {
        if (timeline.Bars.Count == 0) return null;
        var active = timeline.BarAt(elapsedMs);
        var activeIndex = timeline.Bars.FindIndex(bar => Math.Abs(bar.StartMs - active.StartMs) < 0.001);
        if (activeIndex < 0) return null;
        var compileOptions = options.Clone();
        compileOptions.StartBar = 0;
        compileOptions.StartCell = 0;
        compileOptions.StartOccurrence = null;
        compileOptions.CountIn = false;
        var order = PlaybackOrder.Build(source, compileOptions);
        var preferredStart = PlaybackOrder.StartIndex(order, options.StartBar, options.StartOccurrence);
        return ActiveOccurrence(order, timeline.Bars.Select(bar => bar.Bar).ToList(), activeIndex, preferredStart);
    }

    /// <summary>Pure core of <see cref="ActiveOccurrence(ScoreTimeline, SongProject, PlaybackOptions, double)"/>.</summary>
    internal static int? ActiveOccurrence(IReadOnlyList<int> order, IReadOnlyList<int> timelineBars, int activePosition, int preferredStart)
    {
        if (activePosition < 0 || activePosition >= timelineBars.Count) return null;
        var start = PlaybackOrder.TimelineStartIndex(order, timelineBars.Take(activePosition + 1).ToList(), preferredStart);
        var index = start + activePosition;
        if (start < 0 || index >= order.Count || order[index] != timelineBars[activePosition]) return null;
        var occurrence = 1;
        for (var i = 0; i < index; i++)
            if (order[i] == order[index]) occurrence++;
        return occurrence;
    }

    internal static ScoreTimeline CompileLoop(SongProject source, int[] baseToCurrentBar, Plan plan)
    {
        var whole = MidiTimelineBuilder.Build(source, plan.Options, plan.PerformedOrder);
        RemapBarsToBase(whole, baseToCurrentBar);
        return whole;
    }

    internal static void RemapBarsToBase(ScoreTimeline timeline, int[] baseToCurrentBar)
    {
        var inverseBars = new Dictionary<int, int>();
        for (var baseBar = 0; baseBar < baseToCurrentBar.Length; baseBar++)
            inverseBars.TryAdd(baseToCurrentBar[baseBar], baseBar);
        for (var i = 0; i < timeline.Bars.Count; i++)
            if (inverseBars.TryGetValue(timeline.Bars[i].Bar, out var baseBar)) timeline.Bars[i] = timeline.Bars[i] with { Bar = baseBar };
        foreach (var note in timeline.Notes)
            if (inverseBars.TryGetValue(note.Bar, out var baseBar)) note.Bar = baseBar;
    }

    private static ScoreTimeline SpliceFuture(ScoreTimeline current, ScoreTimeline future,
        double boundaryMs, int[] baseToCurrentBar)
    {
        var revised = new ScoreTimeline
        {
            CountInMs = current.CountInMs,
            PlayFromMs = current.PlayFromMs,
            TotalMs = boundaryMs + future.TotalMs,
            TieMerges = current.TieMerges + future.TieMerges,
            TieOrphans = current.TieOrphans + future.TieOrphans,
            LetRingExtensions = current.LetRingExtensions + future.LetRingExtensions,
            MaxLetRingExtensionMs = Math.Max(current.MaxLetRingExtensionMs, future.MaxLetRingExtensionMs)
        };
        revised.ChannelSetup.AddRange(current.ChannelSetup);
        revised.Bars.AddRange(current.Bars.Where(bar => bar.EndMs <= boundaryMs + 0.001));
        revised.Notes.AddRange(current.Notes.Where(note => note.OnsetMs < boundaryMs));

        var activeNotes = new Dictionary<(int Device, int Channel, int Pitch), int>();
        var activeMetronomePairs = new HashSet<int>();
        CountSoundingBefore(current, boundaryMs, activeNotes, activeMetronomePairs);

        revised.Events.AddRange(current.Events.Where(e => e.TimeMs < boundaryMs));
        var preservedFutureReleases = KeepReleasesPastBoundary(current, boundaryMs, revised, activeNotes, activeMetronomePairs);

        AppendFuture(revised, current, future, boundaryMs, baseToCurrentBar);
        revised.TotalMs = Math.Max(revised.TotalMs,
            preservedFutureReleases.Where(e => e.IsNoteOff).Select(e => e.TimeMs).DefaultIfEmpty(boundaryMs).Max());
        revised.LongestSoundingNoteMs = revised.Notes.Select(note => note.DurationMs).DefaultIfEmpty(0).Max();
        revised.LongestSoundingNoteAtBar = revised.Notes
            .Where(note => Math.Abs(note.DurationMs - revised.LongestSoundingNoteMs) < 0.001)
            .Select(note => (double)note.Bar).FirstOrDefault();
        SustainResolver.SortEvents(revised);
        revised.Notes.Sort((left, right) => left.OnsetMs.CompareTo(right.OnsetMs));
        return revised;
    }

    /// <summary>Counts the notes and metronome clicks that sound at the boundary (switched on before it, not yet off).</summary>
    private static void CountSoundingBefore(ScoreTimeline current, double boundaryMs, Dictionary<(int Device, int Channel, int Pitch), int> activeNotes, HashSet<int> activeMetronomePairs)
    {
        foreach (var e in current.Events.Where(e => e.TimeMs < boundaryMs))
        {
            if (e.IsMetronome)
            {
                if (e.IsNoteOn) activeMetronomePairs.Add(e.MetronomePairId);
                else if (e.IsNoteOff) activeMetronomePairs.Remove(e.MetronomePairId);
                continue;
            }
            if (e.IsNoteOn)
            {
                var key = (e.DeviceId, e.Status & 0x0F, e.Data1);
                activeNotes.TryGetValue(key, out var count);
                activeNotes[key] = count + 1;
            }
            else if (e.IsNoteOff)
            {
                var key = (e.DeviceId, e.Status & 0x0F, e.Data1);
                if (activeNotes.TryGetValue(key, out var count) && count > 1) activeNotes[key] = count - 1;
                else activeNotes.Remove(key);
            }
        }
    }

    /// <summary>The releases and resets after the boundary that end a note or click still sounding at it are kept in the revised timeline; they are returned.</summary>
    private static List<ScoreEvent> KeepReleasesPastBoundary(ScoreTimeline current, double boundaryMs, ScoreTimeline revised, Dictionary<(int Device, int Channel, int Pitch), int> activeNotes, HashSet<int> activeMetronomePairs)
    {
        var preservedFutureReleases = new List<ScoreEvent>();
        foreach (var e in current.Events.Where(e => e.TimeMs >= boundaryMs))
        {
            var keep = false;
            if (e.IsMetronome && e.IsNoteOff)
                keep = activeMetronomePairs.Remove(e.MetronomePairId);
            else if (e.IsNoteOff)
            {
                var key = (e.DeviceId, e.Status & 0x0F, e.Data1);
                if (activeNotes.TryGetValue(key, out var count) && count > 0)
                {
                    keep = true;
                    if (count > 1) activeNotes[key] = count - 1;
                    else activeNotes.Remove(key);
                }
            }
            else if (!e.IsMetronome && PlaybackEngine.IsEssentialReleaseOrReset(e)) keep = true;
            if (keep)
            {
                revised.Events.Add(e);
                preservedFutureReleases.Add(e);
            }
        }
        return preservedFutureReleases;
    }

    /// <summary>Appends the events, bars and notes of the future timeline, shifted by the boundary, with the bar numbers mapped back to the base.</summary>
    private static void AppendFuture(ScoreTimeline revised, ScoreTimeline current, ScoreTimeline future, double boundaryMs, int[] baseToCurrentBar)
    {
        var inverseBars = new Dictionary<int, int>();
        for (var baseBar = 0; baseBar < baseToCurrentBar.Length; baseBar++)
            inverseBars.TryAdd(baseToCurrentBar[baseBar], baseBar);
        var metronomePairOffset = current.Events.Where(e => e.IsMetronome)
            .Select(e => e.MetronomePairId).DefaultIfEmpty(0).Max();
        foreach (var e in future.Events)
        {
            if (e.IsSetup) continue;
            e.TimeMs += boundaryMs;
            if (e.IsMetronome) e.MetronomePairId += metronomePairOffset;
            revised.Events.Add(e);
        }
        foreach (var bar in future.Bars)
            revised.Bars.Add(bar with
            {
                Bar = inverseBars.TryGetValue(bar.Bar, out var baseBar) ? baseBar : bar.Bar,
                StartMs = bar.StartMs + boundaryMs,
                EndMs = bar.EndMs + boundaryMs
            });
        foreach (var note in future.Notes)
        {
            note.OnsetMs += boundaryMs;
            if (inverseBars.TryGetValue(note.Bar, out var baseBar)) note.Bar = baseBar;
            revised.Notes.Add(note);
        }
    }
}
