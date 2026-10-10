using System.Windows;
using TabForge.Audio;
using System.Windows.Media;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge.KeyboardMode;

/// <summary>What the Keyboard mode pane needs from its window, beyond the pane basics.</summary>
internal interface IKeyboardModeHost : IPaneHost
{
    /// <summary>The song on show, for its playback state and compiled timeline.</summary>
    DocumentSession ActiveDocument { get; }
    /// <summary>The shared audio engine client: its MIDI input hub and output latency (for keyboard play-along).</summary>
    AudioEngineClient Engine { get; }
}

// Owns: the pane's per-frame step (only while it is on screen): the playing position and loop read from the document's playback state, the keyboard note source of the selected track
//   (rebuilt when the timeline, the track or the song's content changes), the hint text, the theme and the Keyboard mode settings applied to the view, and with the play-along setting on the
//   keyboard run (KeyboardModeKeyboardSession) with its MIDI device choice (rescanned every 2 s for new devices) and the practice track (KeyboardModePracticeTrack); Score and Judge are read-only
//   for the view. The control bars (KeyboardModeControls) read and change the settings through it.
// Does not own: the timing (the compiled timeline and the playback clock it reads), the drawing (KeyboardModeView), the window or the dock.
// Tests: TestKeyboardModeNoteStream, TestKeyboardModeMidiListener.
internal sealed class KeyboardModeFrameController : IDisposable, IKeyboardModeWaitCommands
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(60);
    private const double LoopCheckMs = 250;
    private const int ThemeCheckTicks = 30;
    private const long RescanMs = 2000;

    private readonly IKeyboardModeHost _host;
    private readonly FrameTicker _ticker = new();
    private readonly KeyboardModeClock _clock = new();
    private ScoreTimeline? _sourceTimeline;
    private SongProject? _sourceProject;
    private TrackModel? _sourceTrack;
    private int _sourceRevision = -1;
    private TrackKind? _sourceKind;
    private KeyboardModeLoop? _loop;
    private ScoreTimeline? _loopTimeline;
    private double _loopCheckedAt = double.NegativeInfinity;
    private int _themeTicks;
    private bool _dark = true;
    private KeyboardNoteSource? _keySource;
    private KeyboardModeKeyboardSession? _keys;
    private KeyboardModeWaitMode? _wait;
    private IKeyboardModeSurface? _keysView;
    private KeyboardModePracticeTrack? _practice;
    private long _rescanAt;
    private bool _disposed;

    public KeyboardModeFrameController(IKeyboardModeHost host)
    {
        _host = host;
        _ticker.Tick += (_, _) => Tick();
    }

    /// <summary>The keyboard falling-notes view: it gets the keyboard note source, the theme, the look-ahead, the song time and the run's feedback. Set once by the window.</summary>
    internal IKeyboardModeSurface? Keys
    {
        get => _keysView;
        set
        {
            if (_keysView is not null) { _keysView.Element.IsVisibleChanged -= OnVisibleChanged; _keysView.LookAheadStep -= OnLookAheadStep; }
            _keysView = value;
            if (value is null) return;
            value.Element.IsVisibleChanged += OnVisibleChanged;
            value.LookAheadStep += OnLookAheadStep;
        }
    }

    /// <summary>The next frame builds the note source again (the instrument changed).</summary>
    internal void InvalidateSource() { _sourceRevision = -1; if (_keysView?.Element.IsVisible == true) Tick(); }

    /// <summary>A pretend playing position for off-screen captures (song time and its compiled timeline); null in normal use.</summary>
    internal (double Ms, ScoreTimeline Timeline)? ProbePlay { get; set; }

    /// <summary>How many times a note source was built (a self-test counter).</summary>
    public int SourceBuilds { get; private set; }

    public bool IsTicking => _ticker.IsEnabled;

    /// <summary>The totals of the keyboard run (zero until a keyboard track is judged).</summary>
    public KeyboardModeScore Score => _keys?.Score ?? EmptyScore;
    /// <summary>The judge of the keyboard run, whose results are the last results; null when no run started.</summary>
    public KeyboardModeJudge? Judge => _keys?.Judge;
    internal KeyboardModeKeyboardSession? KeyboardSession => _keys;
    private static readonly KeyboardModeScore EmptyScore = new();

    /// <summary>Makes the MIDI listener of a keyboard run (the window gives the shared input; a self-test gives a fake).</summary>
    internal Func<KeyboardModeMidiListener>? ListenerFactory { get; set; }

    private KeyboardModeSettings Learn => _host.Settings.Learn ??= new KeyboardModeSettings();
    /// <summary>The saved Keyboard mode values (the control bars change them, then call <see cref="SettingsChanged"/>).</summary>
    internal KeyboardModeSettings Settings => Learn;

    /// <summary>Raised at the end of every frame step (the control bars check what changed).</summary>
    internal event Action? Ticked;

    /// <summary>A control bar changed a setting: saved, said in the status line and shown at once.</summary>
    internal void SettingsChanged(string status)
    {
        _host.SaveSettings();
        _host.SetStatus(status);
        Tick();
    }

    /// <summary>The shown track has finger numbers written.</summary>
    internal bool HasFingers => _keySource?.HasFingers ?? false;

    /// <summary>The shared MIDI input, or the one a self-test or an off-screen capture gives (so they never open the real devices).</summary>
    internal MidiInputHub? HubOverride { get; set; }
    private MidiInputHub Hub => HubOverride ?? _host.Engine.MidiInput;

    /// <summary>The MIDI input devices present now.</summary>
    internal IReadOnlyList<string> MidiDevices() => Hub.DeviceNames();

    /// <summary>The devices Keyboard mode hears now: the open ones it listens to (empty while not listening).</summary>
    internal IReadOnlyList<string> HeardDevices
    {
        get
        {
            if (_keys is not { IsListening: true } keys) return Array.Empty<string>();
            var open = Hub.OpenDeviceNames;
            return keys.Listener.Device is { Length: > 0 } one ? (open.Contains(one) ? new[] { one } : Array.Empty<string>()) : open;
        }
    }

    /// <summary>The control bars' state and actions (made once by the window, which gives its transport).</summary>
    internal KeyboardModeControls? Controls { get; set; }

    /// <summary>The practice track (silent in playback, sounding the player's keys); null until a run listens.</summary>
    internal KeyboardModePracticeTrack? Practice => _practice;

    private void OnVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (_keysView?.Element.IsVisible == true) { _themeTicks = ThemeCheckTicks; Tick(); _ticker.Start(); } else { _ticker.Stop(); _keys?.Stop(); _practice?.Clear(); }
    }

    /// <summary>Opens or closes the keyboard pop-out (set by it while it exists; the command and its menu row call RequestPopout).</summary>
    internal Action? PopoutToggle { get; set; }
    internal void RequestPopout() => PopoutToggle?.Invoke();
    internal void Status(string text) => _host.SetStatus(text);

    /// <summary>Wait for the right notes is on (the pop-out's toggle shows it).</summary>
    internal bool WaitOn => Learn.WaitForNotes;

    /// <summary>The look-ahead in seconds.</summary>
    internal int LookAheadSeconds => KeyboardModeSettings.NormalizeLookAhead(Learn.LookAheadSeconds);

    /// <summary>Changes the look-ahead by <paramref name="step"/> seconds as the plus and minus keys do (the pop-out's zoom buttons).</summary>
    public void StepLookAhead(int step) => OnLookAheadStep(step);

    private void OnLookAheadStep(int step)
    {
        var learn = Learn;
        learn.LookAheadSeconds = KeyboardModeSettings.NormalizeLookAhead(learn.LookAheadSeconds + step);
        _host.SaveSettings();
        _host.SetStatus("Keyboard mode look-ahead: " + learn.LookAheadSeconds + " s");
        Tick();
    }

    public void Dispose()
    {
        _disposed = true;
        _ticker.Stop();
        _practice?.Dispose();
        _keys?.Dispose();
        Keys = null;
    }

    /// <summary>What the header adds about the shown notes (left out outside the 88 keys); empty when all fit.</summary>
    public string SourceNote { get; private set; } = "";
    public event Action? SourceNoteChanged;

    private const string NoTrackHint = "Select a track", NoNotesHint = "This track has no notes to show";

    /// <summary>One frame: reads the playing position, keeps the note source current and hands the view the song time at the hit line.</summary>
    internal void Tick()
    {
        if (_disposed) return;
        var learn = Learn;
        var lookAheadMs = KeyboardModeSettings.NormalizeLookAhead(learn.LookAheadSeconds) * 1000.0;
        if (_themeTicks++ >= ThemeCheckTicks || _ticker.Interval != TimeSpan.Zero) { _themeTicks = 0; _dark = ThemeIsDark(); }
        _keysView?.SetLook(_dark, lookAheadMs);
        _keysView?.SetOptions(KeyboardHands.Parse(learn.Hands), learn.ShowNoteNames, learn.ShowFingers);

        var playback = _host.ActiveDocument.Playback;
        var probe = ProbePlay;
        var timeline = probe?.Timeline ?? playback.Timeline;
        var project = _host.Project;
        var track = _host.SelectedTrack;
        SyncSource(timeline, project, track);

        var playing = probe is not null || (playback.IsPlayingVisual && playback.Engine.IsPlaying && playback.PlayheadBar >= 0);
        var paused = probe is null && playing && playback.Engine.IsPaused;
        var interval = playing && !paused ? TimeSpan.Zero : IdleInterval;
        if (_ticker.Interval != interval) _ticker.Interval = interval;

        double real;
        KeyboardModeLoop? loop = null;
        if (probe is { } p) real = p.Ms;
        else if (playing) { real = playback.PlayheadMs; loop = LoopOf(playback.Engine, timeline); }
        else { real = TimeAtCursor(timeline); _clock.Reset(); }
        var now = _clock.Update(real, loop);
        StepKeyboard(real, playing, paused, loop);
        if (_keysView is { Element.IsVisible: true } keysView)
        {
            keysView.SetPlayAlong(_keys is not null && Learn.PlayAlongKeyboard && _keySource is not null ? _keys : null);
            keysView.SetProgress(timeline is { TotalMs: > 0 } t ? real / t.TotalMs : 0);
            keysView.Update(now, loop, _clock.Jumped, playing && !paused);
        }
        Ticked?.Invoke();
    }

    /// <summary>The keyboard run of this frame: listens to the MIDI keyboard only while the setting is on and the selected track is a keyboard track, else nothing is subscribed.</summary>
    private void StepKeyboard(double real, bool playing, bool paused, KeyboardModeLoop? loop)
    {
        var listen = _keySource is not null && Learn.PlayAlongKeyboard;
        if (_keys is null && listen)
        {
            _keys = new KeyboardModeKeyboardSession(ListenerFactory?.Invoke() ?? NewListener());
            _practice ??= new KeyboardModePracticeTrack(_host.Window.Dispatcher);
            _keys.Listener.Thru += _practice.OnThru;
        }
        if (_keys is null) return;
        var input = KeyboardModeSettings.NormalizeMidiInput(Learn.MidiInput);
        _keys.Listener.Device = input == KeyboardModeSettings.MidiAny ? null : input;
        _keys.Hands = KeyboardHands.Parse(Learn.Hands);
        _keys.Tolerances = KeyboardModeTolerances.Of(Learn.TimingTolerance);
        _wait ??= _host is IKeyboardModeWaitHost wait ? new KeyboardModeWaitMode(wait) : null;
        _keys.Wait = _wait;
        if (_wait is not null) _wait.Enabled = listen && Learn.WaitForNotes;
        _keys.SetSource(_keySource, listen);
        if (listen) _keys.Update(real, playing, paused, loop, _clock.Wraps, _clock.Jumped, LatencySec());
        _practice?.Apply(_host.ActiveDocument, _sourceTrack, listen && _keys.IsListening);
        var tick = Environment.TickCount64;
        if (listen && tick - _rescanAt >= RescanMs) { _rescanAt = tick; Hub.Rescan(); }   // a device plugged in while listening is picked up
    }

    /// <summary>Turns "wait for the right notes" on or off (the hotkey).</summary>
    public void ToggleWait()
    {
        Learn.WaitForNotes = !Learn.WaitForNotes;
        _host.SaveSettings();
        _host.SetStatus("Keyboard mode wait for the right notes " + (Learn.WaitForNotes ? "on" : "off"));
    }

    /// <summary>Gives up the chord the song waits for (the hotkey and the control bar's Skip); nothing when it does not wait.</summary>
    public void SkipWait()
    {
        if (_wait?.Skip() == true) _host.SetStatus("Keyboard mode: chord skipped");
    }

    private KeyboardModeMidiListener NewListener() =>
        new(Hub.CreateClient(), stamp => _host.ActiveDocument.Playback.Clock.SecAt(stamp), LatencySec);

    private double LatencySec() => _host.Engine.IsRunning ? _host.Engine.LatencyTicks / (double)System.Diagnostics.Stopwatch.Frequency : 0;

    private static bool ThemeIsDark()
    {
        var brush = Application.Current?.TryFindResource("WindowBrush") as SolidColorBrush;
        return brush is null || KeyboardModePalette.Contrast(brush.Color, Colors.Black) < KeyboardModePalette.Contrast(brush.Color, Colors.White);
    }

    private void SyncSource(ScoreTimeline? timeline, SongProject project, TrackModel? track)
    {
        if (ReferenceEquals(timeline, _sourceTimeline) && ReferenceEquals(project, _sourceProject) && ReferenceEquals(track, _sourceTrack) && project.ContentRevision == _sourceRevision && track?.Kind == _sourceKind) return;
        _sourceTimeline = timeline; _sourceProject = project; _sourceTrack = track; _sourceRevision = project.ContentRevision; _sourceKind = track?.Kind;
        SourceBuilds++;
        var index = track is null ? -1 : project.Tracks.IndexOf(track);
        // Any track with notation is shown as falling keys at its sounding pitches (a guitar, a bass or a drum track too).
        _keySource = KeyboardNoteSource.Suits(track) ? (timeline is null ? KeyboardNoteSource.Empty() : KeyboardNoteSource.Build(timeline, project, index)) : null;
        _keysView?.SetSource(_keySource ?? KeyboardNoteSource.Empty(),
            _keySource is null ? (track is null ? NoTrackHint : NoNotesHint) : timeline is null ? KeyboardChromeDrawer.IdleHint : _keySource.Notes.Count == 0 ? NoNotesHint : "");
        var note = _keySource?.Note ?? "";
        if (note != SourceNote) { SourceNote = note; SourceNoteChanged?.Invoke(); }
    }

    private KeyboardModeLoop? LoopOf(PlaybackEngine engine, ScoreTimeline? timeline)
    {
        var now = Environment.TickCount64;
        if (ReferenceEquals(timeline, _loopTimeline) && now - _loopCheckedAt < LoopCheckMs) return _loop;
        _loopCheckedAt = now; _loopTimeline = timeline;
        _loop = engine.ActiveLoopMs is { } l ? new KeyboardModeLoop(l.StartMs, l.EndMs) : null;
        return _loop;
    }

    /// <summary>The song time of the cursor while stopped: the start of its bar plus its share of it; 0 when the timeline does not have the bar.</summary>
    private double TimeAtCursor(ScoreTimeline? timeline)
    {
        if (timeline is null) return 0;
        var editor = _host.Editor;
        var slots = Math.Max(1, MusicTime.BarSlots(_host.Project, editor.SelectedMeasure));
        foreach (var bar in timeline.Bars)
            if (bar.Bar == editor.SelectedMeasure) return bar.MsAtFraction(editor.SelectedCell / (double)slots);
        return 0;
    }
}
