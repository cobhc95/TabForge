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
    private SongProject? _project;
    private ScoreTimeline? _timeline;
    private double _anchorMs;          // musical ms at the last clock restart
    private double _currentStreamMs;
    private double _startMs;
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

    public PlaybackEngine() : this(new SharedMidiOutput()) { }
    public PlaybackEngine(IMidiOutput output, PlaybackPreferences? preferences = null)
    {
        _output = output;
        _preferences = preferences ?? new PlaybackPreferences();
    }

    /// <summary>Raised on the caller's thread whenever a new playback timeline has been compiled.</summary>
    public event Action<ScoreTimeline>? TimelineChanged;
    /// <summary>Raised when a live arrangement update replaces only the future timeline suffix.</summary>
    public event Action<ScoreTimeline>? TimelineRevised;

    public bool IsPlaying => _running;
    public bool IsPaused => _paused;
    public PlaybackState State => !_running ? PlaybackState.Stopped : _paused ? PlaybackState.Paused : PlaybackState.Playing;
    public double Speed { get; private set; } = 1.0;
    public int CurrentBar { get; private set; }
    public int CurrentCell { get; private set; }
    public ScoreTimeline? Timeline => _timeline;
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
        catch (Exception ex) { Debug.WriteLine($"MIDI diagnostic log could not be saved: {ex}"); }
    }

    public IReadOnlyList<DispatchRecord> DispatchLog { get { lock (_logGate) return _dispatchLog.ToList(); } }

    // ---------- transport ----------

    public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
    {
        Stop();
        var generation = ++_generation;
        options = options.Clone();

        // A loop must start inside its own range, otherwise the wrapped playhead has nowhere to go.
        if (options.Loop)
        {
            var (loopStart, loopEnd) = PlaybackOrder.LoopRange(project, options);
            if (options.StartBar < loopStart || options.StartBar > loopEnd) options.StartBar = loopStart;
        }

        _project = project;
        _options = options;
        SetMetronomeSettings(options.Metronome, options.MetronomeVolume, options.MetronomeAccentVolume,
            options.MetronomeClickVolume, options.MetronomeAccentNote, options.MetronomeClickNote,
            options.MetronomeSubdivision);
        _onPosition = onPosition;
        _onFinished = onFinished;
        Speed = options.Speed <= 0 ? 1.0 : options.Speed;

        // Playback timelines hold candidate clicks for every supported subdivision. The scheduler
        // gates/parameters them live, so changing these controls never restarts the transport.
        var compilerOptions = options.Clone();
        compilerOptions.Metronome = true;
        compilerOptions.LiveMetronomeEvents = true;
        // Mute/solo is gated live at dispatch (see SetMuteSolo) so toggling it never restarts playback.
        if (options.RespectMuteSolo)
        {
            compilerOptions.RespectMuteSolo = false;
            Volatile.Write(ref _audible, AudibleMask(project));
        }
        else
        {
            Volatile.Write(ref _audible, null);
        }
        // The compiler drops every bar before StartBar. When looping, compile from the loop start so the
        // wrap has the whole loop to return to, then begin playing at the requested bar inside it.
        var loopCompileStart = -1;
        if (options.Loop)
        {
            var (loopStart, _) = PlaybackOrder.LoopRange(project, options);
            if (loopStart < options.StartBar)
            {
                loopCompileStart = loopStart;
                compilerOptions.StartBar = loopStart;
                compilerOptions.StartCell = 0;
            }
        }
        var timeline = MidiTimelineBuilder.Build(project, compilerOptions);
        _timeline = timeline;
        lock (_gate)
        {
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            _arrangementRefreshRequested = false;
        }
        CurrentBar = options.StartBar;
        CurrentCell = options.StartCell;

        if (timeline.Events.Count == 0 || timeline.TotalMs <= 0)
        {
            Stop();
            onFinished?.Invoke();
            return;
        }

        _timer.Set(!startPaused);   // RT-10: held while playing only
        _startMs =options.CountIn ? 0 : timeline.PlayFromMs;
        if (loopCompileStart >= 0 && !options.CountIn &&
            timeline.Bars.FirstOrDefault(b => b.Bar == options.StartBar) is { } playBar)
        {
            var slots = Math.Max(1, MusicTime.BarSlots(project, options.StartBar));
            var cell = Math.Clamp(options.StartCell, 0, slots - 1);
            _startMs = playBar.StartMs + (playBar.EndMs - playBar.StartMs) * cell / slots;
        }
        lock (_gate)
        {
            _anchorMs = _startMs;
        }
        _currentStreamMs = _startMs;
        _paused = startPaused;
        _running = true;
        _playbackStartWallMs = _diagnosticClock.Elapsed.TotalMilliseconds;
        if (startPaused) _clock.Reset();
        else _clock.Restart();

        _thread = new Thread(() => SchedulerLoop(timeline, generation, _startMs))
        {
            IsBackground = true,
            Name = "TabForge MIDI sequencer",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        TimelineChanged?.Invoke(timeline);
    }

    public void Pause()
    {
        if (!_running || _paused) return;
        lock (_gate)
        {
            _anchorMs += _clock.Elapsed.TotalMilliseconds * _clockRate;
            _clock.Reset();
            _paused = true;
            // Queue the reset before a concurrent Resume can make the scheduler enqueue rearm setup.
            PanicAsync();
        }
        _timer.Set(false);   // RT-10: paused is idle
    }

    public void Resume()
    {
        if (!_running || !_paused) return;
        _timer.Set(true);
        lock (_gate)
        {
            _clock.Restart();
            _paused = false;
        }
        // Pause sent a device reset, which can clear controllers/bank state on some synths, so
        // restore program/volume/pan before the next note (on the scheduler thread).
        _rearm = true;
    }

    public void Stop()
    {
        var generation = ++_generation;
        _running = false;
        _paused = false;
        var thread = _thread;
        _thread = null;
        if (thread is not null && thread.IsAlive && thread != Thread.CurrentThread)
        {
            // Never block the caller (normally the UI thread) waiting for MIDI work.
            if (!thread.Join(150)) Task.Run(() => thread.Join(1500));
        }
        PanicAsync();
        _timeline = null;
        lock (_gate)
        {
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            _arrangementRefreshRequested = false;
        }
        _activeMetronomeNotes.Clear();
        _clock.Reset();
        lock (_gate) { _anchorMs = 0; }
        _currentStreamMs = 0;
        _timer.Set(false);   // RT-10: stopped is idle
    }

    /// <summary>Rebuild the timeline from a new musical position, preserving play/pause state.</summary>
    public void Seek(SongProject project, int bar, int cell)
    {
        var opts = _options;
        if (opts is null) return;
        var wasPaused = _paused;
        opts.StartBar = Math.Max(0, bar);
        opts.StartCell = Math.Max(0, cell);
        RestartKeepingState(project, opts, wasPaused);
    }

    /// <summary>Change the relative speed, preserving the current musical position.</summary>
    public void SetSpeed(SongProject project, double speed)
    {
        var clamped = Math.Clamp(speed, 0.25, 2.0);
        Speed = clamped;
        var opts = _options;
        if (opts is null || !_running || opts.Speed.Equals(clamped)) return; // unchanged: never restart
        var pos = Playhead();
        opts.Speed = clamped;
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        RestartKeepingState(project, opts, _paused);
    }

    private void RestartKeepingState(SongProject project, PlaybackOptions opts, bool wasPaused)
    {
        var onPosition = _onPosition ?? (_ => { });
        var onFinished = _onFinished ?? (() => { });
        opts.CountIn = false; // seeks / speed / option changes mid-song never replay the count-in
        Start(project, opts, onPosition, onFinished, startPaused: wasPaused);
    }

    public void SetLoop(bool loop) { if (_options is not null) _options.Loop = loop; }

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
        RefreshMix(project);
    }

    internal static bool[] AudibleMask(SongProject project)
    {
        return project.Tracks.Select(t => MixerGroups.IsAudible(project, t)).ToArray();
    }

    /// <summary>
    /// Applies mute/solo to running playback without recompiling or restarting: newly silenced
    /// tracks get All Notes Off on their channel, and their future note-ons are skipped.
    /// </summary>
    public void SetMuteSolo(SongProject project)
    {
        var mask = AudibleMask(project);
        var previous = Volatile.Read(ref _audible);
        Volatile.Write(ref _audible, mask);
        var timeline = _timeline;
        if (timeline is null || !_running || previous is null) return;
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
            EnqueueOutputOperation(() => { foreach (var cut in cuts) _output.Send(cut.Device, 0xB0 | cut.Channel, 123, 0); });
    }

    private int _loopRangeVersion;

    /// <summary>Updates the loop bars of a running playback; the scheduler picks the new bounds up on its next tick.</summary>
    public void SetLoopRange(int startBar, int endBar, int startCell = 0, int endCell = -1)
    {
        if (_options is null) return;
        _options.LoopStartBar = startBar;
        _options.LoopEndBar = endBar;
        _options.LoopStartCell = startCell;
        _options.LoopEndCell = endCell;
        // A loop that now begins before the compiled timeline needs a recompile from its start.
        var timeline = _timeline;
        if (_running && _options.Loop && _project is { } project && timeline is not null &&
            !timeline.Bars.Any(b => b.Bar == Math.Min(startBar, endBar)))
        {
            var pos = Playhead();
            _options.StartBar = pos.Bar;
            _options.StartCell = pos.Cell;
            RestartKeepingState(project, _options, _paused);
            return;
        }
        Interlocked.Increment(ref _loopRangeVersion);
    }

    /// <summary>Applies metronome controls directly to the scheduler without rebuilding or stopping playback.</summary>
    public void SetMetronomeSettings(bool enabled, int volume, int accentVolume, int clickVolume,
        int accentNote, int clickNote, int subdivisions)
    {
        Volatile.Write(ref _metronomeEnabled, enabled ? 1 : 0);
        Volatile.Write(ref _metronomeVolume, Math.Clamp(volume, 0, 100));
        Volatile.Write(ref _metronomeAccentVolume, Math.Clamp(accentVolume, 0, 100));
        Volatile.Write(ref _metronomeClickVolume, Math.Clamp(clickVolume, 0, 100));
        Volatile.Write(ref _metronomeAccentNote, Math.Clamp(accentNote, 0, 127));
        Volatile.Write(ref _metronomeClickNote, Math.Clamp(clickNote, 0, 127));
        Volatile.Write(ref _metronomeSubdivision, subdivisions is 1 or 2 or 3 or 4 ? subdivisions : 1);

        if (_options is not { } options) return;
        options.Metronome = enabled;
        options.MetronomeVolume = Math.Clamp(volume, 0, 100);
        options.MetronomeAccentVolume = Math.Clamp(accentVolume, 0, 100);
        options.MetronomeClickVolume = Math.Clamp(clickVolume, 0, 100);
        options.MetronomeAccentNote = Math.Clamp(accentNote, 0, 127);
        options.MetronomeClickNote = Math.Clamp(clickNote, 0, 127);
        options.MetronomeSubdivision = subdivisions is 1 or 2 or 3 or 4 ? subdivisions : 1;
    }

    /// <summary>
    /// Applies a live option change (metronome / count-in). If the transport is running the timeline
    /// is recompiled from the current musical position so the change is heard immediately instead of
    /// being silently ignored until the next Play.
    /// </summary>
    public void UpdateOptions(SongProject project, Action<PlaybackOptions> mutate)
    {
        var opts = _options;
        if (opts is null) return;
        var before = opts.Clone();
        mutate(opts);
        if (!_running || opts.CompilesSameAs(before)) return; // unchanged: never restart
        var pos = Playhead();
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        RestartKeepingState(project, opts, _paused);
    }

    /// <summary>
    /// Sends the current volume/pan for every track on its actual channel and updates the stored
    /// setup values, so a later loop/seek re-sends the new mix. Cheap enough for live slider drags
    /// (two CC messages per track) and avoids recompiling the whole timeline.
    /// </summary>
    public void RefreshMix(SongProject project)
    {
        var timeline = _timeline;
        if (timeline is null || !_running) return;
        foreach (var group in timeline.ChannelSetup.Where(e => e.TrackIndex >= 0).GroupBy(e => e.TrackIndex))
        {
            var trackIndex = group.Key;
            if (trackIndex < 0 || trackIndex >= project.Tracks.Count) continue;
            var track = project.Tracks[trackIndex];
            foreach (var e in group)
            {
                if ((e.Status & 0xF0) != 0xB0) continue;
                if (e.Data1 == 7) { e.Data2 = MixerGroups.Volume(project, track); lock (_outputGate) _output.Send(e.DeviceId, e.Status, e.Data1, MasterScaled(e.Status, e.Data1, e.Data2)); }
                else if (e.Data1 == 10) { e.Data2 = MixerGroups.Pan(project, track); lock (_outputGate) _output.Send(e.DeviceId, e.Status, e.Data1, e.Data2); }
            }
        }
    }

    /// <summary>
    /// Recompiles the timeline from the current musical position. Used when a mixer change (mute,
    /// solo, volume, pan, program) must be heard immediately rather than at the next Play.
    /// </summary>
    public void Rebuild(SongProject project)
    {
        var opts = _options;
        // Stopped: nothing restarts, so push the current instruments / levels now (an instrument change must
        // register before the next play, not keep the stale programs).
        if (!_running) { SendSetupFromProject(project); return; }
        if (opts is null) return;
        var pos = Playhead();
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        RestartKeepingState(project, opts, _paused);
    }

    // ---------- scheduler ----------

    /// <summary>
    /// RT-10 / D10: the 1 ms Windows timer resolution (shared, reference-counted <see cref="Audio.Contracts.NativeTimer"/>) is held only
    /// while this engine plays: raised on start and resume, released on pause, stop and dispose (an idle document costs no battery).
    /// </summary>
    private readonly Audio.Contracts.NativeTimer.Hold _timer = new();

    /// <summary>Self-test: this engine currently holds the 1 ms timer resolution.</summary>
    internal bool HoldsTimerResolution => _timer.IsHeld;

    // ---------- preview ----------

    public void Dispose()
    {
        Stop();
        StopDiagnostics();
        // Shutdown is best effort: a failing device must not stop the rest of the cleanup.
        try { EnqueueOutputOperation(_output.Dispose).GetAwaiter().GetResult(); }
        catch (Exception ex) { Debug.WriteLine($"MIDI output shutdown failed: {ex}"); }
        _timer.Set(false);
    }
}
