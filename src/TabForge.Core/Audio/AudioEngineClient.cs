using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: the client link to the audio-engine process (slots, ownership and warm parking, wiring, MIDI routes, clips).
// Does not own: the engine process itself (TabForge.AudioEngine/EngineHost) or the plug-in trust decision (PluginTrust).
// Tests: AvaloniaEngineRoutingTests, AudioSmokeTests, EngineGmAudibleTests.

/// <summary>One line of the MIDI log: song time, the message, and which processor stage logged it.</summary>
public readonly record struct MidiLogLine(float Time, byte Status, byte Data1, byte Data2, int Stage);

/// <summary>
/// TabForge's only link to the audio engine process (TabForge.exe --audio-engine), which hosts plug-ins and the
/// audio device. The engine starts only when some track plays through plug-ins; it has one owner (the active document) and
/// stays warm for <see cref="WarmIdle"/> after the last engine track goes away (R-10), so a song without plug-ins runs as before. If the engine dies (a plug-in crashed), the plug-in that was
/// running is switched off, the engine restarts, and playback carries on; TabForge itself is never affected.
/// </summary>
public sealed partial class AudioEngineClient : IDisposable
{
    /// <summary>How tracks sound (route, auto GM) for this engine: the settings applier writes it, every routing decision reads it. One per client, never a static.</summary>
    public TabForge.Models.MixerOptions Mixer { get; } = new();
    /// <summary>The program's one live input (every window and tab shares this client). See LiveInput/README.md.</summary>
    public TabForge.LiveInput.LiveInputArm LiveInput { get; } = new();
    /// <summary>The metronome and count-in clicks' own engine slot (no song track). See ClickDestination.cs.</summary>
    public ClickDestination Click { get; } = new();

    /// <summary>The MIDI input devices, shared by recording and Keyboard mode (each takes a client; the devices open while one holds them).</summary>
    public MidiInputHub MidiInput { get; } = new(new MidiInputCapture());

    /// <summary>The window's MIDI input monitor (a keyboard heard through the armed or selected track); live input's stand-in yields to its armed tracks. Null before a window makes one.</summary>
    public TabForge.LiveInput.MidiInputMonitor? MidiMonitor { get; set; }

    /// <summary>True while live input's stand-in listens to a MIDI keyboard (the selected track's monitoring then stays silent).</summary>
    public bool LiveInputMidiOn => LiveInput.Midi?.IsOn == true;

    /// <summary>UI thread: raised at the end of <see cref="Sync"/>, when slots may have been given, moved or freed (the MIDI monitor re-reads its tracks' slots).</summary>
    public event Action? Synced;

    /// <summary>Raised on the MIDI driver thread (nothing that waits) when an armed track hears a note-on; the armed rows wake their meters with it.</summary>
    public event Action? MidiNoteHeard;

    /// <summary>MIDI driver thread: the active window's monitor heard a note-on for an armed track.</summary>
    public void RaiseMidiNoteHeard() => MidiNoteHeard?.Invoke();

    /// <summary>Which plug-in slots may load and which files changed at load (fails closed: untrusted slots are sent as Skip).</summary>
    public IPluginLoadGate Trust { get; }

    /// <summary>A client whose load gate asks <paramref name="trust"/> (the app's one trust state; a test passes its own).</summary>
    public AudioEngineClient(TabForge.Plugins.PluginTrust trust) : this(new TabForge.Plugins.PluginTrustLoadGate(TabForge.Platform.PluginFileTrusts.Create(), trust)) { }

    public AudioEngineClient(IPluginLoadGate trust)
    {
        Trust = trust ?? throw new ArgumentNullException(nameof(trust));
        MediaAccess.Changed += OnMediaApprovalChanged; MediaAccess.Resolved += OnMediaResolved;
    }

    /// <summary>A background classification finished (worker thread): clips that waited for it are judged again on the UI thread. Nothing to do before the first Sync (no slots, no UI context yet).</summary>
    private void OnMediaResolved() { if (_ui is not null) RaiseOnUi(RefreshClips); }

    /// <summary>An approval was given or revoked (any document): the clips of every live slot are judged again with their own document's context.</summary>
    private void OnMediaApprovalChanged() => RaiseOnUi(RefreshClips);

    internal void RefreshClips()
    {
        if (Rendering) return;   // like Sync: nothing is sent to the engine mid-render (the end of the render re-judges the clips)
        ResyncIfDropped();
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
    private readonly HashSet<object> _tunerUsers = new(ReferenceEqualityComparer.Instance);   // UI thread only; other threads read _tunerWanted

    /// <summary>
    /// Tools > Tuner and the live input strip each say whether they want the tuner. The engine detects the pitch of the armed input
    /// (needs an armed track, the input is only open then) while any of them does, so closing one never turns it off for the other.
    /// </summary>
    public void SetTunerWanted(object consumer, bool on)
    {
        if (on) _tunerUsers.Add(consumer); else _tunerUsers.Remove(consumer);
        _tunerWanted = _tunerUsers.Count > 0;
        if (_shared is { } shared) shared.TunerOn = TunerWanted;
    }
    private volatile bool _tunerWanted;   // snapshot of _tunerUsers for Start, which may run off the UI thread
    private bool TunerWanted => _tunerWanted;
    /// <summary>The engine's latest tuner reading (Hz 0: silence / no pitch) and a counter that changes with each one.</summary>
    public (float Hz, float Clarity, int Seq) TunerReading => _shared?.Tuner ?? default;
    /// <summary>A track's current output level (0..1), for meters; 0 when it does not play through the engine.</summary>
    /// <summary>An armed track's input level (before its effects), 0..1+.</summary>
    public float InputPeakOf(TrackModel track) => _shared is { } shared && _slots.TryGetValue(track, out var slot) ? shared.InputPeak(slot) : 0;
    public float PeakOf(TrackModel track) => _shared is { } shared && _slots.TryGetValue(track, out var slot) ? shared.Peak(slot) : 0;
    public string? DeviceDescription { get; private set; }
    /// <summary>The running output: driver (WASAPI, ASIO...), device, sample rate, buffer and total output latency.</summary>
    public (string Driver, string Device, int Rate, int Buffer, int LatencyMs)? Output { get; private set; }
    /// <summary>The channel names of the ASIO device the running engine has open (Device: as configured), as the driver reports them; null when the engine is not running ASIO or reported none.</summary>
    public (string Device, string[] Inputs, string[] Outputs)? AsioChannels { get; private set; }

    private volatile int _engineProtocol;   // what the running engine's Ready event said (0: none yet; 1: an engine from before the protocol version)

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
    /// <summary>More tracks need the engine than it has slots for (the argument): the extra tracks stay silent. Raised on the UI thread at each sync that meets the limit.</summary>
    public event Action<int>? TrackLimitReached;
    /// <summary>A running device is about to be reopened with other settings (pop protection on); raised before the request is sent.</summary>
    public event Action? DeviceChanging;
    /// <summary>The device is open and playing.</summary>
    public event Action? DeviceReady;
    /// <summary>A plug-in reported a parameter edit: the document (or song) that owns its slot, which has unsaved changes; null when no owner is known.</summary>
    public event Action<object?>? PluginEdited;
    /// <summary>The engine started or stopped.</summary>
    public event Action? StatusChanged;
    /// <summary>A track's chain (plug-ins / General MIDI synth) is ready in the engine: fresh synths still need their program changes.</summary>
    public event Action? ChainLoaded;
    /// <summary>The engine's per-request acknowledgement (slot, generation, every plug-in's outcome); raised just before <see cref="ChainLoaded"/>.</summary>
    public event Action<ChainAck>? ChainAcknowledged;

    private readonly Dictionary<int, int> _chainRequests = new();
    private readonly Dictionary<int, ChainAck> _lastAcks = new();
    private readonly object _chainLock = new();   // guards both dictionaries: the pipe reader thread writes acks while the UI / tests read
    /// <summary>Latest acknowledgement per slot (UI thread), so a waiter can start from what already arrived.</summary>
    public IReadOnlyCollection<ChainAck> LastAcknowledgements { get { lock (_chainLock) return _lastAcks.Values.ToList(); } }
    /// <summary>Every slot that has a chain request in flight or done, with the generation of its latest request.</summary>
    public IReadOnlyList<(int Slot, int Generation)> RequestedChains { get { lock (_chainLock) return _chainRequests.Where(kv => _slots.ContainsValue(kv.Key) && !_parkedSince.ContainsKey(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList(); } }   // parked chains of other documents are not the owner's

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
    private float? _windowsPathOffset;
    private bool _measuringPathOffset;
    private int? _pathOffsetSentTo;
    private (int Pid, int Ms) _tunerWindowSent;   // tuner analysis window the running engine was last told
    private (int Pid, string Path) _bankSent;   // sound bank the running engine was last told
    private (int Pid, bool On) _limiterSent;   // live safety limiter state the running engine was last told

    private readonly Dictionary<int, string> _sentWiring = new();

    private readonly Dictionary<int, string> _sentProcessors = new();

    /// <summary>MIDI log lines of a watched track (engine slot, lines); raised on the UI thread.</summary>
    public event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived;

    private readonly Dictionary<int, string> _sentRoutes = new();

    private readonly Dictionary<int, string> _sentBypass = new();
    private readonly Dictionary<int, bool> _sentSynth = new();
    // Key: the engine slot. Value: the (volume, pan) the engine holds for it, so a live level push sends only the slots that changed.
    // Entries exist only for slots sent since the engine last started (at most the engine's slot count); written and cleared on the control thread.
    private readonly Dictionary<int, (int Volume, int Pan)> _sentTrackMix = new();
    private readonly Dictionary<int, string> _sentGain = new();

    private readonly Dictionary<int, string> _sentAudio = new();

    /// <summary>Level, clips and arm for one track: sent only when they changed (cheap to call often).</summary>
    // What each slot's clips were last judged with: the owning document's context. A later approval change re-judges them (RefreshClips)
    // for every document that has live slots, not only the one that synced last.
    private readonly Dictionary<int, (TrackModel Track, MediaContext Media, (int Volume, int Pan) Mix)> _audioContext = new();

    /// <summary>Settings > Audio &amp; VST > Recording offset (ms), taken from the settings on every <see cref="Sync"/>.</summary>
    public int RecordingOffsetMs { get; set; }

    private Dictionary<int, TrackModel> _recordingTracks = new();   // guarded by _recordingGate: the UI thread replaces and edits it, the reader thread looks tracks up
    private readonly object _recordingGate = new();

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
    /// <summary>A plug-in's parameter list arrived (asked with <see cref="RequestParameters"/>): engine slot, plug-in index, parameters.</summary>
    public event Action<int, int, IReadOnlyList<PluginParameterInfo>>? ParametersReceived;

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

    // ---- offline render (RenderProtocol.cs) ----
    private TaskCompletionSource<RenderResult>? _renderTask;
    private IProgress<RenderProgressInfo>? _renderProgress;

    /// <summary>Sends one command. True when the engine (or its connect queue) took it; false when it was dropped because the pipe is down: the caller must not remember it as sent (see <see cref="NoteDropped"/>).</summary>
    private bool Send(EngineCommand command, Action<BinaryWriter>? payload = null)
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
                return true;
            }
        }
        var pipe = _pipe;
        if (pipe is not { IsConnected: true }) return (_fakeLinkUp && pipe is null) || NoteDropped(command, "the engine pipe is not connected");
        try { Frames.Write(pipe, (byte)command, payload); return true; }
        catch (IOException ex) { Services.Trace.Error(Services.Trace.Engine, "engine: send command: " + ex.Message); /* the engine is gone; OnEngineEnded handles it */ return NoteDropped(command, ex.Message); }
        catch (ObjectDisposedException ex) { Services.Trace.Error(Services.Trace.Engine, "engine: send command: " + ex.Message); return NoteDropped(command, ex.Message); }
    }

    /// <summary>What a handler bug does besides the trace line: nothing (a Debug.Fail here would fail-fast the whole process from the reader thread); a test replaces it to count faults.</summary>
    internal Action<string> OnHandlerFault { get; set; } = _ => { };

    private static bool StillConnected(Stream pipe) => pipe is not NamedPipeServerStream { IsConnected: false };

    internal void ReadEvents(Stream pipe)
    {
        try
        {
            while (StillConnected(pipe))
            {
                var next = Frames.Read(pipe, FrameLimits.ForEvent);
                if (next is null) break;
                using var frame = next.Value;
                var (type, r) = frame;
                try { HandleEvent(type, r); }
                catch (Exception ex) when (ex is not (ObjectDisposedException or OutOfMemoryException) && (ex is not (IOException or EndOfStreamException) || StillConnected(pipe)))
                {
                    // A bug in one frame's handling, or a frame that ends early, drops that frame, not the engine connection; a pipe that is gone still ends the engine.
                    Services.Trace.Error(Services.Trace.Engine, $"engine: event {type} dropped: " + ex);
                    OnHandlerFault("engine event handler: " + ex);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any fault on this thread (a closed pipe, a bad frame, a handler that threw) ends the reading, never the app: the engine counts as gone and the crash path restarts it.
            Services.Trace.Error(Services.Trace.Engine, "engine: read frames: " + ex);
            OnEngineEnded(_process);
        }
    }

    /// <summary>Reader thread: a plug-in in <paramref name="slot"/> reported a parameter edit; the UI thread tells the slot's owner.</summary>
    internal void OnPluginEdited(int slot) => RaiseOnUi(() => PluginEdited?.Invoke(IsAppWideSlot(slot) ? null : _slotOwners.GetValueOrDefault(slot)));

    /// <summary>The live input chain and the app-wide monitor chain live in the app settings: an edit in them belongs to no song.</summary>
    private bool IsAppWideSlot(int slot) =>
        SlotOf(LiveInput.Track) == slot || (slot == TabForge.Models.MixerBuses.MonitorSlot && _slots.Keys.Any(t => t.BusSlot == slot && ReferenceEquals(t.Bus, _appWideMonitor)));
    private TabForge.Models.BusChain? _appWideMonitor;

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
