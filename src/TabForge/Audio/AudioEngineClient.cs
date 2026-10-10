using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

/// <summary>One line of the MIDI log: song time, the message, and which processor stage logged it.</summary>
public readonly record struct MidiLogLine(float Time, byte Status, byte Data1, byte Data2, int Stage);

/// <summary>What happened to one plug-in's state when states were collected before a save.</summary>
public enum PluginStateOutcome { Captured, Unchanged, TimedOut, Failed, TooLarge }

public sealed record PluginStateResult(TrackModel Track, PluginSlot Plugin, PluginStateOutcome Outcome);

/// <summary>Per-plug-in result of <see cref="AudioEngineClient.CollectStates"/>.</summary>
public sealed class StateCollection
{
    public List<PluginStateResult> Results { get; } = new();
    /// <summary>Plug-ins whose current state could not be read (timed out, failed, too large): the song keeps their previous settings.</summary>
    public int IncompleteCount => Results.Count(r => r.Outcome is PluginStateOutcome.TimedOut or PluginStateOutcome.Failed or PluginStateOutcome.TooLarge);
    public bool Complete => IncompleteCount == 0;
    /// <summary>The save warning, or null when every state was read.</summary>
    public string? Warning => Complete ? null
        : $"{IncompleteCount} plug-in state{(IncompleteCount == 1 ? "" : "s")} could not be read ({string.Join(", ", Results.Where(r => r.Outcome is PluginStateOutcome.TimedOut or PluginStateOutcome.Failed or PluginStateOutcome.TooLarge).Select(r => $"{(r.Plugin.Name.Length > 0 ? r.Plugin.Name : Path.GetFileNameWithoutExtension(r.Plugin.Path))}: {Describe(r.Outcome)}").Distinct().Take(4))}); the saved song has their previous settings";

    private static string Describe(PluginStateOutcome outcome) => outcome switch
    {
        PluginStateOutcome.TimedOut => "no answer",
        PluginStateOutcome.TooLarge => $"larger than {PluginStateLimits.MaxRawBytes / 1048576} MiB",
        _ => "failed",
    };
}

/// <summary>
/// TabForge's only link to the audio engine process (TabForge.exe --audio-engine), which hosts plug-ins and the
/// audio device. The engine starts only when some track plays through plug-ins; it has one owner (the active document) and
/// stays warm for <see cref="WarmIdle"/> after the last engine track goes away (R-10), so a song without plug-ins runs as before. If the engine dies (a plug-in crashed), the plug-in that was
/// running is switched off, the engine restarts, and playback carries on; TabForge itself is never affected.
/// </summary>
public sealed partial class AudioEngineClient : IDisposable
{
    public static AudioEngineClient Instance { get; } = new();

    /// <summary>How tracks sound (route, auto GM) for this engine: the settings applier writes it, every routing decision reads it. One per client, never a static.</summary>
    public TabForge.Models.MixerOptions Mixer { get; } = new();

    /// <summary>The MIDI input devices, shared by recording and Keyboard mode (each takes a client; the devices open while one holds them).</summary>
    public MidiInputHub MidiInput { get; } = new(new MidiInputCapture());

    public AudioEngineClient() { MediaAccess.Changed += OnMediaApprovalChanged; MediaAccess.Resolved += OnMediaResolved; }

    /// <summary>A background classification finished (worker thread): clips that waited for it are judged again on the UI thread. Nothing to do before the first Sync (no slots, no UI context yet).</summary>
    private void OnMediaResolved() { if (_ui is not null) RaiseOnUi(RefreshClips); }

    /// <summary>An approval was given or revoked (any document): the clips of every live slot are judged again with their own document's context.</summary>
    private void OnMediaApprovalChanged() => RaiseOnUi(RefreshClips);

    internal void RefreshClips()
    {
        if (Rendering) return;   // like Sync: nothing is sent to the engine mid-render (the end of the render re-judges the clips)
        foreach (var (slot, state) in _audioContext.ToList())
        {
            if (state.Media.IsClosed || !_slots.ContainsValue(slot) || _parkedSince.ContainsKey(slot)) continue;   // a closed document asks for nothing; gone, or parked (no clips): its owner's next sync judges them
            SyncAudio(state.Track, slot, state.Mix, state.Media);
        }
    }

    private readonly object _gate = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private SharedBlock? _shared;
    private bool _stopping;
    private readonly List<DateTime> _crashes = new();
    private readonly Dictionary<TrackModel, int> _slots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, string> _sentChains = new();
    /// <summary>Per slot: the plug-in ids of the chain last sent (the engine's chain order), so a state reply's index names its plug-in.</summary>
    private readonly Dictionary<int, string[]> _sentChainIds = new();
    private readonly Dictionary<int, StateRequest> _stateRequests = new();   // by request id
    private EngineConfig? _config;
    private SynchronizationContext? _ui;

    public bool IsRunning { get; private set; }
    /// <summary>Delay from a message's time stamp to it being heard; Windows MIDI is delayed by this too.</summary>
    public long LatencyTicks => _shared?.LatencyTicks ?? 0;
    public double CpuLoad => _shared?.CpuLoad ?? 0;
    /// <summary>The tuner window is open: the engine detects the pitch of the armed input (needs an armed track, the input is only open then).</summary>
    public bool TunerOn { set { if (_shared is { } shared) shared.TunerOn = value; } }
    /// <summary>The engine's latest tuner reading (Hz 0: silence / no pitch) and a counter that changes with each one.</summary>
    public (float Hz, float Clarity, int Seq) TunerReading => _shared?.Tuner ?? default;
    /// <summary>A track's current output level (0..1), for meters; 0 when it does not play through the engine.</summary>
    /// <summary>An armed track's input level (before its effects), 0..1+.</summary>
    public float InputPeakOf(TrackModel track) => _shared is { } shared && _slots.TryGetValue(track, out var slot) ? shared.InputPeak(slot) : 0;
    public float PeakOf(TrackModel track) => _shared is { } shared && _slots.TryGetValue(track, out var slot) ? shared.Peak(slot) : 0;
    public string? DeviceDescription { get; private set; }
    /// <summary>The running output: driver (WASAPI, ASIO...), device, sample rate, buffer and total output latency.</summary>
    public (string Driver, string Device, int Rate, int Buffer, int LatencyMs)? Output { get; private set; }
    /// <summary>The ASIO driver's channel names (as it reports them), from the last start; null until known.</summary>
    public (string[] Inputs, string[] Outputs)? AsioChannels { get; private set; }

    /// <summary>A plug-in crashed the engine and was switched off (its path; raised on the UI thread).</summary>
    public event Action<string>? PluginCrashed;

    /// <summary>True only while <see cref="PluginCrashed"/> is raised for one plug-in that crashed in its slot: the engine kept running and every other chain is still loaded, so only the songs that use that plug-in need a sync. False when the whole engine process ended.</summary>
    internal bool CrashLeftEngineRunning { get; private set; }
    /// <summary>
    /// A plug-in call is slow but within its limit (path, what it is doing, seconds so far; raised on the UI thread): show "still
    /// loading". It is not a crash; only if it passes its limit does the engine end and the plug-in get switched off.
    /// </summary>
    public event Action<string, PluginCallKind, int>? PluginSlow;
    /// <summary>A plug-in could not be loaded (path, reason).</summary>
    public event Action<string, string>? PluginFailed;
    /// <summary>
    /// RT-02: a plug-in produced non-finite audio (NaN / infinity) and the engine now skips it until it is switched on again (path; index -1:
    /// the track's own sound, whose block was silenced). Raised on the UI thread, once per occurrence.
    /// </summary>
    public event Action<string, int>? PluginMisbehaved;
    /// <summary>The audio device could not be opened or stopped working.</summary>
    public event Action<string>? DeviceError;
    /// <summary>A plug-in reported a parameter edit: the document (or song) that owns its slot, which has unsaved changes; null when no owner is known.</summary>
    public event Action<object?>? PluginEdited;
    /// <summary>The engine started or stopped.</summary>
    public event Action? StatusChanged;
    /// <summary>A track's chain (plug-ins / General MIDI synth) is ready in the engine: fresh synths still need their program changes.</summary>
    public event Action? ChainLoaded;
    /// <summary>The engine's per-request acknowledgement (slot, generation, every plug-in's outcome); raised just before <see cref="ChainLoaded"/>.</summary>
    public event Action<ChainAck>? ChainAcknowledged;

    private readonly Dictionary<int, int> _chainRequests = new();
    /// <summary>How many LoadChain requests were sent for a slot since the engine started (the engine counts the same way).</summary>
    public int ChainGenerationOf(int slot) => _chainRequests.GetValueOrDefault(slot);
    private readonly Dictionary<int, ChainAck> _lastAcks = new();
    /// <summary>Latest acknowledgement per slot (UI thread), so a waiter can start from what already arrived.</summary>
    public IReadOnlyCollection<ChainAck> LastAcknowledgements => _lastAcks.Values.ToList();
    /// <summary>Every slot that has a chain request in flight or done, with the generation of its latest request.</summary>
    public IReadOnlyList<(int Slot, int Generation)> RequestedChains => _chainRequests.Where(kv => _slots.ContainsValue(kv.Key) && !_parkedSince.ContainsKey(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();   // parked chains of other documents are not the owner's

    /// <summary>Plug-ins switched off after crashing (full paths); owned by the app settings.</summary>
    public Func<ICollection<string>>? Quarantine { get; set; }

    /// <summary>
    /// Brings the engine in line with the tracks that need it: starts / stops it, loads changed chains, removes
    /// tracks that no longer play through plug-ins. Call on the UI thread after any chain, source or song change.
    /// </summary>
    /// <param name="mix">The track's effective level and pan (0..127; level 0 when muted); default: its own settings.</param>
    /// <summary>True while File > Render runs: <see cref="Sync"/> does nothing, so the engine is not stopped or reloaded mid-render.</summary>
    public bool Rendering
    {
        get => _rendering;
        set
        {
            var was = _rendering;
            _rendering = value;
            if (was && !value) RaiseOnUi(RefreshClips);   // approvals that changed during the render are applied to the live slots now
        }
    }
    private volatile bool _rendering;

    /// <summary>The document (or song) that made the latest <see cref="Sync"/>; null before any. Engine events that re-sync act only in its window.</summary>
    public object? CurrentOwner { get; private set; }

    /// <summary>
    /// Multi-tab playback: true while a document plays (or is paused). Its tracks keep their own live slots when another document
    /// becomes the owner; they are parked only once it has stopped. Null: every other document is parked (the R-10 behaviour).
    /// </summary>
    public Func<object, bool>? IsOwnerPlaying { get; set; }

    // ---- R-10: one owner, warm engine (one engine process is shared by all documents and kept warm while idle) ----

    /// <summary>How long the engine stays running without engine tracks, and how long another document's chains stay parked.</summary>
    public const int DefaultWarmIdleMinutes = 5;
    /// <summary>Zero or less: the old behaviour (a Sync without engine tracks stops the engine at once; nothing is parked).</summary>
    public TimeSpan WarmIdle { get; set; } = TimeSpan.FromMinutes(DefaultWarmIdleMinutes);
    private static readonly object TracksOnlyOwner = new();
    /// <summary>Document that each slot's chain belongs to (the slot key's document part; tracks are keyed by reference).</summary>
    private readonly Dictionary<int, object> _slotOwners = new();
    /// <summary>Song owner ids (the engine keeps one transport per id) and the last position sent per id; they survive an engine restart.</summary>
    private readonly SongOwnerIds _songIds = new();
    /// <summary>Slots of documents that are not the owner: loaded, silent, removed once parked longer than <see cref="WarmIdle"/>.</summary>
    private readonly Dictionary<int, DateTime> _parkedSince = new();
    private DateTime? _idleSince;
    private Timer? _warmTimer;
    internal Func<DateTime> WarmClock { get; set; } = () => DateTime.UtcNow;

    private string _sentGraph = "";
    private static float? _windowsPathOffset;
    private static bool _measuringPathOffset;
    private int? _pathOffsetSentTo;
    private (int Pid, bool On) _limiterSent;   // live safety limiter state the running engine was last told

    private readonly Dictionary<int, string> _sentWiring = new();

    private readonly Dictionary<int, string> _sentProcessors = new();

    /// <summary>MIDI log lines of a watched track (engine slot, lines); raised on the UI thread.</summary>
    public event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived;

    private readonly Dictionary<int, string> _sentRoutes = new();

    private readonly Dictionary<int, string> _sentBypass = new();
    private readonly Dictionary<int, bool> _sentSynth = new();
    private readonly Dictionary<int, string> _sentGain = new();

    private readonly Dictionary<int, string> _sentAudio = new();

    /// <summary>Level, clips and arm for one track: sent only when they changed (cheap to call often).</summary>
    // What each slot's clips were last judged with: the owning document's context. A later approval change re-judges them (RefreshClips)
    // for every document that has live slots, not only the one that synced last.
    private readonly Dictionary<int, (TrackModel Track, MediaContext Media, (int Volume, int Pan) Mix)> _audioContext = new();

    /// <summary>Settings > Audio &amp; VST > Recording offset (ms), taken from the settings on every <see cref="Sync"/>.</summary>
    public int RecordingOffsetMs { get; set; }

    private Dictionary<int, TrackModel> _recordingTracks = new();

    /// <summary>A take finished: (track, file, song start seconds, length seconds) on the UI thread.</summary>
    public event Action<TrackModel, string, double, double>? Recorded;
    /// <summary>The audio input could not be opened (message).</summary>
    public event Action<string>? InputError;
    /// <summary>A recording lost input to a slow disk: one summary line (UI thread), after the take's Recorded event.</summary>
    public event Action<string>? RecordingLoss;
    /// <summary>The master output tap delivered a chunk (sample rate, first frame, interleaved stereo floats, frame count). Raised on the reader thread; the array belongs to the handler.</summary>
    public event Action<int, long, float[], int>? MasterAudio;

    private readonly object _writeGate = new();
    /// <summary>The block of an engine that ended; disposed at the next Start (under the write lock), never while a writer holds it.</summary>
    private SharedBlock? _retiredShared;

    /// <summary>A plug-in's program or preset state was changed from TabForge (automatic pitch matching measures it again).</summary>
    public event Action<TrackModel, PluginSlot>? PresetChanged;
    /// <summary>The automatic pitch matcher (set by the main window).</summary>
    public AutoPitchMatcher? AutoPitch { get; set; }
    /// <summary>An answer to <see cref="MeasurePitch"/>, on the UI thread.</summary>
    public event Action<PitchResult>? PitchMeasured;
    private int _pitchRequest;

    /// <summary>A plug-in window opened or resized: (engine slot, plug-in index, width, height) on the UI thread.</summary>
    public event Action<int, int, int, int>? EditorSized;
    /// <summary>The user closed a floating plug-in window.</summary>
    public event Action? EditorClosed;
    /// <summary>A plug-in's programs: (engine slot, plug-in index, current, names) on the UI thread.</summary>
    public event Action<int, int, int, IReadOnlyList<string>>? ProgramsReceived;

    /// <summary>One outstanding state request: the engine answers one frame per plug-in.</summary>
    internal sealed class StateRequest
    {
        public int Id { get; init; }
        public int Slot { get; init; }
        /// <summary>The plug-in ids of the engine's chain when the request was made (a reply's index names its plug-in); null: unknown, replies match by position.</summary>
        public string[]? PluginIds { get; init; }
        /// <summary>Chain length the engine reported; -1 until the first frame arrives.</summary>
        public int Expected = -1;
        public int Received;
        public readonly Dictionary<int, (PluginStateStatus Status, string State)> Replies = new();
        public readonly TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private int _nextStateRequest;
    private int _nextChainLoad;

    private bool Start()
    {
        _stopping = false;
        var session = Guid.NewGuid().ToString("N");
        lock (_writeGate) { _retiredShared?.Dispose(); _retiredShared = null; }
        try
        {
            Volatile.Write(ref _shared, SharedBlock.Create(EngineNames.SharedMemory(session)));
            _pipe = new NamedPipeServerStream(EngineNames.Pipe(session), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("TabForge's own path is unknown.");
            _process = Process.Start(new ProcessStartInfo(exe, $"--audio-engine {session} {Environment.ProcessId}")
            {
                UseShellExecute = false, CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("The audio engine did not start.");
            ChildProcessJob.ForThisProcess.Add(_process);   // R-08: ends with TabForge, however TabForge ends
            _process.EnableRaisingEvents = true;
            // R-08: no blocking wait here. Commands sent until the engine connects are queued in order and flushed on connect
            // (Configure first), so Sync returns at once; the engine answers Ready once its device is open.
            lock (_sendGate) _queued = new List<byte[]>();
            _deafKill = false;
            IsRunning = true;
            _config = null; Output = null;
            var process = _process;
            var pipe = _pipe;
            process.Exited += (_, _) => OnEngineEnded(process);
            _ = ConnectAsync(pipe, process);
            RaiseOnUi(() => StatusChanged?.Invoke());
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or AggregateException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Services.Trace.Error(Services.Trace.Engine, "engine: start: " + ex.Message);
            RaiseOnUi(() => DeviceError?.Invoke($"The audio engine could not start: {ex.GetBaseException().Message}"));
            KillQuietly(_process);
            Cleanup();
            return false;
        }
    }

    /// <summary>Waits (off the UI thread) for the engine to connect: then flushes the queued commands and starts reading and pinging.</summary>
    private async Task ConnectAsync(NamedPipeServerStream pipe, Process process)
    {
        var connected = false;
        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromMilliseconds(ConnectTimeoutMs)).ConfigureAwait(false);
            connected = true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Engine, "engine: wait for pipe connection: " + ex.Message); }
        if (!connected)
        {
            if (!ReferenceEquals(pipe, _pipe)) return;   // stopped meanwhile
            KillQuietly(process);                         // R-08: the timed-out child does not linger
            RaiseOnUi(() =>
            {
                if (!ReferenceEquals(pipe, _pipe)) return;
                DeviceError?.Invoke("The audio engine could not start: it did not answer.");
                Cleanup();
                StatusChanged?.Invoke();
            });
            return;
        }
        lock (_sendGate)
        {
            if (!ReferenceEquals(pipe, _pipe)) return;
            try { foreach (var frame in _queued ?? new List<byte[]>()) pipe.Write(frame); pipe.Flush(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Services.Trace.Error(Services.Trace.Engine, "engine: send queued frames: " + ex.Message); /* the engine is gone; OnEngineEnded handles it */ }
            _queued = null;
        }
        Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp());
        new Thread(() => ReadEvents(pipe)) { IsBackground = true, Name = "TabForge engine events" }.Start();
        var timer = new Timer(_ => CheckLiveness(pipe, process), null, 1000, 1000);
        Interlocked.Exchange(ref _liveness, timer)?.Dispose();
    }

    private const int ConnectTimeoutMs = 8000;
    private readonly object _sendGate = new();
    /// <summary>Frames sent while the engine is still connecting (null once connected).</summary>
    private List<byte[]>? _queued;
    private Timer? _liveness;
    private long _lastAlive;
    private int _pingSeq;
    private volatile bool _deafKill, _awaitingReady;

    /// <summary>
    /// R-05, once a second (timer thread): pings the engine, whose main thread answers. No answer for
    /// <see cref="EngineWatchdog.DeafLimitSec"/> while no plug-in call was in progress, no device was being opened and nothing renders:
    /// the engine is deaf; it is killed and the crash path restarts it, quarantining nothing (see <see cref="OnEngineEnded"/>). Work
    /// that is attributed to a plug-in call is judged by the engine's own watchdog (per-kind limits).
    /// </summary>
    private void CheckLiveness(NamedPipeServerStream pipe, Process process)
    {
        if (!ReferenceEquals(pipe, _pipe) || !IsRunning || _stopping || _deafKill) return;
        var now = Stopwatch.GetTimestamp();
        var busyAttributed = Volatile.Read(ref _shared)?.MainCallRecent(EngineWatchdog.DeafLimitSec) ?? false;
        if (busyAttributed || _awaitingReady || Rendering || _renderTask is { Task.IsCompleted: false }) Volatile.Write(ref _lastAlive, now);
        if (now - Volatile.Read(ref _lastAlive) > EngineWatchdog.DeafLimitSec * Stopwatch.Frequency)
        {
            _deafKill = true;
            Debug.WriteLine($"audio engine: no answer for {EngineWatchdog.DeafLimitSec} s outside plug-in calls; restarting it");
            KillQuietly(process);
            return;
        }
        Send(EngineCommand.Ping, new PingMessage(Interlocked.Increment(ref _pingSeq)).Write);
    }

    private static void KillQuietly(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { Services.Trace.Error(Services.Trace.Engine, "engine: kill process: " + ex.Message); }
    }

    /// <summary>Stops the engine now (app exit, or the warm period ended; see <see cref="WarmIdle"/>). A Sync without engine tracks no longer does.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        _stopping = true;
        Send(EngineCommand.Shutdown);
        try { if (_process is { HasExited: false } p && !p.WaitForExit(1500)) p.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Services.Trace.Error(Services.Trace.Engine, "engine: wait for exit: " + ex.Message); }
        Cleanup();
        RaiseOnUi(() => StatusChanged?.Invoke());
    }

    /// <summary>
    /// Process.Exited (a thread-pool thread): only the breadcrumb is read here (the block stays mapped until the next Start).
    /// Everything else, including Cleanup of the UI-owned dictionaries, runs on the UI thread, and only if that engine is
    /// still the current one (a Stop or a restart in between wins).
    /// </summary>
    private void OnEngineEnded(Process? process)
    {
        if (_stopping || !IsRunning) return;
        // Unexpected exit: which plug-in call was running? Only a crash or an attributed hang blames one. A deaf-engine restart (R-05)
        // or the engine's own unattributed-hang exit (71) quarantines nothing, even if an audio block happened to be in a plug-in.
        var exitCode = ExitCodeOf(process);
        var unattributed = _deafKill || exitCode == EngineWatchdog.ExitUnattributedHang;
        var shared = Volatile.Read(ref _shared);
        var crumb = unattributed ? null : exitCode == EngineWatchdog.ExitAudioHung ? shared?.AudioPluginCall() : shared?.LastPluginCall();
        var crumbPath = crumb?.Path ?? "";
        RaiseOnUi(() =>
        {
            if (_stopping || !IsRunning || !ReferenceEquals(process, _process)) return;
            FailRender(new RenderException("The audio engine stopped while rendering" + (crumbPath.Length > 0 ? $" (in {System.IO.Path.GetFileName(crumbPath)})" : "") + ".", pluginPath: crumbPath));
            Cleanup();
            var path = crumb?.Path;
            if (path is { Length: > 0 })
            {
                var quarantine = Quarantine?.Invoke();
                if (quarantine is not null && !quarantine.Contains(path, StringComparer.OrdinalIgnoreCase)) quarantine.Add(path);
            }
            CrashLeftEngineRunning = false;
            PluginCrashed?.Invoke(path ?? "");
            _crashes.Add(DateTime.UtcNow);
            _crashes.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(1));
            StatusChanged?.Invoke();
        });
    }

    private static int? ExitCodeOf(Process? process)
    {
        try { return process is { HasExited: true } ? process.ExitCode : null; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } // Not logged: exit code probe: null when not exited or not accessible
    }

    /// <summary>After a crash the app calls Sync again; this says whether restarting is still sensible.</summary>
    public bool TooManyCrashes => _crashes.Count >= 3;

    private void Cleanup()
    {
        IsRunning = false;
        FailRender(new RenderException("The audio engine was stopped."));
        _slots.Clear();
        _sentChains.Clear();
        _sentChainIds.Clear();
        _chainRequests.Clear();
        _lastAcks.Clear();
        _sentAudio.Clear(); _audioContext.Clear();
        _sentRoutes.Clear();
        _sentProcessors.Clear();
        _sentWiring.Clear();
        _sentBypass.Clear();
        _sentGain.Clear();
        _sentSynth.Clear();
        _sentGraph = "";
        ResetWarmState();   // Stop, a crash or a failed start: nothing stays parked, no warm timer is left armed
        lock (_gate)
        {
            foreach (var request in _stateRequests.Values) request.Done.TrySetResult(false);   // unanswered plug-ins report as timed out
            _stateRequests.Clear();
        }
        Interlocked.Exchange(ref _liveness, null)?.Dispose();
        lock (_sendGate)
        {
            _queued = null;
            try { _pipe?.Dispose(); } catch (IOException) { } // Not logged: pipe dispose during teardown: the pipe may already be closed.
            _pipe = null;
        }
        _awaitingReady = false;
        // Unpublished, not disposed: a producer (scheduler thread) may have read it just before; see Write / Start.
        lock (_writeGate)
        {
            if (Volatile.Read(ref _shared) is { } shared) { _retiredShared?.Dispose(); _retiredShared = shared; }
            Volatile.Write(ref _shared, null);
        }
        _process?.Dispose();
        _process = null;
        _config = null; Output = null;
    }

    // ---- offline render (RenderProtocol.cs) ----
    private TaskCompletionSource<RenderResult>? _renderTask;
    private IProgress<RenderProgressInfo>? _renderProgress;

    private void Send(EngineCommand command, Action<BinaryWriter>? payload = null)
    {
        SentForTest?.Invoke(command);
        if (command == EngineCommand.Configure) _awaitingReady = true;   // opening a device can take seconds: liveness waits for Ready
        lock (_sendGate)
        {
            if (_queued is { } queue)   // still connecting: keep the order, flushed on connect
            {
                using var frame = new MemoryStream();
                Frames.Write(frame, (byte)command, payload);
                queue.Add(frame.ToArray());
                return;
            }
        }
        var pipe = _pipe;
        if (pipe is not { IsConnected: true }) return;
        try { Frames.Write(pipe, (byte)command, payload); }
        catch (IOException ex) { Services.Trace.Error(Services.Trace.Engine, "engine: send command: " + ex.Message); /* the engine is gone; OnEngineEnded handles it */ }
        catch (ObjectDisposedException ex) { Services.Trace.Error(Services.Trace.Engine, "engine: send command: " + ex.Message); }
    }

    private void ReadEvents(NamedPipeServerStream pipe)
    {
        try
        {
            while (pipe is { IsConnected: true })
            {
                var frame = Frames.Read(pipe);
                if (frame is null) break;
                var (type, r) = frame.Value;
                switch ((EngineEvent)type)
                {
                    case EngineEvent.Pong: Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp()); break;
                    case EngineEvent.PluginSlow:
                    {
                        r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024); var kind = (PluginCallKind)r.ReadInt32(); var seconds = r.ReadInt32();
                        RaiseOnUi(() => PluginSlow?.Invoke(path, kind, seconds));
                        break;
                    }
                    case EngineEvent.Ready:
                    {
                        _awaitingReady = false;
                        Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp());
                        var rate = r.ReadInt32(); var latency = r.ReadInt32(); var description = r.ReadBoundedString(512);
                        var buffer = r.ReadInt32();
                        var inNames = new string[Math.Clamp(r.ReadInt32(), 0, 128)];
                        for (var i = 0; i < inNames.Length; i++) inNames[i] = r.ReadBoundedString(128);
                        var outNames = new string[Math.Clamp(r.ReadInt32(), 0, 128)];
                        for (var i = 0; i < outNames.Length; i++) outNames[i] = r.ReadBoundedString(128);
                        if (inNames.Length + outNames.Length > 0) AsioChannels = (inNames, outNames);
                        DeviceDescription = $"{description} · {rate} Hz · {latency} ms";
                        if (_config is { } running) Output = (running.Driver, description, rate, buffer, latency);
                        RaiseOnUi(() => StatusChanged?.Invoke());
                        break;
                    }
                    case EngineEvent.ChainLoaded:
                    {
                        var slot = r.ReadInt32(); r.ReadInt32();
                        var generation = r.ReadInt32();
                        var results = new List<PluginLoadResult>();
                        for (int i = 0, n = Math.Clamp(r.ReadInt32(), 0, 512); i < n; i++)
                            results.Add(new PluginLoadResult(r.ReadInt32(), (PluginLoadStatus)r.ReadByte(), r.ReadBoundedString(1024)));
                        var ack = new ChainAck(slot, generation, results);
                        foreach (var changed in results.Where(x => x.Status == PluginLoadStatus.BlockedChanged)) PluginTrust.MarkChanged(changed.Path);   // the file no longer matches its approval: untrusted until approved again
                        RaiseOnUi(() => { _lastAcks[ack.Slot] = ack; ChainAcknowledged?.Invoke(ack); ChainLoaded?.Invoke(); });
                        break;
                    }
                    case EngineEvent.DeviceError: { _awaitingReady = false; var message = r.ReadBoundedString(); RaiseOnUi(() => DeviceError?.Invoke(message)); break; }
                    case EngineEvent.PluginFailed:
                    {
                        r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024); var why = r.ReadBoundedString();
                        RaiseOnUi(() => PluginFailed?.Invoke(path, why));
                        break;
                    }
                    case EngineEvent.PluginMisbehaved:
                    {
                        r.ReadInt32(); var index = r.ReadInt32(); var path = r.ReadBoundedString(1024);
                        RaiseOnUi(() => PluginMisbehaved?.Invoke(path, index));
                        break;
                    }
                    case EngineEvent.PluginState: OnPluginState(r); break;
                    case EngineEvent.PitchMeasured:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var id = r.ReadInt32(); var status = (PitchMatch.Status)r.ReadByte();
                        var transpose = Math.Clamp(r.ReadInt32(), -48, 48); var confidence = r.ReadDouble();
                        var n = Math.Clamp(r.ReadInt32(), 0, 8);
                        var notes = new int[n]; var offsets = new double[n];
                        for (var i = 0; i < n; i++) { notes[i] = r.ReadInt32(); offsets[i] = r.ReadDouble(); }
                        var result = new PitchResult(slot, index, id, status, transpose, confidence, notes, offsets);
                        RaiseOnUi(() => PitchMeasured?.Invoke(result));
                        break;
                    }
                    case EngineEvent.StateChanged: OnPluginEdited(r.ReadInt32()); break;
                    case EngineEvent.Recorded:
                    {
                        var slot = r.ReadInt32(); var path = r.ReadBoundedString(1024); var start = r.ReadDouble(); var length = r.ReadDouble();
                        RaiseOnUi(() => { if (_recordingTracks.TryGetValue(slot, out var track)) Recorded?.Invoke(track, path, start, length); });
                        break;
                    }
                    case EngineEvent.InputError: { var message = r.ReadBoundedString(); RaiseOnUi(() => InputError?.Invoke(message)); break; }
                    case EngineEvent.MasterAudio: ReadMasterAudio(r); break;
                    case EngineEvent.RecordingLoss: { var message = r.ReadBoundedString(); RaiseOnUi(() => RecordingLoss?.Invoke(message)); break; }
                    case EngineEvent.EditorClosed: RaiseOnUi(() => EditorClosed?.Invoke()); break;
                    case EngineEvent.EditorSize:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var width = Math.Clamp(r.ReadInt32(), 0, 8192); var height = Math.Clamp(r.ReadInt32(), 0, 8192);
                        RaiseOnUi(() => EditorSized?.Invoke(slot, index, width, height));
                        break;
                    }
                    case EngineEvent.Programs:
                    {
                        var slot = r.ReadInt32(); var index = r.ReadInt32(); var current = r.ReadInt32(); var count = Math.Clamp(r.ReadInt32(), 0, 512);
                        var names = new List<string>(count);
                        for (var i = 0; i < count; i++) names.Add(r.ReadBoundedString(256));
                        RaiseOnUi(() => ProgramsReceived?.Invoke(slot, index, current, names));
                        break;
                    }
                    case EngineEvent.MidiLog:
                    {
                        var slot = r.ReadInt32(); var count = Math.Clamp(r.ReadInt32(), 0, 4096);
                        var lines = new MidiLogLine[count];
                        for (var i = 0; i < count; i++) lines[i] = new MidiLogLine(r.ReadSingle(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                        RaiseOnUi(() => MidiLogReceived?.Invoke(slot, lines));
                        break;
                    }
                    case EngineEvent.RenderProgress:
                    {
                        var info = new RenderProgressInfo(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                        var sink = _renderProgress;
                        if (sink is not null) RaiseOnUi(() => sink.Report(info));
                        break;
                    }
                    case EngineEvent.RenderDone:
                    {
                        var result = RenderResult.Read(r);
                        var source = _renderTask; _renderTask = null; _renderProgress = null;
                        source?.TrySetResult(result);
                        break;
                    }
                    case EngineEvent.RenderFailed:
                    {
                        var message = r.ReadBoundedString(); var cancelled = r.ReadBoolean(); var plugin = r.ReadBoundedString(1024);
                        FailRender(new RenderException(message, cancelled, plugin));
                        break;
                    }
                    case EngineEvent.PluginCrashed:
                    {
                        var slot = r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024);
                        RaiseOnUi(() =>
                        {
                            var quarantine = Quarantine?.Invoke();
                            if (quarantine is not null && !quarantine.Contains(path, StringComparer.OrdinalIgnoreCase)) quarantine.Add(path);
                            _sentChains.Remove(slot); // the next Sync reloads this track's chain without it
                            CrashLeftEngineRunning = true;
                            PluginCrashed?.Invoke(path);
                            CrashLeftEngineRunning = false;
                        });
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException) { Services.Trace.Error(Services.Trace.Engine, "engine: read frames: " + ex.Message); }
    }

    /// <summary>Reader thread: a plug-in in <paramref name="slot"/> reported a parameter edit; the UI thread tells the slot's owner.</summary>
    internal void OnPluginEdited(int slot) => RaiseOnUi(() => PluginEdited?.Invoke(_slotOwners.GetValueOrDefault(slot)));

    private void RaiseOnUi(Action action)
    {
        if (_ui is { } context) context.Post(_ => action(), null); else action();
    }

    public void Dispose()
    {
        MediaAccess.Changed -= OnMediaApprovalChanged;
        MediaAccess.Resolved -= OnMediaResolved;
        Stop();
    }
}
