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

// Owns: refreshing the playing timeline after score edits: the background and live arrangement recompiles, the loop refresh and the refresh reserved for audio growth.
// Does not own: compiling the timeline (ArrangementRefreshCompiler.cs, ScoreToMidiCompiler.cs) or dispatching its events (PlaybackEngine.Scheduler.cs).
// Tests: no dedicated test class.

/// <summary>Live arrangement refresh: swaps the future part of the running timeline without restarting playback.</summary>
public sealed partial class PlaybackEngine : IDisposable, IAudioGrowthRefreshHost
{
    // Project is the live song (owner thread, identity only); Snapshot is the copy the timeline was compiled from (all the scheduler may read).
    private sealed record ArrangementRefresh(ScoreTimeline Timeline, double BoundaryMs, SongProject Project, SongSnapshot Snapshot);
    private sealed record LoopRefresh(ScoreTimeline Timeline, SongProject Project, int Seq, SongSnapshot Snapshot);

    internal bool HasReservedArrangementRefresh { get { lock (_gate) return _arrangementRefreshRequested; } }

    /// <summary>
    /// Recompiles traversal after the currently playing bar using the live arrangement. The existing
    /// bar and its queued releases remain on the old timeline; the scheduler swaps at that bar's end.
    /// </summary>
    public void RefreshArrangement(SongProject project, int[] baseToCurrentBar, int? continueAtBar = null)
        => RefreshArrangement(project, _seekRebuild.TakeSnapshot(project), baseToCurrentBar, continueAtBar);

    /// <summary>
    /// <see cref="RefreshArrangement(SongProject, int[], int?)"/> on a pool thread. The song is copied here, on the calling (owner) thread,
    /// and the compile runs on the copy, so edits made while it runs cannot reach it.
    /// </summary>
    public Task RefreshArrangementInBackground(SongProject project, int[] baseToCurrentBar, int? continueAtBar = null)
    {
        if (!_running || _options is null || baseToCurrentBar.Length == 0) return Task.CompletedTask;
        var snapshot = SongSnapshot.Take(project);
        return Task.Run(() => RefreshArrangement(project, snapshot, baseToCurrentBar, continueAtBar, compileLoop: true));
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
        var tooLate = await Task.Run(() => !RefreshArrangement(project, snapshot, baseToCurrentBar, null, lead, live: true, compileLoop: true));
        Volatile.Write(ref _lastLiveCompileMs, clock.Elapsed.TotalMilliseconds);
        if (tooLate) return 50;
        return Interlocked.Exchange(ref _liveRefreshMissed, 0) == 1 ? 60 : 0;
    }

    /// <summary>The least time the playing bar must have left for a live edit to be compiled and swapped in at its end.</summary>
    public const double LiveRefreshMinLeadMs = 150;

    /// <summary>The lead a live compile needs, as a multiple of the last live compile's duration.</summary>
    private const double LiveRefreshLeadFactor = 1.5;

    /// <param name="project">The song the engine plays (it replaces the engine's song when the new timeline swaps in).</param>
    /// <param name="source">The immutable copy of <paramref name="project"/> to compile from; it is published with the timeline.</param>
    /// <remarks>
    /// Each refresh takes a sequence number under the engine lock; only the newest may publish or clear the pending swap, so concurrent refreshes
    /// (an edit's own, the live one) never cancel each other: the later compile, made from a later copy of the song, simply replaces the earlier.
    /// <paramref name="live"/> refreshes never hold the scheduler: when not ready at the bar line they are dropped and the old schedule plays on.
    /// </remarks>
    private bool RefreshArrangement(SongProject project, SongSnapshot source, int[] baseToCurrentBar, int? continueAtBar, double minLeadMs = 0, bool live = false, bool compileLoop = false)
    {
        var timeline = _timeline;
        var options = _options;
        if (timeline is null || options is null || !_running || baseToCurrentBar.Length == 0) return true;
        if (timeline.Bars.Count == 0) return true;
        ScoreBar activeBar;

        int seq;
        lock (_gate)
        {
            if (!_running || !ReferenceEquals(_timeline, timeline)) return true;
            var position = Playhead();
            activeBar = timeline.BarAt(position.ElapsedMs);
            if (activeBar.EndMs <= position.ElapsedMs) return true;
            if (activeBar.EndMs - position.ElapsedMs < minLeadMs) return false;
            if (live && (activeBar.Bar < 0 || activeBar.Bar >= baseToCurrentBar.Length || baseToCurrentBar[activeBar.Bar] < 0)) return true;   // the playing bar is gone: the structure edit that removed it chose where to continue
            seq = ++_refreshSeq;
            _arrangementRefreshBoundaryMs = activeBar.EndMs;
            _arrangementRefreshRequested = true;
            _arrangementRefreshLive = live;
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
        }

        try
        {
            var plan = source.CompileFuture(timeline, options, baseToCurrentBar, activeBar, continueAtBar);
            lock (_gate)
            {
                if (!_running || !ReferenceEquals(_timeline, timeline) || seq != _refreshSeq || !_arrangementRefreshRequested) return true;   // superseded, or the bar line passed first
                _pendingArrangementRefresh = new ArrangementRefresh(plan.Timeline, activeBar.EndMs, project, source);
            }
            if (compileLoop && options.Loop)
            {
                // The loop wraps back over bars the splice keeps as they were: the whole song is compiled again for the next wrap.
                var whole = source.CompileLoop(baseToCurrentBar, plan);
                lock (_gate)
                {
                    if (!_running || !ReferenceEquals(_timeline, timeline) || seq != _refreshSeq) return true;
                    _pendingLoopRefresh = new LoopRefresh(whole, project, seq, source);
                }
            }
            return true;
        }
        catch // Not logged: playback path: no logging on this path
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

    /// <summary>
    /// Reserves the active bar's end on the calling thread before a caller starts a potentially slow audio upload.
    /// The song copy is also taken here; all timeline compilation runs on that immutable copy.
    /// </summary>
    internal bool RefreshArrangementForAudioGrowth(SongProject project, int[] baseToCurrentBar)
    {
        var timeline = _timeline;
        var options = _options;
        if (!_running || timeline is null || options is null || timeline.Bars.Count == 0 || baseToCurrentBar.Length == 0) return false;

        var snapshot = SongSnapshot.Take(project);
        AudioGrowthReservation reservation;
        lock (_gate)
        {
            if (!_running || !ReferenceEquals(_timeline, timeline)) return false;
            var position = Playhead();
            var activeBar = timeline.BarAt(Math.Min(position.ElapsedMs, timeline.TotalMs));
            var atFinalBoundary = Math.Abs(activeBar.EndMs - timeline.TotalMs) < 0.01 &&
                position.ElapsedMs <= timeline.TotalMs + 2.0;
            if (activeBar.EndMs <= position.ElapsedMs && !atFinalBoundary) return false;

            var sequence = ++_refreshSeq;
            _arrangementRefreshBoundaryMs = activeBar.EndMs;
            _arrangementRefreshRequested = true;
            _arrangementRefreshLive = false;
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            reservation = new AudioGrowthReservation(
                Volatile.Read(ref _generation), sequence, timeline, options.Clone(), activeBar, project, snapshot);
        }

        try
        {
            new AudioGrowthRefreshFlow(this).Start(reservation, baseToCurrentBar.ToArray());
            return true;
        }
        catch (Exception ex) // Not logged: playback path: no logging on this path
        {
            ((IAudioGrowthRefreshHost)this).CompleteAudioGrowthRefresh(reservation, null, null, ex.Message);
            if (ex is OutOfMemoryException) throw;
            return false;
        }
    }

    void IAudioGrowthRefreshHost.CompleteAudioGrowthRefresh(AudioGrowthReservation reservation,
        ArrangementRefreshCompiler.Plan? plan, ScoreTimeline? loopTimeline, string? failure)
    {
        var canceled = false;
        lock (_gate)
        {
            var current = _running && reservation.Generation == Volatile.Read(ref _generation) &&
                ReferenceEquals(_timeline, reservation.Timeline) && reservation.Sequence == _refreshSeq &&
                _arrangementRefreshRequested && !_arrangementRefreshLive;
            if (current && plan is not null)
            {
                _pendingArrangementRefresh = new ArrangementRefresh(plan.Timeline, reservation.ActiveBar.EndMs, reservation.Project, reservation.Snapshot);
                if (loopTimeline is not null)
                    _pendingLoopRefresh = new LoopRefresh(loopTimeline, reservation.Project, reservation.Sequence, reservation.Snapshot);
            }
            else if (current)
            {
                _refreshSeq++;
                _arrangementRefreshRequested = false;
                _pendingArrangementRefresh = null;
                _pendingLoopRefresh = null;
                canceled = true;
            }
        }
        if (canceled) Services.Trace.Error(Services.Trace.Playback, $"Audio growth arrangement refresh was canceled; existing playback schedule resumes ({failure}).");
    }

}
