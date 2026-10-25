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

// Owns: the engine composition (output, clocks, gates, dispatch record) and the start/stop lifecycle.
// Does not own: the scheduler loop (PlaybackEngine.Scheduler.cs), transport requests (PlaybackEngine.Transport.cs), output dispatch (PlaybackEngine.Scheduler.Output.cs).
// Tests: PlaybackStartHoldTests, PlaybackSeekSwapTests, AudiblePlayheadLatencyRiseTests.

/// <summary>One dispatched MIDI message with the timestamps used to measure scheduling jitter.</summary>
public readonly record struct DispatchRecord(double StreamMs, double LatencyMs, int TrackIndex, int Status, int Data1, int Data2, int DeviceId)
{
    public bool IsNoteOn => (Status & 0xF0) == 0x90 && Data2 > 0;
    public bool IsNoteOff => (Status & 0xF0) == 0x80 || ((Status & 0xF0) == 0x90 && Data2 == 0);
    /// <summary>How far after its musical time the message actually left the app (negative = early).</summary>
    public double AheadMs => -LatencyMs;
    public override string ToString() =>
        $"stream={StreamMs:0.000} latency={LatencyMs:0.000} trk={TrackIndex} dev={DeviceId} {Status:X2} {Data1} {Data2}";
}

/// <summary>
/// TabForge playback engine: transport plus a single background scheduler thread.
/// <para>
/// The scheduler computes the current musical time from a monotonic clock and dispatches every event
/// whose time has arrived, using a very small lead (~1 ms) so notes are neither sent early nor
/// grouped together. Sleeps are targeted at the next event, so a dense passage is dispatched
/// precisely while a long rest costs almost no CPU. Musical time never depends on the UI thread or
/// on frame rate.
/// </para>
/// </summary>
public sealed partial class PlaybackEngine : IDisposable
{
    /// <summary>How early an event may be sent to absorb the OS sleep granularity.</summary>
    private const double DispatchLeadMs = 1.0;
    /// <summary>Musical events older than this are stale (a stall); state is reconstructed at the current time.</summary>
    private const double MaxLateMs = 150.0;
    /// <summary>A backlog this large means the thread was suspended; jump to now and re-arm the device.</summary>
    private const double StaleResyncMs = 250.0;
    private const double PositionIntervalMs = 16.0;
    private const double MaxSleepMs = 15.0;

    private readonly IMidiOutput _output;
    private readonly object _gate = new();
    private readonly object _outputGate = new();
    private readonly object _outputOperationGate = new();
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _diagnosticClock = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<int, int> _activeMetronomeNotes = new();
    private Task _outputOperationTail = Task.CompletedTask;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _paused;
    private PlaybackOptions? _options;
    // The running song and its timeline travel as ONE immutable holder, replaced by a single volatile write (Publish), so a reader never pairs a new timeline with an old song.
    // TrackIds: the ids of the tracks in the timeline's index space (the song's track list when the timeline was published).
    // Project is the LIVE song: owner (UI) thread only - the identity key of the seek-reuse check and the song a restart recompiles. Snapshot is the immutable copy the timeline
    // was compiled from: the only song the scheduler thread and pool threads may read. Null only while stopped (no scheduler runs then).
    private sealed record SongState(ScoreTimeline? Timeline, SongProject? Project, SongSnapshot? Snapshot, Guid[] TrackIds);
    private SongState? _song;
    /// <summary>The live song this engine plays: owner (UI) thread only (the stopped setup send, SetPracticeSilence, a loop restart). The scheduler and pool threads read <see cref="SongState.Snapshot"/> through <see cref="IScoreQuery"/> instead (PlaybackScoreQueryGateTests).</summary>
    private SongProject? LiveProject => Volatile.Read(ref _song)?.Project;
    private ScoreTimeline? _timeline => Volatile.Read(ref _song)?.Timeline;
    private double _anchorMs;          // musical ms at the last clock restart
    private double _currentStreamMs;
    private double _startMs;
    /// <summary>A count-in plays before a start inside a loop: the scheduler plays it on its own thread, then starts at <see cref="_startMs"/>.</summary>
    private bool _countInBeforeStart;
    private Action<PlaybackPosition>? _onPosition;
    private Action? _onFinished;
    private int _generation;
    private int _metronomeEnabled;
    private int _metronomeVolume = 70;
    private int _metronomeAccentVolume = 100;
    private int _metronomeClickVolume = 76;
    private int _metronomeAccentNote = 34;
    private int _metronomeClickNote = 33;
    private int _metronomeSubdivision = 1;
    // Boosted metronome: each click is layered with extra sharp percussion hits (claves, side stick,
    // high woodblock) at the same velocity. MIDI velocity tops out at 127, so stacking hits is what
    // makes the click cut through a loud mix.
    private static readonly int[] BoostLayers = { 75, 37, 76 };
    /// <summary>Metronome boost, count-in and loop settings this engine reads.</summary>
    private volatile PlaybackPreferences _preferences;
    /// <summary>The settings-driven behaviour this engine reads (the scheduler reads it live); the document manager gives every engine the application's instance.</summary>
    public PlaybackPreferences Preferences { get => _preferences; set => _preferences = value; }

    /// <summary>Writes into this engine's preferences; a running song hears the change on its next click, section or wrap.</summary>
    public void SetPreferences(Action<PlaybackPreferences> write) => write(_preferences);

    private double _playbackStartWallMs;
    private volatile bool _rearm;
    private volatile bool _diagnostics;
    private readonly List<DispatchRecord> _dispatchLog = new();
    private string? _diagnosticPath;
    private readonly object _logGate = new();
    private ArrangementRefresh? _pendingArrangementRefresh;
    private bool _arrangementRefreshRequested;
    private double _arrangementRefreshBoundaryMs;
    private bool _arrangementRefreshLive;           // under _gate: the request is a live edit's (never waited for at the bar line)
    private int _refreshSeq;                        // under _gate: the newest refresh; older ones may not publish
    private LoopRefresh? _pendingLoopRefresh;       // under _gate: the whole song recompiled, swapped in at the next loop wrap
    private int _liveRefreshMissed;                 // set by the scheduler when a live refresh was not ready at its bar line
    private double _lastLiveCompileMs;
    private PlaybackScheduleReuse? _scheduleReuse;
    private PendingPlaybackSeek? _pendingSeek;
    private readonly PlaybackSeekRebuild _seekRebuild = new();
    private long _seekRequestId;
    private int _lateNotesDropped;   // note-ons the scheduler skipped as too late since the last start; written by the scheduler thread, read by any

    /// <summary>Note-ons skipped since the last start because the scheduler reached them more than MaxLateMs late (a stall): the first thing to read when notes go missing.</summary>
    public int LateNotesDropped => Volatile.Read(ref _lateNotesDropped);

    public PlaybackEngine(IMidiOutput output, PlaybackPreferences? preferences = null)
    {
        _output = output;
        _preferences = preferences ?? new PlaybackPreferences();
    }

    /// <summary>Raised on the caller's thread whenever a new playback timeline has been compiled.</summary>
    public event Action<ScoreTimeline>? TimelineChanged;
    /// <summary>Raised when a live arrangement update replaces only the future timeline suffix.</summary>
    public event Action<ScoreTimeline>? TimelineRevised;

    /// <summary>True while it plays or pauses, and from <see cref="BeginStart"/> until its compile is ready (the start is under way).</summary>
    public bool IsPlaying => _running || Volatile.Read(ref _pendingStart) is not null;
    public bool IsPaused => _paused || Volatile.Read(ref _pendingStart) is { StartPaused: true };
    public PlaybackState State => !IsPlaying ? PlaybackState.Stopped : IsPaused ? PlaybackState.Paused : PlaybackState.Playing;
    public double Speed { get; private set; } = 1.0;
    public int CurrentBar { get; private set; }
    public int CurrentCell { get; private set; }
    public ScoreTimeline? Timeline => _timeline;
    /// <summary>Self-test: the published song's copy, its track ids and the loop bounds the drawn playhead is held within.</summary>
    internal SongProject? PublishedSongCopy => Volatile.Read(ref _song)?.Snapshot?.Song;
    internal Guid[] PublishedTrackIds => Volatile.Read(ref _song)?.TrackIds ?? Array.Empty<Guid>();
    internal LoopPlayheadHold LoopBoundsHold => _loopHold;
    internal int SchedulerThreadId => _thread?.ManagedThreadId ?? 0;
    public double TotalMs => _timeline?.TotalMs ?? 0;

    // ---------- diagnostics ----------

    /// <summary>Records every dispatched message (with timestamps) for offline analysis.</summary>
    public void StartDiagnostics(string? dumpPath = null)
    {
        if (!string.IsNullOrWhiteSpace(dumpPath)) dumpPath = FilePathPolicy.OutputFile(dumpPath, "diagnostic log");
        lock (_logGate) { _dispatchLog.Clear(); _diagnosticPath = dumpPath; }
        _diagnostics = true;
    }

    public void StopDiagnostics()
    {
        _diagnostics = false;
        IReadOnlyList<DispatchRecord> copy;
        string? path;
        lock (_logGate) { copy = _dispatchLog.ToList(); path = _diagnosticPath; }
        if (path is null) return;
        try
        {
            FilePathPolicy.WriteAtomically(path, stream =>
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: true);
                var bytesWritten = 0L;
                foreach (var record in copy)
                {
                    var line = record.ToString();
                    var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                    if (bytesWritten + lineBytes > InputLimits.MaxDiagnosticLogBytes)
                    {
                        writer.WriteLine("[diagnostic capture truncated at the configured output limit]");
                        break;
                    }
                    writer.WriteLine(line);
                    bytesWritten += lineBytes;
                }
                writer.Flush();
            }, createDirectory: true);
        }
        catch (Exception ex) { Services.Trace.Error(Services.Trace.Playback, "MIDI diagnostic log: save: " + ex.Message); }
    }

    public IReadOnlyList<DispatchRecord> DispatchLog { get { lock (_logGate) return _dispatchLog.ToList(); } }

    // ---------- transport ----------

    public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
    {
        Stop();
        var generation = ++_generation;
        options = options.Clone();
        PlaybackSeekRebuild.KeepStartInsideLoop(project, options);
        if (options.Loop) options.StartOccurrence = null;   // T1 enters a loop by bar/cell, not by a pass
        // A loop compiles from its start and begins at the requested bar inside it (see PlaybackSeekRebuild.CompilerOptionsFor).
        var snapshot = _seekRebuild.TakeSnapshot(project);   // cached by content revision (every song edit, mixer included, bumps it); the timeline compiles from it and is published with it
        var timeline = snapshot.Compile(options, out var loopCompileStart);
        StartCompiled(project, options, timeline, snapshot, loopCompileStart, generation, onPosition, onFinished, startPaused);
    }

    /// <summary>Loop count, count-in per loop and speed trainer; see <see cref="PlaybackPreferences.Loop"/>.</summary>
    public sealed record LoopBehaviour(int Count = 0, bool CountInEachLoop = false,
        bool Trainer = false, int TrainerFromPercent = 50, int TrainerToPercent = 100, int TrainerStepPercent = 10);
    /// <summary>Raised (scheduler thread) after each completed loop: engine and loops completed so far.</summary>
    public static event Action<PlaybackEngine, int>? LoopCompleted;
    private int _loopsDone;

    private int[] _sectionStarts = Array.Empty<int>();
    public void SetSectionStarts(IEnumerable<int> bars) => Volatile.Write(ref _sectionStarts, bars.Distinct().OrderBy(b => b).ToArray());

    // Score bars the transport jumps over ("skip playing area"). Mapped to timeline ms lazily.
    private (int Start, int End)[] _skipBars = Array.Empty<(int, int)>();
    private (ScoreTimeline? Timeline, (int, int)[] Bars, (double Start, double End)[] Ms) _skipCache;
    public void SetSkipRanges(IEnumerable<(int Start, int End)> ranges) => Volatile.Write(ref _skipBars, ranges.ToArray());

    // Speed-trainer rate applied to the playback clock (1 = compiled speed); changes seamlessly.
    private double _clockRate = 1.0;
    private bool[]? _audible;
    private int _masterVolume = 100;

    // Last channel volume (CC7, after master) sent per MIDI channel; GM power-on default is 100.
    private readonly int[] _channelVolume = Enumerable.Repeat(100, 16).ToArray();

    /// <summary>Master volume for all tracks, 0–100 %, applied live by resending channel volume.</summary>
    public void SetMasterVolume(SongProject project, int percent)
    {
        Volatile.Write(ref _masterVolume, Math.Clamp(percent, 0, 100));
        PushLiveChannelState(project);
    }

    /// <summary>The routed MIDI output this engine plays through; null when its output is not routed.</summary>
    public Audio.RoutedMidiOutput? Routing => _output as Audio.RoutedMidiOutput;

    private bool _liveSolo;

    internal static bool[] AudibleMask(SongProject project, bool liveSolo = false)
    {
        return project.Tracks.Select(t => MixerGroups.IsAudible(project, t, liveSolo)).ToArray();
    }

    /// <summary>
    /// Applies the track mix to running playback without recompiling or restarting: the tracks' current levels, pan and sounds are sent (PushLiveChannelState),
    /// newly silenced tracks get All Notes Off on their channel, and their future note-ons are skipped.
    /// </summary>
    public void SetMuteSolo(SongProject project, bool liveSolo = false)
    {
        Volatile.Write(ref _liveSolo, liveSolo);
        PushLiveChannelState(project);
        var mask = AudibleMask(project, liveSolo);
        var previous = Volatile.Read(ref _audible);
        Volatile.Write(ref _audible, mask);
        if (previous is null) return;
        var practice = Volatile.Read(ref _practiceSilence);
        CutSilenced(WithoutTrack(mask, practice), WithoutTrack(previous, practice));
    }

    private int _practiceSilence = -1;

    /// <summary>Track index silenced by practice mode (-1 = none); independent of mute/solo and never saved.</summary>
    public int PracticeSilence => Volatile.Read(ref _practiceSilence);

    /// <summary>
    /// Silences one track's playback (index into the project's tracks; -1 clears) without touching its mute state.
    /// A running track gets All Notes Off; the setting survives Start/Stop until the caller clears it.
    /// </summary>
    public void SetPracticeSilence(int trackIndex)
    {
        trackIndex = Math.Max(-1, trackIndex);
        var previousIndex = Interlocked.Exchange(ref _practiceSilence, trackIndex);
        var s = Volatile.Read(ref _song);
        if (trackIndex != previousIndex && previousIndex >= 0 && s?.Timeline is { } live && _running)
        {
            var at = Volatile.Read(ref _currentStreamMs);
            EnqueueOutputOperation(() => RestoreChannelStateAt(live, live.PlayFromMs, at, previousIndex));   // the track's program, volume and bend the silence skipped
        }
        if (trackIndex < 0 || trackIndex == previousIndex || LiveProject is not { } project) return;
        var mask = Volatile.Read(ref _audible) ?? Enumerable.Repeat(true, project.Tracks.Count).ToArray();
        CutSilenced(WithoutTrack(mask, trackIndex), mask);
    }

    private static bool[] WithoutTrack(bool[] mask, int track)
    {
        if (track < 0 || track >= mask.Length || !mask[track]) return mask;
        var copy = (bool[])mask.Clone();
        copy[track] = false;
        return copy;
    }

    /// <summary>All Notes Off for tracks that were audible in <paramref name="previous"/> and are not in <paramref name="mask"/>.</summary>
    private void CutSilenced(bool[] mask, bool[] previous, bool releaseSustain = false)
    {
        var timeline = _timeline;
        if (timeline is null || !_running) return;
        // Every channel a track plays on (its own and its effect channel for bent notes).
        var channelsOf = timeline.ChannelSetup.Where(e => e.TrackIndex >= 0)
            .GroupBy(e => e.TrackIndex)
            .ToDictionary(g => g.Key, g => g.Select(e => (Device: e.DeviceId, Channel: e.Status & 0x0F)).Distinct().ToList());
        var cuts = new List<(int Device, int Channel)>();
        for (var t = 0; t < mask.Length && t < previous.Length; t++)
        {
            if (mask[t] || !previous[t] || !channelsOf.TryGetValue(t, out var targets)) continue;
            foreach (var target in targets)
            {
                // Don't cut a channel another audible track is still using.
                var shared = channelsOf.Any(pair => pair.Key != t && pair.Key < mask.Length && mask[pair.Key] && pair.Value.Contains(target));
                if (!shared) cuts.Add(target);
            }
        }
        // The cuts go out on the output queue: a driver that is slow or busy with the scheduler never holds the UI thread.
        if (cuts.Count > 0)
            EnqueueOutputOperation(() => { foreach (var cut in cuts) { _output.Send(cut.Device, 0xB0 | cut.Channel, 123, 0); if (releaseSustain) _output.Send(cut.Device, 0xB0 | cut.Channel, 64, 0); } });
    }

    private int _loopRangeVersion;

    // Tracks deleted while the song plays: their events of the playing timeline are dropped at once (this timeline only; the next published one is compiled without them).
    private sealed record DroppedTracks(ScoreTimeline Timeline, bool[] Dropped);
    private DroppedTracks? _dropped;

    /// <summary>
    /// Tracks deleted from the playing song fall silent at once, not at the next bar line: they get All Notes Off and the rest of the playing schedule skips them.
    /// The refresh that follows publishes a schedule without them (Undo of the delete brings them back from the next published schedule).
    /// </summary>
    public void SilenceTracks(IReadOnlyCollection<Guid> removed)
    {
        if (!_running || removed.Count == 0 || Volatile.Read(ref _song) is not { Timeline: { } timeline } song) return;
        var dropped = song.TrackIds.Select(id => removed.Contains(id)).ToArray();
        if (!dropped.Contains(true)) return;
        var before = Volatile.Read(ref _dropped);
        if (before is not null && ReferenceEquals(before.Timeline, timeline))
            for (var i = 0; i < dropped.Length && i < before.Dropped.Length; i++) dropped[i] |= before.Dropped[i];
        Volatile.Write(ref _dropped, new DroppedTracks(timeline, dropped));
        // Explicit note-offs first (synths that ignore CC123 would hang), only this track's notes, so a shared channel keeps the others'. Then CC123/CC64 on the exclusive channels as a backstop.
        EnqueueOutputOperation(() => ReleaseSoundingNotes(dropped));
        CutSilenced(dropped.Select(d => !d).ToArray(), Enumerable.Repeat(true, dropped.Length).ToArray(), releaseSustain: true);
    }

    // Sounding notes by channel*128+note: the track index + 1 that started it (0 = silent) and its device. Written by Dispatch and read here, both under _outputGate; fixed arrays, no allocation.
    private readonly int[] _soundingTrack = new int[16 * 128];
    private readonly int[] _soundingDevice = new int[16 * 128];

    internal void NoteSounding(ScoreEvent e)
    {
        var slot = ((e.Status & 0x0F) << 7) | (e.Data1 & 0x7F);
        if (e.IsNoteOn) { _soundingTrack[slot] = e.TrackIndex + 1; _soundingDevice[slot] = e.DeviceId; }
        else if (_soundingTrack[slot] == e.TrackIndex + 1) _soundingTrack[slot] = 0;
    }

    private void ClearSoundingNotes() => Array.Clear(_soundingTrack);

    /// <summary>Note-off for every sounding note of the <paramref name="tracks"/> (flags by track index). Runs under _outputGate.</summary>
    internal void ReleaseSoundingNotes(bool[] tracks)
    {
        for (var slot = 0; slot < _soundingTrack.Length; slot++)
        {
            var track = _soundingTrack[slot] - 1;
            if (track < 0 || track >= tracks.Length || !tracks[track]) continue;
            _output.Send(_soundingDevice[slot], 0x80 | (slot >> 7), slot & 0x7F, 0);
            _soundingTrack[slot] = 0;
        }
    }

    /// <summary>The live pass for a restart driven from outside the engine (the transport's speed change); see <see cref="RestartOccurrence"/>.</summary>
    public int? LiveOccurrence(SongProject project) =>
        _options is { } opts ? RestartOccurrence(project, opts, Playhead()) : null;

    /// <summary>The pass to resume at for an internal restart at <paramref name="pos"/>: the live bar's performance in the full order (null = first or unknown).</summary>
    private int? RestartOccurrence(SongProject project, PlaybackOptions opts, PlaybackPosition pos)
    {
        var timeline = _timeline;
        if (timeline is null || opts.Loop || !opts.RepeatExpansion || _seekRebuild.IsPending) return null;
        var occurrence = ArrangementRefreshCompiler.ActiveOccurrence(timeline, project, opts, pos.ElapsedMs);
        return occurrence is > 1 ? occurrence : null;
    }

    /// <summary>Self-test: this engine currently holds the 1 ms timer resolution.</summary>
    internal bool HoldsTimerResolution => _timer.IsHeld;

    // ---------- preview ----------

    public void Dispose()
    {
        Stop();
        StopDiagnostics();
        // Shutdown is best effort: a failing device must not stop the rest of the cleanup.
        try { EnqueueOutputOperation(_output.Dispose).GetAwaiter().GetResult(); }
        catch { } // swallow: playback path, no logging on this path
        _timer.Set(false);
    }
}
