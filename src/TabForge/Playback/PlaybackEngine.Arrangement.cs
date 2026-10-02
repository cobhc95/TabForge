using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Models;
using TabForge.Services;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Playback;

/// <summary>Live arrangement refresh: swaps the future part of the running timeline without restarting playback.</summary>
public sealed partial class PlaybackEngine : IDisposable
{
    private sealed record ArrangementRefresh(ScoreTimeline Timeline, double BoundaryMs, SongProject Project);
    private sealed record LoopRefresh(ScoreTimeline Timeline, SongProject Project, int Seq);

    /// <summary>
    /// Recompiles traversal after the currently playing bar using the live arrangement. The existing
    /// bar and its queued releases remain on the old timeline; the scheduler swaps at that bar's end.
    /// </summary>
    public void RefreshArrangement(SongProject project, int[] baseToCurrentBar, int? continueAtBar = null)
        => RefreshArrangement(project, project, baseToCurrentBar, continueAtBar);

    /// <summary>
    /// <see cref="RefreshArrangement(SongProject, int[], int?)"/> on a pool thread. The song is copied here, on the calling (owner) thread,
    /// and the compile runs on the copy, so edits made while it runs cannot reach it.
    /// </summary>
    public Task RefreshArrangementInBackground(SongProject project, int[] baseToCurrentBar, int? continueAtBar = null)
    {
        if (!_running || _options is null || baseToCurrentBar.Length == 0) return Task.CompletedTask;
        var snapshot = SongSnapshot.Take(project);
        return Task.Run(() => RefreshArrangement(project, snapshot.Song, baseToCurrentBar, continueAtBar, compileLoop: true));
    }

    /// <summary>
    /// The refresh behind a live edit: <see cref="RefreshArrangementInBackground"/> for a song edited while it plays. It is compiled ahead of the
    /// playing bar's end and swapped in there; when the compile is not ready by then the scheduler keeps the old schedule (nothing is dropped or
    /// delayed) and the edit is taken one bar later. While looping, the loop range is recompiled too and swapped in at the next wrap, so an edit to a
    /// bar the playhead already passed is heard when the loop comes round. Returns 0 when done, otherwise the milliseconds to wait before asking again
    /// (the bar had too little time left for the compile, or the compile missed the bar line).
    /// </summary>
    public async Task<int> RefreshArrangementLive(SongProject project, int[] baseToCurrentBar)
    {
        var timeline = _timeline;
        if (!_running || _options is null || timeline is null || timeline.Bars.Count == 0 || baseToCurrentBar.Length == 0) return 0;
        var position = Playhead();
        var remaining = timeline.BarAt(position.ElapsedMs).EndMs - position.ElapsedMs;
        var lead = Math.Max(LiveRefreshMinLeadMs, LiveRefreshLeadFactor * Volatile.Read(ref _lastLiveCompileMs));
        if (remaining < lead) return (int)Math.Clamp(remaining + 40, 50, 2000);
        var snapshot = SongSnapshot.Take(project);
        var clock = Stopwatch.StartNew();
        var tooLate = await Task.Run(() => !RefreshArrangement(project, snapshot.Song, baseToCurrentBar, null, lead, live: true, compileLoop: true));
        Volatile.Write(ref _lastLiveCompileMs, clock.Elapsed.TotalMilliseconds);
        if (tooLate) return 50;
        return Interlocked.Exchange(ref _liveRefreshMissed, 0) == 1 ? 60 : 0;
    }

    /// <summary>The least time the playing bar must have left for a live edit to be compiled and swapped in at its end.</summary>
    public const double LiveRefreshMinLeadMs = 150;

    /// <summary>The lead a live compile needs, as a multiple of the last live compile's duration.</summary>
    private const double LiveRefreshLeadFactor = 1.5;

    /// <param name="project">The song the engine plays (it replaces the engine's song when the new timeline swaps in).</param>
    /// <param name="source">The song to compile from: <paramref name="project"/> itself, or an immutable copy of it.</param>
    /// <remarks>
    /// Each refresh takes a sequence number under the engine lock; only the newest may publish or clear the pending swap, so concurrent refreshes
    /// (an edit's own, the live one) never cancel each other: the later compile, made from a later copy of the song, simply replaces the earlier.
    /// <paramref name="live"/> refreshes never hold the scheduler: when not ready at the bar line they are dropped and the old schedule plays on.
    /// </remarks>
    private bool RefreshArrangement(SongProject project, SongProject source, int[] baseToCurrentBar, int? continueAtBar, double minLeadMs = 0, bool live = false, bool compileLoop = false)
    {
        var timeline = _timeline;
        var options = _options;
        if (timeline is null || options is null || !_running || baseToCurrentBar.Length == 0) return true;

        var position = Playhead();
        if (timeline.Bars.Count == 0) return true;
        var activeBar = timeline.BarAt(position.ElapsedMs);
        if (activeBar.EndMs <= position.ElapsedMs) return true;
        if (activeBar.EndMs - position.ElapsedMs < minLeadMs) return false;
        if (live && (activeBar.Bar < 0 || activeBar.Bar >= baseToCurrentBar.Length || baseToCurrentBar[activeBar.Bar] < 0)) return true;   // the playing bar is gone: the structure edit that removed it chose where to continue

        int seq;
        lock (_gate)
        {
            if (!_running || !ReferenceEquals(_timeline, timeline)) return true;
            seq = ++_refreshSeq;
            _arrangementRefreshBoundaryMs = activeBar.EndMs;
            _arrangementRefreshRequested = true;
            _arrangementRefreshLive = live;
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
        }

        try
        {
            var currentBar = activeBar.Bar >= 0 && activeBar.Bar < baseToCurrentBar.Length
                ? baseToCurrentBar[activeBar.Bar]
                : activeBar.Bar;
            var liveOptions = options.Clone();
            liveOptions.StartBar = 0;
            liveOptions.StartCell = 0;
            liveOptions.CountIn = false;
            var order = PlaybackOrder.Build(source, liveOptions);
            var activeTimelineIndex = timeline.Bars.FindIndex(bar => Math.Abs(bar.StartMs - activeBar.StartMs) < 0.001);
            var occurrence = activeTimelineIndex < 0 ? 0 : timeline.Bars
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
            if (activeOrderIndex < 0 && order.Count > 0 && !hasContinuation)
                activeOrderIndex = order.Count - 1;
            var futureOrder = order.Skip(activeOrderIndex + 1).ToArray();

            liveOptions.Metronome = true;
            liveOptions.LiveMetronomeEvents = true;
            var future = futureOrder.Length == 0
                ? new ScoreTimeline()
                : MidiTimelineBuilder.Build(source, liveOptions, futureOrder);
            var revised = SpliceArrangementFuture(timeline, future, activeBar.EndMs, baseToCurrentBar);
            lock (_gate)
            {
                if (!_running || !ReferenceEquals(_timeline, timeline) || seq != _refreshSeq || !_arrangementRefreshRequested) return true;   // superseded, or the bar line passed first
                _pendingArrangementRefresh = new ArrangementRefresh(revised, activeBar.EndMs, project);
            }
            if (compileLoop && options.Loop)
            {
                // The loop wraps back over bars the splice keeps as they were: the whole song is compiled again for the next wrap.
                var whole = MidiTimelineBuilder.Build(source, liveOptions, order.ToArray());
                RemapBarsToBase(whole, baseToCurrentBar);
                lock (_gate)
                {
                    if (!_running || !ReferenceEquals(_timeline, timeline) || seq != _refreshSeq) return true;
                    _pendingLoopRefresh = new LoopRefresh(whole, project, seq);
                }
            }
            return true;
        }
        catch
        {
            lock (_gate)
            {
                if (seq == _refreshSeq)
                {
                    _arrangementRefreshRequested = false;
                    _pendingArrangementRefresh = null;
                    _pendingLoopRefresh = null;
                }
            }
            return true;
        }
    }

    /// <summary>The bar numbers of a freshly compiled timeline (current score bars) in the numbering the running timeline uses (the bars it started with).</summary>
    private static void RemapBarsToBase(ScoreTimeline timeline, int[] baseToCurrentBar)
    {
        var inverseBars = new Dictionary<int, int>();
        for (var baseBar = 0; baseBar < baseToCurrentBar.Length; baseBar++)
            inverseBars.TryAdd(baseToCurrentBar[baseBar], baseBar);
        for (var i = 0; i < timeline.Bars.Count; i++)
            if (inverseBars.TryGetValue(timeline.Bars[i].Bar, out var baseBar)) timeline.Bars[i] = timeline.Bars[i] with { Bar = baseBar };
        foreach (var note in timeline.Notes)
            if (inverseBars.TryGetValue(note.Bar, out var baseBar)) note.Bar = baseBar;
    }

    private static ScoreTimeline SpliceArrangementFuture(ScoreTimeline current, ScoreTimeline future,
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
            else if (!e.IsMetronome && PlaybackEngine.IsEssentialReleaseOrReset(e))
                keep = true;
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
