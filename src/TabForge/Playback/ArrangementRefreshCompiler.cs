using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: immutable-input compilation and splicing for a future arrangement refresh.
// Does not own: transport reservation, publication, output, or live project mutation.
// Tests: TestLiveEditLoop, TestLongAudioClipGrowthPlayback.
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
        compileOptions.CountIn = false;
        var order = PlaybackOrder.Build(source, compileOptions);
        var activeTimelineIndex = current.Bars.FindIndex(bar => Math.Abs(bar.StartMs - activeBar.StartMs) < 0.001);
        var occurrence = activeTimelineIndex < 0 ? 0 : current.Bars
            .Take(activeTimelineIndex + 1).Count(bar => bar.Bar == activeBar.Bar) - 1;
        var matchingPositions = order.Select((bar, index) => (bar, index))
            .Where(entry => entry.bar == currentBar).Select(entry => entry.index).ToArray();
        var removedActiveBar = currentBar < 0;
        var activeOrderIndex = removedActiveBar
            ? order.FindIndex(bar => bar >= Math.Max(0, continueAtBar ?? 0)) - 1
            : matchingPositions.Length > 0
                ? matchingPositions[Math.Clamp(occurrence, 0, matchingPositions.Length - 1)]
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

        revised.Events.AddRange(current.Events.Where(e => e.TimeMs < boundaryMs));
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
}
