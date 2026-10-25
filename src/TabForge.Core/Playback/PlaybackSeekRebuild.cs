using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the background rebuild a seek needs when the playing schedule cannot be reused. It keeps the newest request only (a newer seek or a stop
//     cancels the older compile and its result is dropped), compiles it from an immutable copy of the song, and hands the finished timeline to the
//     scheduler, which swaps it in. It also holds the compile rules that a start and a seek share: the compiler options, the loop start clamp,
//     the loop entry position and the held playhead before a rebuilt timeline arrives.
// Does not own: the scheduler, the timeline swap and the playhead (PlaybackEngine.Scheduler.cs, PlaybackEngine.Transport.cs), the song (a
//     SongSnapshot is passed in), the MIDI output.
// Tests: no dedicated test class.
// Threads: Queue, Cancel, IsPending and TryTake are called under the engine's lock. A compile runs on a thread-pool thread and publishes only
//     under this object's own lock, and only when its request is still the newest one.

/// <summary>Background rebuild of the playing timeline for a seek, and the compile rules a start and a seek share.</summary>
internal sealed class PlaybackSeekRebuild
{
    private readonly object _gate = new();

    // UI thread only (snapshots are taken there). The encoder keeps the last song state, so each seek copies only the bars an edit changed; its ceiling is one song state.
    private readonly ProjectStateEncoder _snapshotEncoder = new(ProjectStateEncoder.PlaybackParallelEncodeBars);
    private Request? _latest;   // the newest request not yet taken by the scheduler; guarded by _gate
    // UI thread only. The last copy and the song and the content revision and mix stamp it was taken at: a start or seek on a song no edit touched since reuses it.
    private WeakReference<SongProject>? _lastSong;
    private int _lastRevision;
    private string? _lastMix;
    private SongSnapshot? _lastSnapshot;

    /// <summary>One seek's rebuild: the song copy and options it compiles from, and the finished timeline or the failure.</summary>
    internal sealed class Request
    {
        internal Request(long id, SongProject project, SongSnapshot snapshot, PlaybackOptions options)
        {
            Id = id;
            Project = project;
            Snapshot = snapshot;
            Options = options;
        }

        public long Id { get; }

        /// <summary>The live song the engine plays; the scheduler takes it over when the rebuilt timeline swaps in.</summary>
        public SongProject Project { get; }

        public SongSnapshot Snapshot { get; }
        public PlaybackOptions Options { get; }
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>The compiled timeline; null until the compile finishes or when it failed. Guarded by the owner's lock.</summary>
        public ScoreTimeline? Timeline { get; internal set; }

        /// <summary>The song ms where playback resumes on <see cref="Timeline"/>. Guarded by the owner's lock.</summary>
        public double StartMs { get; internal set; }

        /// <summary>The exception that stopped the compile; null on success. Guarded by the owner's lock.</summary>
        public Exception? Failure { get; internal set; }

        /// <summary>True once the compile has produced a timeline or a failure. Guarded by the owner's lock.</summary>
        public bool Finished { get; internal set; }
    }

    /// <summary>
    /// An immutable copy of the song for a start or seek, taken on the UI thread. A song no edit or mixer change has touched since the previous copy gives that copy
    /// again (no encode); otherwise only the bars changed since the previous copy are encoded.
    /// </summary>
    internal SongSnapshot TakeSnapshot(SongProject project)
    {
        // Owner (UI) thread only: the runtime flags the copy carries (engine-side plug-in state, manual GM-off) are read here once, and MixStamp keys the cache on them.
        var revision = project.ContentRevision;
        var mix = PlaybackScheduleReuse.MixStamp(project);
        if (_lastSnapshot is { } last && _lastRevision == revision && _lastMix == mix && _lastSong is { } song && song.TryGetTarget(out var same) && ReferenceEquals(same, project)) return last;
        var snapshot = SongSnapshot.Take(project, _snapshotEncoder);
        _lastSong = new WeakReference<SongProject>(project);
        _lastRevision = revision;
        _lastMix = mix;
        _lastSnapshot = snapshot;
        return snapshot;
    }

    /// <summary>Queues the rebuild for a seek and returns at once; the compile runs on a thread-pool thread. An older request not yet taken is cancelled.</summary>
    internal void Queue(long id, SongProject project, SongSnapshot snapshot, PlaybackOptions options)
    {
        var request = new Request(id, project, snapshot, options);
        lock (_gate)
        {
            _latest?.Cancellation.Cancel();
            _latest = request;
        }
        _ = Task.Run(() => Compile(request));
    }

    /// <summary>Drops the pending request (a stop, or a seek the schedule reuses): its compile is cancelled and its result is ignored.</summary>
    internal void Cancel()
    {
        lock (_gate)
        {
            _latest?.Cancellation.Cancel();
            _latest = null;
        }
    }

    /// <summary>True while a request waits for the scheduler; the scheduler then holds the seek and the playhead holds at its target.</summary>
    internal bool IsPending
    {
        get { lock (_gate) return _latest is not null; }
    }

    /// <summary>Takes the newest request once its compile has finished, success or failure; false while it still compiles or none is pending.</summary>
    internal bool TryTake(out Request? request)
    {
        lock (_gate)
        {
            if (_latest is not { Finished: true } finished)
            {
                request = null;
                return false;
            }
            _latest = null;
            request = finished;
            return true;
        }
    }

    private void Compile(Request request)
    {
        try
        {
            IScoreQuery score = request.Snapshot;
            var timeline = score.Compile(request.Options, out var loopCompileStart, request.Cancellation.Token);
            var startMs = score.LoopEntryMs(timeline, request.Options, loopCompileStart, timeline.PlayFromMs);
            Publish(request, timeline, startMs, null);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            // A newer seek or a stop replaced this request, so nothing is published.
        }
        catch (Exception ex)
        {
            // The scheduler waits on this request, so the failure is published: it ends playback and the trace names the cause.
            TabForge.Services.Trace.Error(TabForge.Services.Trace.Playback, "Seek rebuild failed: " + ex.Message);
            Publish(request, null, 0, ex);
        }
    }

    private void Publish(Request request, ScoreTimeline? timeline, double startMs, Exception? failure)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_latest, request)) return;   // superseded or cancelled: the result is dropped
            request.Timeline = timeline;
            request.StartMs = startMs;
            request.Failure = failure;
            request.Finished = true;
        }
    }

    /// <summary>The song ms the held playhead shows until the rebuilt timeline arrives: the target bar's start on the timeline on screen, else the position it holds now.</summary>
    internal static double ProvisionalMs(ScoreTimeline? timeline, int bar, int cell, double heldMs)
    {
        if (timeline is null || !TryFindBar(timeline, bar, out var scoreBar)) return heldMs;
        var slots = Math.Max(1, scoreBar.Slots);
        return scoreBar.MsAtFraction((double)Math.Clamp(cell, 0, slots - 1) / slots);
    }

    /// <summary>The first performed bar with source index <paramref name="bar"/>; false when the timeline has none.</summary>
    private static bool TryFindBar(ScoreTimeline timeline, int bar, out ScoreBar found)
    {
        foreach (var candidate in timeline.Bars)
        {
            if (candidate.Bar != bar) continue;
            found = candidate;
            return true;
        }
        found = default;
        return false;
    }

    /// <summary>A loop must start inside its own range, otherwise the wrapped playhead has nowhere to go: a start outside it moves to the loop start.</summary>
    internal static void KeepStartInsideLoop(SongProject project, PlaybackOptions options)
    {
        if (!options.Loop) return;
        var (loopStart, loopEnd) = PlaybackOrder.LoopRange(project, options);
        if (options.StartBar < loopStart || options.StartBar > loopEnd)
        {
            options.StartBar = loopStart;
            options.StartOccurrence = null;   // the moved start is the loop's own first performance
        }
    }

    /// <summary>
    /// The options a timeline compiles with: the metronome and its live click on, and a loop compiled from its start so the wrap has the whole loop
    /// to return to. <paramref name="loopCompileStart"/> is that start bar, or -1 when the loop is not compiled from its start.
    /// </summary>
    internal static PlaybackOptions CompilerOptionsFor(SongProject project, PlaybackOptions options, out int loopCompileStart)
    {
        var compilerOptions = options.Clone();
        compilerOptions.Metronome = true;
        compilerOptions.LiveMetronomeEvents = true;
        // Mute/solo is gated live at dispatch (see SetMuteSolo), so the compile keeps every track.
        if (options.RespectMuteSolo) compilerOptions.RespectMuteSolo = false;
        // The compiler drops every bar before StartBar. When looping, compile from the loop start so the wrap has the whole loop to return to.
        loopCompileStart = -1;
        if (options.Loop)
        {
            // A loop is the unchanged T1 path: it compiles whole passes and enters by bar/cell, so a pass-aware occurrence does not apply.
            compilerOptions.StartOccurrence = null;
            var (loopStart, _) = PlaybackOrder.LoopRange(project, options);
            if (loopStart < options.StartBar)
            {
                loopCompileStart = loopStart;
                compilerOptions.StartBar = loopStart;
                compilerOptions.StartCell = 0;
            }
        }
        return compilerOptions;
    }

    /// <summary>
    /// The song ms where playback begins: inside a loop compiled from its start, the requested cell of the requested bar; otherwise
    /// <paramref name="fallbackMs"/>.
    /// </summary>
    internal static double LoopEntryMs(SongProject project, ScoreTimeline timeline, PlaybackOptions options, int loopCompileStart, double fallbackMs)
    {
        if (loopCompileStart < 0 || !TryFindBar(timeline, options.StartBar, out var playBar)) return fallbackMs;
        var slots = Math.Max(1, MusicTime.BarSlots(project, options.StartBar));
        var cell = Math.Clamp(options.StartCell, 0, slots - 1);
        return playBar.StartMs + (playBar.EndMs - playBar.StartMs) * cell / slots;
    }
}
