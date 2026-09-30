using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Models;
using TabForge.Services;

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
public sealed class PlaybackEngine : IDisposable
{
    private sealed record ArrangementRefresh(ScoreTimeline Timeline, double BoundaryMs, SongProject Project);

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
    public static bool MetronomeBoost { get; set; } = true;
    /// <summary>Count-in click level (0-100 %), separate from the metronome; boosted like the metronome.</summary>
    public static int CountInVolume { get; set; } = 70;
    /// <summary>Count-in sound; -1 = use the metronome notes.</summary>
    public static int CountInAccentNote { get; set; } = -1;
    public static int CountInClickNote { get; set; } = -1;
    private double _playbackStartWallMs;
    private volatile bool _rearm;
    private volatile bool _diagnostics;
    private readonly List<DispatchRecord> _dispatchLog = new();
    private string? _diagnosticPath;
    private readonly object _logGate = new();
    private ArrangementRefresh? _pendingArrangementRefresh;
    private bool _arrangementRefreshRequested;
    private double _arrangementRefreshBoundaryMs;

    public PlaybackEngine() : this(new SharedMidiOutput()) { }
    public PlaybackEngine(IMidiOutput output) => _output = output;

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

    public IReadOnlyList<MidiOutputDeviceInfo> GetDevices() => _output.Devices;

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

    /// <summary>Sends every channel's program, volume and pan again at the next tick (a synth that was just created has none).</summary>
    public void RearmChannelSetup()
    {
        _rearm = true;
        // Not playing: nothing will consume the flag, so send the setup to the (new) synths now.
        if (_running) return;
        if (_project is { } project) SendSetupFromProject(project);
        else if (_timeline is { } timeline) lock (_outputGate) SendChannelSetupCore(timeline);
    }

    /// <summary>Stopped: compiles the song's channel setup from the live project and sends it.</summary>
    private void SendSetupFromProject(SongProject project)
    {
        _project = project;
        try
        {
            var opts = (_options ?? new PlaybackOptions()).Clone();
            opts.Metronome = false;
            opts.CountIn = false;
            opts.Loop = false;
            var timeline = MidiTimelineBuilder.Build(project, opts);
            lock (_outputGate) SendChannelSetupCore(timeline);
        }
        catch (Exception ex) { Debug.WriteLine($"Channel setup refresh failed: {ex}"); }
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
            _arrangementRefreshRequested = false;
        }
        _activeMetronomeNotes.Clear();
        _clock.Reset();
        lock (_gate) { _anchorMs = 0; }
        _currentStreamMs = 0;
        _timer.Set(false);   // RT-10: stopped is idle
    }

    /// <summary>All notes off, on a background thread so a slow driver cannot freeze the UI.</summary>
    public void PanicAsync()
    {
        var timeline = _timeline;
        EnqueueOutputOperation(() =>
        {
            _output.ResetAll();
            SendNeutralExpressionState(timeline);
        });
    }

    private void SendNeutralExpressionState(ScoreTimeline? timeline)
    {
        if (timeline is null) return;
        foreach (var e in timeline.ChannelSetup)
        {
            var kind = e.Status & 0xF0;
            if ((kind == 0xE0 && e.Data1 == 0 && e.Data2 == 64) ||
                (kind == 0xB0 && e.Data1 == 1 && e.Data2 == 0))
                _output.Send(e.DeviceId, e.Status, e.Data1, e.Data2);
        }
    }

    /// <summary>
    /// Reset/setup operations are serialized in call order. Resume therefore cannot send setup
    /// before a queued pause reset and then have that reset erase the freshly armed channel state.
    /// </summary>
    private Task EnqueueOutputOperation(Action operation)
    {
        lock (_outputOperationGate)
        {
            var previous = _outputOperationTail;
            _outputOperationTail = Task.Run(() =>
            {
                try { previous.GetAwaiter().GetResult(); }
                catch (Exception ex) { Debug.WriteLine($"Previous MIDI cleanup operation failed: {ex}"); }
                lock (_outputGate)
                {
                    try { operation(); }
                    catch (Exception ex) { Debug.WriteLine($"MIDI output cleanup operation failed: {ex}"); }
                }
            });
            return _outputOperationTail;
        }
    }

    private void ResetAndSetup(ScoreTimeline timeline, int generation)
    {
        _activeMetronomeNotes.Clear();
        EnqueueOutputOperation(() =>
        {
            _output.ResetAll();
            if (_running && generation == Volatile.Read(ref _generation) && !_paused)
                SendChannelSetupCore(timeline);
        }).GetAwaiter().GetResult();
    }

    private void RearmChannelSetup(ScoreTimeline timeline, int generation)
    {
        EnqueueOutputOperation(() =>
        {
            if (_running && generation == Volatile.Read(ref _generation) && !_paused)
                SendChannelSetupCore(timeline);
        }).GetAwaiter().GetResult();
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

    /// <summary>Loop behaviour read by the scheduler at every wrap (cheap: no recompile, no restart).</summary>
    public sealed record LoopBehaviour(int Count = 0, bool CountInEachLoop = false,
        bool Trainer = false, int TrainerFromPercent = 50, int TrainerToPercent = 100, int TrainerStepPercent = 10);
    public static LoopBehaviour LoopSettings { get; set; } = new();
    /// <summary>Raised (scheduler thread) after each completed loop: engine and loops completed so far.</summary>
    public static event Action<PlaybackEngine, int>? LoopCompleted;
    private int _loopsDone;

    /// <summary>Count-in before every section start while playing (bars from the song's markers).</summary>
    public static bool CountInEachSection { get; set; }
    private int[] _sectionStarts = Array.Empty<int>();
    public void SetSectionStarts(IEnumerable<int> bars) => Volatile.Write(ref _sectionStarts, bars.Distinct().OrderBy(b => b).ToArray());

    private static List<double> SectionStartMs(ScoreTimeline timeline, int[] bars)
    {
        var list = new List<double>();
        if (bars.Length == 0) return list;
        var previous = -1;
        foreach (var bar in timeline.Bars)
        {
            if (bar.Bar != previous && Array.BinarySearch(bars, bar.Bar) >= 0) list.Add(bar.StartMs);
            previous = bar.Bar;
        }
        return list;
    }

    // Score bars the transport jumps over ("skip playing area"). Mapped to timeline ms lazily.
    private (int Start, int End)[] _skipBars = Array.Empty<(int, int)>();
    private (ScoreTimeline? Timeline, (int, int)[] Bars, (double Start, double End)[] Ms) _skipCache;
    public void SetSkipRanges(IEnumerable<(int Start, int End)> ranges) => Volatile.Write(ref _skipBars, ranges.ToArray());

    private (double Start, double End)[] SkipMs(ScoreTimeline timeline)
    {
        var bars = Volatile.Read(ref _skipBars);
        var cache = _skipCache;
        if (ReferenceEquals(cache.Timeline, timeline) && ReferenceEquals(cache.Bars, bars)) return cache.Ms;
        var list = new List<(double, double)>();
        foreach (var bar in timeline.Bars)
        {
            if (!bars.Any(r => bar.Bar >= r.Item1 && bar.Bar <= r.Item2)) continue;
            if (list.Count > 0 && Math.Abs(list[^1].Item2 - bar.StartMs) < 0.5) list[^1] = (list[^1].Item1, bar.EndMs);
            else list.Add((bar.StartMs, bar.EndMs));
        }
        var ms = list.ToArray();
        _skipCache = (timeline, bars, ms);
        return ms;
    }
    // Speed-trainer rate applied to the playback clock (1 = compiled speed); changes seamlessly.
    private double _clockRate = 1.0;
    private double TrainerRate(int loopsDone)
    {
        var s = LoopSettings;
        if (!s.Trainer) return 1.0;
        var pct = Math.Min(Math.Max(s.TrainerFromPercent, 10) + Math.Max(0, s.TrainerStepPercent) * loopsDone, Math.Max(s.TrainerToPercent, 10));
        return Math.Clamp(pct / 100.0, 0.1, 2.0);
    }

    private bool[]? _audible;
    private int _masterVolume = 100;

    // Last channel volume (CC7, after master) sent per MIDI channel; GM power-on default is 100.
    private readonly int[] _channelVolume = Enumerable.Repeat(100, 16).ToArray();

    /// <summary>The master knob scales everything: channel volume (CC7) of every track, the metronome and note preview.</summary>
    private int MasterScaled(int status, int data1, int data2)
    {
        if ((status & 0xF0) != 0xB0 || data1 != 7) return data2;
        var scaled = Math.Clamp((int)Math.Round(data2 * Volatile.Read(ref _masterVolume) / 100.0), 0, 127);
        Volatile.Write(ref _channelVolume[status & 0x0F], scaled);
        return scaled;
    }

    /// <summary>
    /// Metronome velocity. The click shares the drum channel, so it is lifted by that channel's current
    /// CC7 (the drum track's fader must not quieten the click), then scaled by master like everything else.
    /// </summary>
    private int MetronomeVelocity(int velocity, int channel)
    {
        var master = Volatile.Read(ref _masterVolume) / 100.0;
        var channelLevel = Math.Max(8, Volatile.Read(ref _channelVolume[channel & 0x0F])) / 127.0;
        return Math.Clamp((int)Math.Round(velocity * master / channelLevel), 1, 127);
    }

    /// <summary>Master volume for all tracks, 0–100 %, applied live by resending channel volume.</summary>
    public void SetMasterVolume(SongProject project, int percent)
    {
        Volatile.Write(ref _masterVolume, Math.Clamp(percent, 0, 100));
        RefreshMix(project);
    }

    private static bool[] AudibleMask(SongProject project)
    {
        var anySolo = project.Tracks.Any(t => t.Solo);
        return project.Tracks.Select(t => !t.Mute && (!anySolo || t.Solo)).ToArray();
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
        for (var t = 0; t < mask.Length && t < previous.Length; t++)
        {
            if (mask[t] || !previous[t] || !channelsOf.TryGetValue(t, out var targets)) continue;
            foreach (var target in targets)
            {
                // Don't cut a channel another audible track is still using.
                var shared = channelsOf.Any(pair => pair.Key != t && pair.Key < mask.Length && mask[pair.Key] && pair.Value.Contains(target));
                if (shared) continue;
                lock (_outputGate) _output.Send(target.Device, 0xB0 | target.Channel, 123, 0);
            }
        }
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

    /// <summary>
    /// Recompiles traversal after the currently playing bar using the live arrangement. The existing
    /// bar and its queued releases remain on the old timeline; the scheduler swaps at that bar's end.
    /// </summary>
    public void RefreshArrangement(SongProject project, int[] baseToCurrentBar, int? continueAtBar = null)
    {
        var timeline = _timeline;
        var options = _options;
        if (timeline is null || options is null || !_running || baseToCurrentBar.Length == 0) return;

        var position = Playhead();
        if (timeline.Bars.Count == 0) return;
        var activeBar = timeline.BarAt(position.ElapsedMs);
        if (activeBar.EndMs <= position.ElapsedMs) return;

        lock (_gate)
        {
            if (!_running || !ReferenceEquals(_timeline, timeline)) return;
            _arrangementRefreshBoundaryMs = activeBar.EndMs;
            _arrangementRefreshRequested = true;
            _pendingArrangementRefresh = null;
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
            var order = PlaybackOrder.Build(project, liveOptions);
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
                : MidiTimelineBuilder.Build(project, liveOptions, futureOrder);
            var revised = SpliceArrangementFuture(timeline, future, activeBar.EndMs, baseToCurrentBar);
            lock (_gate)
            {
                if (!_running || !ReferenceEquals(_timeline, timeline)) return;
                _pendingArrangementRefresh = new ArrangementRefresh(revised, activeBar.EndMs, project);
            }
        }
        catch
        {
            lock (_gate)
            {
                if (Math.Abs(_arrangementRefreshBoundaryMs - activeBar.EndMs) < 0.001)
                {
                    _arrangementRefreshRequested = false;
                    _pendingArrangementRefresh = null;
                }
            }
        }
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

    public PlaybackPosition Playhead()
    {
        var timeline = _timeline;
        if (timeline is null) return new PlaybackPosition();
        return PlayheadMapper.Map(timeline, Volatile.Read(ref _currentStreamMs), CurrentBar, CurrentCell);
    }

    /// <summary>
    /// Song position being heard: the scheduler position minus the output latency. The latency is wall-clock time and
    /// the timeline is already compiled at the playback speed, so it converts at the clock rate (speed trainer) only.
    /// </summary>
    internal static double AudibleStreamMs(double schedulerMs, double latencyWallMs, double clockRate) =>
        schedulerMs - Math.Max(0, latencyWallMs) * Math.Clamp(clockRate, 0.1, 4);

    /// <summary>The playhead for drawing: the live clock now (not the last 16 ms report), at the audible position.</summary>
    public PlaybackPosition AudiblePlayhead()
    {
        var timeline = _timeline;
        if (timeline is null) return new PlaybackPosition();
        double ms;
        lock (_gate)
        {
            if (!_running || _paused) return Playhead();
            var latency = _output is Audio.RoutedMidiOutput routed ? routed.AudibleLatencyMs : 0;
            ms = AudibleStreamMs(_anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate, latency, _clockRate);
        }
        // Never run ahead of what the scheduler has reached (a stalled scheduler) nor back before the start.
        ms = Math.Max(0, Math.Min(ms, Volatile.Read(ref _currentStreamMs) + MaxSleepMs + PositionIntervalMs));
        return PlayheadMapper.Map(timeline, ms, CurrentBar, CurrentCell);
    }

    // ---------- scheduler ----------

    private void SchedulerLoop(ScoreTimeline timeline, int generation, double startMs)
    {
        var events = timeline.Events;
        var opts = _options!;
        var (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
        var loopRangeVersion = Volatile.Read(ref _loopRangeVersion);
        _loopsDone = 0;
        LoopCompleted?.Invoke(this, 0);
        lock (_gate)
        {
            // Re-anchor so a trainer rate change never jumps the position.
            _anchorMs += _clock.Elapsed.TotalMilliseconds * _clockRate;
            _clock.Restart();
            _clockRate = opts.Loop ? TrainerRate(0) : 1.0;
        }

        // Start clean: kill anything left from a previous run, then restore deterministic channel state.
        ResetAndSetup(timeline, generation);

        // The reset above waits for every queued device reset (Stop() of the previous run queues one
        // per jump while playing, and a driver can take hundreds of ms to flush sounding notes). The
        // clock started in Start(), so without this the position is already past the target when the
        // loop begins and the first note(s) at the target read as stale (> MaxLateMs late) and are dropped.
        lock (_gate)
        {
            if (!_paused && generation == Volatile.Read(ref _generation))
            {
                _anchorMs = startMs;
                _clock.Restart();
            }
        }

        var index = FirstIndexAtOrAfter(events, startMs);
        List<double>? sectionMs = null;
        var nextSection = 0;
        var lastCountInMs = startMs;
        var lastReportMs = double.NegativeInfinity;
        var lastDispatchMs = startMs;

        while (_running && generation == Volatile.Read(ref _generation))
        {
            double ms;
            lock (_gate)
            {
                ms = _paused ? _anchorMs : _anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate;
            }

            var currentLoopVersion = Volatile.Read(ref _loopRangeVersion);
            if (currentLoopVersion != loopRangeVersion)
            {
                loopRangeVersion = currentLoopVersion;
                (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
            }

            var waitForArrangement = false;
            var timelineRevised = false;
            lock (_gate)
            {
                if (_arrangementRefreshRequested && ms + 0.001 >= _arrangementRefreshBoundaryMs)
                {
                    if (_pendingArrangementRefresh is not { } refresh)
                    {
                        waitForArrangement = true;
                    }
                    else
                    {
                        timeline = refresh.Timeline;
                        Volatile.Write(ref _timeline, timeline);
                        _project = refresh.Project;
                        events = timeline.Events;
                        index = FirstIndexAtOrAfter(events, refresh.BoundaryMs);
                        lastDispatchMs = ms;
                        _pendingArrangementRefresh = null;
                        _arrangementRefreshRequested = false;
                        (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
                        timelineRevised = true;
                    }
                }
            }
            if (waitForArrangement)
            {
                Thread.Sleep(1);
                continue;
            }
            if (timelineRevised) { TimelineRevised?.Invoke(timeline); sectionMs = null; }

            // A paused transport retains its timeline position but must not dispatch upcoming notes.
            // This also makes seeking while paused safe: the replacement scheduler starts paused.
            if (_paused)
            {
                if (ms - lastReportMs >= PositionIntervalMs)
                {
                    lastReportMs = ms;
                    ReportPosition(ms, timeline);
                }
                Thread.Sleep((int)PositionIntervalMs);
                continue;
            }

            if (_rearm)
            {
                _rearm = false;
                RearmChannelSetup(timeline, generation);
                // Then whatever changed since the start (a new program, volume or pan set in the song) as of now, so a
                // synth created mid-song sounds like the rest, not like its defaults.
                if (ms > startMs) RestoreChannelStateAt(timeline, startMs, ms);
            }

            var loopEnabled = loopAvailable && opts.Loop;
            if (loopEnabled && ms >= loopEndMs)
            {
                var settings = LoopSettings;
                _loopsDone++;
                LoopCompleted?.Invoke(this, _loopsDone);
                if (settings.Count > 0 && _loopsDone >= settings.Count)
                {
                    // All requested loops played: finish at the loop end.
                    _running = false;
                    _timer.Set(false);
                    PanicAsync();
                    ReportPosition(loopEndMs, timeline);
                    _onFinished?.Invoke();
                    break;
                }
                var length = loopEndMs - loopStartMs;
                var overshoot = ms - loopEndMs;
                var wrapped = loopStartMs + (length > 0 ? overshoot % length : 0);
                // Seamless wrap: release sounding notes and restore the controller state of the loop
                // start (a few messages) instead of a full device reset, so there is no gap.
                ReleaseForLoopWrap(timeline);
                RestoreChannelStateAt(timeline, startMs, wrapped);
                if (settings.CountInEachLoop) PlayLoopCountIn(timeline, loopStartMs, generation);
                lock (_gate)
                {
                    _anchorMs = settings.CountInEachLoop ? loopStartMs : wrapped;
                    _clock.Restart();
                    _clockRate = TrainerRate(_loopsDone);
                    ms = _anchorMs;
                }
                // Keep events at the loop's first instant in the dispatch window. If the scheduler
                // wakes a few milliseconds after the boundary, indexing from the wrapped clock would
                // skip a note-on exactly at loopStart and create an audible missing beat.
                index = FirstIndexAtOrAfter(events, loopStartMs);
                lastDispatchMs = ms;
                // The position clock moved backwards. Start a new reporting interval from the
                // wrapped timestamp or the stale pre-wrap value suppresses every UI update until
                // the playhead reaches the previous iteration's time again.
                lastReportMs = ms;
                ReportPosition(ms, timeline);
            }

            // Section count-in: when playback reaches a section start, hold the music for the count-in.
            if (CountInEachSection && !_paused)
            {
                sectionMs ??= SectionStartMs(timeline, Volatile.Read(ref _sectionStarts));
                if (ms < lastCountInMs - 1)
                {
                    // Jumped back (loop wrap / skip): arm the sections ahead of the new position again.
                    lastCountInMs = ms;
                    nextSection = sectionMs.FindIndex(t => t > ms + 1);
                    if (nextSection < 0) nextSection = sectionMs.Count;
                }
                while (nextSection < sectionMs.Count && sectionMs[nextSection] <= lastCountInMs + 1) nextSection++;
                if (nextSection < sectionMs.Count && ms >= sectionMs[nextSection] && sectionMs[nextSection] > startMs + 1)
                {
                    var at = sectionMs[nextSection++];
                    lastCountInMs = at;
                    PlayLoopCountIn(timeline, at, generation);
                    lock (_gate)
                    {
                        _anchorMs = at;
                        _clock.Restart();
                        ms = _anchorMs;
                    }
                    index = FirstIndexAtOrAfter(events, ms);
                    lastDispatchMs = ms;
                }
            }
            else sectionMs = null;

            var skips = SkipMs(timeline);
            foreach (var (skipStart, skipEnd) in skips)
            {
                if (ms < skipStart || ms >= skipEnd) continue;
                // Jump over the skipped area exactly like a seamless loop wrap.
                ReleaseForLoopWrap(timeline);
                RestoreChannelStateAt(timeline, startMs, skipEnd);
                lock (_gate)
                {
                    _anchorMs = skipEnd;
                    _clock.Restart();
                    ms = _anchorMs;
                }
                index = FirstIndexAtOrAfter(events, ms);
                lastDispatchMs = ms;
                ReportPosition(ms, timeline);
                break;
            }

            // A backlog this big means the thread was suspended; jump to now instead of burst-sending.
            if (ms - lastDispatchMs > StaleResyncMs && index < events.Count && events[index].TimeMs < ms - StaleResyncMs)
            {
                ResetAndSetup(timeline, generation);
                RestoreChannelStateAt(timeline, startMs, ms);
                index = FirstIndexAtOrAfter(events, ms);
                lastDispatchMs = ms;
            }

            var horizon = ms + DispatchLeadMs;
            // Never send notes past the loop end: they belong to the bar after the loop.
            if (loopEnabled) horizon = Math.Min(horizon, loopEndMs - 0.001);
            if (sectionMs is not null && nextSection < sectionMs.Count && sectionMs[nextSection] > ms)
                horizon = Math.Min(horizon, sectionMs[nextSection] - 0.001); // hold notes of the next section until after its count-in
            foreach (var (skipStart, skipEnd) in skips)
                if (skipStart > ms) { horizon = Math.Min(horizon, skipStart - 0.001); break; }
            lock (_gate)
            {
                if (_arrangementRefreshRequested && ms < _arrangementRefreshBoundaryMs)
                    horizon = Math.Min(horizon, _arrangementRefreshBoundaryMs - 0.001);
            }
            var staleStateDropped = false;
            while (index < events.Count && events[index].TimeMs <= horizon)
            {
                var e = events[index++];
                if (e.IsSetup) continue;                             // sent by SendChannelSetup
                var stale = e.TimeMs < ms - MaxLateMs;
                if (stale && IsEssentialReleaseOrReset(e))
                {
                    if (staleStateDropped)
                    {
                        RestoreChannelStateAt(timeline, startMs, ms);
                        staleStateDropped = false;
                    }
                    Dispatch(e, ms, startMs);
                    continue;
                }
                if (stale && IsChannelStateMessage(e))
                {
                    // Do not play an old bend/controller ramp after a stall. Before the next live
                    // event, restore the last state that should be active now (including a missed
                    // pitch-wheel centre reset), rather than leaving the device in the last sent state.
                    staleStateDropped = true;
                    continue;
                }
                if (stale) continue;                                 // old attacks are skipped, releases are essential
                if (staleStateDropped)
                {
                    RestoreChannelStateAt(timeline, startMs, ms);
                    staleStateDropped = false;
                }
                Dispatch(e, ms, startMs);
            }
            if (staleStateDropped) RestoreChannelStateAt(timeline, startMs, ms);
            lastDispatchMs = ms;

            if (!loopEnabled && ms >= timeline.TotalMs)
            {
                _running = false;
                _timer.Set(false);
                PanicAsync();
                ReportPosition(ms, timeline);
                _onFinished?.Invoke();
                break;
            }

            if (ms - lastReportMs >= PositionIntervalMs)
            {
                lastReportMs = ms;
                ReportPosition(ms, timeline);
            }

            var nextMs = index < events.Count ? events[index].TimeMs : double.MaxValue;
            if (loopEnabled) nextMs = Math.Min(nextMs, loopEndMs + DispatchLeadMs); // wake exactly at the wrap
            SleepUntil(ms, nextMs, _clockRate);
        }
    }

    private void ReleaseForLoopWrap(ScoreTimeline timeline)
    {
        _activeMetronomeNotes.Clear();
        var channels = new HashSet<(int Device, int Channel)>();
        foreach (var e in timeline.ChannelSetup) channels.Add((e.DeviceId, e.Status & 0x0F));
        lock (_outputGate)
            foreach (var (device, channel) in channels)
            {
                _output.Send(device, 0xB0 | channel, 64, 0);  // sustain off
                _output.Send(device, 0xB0 | channel, 123, 0); // all notes off
            }
    }

    // One bar of clicks before the loop restarts (accent on the first beat), at the loop's tempo.
    private void PlayLoopCountIn(ScoreTimeline timeline, double loopStartMs, int generation)
    {
        var bar = timeline.BarAt(loopStartMs);
        // Quarter-note length on the timeline clock, then in wall time under the trainer rate.
        var barMs = Math.Max(1, bar.EndMs - bar.StartMs);
        var quarter = 60000.0 / Math.Max(20, bar.Tempo) / Math.Max(0.1, Speed);
        if (Math.Round(barMs / quarter) is < 1 or > 12) quarter = 60000.0 / Math.Max(20, bar.Tempo);
        var beats = Math.Clamp((int)Math.Round(barMs / quarter), 1, 12);
        var beatMs = quarter / Math.Max(0.1, _clockRate);
        var device = timeline.ChannelSetup.Count > 0 ? timeline.ChannelSetup[0].DeviceId : 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var beat = 0; beat < beats && _running && generation == Volatile.Read(ref _generation); beat++)
        {
            var note = beat == 0
                ? (CountInAccentNote >= 0 ? CountInAccentNote : Volatile.Read(ref _metronomeAccentNote))
                : (CountInClickNote >= 0 ? CountInClickNote : Volatile.Read(ref _metronomeClickNote));
            var velocity = MetronomeVelocity(Math.Clamp((int)Math.Round(127 * CountInVolume / 100.0), 1, 127), 9);
            lock (_outputGate)
            {
                _output.Send(device, 0x99, note, velocity);
                if (MetronomeBoost) foreach (var layer in BoostLayers) if (layer != note) _output.Send(device, 0x99, layer, velocity);
            }
            var until = (beat + 1) * beatMs;
            while (clock.Elapsed.TotalMilliseconds < until - 1) Thread.Sleep(1);
        }
    }

    private void Dispatch(ScoreEvent e, double streamMs, double startMs)
    {
        var status = e.Status;
        var data1 = e.Data1;
        var data2 = e.Data2;
        // Live mute/solo: every track is compiled, silenced tracks simply do not start notes.
        if (e.IsNoteOn && e.TrackIndex >= 0 && Volatile.Read(ref _audible) is { } audible &&
            e.TrackIndex < audible.Length && !audible[e.TrackIndex]) return;
        if (e.IsMetronome)
        {
            if (e.IsNoteOff)
            {
                if (!_activeMetronomeNotes.TryRemove(e.MetronomePairId, out data1)) return;
                data2 = 0;
            }
            else
            {
                if (!e.IsCountInClick && Volatile.Read(ref _metronomeEnabled) == 0) return;
                var subdivisions = Volatile.Read(ref _metronomeSubdivision);
                if (e.MetronomeTick % (12 / subdivisions) != 0) return;
                var note = e.IsMetronomeAccent
                    ? (e.IsCountInClick && CountInAccentNote >= 0 ? CountInAccentNote : Volatile.Read(ref _metronomeAccentNote))
                    : (e.IsCountInClick && CountInClickNote >= 0 ? CountInClickNote : Volatile.Read(ref _metronomeClickNote));
                var relativeVolume = e.IsMetronomeAccent
                    ? Volatile.Read(ref _metronomeAccentVolume)
                    : Volatile.Read(ref _metronomeClickVolume);
                // Full scale: at 100% the click hits maximum velocity at full channel level, above the song.
                var level = e.IsCountInClick ? CountInVolume : Volatile.Read(ref _metronomeVolume);
                data2 = (int)Math.Round(127 * level / 100.0 * relativeVolume / 100.0);
                if (data2 <= 0) return;
                data2 = MetronomeVelocity(Math.Clamp(data2, 1, 127), status);
                data1 = note;
                _activeMetronomeNotes[e.MetronomePairId] = note;
            }
        }
        data2 = MasterScaled(status, data1, data2);
        lock (_outputGate)
        {
            _output.Send(e.DeviceId, status, data1, data2);
            if (e.IsMetronome && MetronomeBoost)
                foreach (var layer in BoostLayers)
                    if (layer != data1) _output.Send(e.DeviceId, status, layer, data2);
        }
        if (!_diagnostics) return;
        var wall = _diagnosticClock.Elapsed.TotalMilliseconds - _playbackStartWallMs;
        var latency = wall - (streamMs - startMs);
        var record = new DispatchRecord(streamMs, latency, e.TrackIndex, status, data1, data2, e.DeviceId);
        lock (_logGate) { if (_dispatchLog.Count < InputLimits.MaxDiagnosticRecords) _dispatchLog.Add(record); }
    }

    private void SendChannelSetupCore(ScoreTimeline timeline)
    {
        foreach (var e in timeline.ChannelSetup)
            _output.Send(e.DeviceId, e.Status, e.Data1, MasterScaled(e.Status, e.Data1, e.Data2));
    }

    /// <summary>
    /// Reapply only the latest persistent channel messages at <paramref name="timeMs"/>. This avoids
    /// replaying a stale modulation ramp while ensuring a skipped reset cannot leave MIDI state stuck.
    /// </summary>
    internal void RestoreChannelStateAt(ScoreTimeline timeline, double startMs, double timeMs)
    {
        var latest = new Dictionary<(int Device, int Status, int Data1), ScoreEvent>();
        foreach (var e in timeline.ChannelSetup)
            if (IsChannelStateMessage(e)) latest[ChannelStateKey(e)] = e;

        var events = timeline.Events;
        for (var i = FirstIndexAtOrAfter(events, startMs); i < events.Count && events[i].TimeMs <= timeMs; i++)
        {
            var e = events[i];
            if (!e.IsSetup && IsChannelStateMessage(e)) latest[ChannelStateKey(e)] = e;
        }

        lock (_outputGate)
            foreach (var e in latest.Values.OrderBy(e => e.TimeMs))
                _output.Send(e.DeviceId, e.Status, e.Data1, MasterScaled(e.Status, e.Data1, e.Data2));
    }

    private static bool IsChannelStateMessage(ScoreEvent e)
    {
        var kind = e.Status & 0xF0;
        return kind switch
        {
            0xA0 or 0xC0 or 0xD0 or 0xE0 => true,
            0xB0 => e.Data1 < 120, // channel-mode messages (all notes off/reset) are not state snapshots
            _ => false
        };
    }

    internal static bool IsEssentialReleaseOrReset(ScoreEvent e)
    {
        if (e.IsNoteOff) return true;
        var kind = e.Status & 0xF0;
        if (kind == 0xE0 && e.Data1 == 0 && e.Data2 == 64) return true; // pitch-wheel centre
        if (kind == 0xB0 && e.Data1 == 1 && e.Data2 == 0) return true; // modulation-wheel neutral
        return kind == 0xB0 && e.Data1 is 120 or 121 or 123;
    }

    private static (int Device, int Status, int Data1) ChannelStateKey(ScoreEvent e)
    {
        var kind = e.Status & 0xF0;
        return (e.DeviceId, e.Status, kind is 0xA0 or 0xB0 ? e.Data1 : -1);
    }

    private void ReportPosition(double ms, ScoreTimeline timeline)
    {
        Volatile.Write(ref _currentStreamMs, ms);
        var pos = PlayheadMapper.Map(timeline, ms, CurrentBar, CurrentCell);
        CurrentBar = pos.Bar;
        CurrentCell = pos.Cell;
        _onPosition?.Invoke(pos);
    }

    /// <summary>Sleeps until shortly before the next event, or a short tick so the UI keeps updating.</summary>
    private static void SleepUntil(double nowMs, double nextEventMs, double rate = 1.0)
    {
        var wait = ((nextEventMs - DispatchLeadMs) - nowMs) / Math.Max(0.1, rate);
        if (wait <= 0.2) { Thread.Yield(); return; }
        var sleep = (int)Math.Min(Math.Ceiling(wait), MaxSleepMs);
        Thread.Sleep(Math.Max(1, sleep));
    }

    private static int FirstIndexAtOrAfter(List<ScoreEvent> events, double ms)
    {
        var lo = 0;
        var hi = events.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (events[mid].TimeMs < ms) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private (double startMs, double endMs, bool available) ComputeLoopBounds(ScoreTimeline timeline, PlaybackOptions opts)
    {
        var project = _project;
        if (project is null) return (0, 0, false);
        return PlaybackOrder.LoopBounds(timeline, project, opts);
    }

    /// <summary>
    /// RT-10 / D10: the 1 ms Windows timer resolution (shared, reference-counted <see cref="Audio.Contracts.NativeTimer"/>) is held only
    /// while this engine plays: raised on start and resume, released on pause, stop and dispose (an idle document costs no battery).
    /// </summary>
    private readonly Audio.Contracts.NativeTimer.Hold _timer = new();

    /// <summary>Self-test: this engine currently holds the 1 ms timer resolution.</summary>
    internal bool HoldsTimerResolution => _timer.IsHeld;

    // ---------- preview ----------

    public async Task PreviewNoteAsync(int deviceId, int channel = 0, int program = 24, int note = 64, int ms = 350)
    {
        lock (_outputGate)
        {
            _output.Send(deviceId, 0xC0 | (channel & 0x0F), Math.Clamp(program, 0, 127), 0);
            var velocity = Math.Clamp((int)Math.Round(100 * Volatile.Read(ref _masterVolume) / 100.0), 1, 127);
            _output.Send(deviceId, 0x90 | (channel & 0x0F), Math.Clamp(note, 0, 127), velocity);
        }
        try { await Task.Delay(Math.Clamp(ms, 60, 2000)); }
        finally { lock (_outputGate) _output.Send(deviceId, 0x80 | (channel & 0x0F), Math.Clamp(note, 0, 127), 0); }
    }

    /// <summary>MIDI input monitoring: one message straight out (see <see cref="IMidiOutput.SendLive"/>).</summary>
    public void SendLive(int deviceId, int status, int data1, int data2)
    {
        lock (_outputGate) _output.SendLive(deviceId, status, data1, data2);
    }

    public void PreviewNote(int deviceId, int channel, int program, int note, int ms = 300) =>
        _ = PreviewNoteAsync(deviceId, channel, program, note, ms);

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
