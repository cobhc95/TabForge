using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: one window's Keyboard mode run (the Learn engine): the judge, score and feedback of the shown song's keyboard track, the wait gate that
// pauses the song at each chord until its notes are pressed, the MIDI listener queue (the MIDI listener
//   parity: song-time stamping with latency compensation, unstamped pitch-only presses while paused), the practice silence of the practised track
//   (practice track: PlaybackEngine.SetPracticeSilence only, never Mute), the bar-to-session drive (play/stop/speed/loop/track,
//   persisted Learn settings for wait/midi/hands/look-ahead/names/fingers) and the per-frame strip feed (Source/Judge/Score/Feedback/Waiting/Awaited/Loop).
//   One instance per window, owned by MainWindowDocumentPanes and shared by the docked pane and the pop-out, so both bars stay in sync.
// Does not own: the bars or strips (each binding drives its own pair through this engine), the transport (the session), the devices (the MIDI hub),
//   the loop range (SelectionLoopLink.LoopSeconds via LoopSecondsProvider), the settings file (MainWindowSettings, through AttachSettings) or drawing.
// Tests: TabForge.Avalonia.Tests.KeyboardLearnEngineTests.
// Threads: UI thread only, except OnMidiMessage (the MIDI driver thread, lock-free enqueue only).
public sealed partial class KeyboardLearnEngine : IDisposable
{
    /// <summary>A press this long before the pause still counts for the chord.</summary>
    public const long PreWindowMs = 300;
    private const double CatchUpSec = 0.1;
    private const double StartSlackSec = 0.05;
    private const double MissGraceSec = 0.02;
    private const long RescanMs = 2000;
    private const int MaxQueued = 512;

    private AppSettings? _settings;
    private Action? _saveSettings;
    private Func<MidiInputHub?>? _hubProvider;
    private Func<double>? _latencySec;
    private SongDocument? _document;
    private AvaloniaPlaybackSession? _session;
    private IMidiInputClient? _midi;
    private readonly ConcurrentQueue<KeyboardModePlayed> _queue = new();
    private int _queued;
    private bool _paneAttached, _popoutOpen;
    private long _rescanAt;
    private bool _disposed;

    // Run state.
    private KeyboardNoteSource? _source;
    private object? _timelineOfSource;
    private SongProject? _projectOfSource;
    private int _trackOfSource = -1, _revisionOfSource = -1;
    private KeyboardHandsFilter _handsOfJudge;
    private KeyboardModeTolerances? _tolOfJudge;
    private bool _wasRunning;
    private double _lastRealMs;
    private double _biasSec;
    private readonly Queue<double> _offsets = new();
    private const int OffsetWindow = 8, MinOffsets = 3;
    private const double MaxSpreadMs = 160, MinBiasMs = 30, MaxBiasMs = 700;

    // Wait state.
    private KeyboardModeJudge? _waitJudge;
    private int _waitGroup;
    private int _waitYielded = -1;
    private bool _waiting, _waitCatchUp, _waitPausedOwn;
    private readonly Dictionary<int, (long Seq, long Tick)> _latest = new();
    private readonly Dictionary<int, long> _used = new();
    private long _seq, _waitStartTick;

    /// <summary>The totals of the current run.</summary>
    public KeyboardModeScore Score { get; } = new();
    /// <summary>The judge of the current run (its results, in onset order, are the last results); null until a run starts.</summary>
    public KeyboardModeJudge? Judge { get; private set; }
    /// <summary>What the strips show about the player: held keys and the latest grade flash.</summary>
    public KeyboardFeedback Feedback { get; } = new();
    /// <summary>True while the song is paused by this engine and a chord is awaited.</summary>
    public bool IsWaiting => _waiting;
    /// <summary>The keys of the awaited chord that are not resolved yet (empty when nothing is awaited).</summary>
    public KeyMask Awaited { get; private set; }
    /// <summary>The loop in song milliseconds, or null.</summary>
    public KeyboardModeLoop? Loop { get; private set; }
    /// <summary>How many pauses and resumes the wait gate issued (a self-test counter).</summary>
    public int Pauses { get; private set; }
    public int Resumes { get; private set; }
    /// <summary>How many fresh runs were started (a self-test counter).</summary>
    public int Runs { get; private set; }
    /// <summary>True while subscribed to a MIDI input.</summary>
    public bool IsListening => _midi is not null;
    /// <summary>The loop range in song seconds while looping is on; null when the loop is off (SelectionLoopLink.LoopSeconds).</summary>
    public Func<(double Start, double End)?>? LoopSecondsProvider { get; set; }

    /// <summary>Milliseconds clock (a self-test replaces it).</summary>
    internal Func<long> Now { get; set; } = () => Environment.TickCount64;

    private KeyboardModeSettings? Learn => _settings?.Learn;

    /// <summary>The settings this engine persists bar choices to; null leaves choices unpersisted (captures and self-tests).</summary>
    public void AttachSettings(AppSettings? settings, Action? saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
    }

    /// <summary>Where MIDI devices and the output latency come from; null never listens (captures and self-tests).</summary>
    public void AttachMidi(Func<MidiInputHub?> hubProvider, Func<double>? latencySec = null)
    {
        _hubProvider = hubProvider;
        _latencySec = latencySec ?? (() => 0);
    }

    /// <summary>The song and session the run follows (null unbinds); the track follows the document's TrackIndex.</summary>
    public void BindSong(SongDocument? document, AvaloniaPlaybackSession? session)
    {
        if (ReferenceEquals(_document, document) && ReferenceEquals(_session, session)) return;
        if (_document is not null) _document.TrackIndexChanged -= OnTrackIndexChanged;
        StopListening(clearPractice: true);
        RearmWait();
        _document = document;
        _session = session;
        _source = null;
        Judge = null;
        _timelineOfSource = null;
        if (_document is not null) _document.TrackIndexChanged += OnTrackIndexChanged;
    }

    private void OnTrackIndexChanged()
    {
        // A track picked outside the bar (the track menu, a score click): the next frame rebuilds the source and the run.
        _source = null;
        Judge = null;
        RearmWait();
    }

    /// <summary>The docked pane is attached (visible) or not; hidden with no pop-out stops listening when the pane is hidden.</summary>
    public void SetPaneAttached(bool attached)
    {
        _paneAttached = attached;
        if (!attached && !_popoutOpen) OnHidden();
    }

    /// <summary>The pop-out is open or not; closed with no pane stops listening.</summary>
    public void SetPopoutOpen(bool open)
    {
        _popoutOpen = open;
        if (!open && !_paneAttached) OnHidden();
    }

    private void OnHidden()
    {
        StopListening(clearPractice: true);
        RearmWait();
        Feedback.Reset();
        _wasRunning = false;
    }

    /// <summary>True while the song is actually playing (not paused, not stopped).</summary>
    private bool HostIsPlaying => _session is { IsPlaying: true, IsPaused: false };

    // ---- bar drive ----

    /// <summary>A bar choice changed: drives the session and persists the Learn-owned choices.</summary>
    public void ApplyBar(KeyboardModeBar bar)
    {
        if (_disposed || _settings is null && _session is null) return;
        var learn = Learn;
        // Play / stop (the bar's own buttons): while the wait gate holds the song, the transport is its own.
        if (_session is { } session && !_waiting)
        {
            if (bar.IsPlaying && !session.IsPlaying) session.PlayOrPause();
            else if (!bar.IsPlaying && session.IsPlaying && !session.IsPaused) session.Stop();
        }
        else if (_session is { } waiting && _waiting && !bar.IsPlaying && waiting.IsPlaying)
        {
            // Stopped by hand while a chord is awaited: nothing is left to resume.
            waiting.Stop();
            RearmWait();
        }
        if (_session is { } speed && Math.Abs(bar.Speed - speed.Speed) > 0.001)
            speed.SetSpeed(bar.Speed);
        if (_document is { } looped && bar.LoopOn != looped.Selection.LoopRepeat)
            looped.Selection.SetLoopRepeat(bar.LoopOn);   // the Loop button state lives in the song selection; the loop link pushes it to the transport
        if (_document is { } document && bar.TrackIndex >= 0 && bar.TrackIndex < document.Project.Tracks.Count
            && bar.TrackIndex != document.TrackIndex)
            document.TrackIndex = bar.TrackIndex;
        if (learn is null) return;
        var hands = bar.Hands;
        if (!string.Equals(learn.Hands, KeyboardHands.NameOf(hands), StringComparison.Ordinal))
            Change(learn, s => s.Hands = KeyboardHands.NameOf(hands));
        if (learn.LookAheadSeconds != bar.LookAheadSeconds)
            Change(learn, s => s.LookAheadSeconds = bar.LookAheadSeconds);
        if (!string.Equals(learn.MidiInput, bar.MidiInput, StringComparison.Ordinal))
            Change(learn, s => s.MidiInput = KeyboardModeSettings.NormalizeMidiInput(bar.MidiInput));
        if (learn.WaitForNotes != bar.WaitOn)
            Change(learn, s => s.WaitForNotes = bar.WaitOn);
        if (learn.ShowNoteNames != bar.ShowNames)
            Change(learn, s => s.ShowNoteNames = bar.ShowNames);
        if (learn.ShowFingers != bar.ShowFingers)
            Change(learn, s => s.ShowFingers = bar.ShowFingers);
    }

    private void Change(KeyboardModeSettings learn, Action<KeyboardModeSettings> change)
    {
        change(learn);
        _saveSettings?.Invoke();
    }

    /// <summary>Learn.ToggleWait: the song stops at each chord until its notes are pressed, or carries on.</summary>
    public void ToggleWait()
    {
        if (Learn is { } learn) { Change(learn, s => s.WaitForNotes = !s.WaitForNotes); }
    }

    /// <summary>Learn.WaitSkip: gives up the awaited chord (a miss) and resumes. False when nothing is awaited.</summary>
    public bool SkipWait()
    {
        if (!_waiting || Judge is null) return false;
        Judge.MissGroup(_waitGroup);
        FinishWait(resolveNotes: false);
        return true;
    }

    /// <summary>Learn.LookAheadLonger/Shorter: shows one more (or fewer) seconds above the keys.</summary>
    public void StepLookAhead(int step)
    {
        if (Learn is { } learn) Change(learn, s => s.LookAheadSeconds = KeyboardModeSettings.NormalizeLookAhead(s.LookAheadSeconds + step));
    }

    // ---- per-frame feed (the frame tick, minus drawing) ----

    /// <summary>One frame for one strip+bar pair: keeps the note source current, runs the judge/wait/score/feedback and feeds the strip, then syncs the bar.</summary>
    public void Follow(KeyboardStripView strip, KeyboardModeBar bar)
    {
        if (_disposed) return;
        var session = _session;
        var document = _document;
        if (session is null || document is null)
        {
            SyncBar(bar, null, null, -1);
            return;
        }
        var project = document.Project;
        var timeline = session.Transport.Timeline;
        var track = Math.Clamp(document.TrackIndex, 0, Math.Max(0, project.Tracks.Count - 1));
        if (project.Tracks.Count == 0) track = -1;
        SyncSource(timeline, project, track);
        var loop = CurrentLoop(session);
        Loop = loop;

        var running = session.IsPlaying;
        var paused = session.IsPaused;
        var real = session.Transport.PlayheadMs;
        var listen = _source is not null && (Learn?.PlayAlongKeyboard ?? true);
        // A hands or tolerance change starts a fresh run.
        if (Judge is not null && (_handsOfJudge != KeyboardHands.Parse(Learn?.Hands) || !Equals(_tolOfJudge, KeyboardModeTolerances.Of(Learn?.TimingTolerance))))
        {
            Judge = null;
            RearmWait();
        }
        EnsureListener(listen);
        // The hub rescan runs only while a Learn pane is actually shown (docked or pop-out); a hidden engine never scans.
        if (listen && (_paneAttached || _popoutOpen) && _midi is not null && Now() - _rescanAt >= RescanMs)
        {
            _rescanAt = Now();
            _hubProvider?.Invoke()?.Rescan();
        }
        if (running && (!_wasRunning || Judge is null || _sourceChanged || real < _lastRealMs - 100))
        {
            StartRun(real / 1000, loop);
            _sourceChanged = false;
        }
        _wasRunning = running;
        _lastRealMs = real;
        if (!running) DropWait();
        else if (Judge is null) RearmWait();
        if (!running || Judge is null)
        {
            Drain(e => Feedback.Played(e, KeyHold.Held));
        }
        else
        {
            Drain(e => { e = Calibrated(e); var hit = Judge.Feed(e); if (hit is null) Press(e); Feedback.Played(e, hit is not null || Awaits(e.Midi)); });
            if (!listen || Learn?.WaitForNotes != true)
            {
                if (_waiting) RearmWait();
            }
            else StepWait(Judge, real / 1000, running);
            paused |= _waiting;
            if (!paused) Judge.Advance(KeyboardModeJudge.Compensate(real / 1000, _latencySec?.Invoke() ?? 0) - MissGraceSec - Math.Max(0, _biasSec));
            Score.Absorb(Judge);
            Feedback.Scan(Judge, Now());
            RefreshAwaited();
        }
        ApplyPracticeSilence(listen && IsListening ? track : -1);

        strip.Source = _source ?? KeyboardNoteSource.Empty();
        strip.Judge = Judge;
        strip.Score = Score;
        strip.Feedback = Feedback;
        strip.Waiting = _waiting;
        strip.Awaited = Awaited;
        strip.Loop = loop;
        strip.Progress = timeline is { TotalMs: > 0 } t ? Math.Clamp(real / t.TotalMs, 0, 1) : 0;
        strip.InvalidateVisual();

        SyncBar(bar, project, timeline, track);
    }

    private bool _sourceChanged;

    private void SyncSource(object? timeline, SongProject project, int track)
    {
        var trackModel = track >= 0 && track < project.Tracks.Count ? project.Tracks[track] : null;
        if (ReferenceEquals(timeline, _timelineOfSource) && ReferenceEquals(project, _projectOfSource) && track == _trackOfSource
            && project.ContentRevision == _revisionOfSource && Equals(trackModel?.Kind, _kindOfSource)) return;
        _timelineOfSource = timeline;
        _projectOfSource = project;
        _trackOfSource = track;
        _revisionOfSource = project.ContentRevision;
        _kindOfSource = trackModel?.Kind;
        _source = timeline is null || trackModel is null ? KeyboardNoteSource.Empty() : KeyboardNoteSource.Build((ScoreTimeline)timeline, project, track);
        _sourceChanged = true;
        Score.Reset();
        Feedback.Reset();
        Judge = null;
        RearmWait();
    }

    private object? _kindOfSource;

    private KeyboardModeLoop? CurrentLoop(AvaloniaPlaybackSession session)
    {
        if (!session.Loop) return null;
        if (LoopSecondsProvider?.Invoke() is not { } range) return null;
        var loop = new KeyboardModeLoop(range.Start * 1000, range.End * 1000);
        return loop.IsUsable ? loop : null;
    }

    // ---- bar poll (controls poll: a bar follows hotkeys, the play bar and devices) ----

    private int _syncSig;
    private string _syncMidi = "";

    private void SyncBar(KeyboardModeBar bar, SongProject? project, object? timeline, int track)
    {
        var session = _session;
        var learn = Learn;
        bar.IsPlaying = session?.IsPlaying == true;
        bar.WaitOn = learn?.WaitForNotes == true;
        bar.LoopOn = _document?.Selection.LoopRepeat == true;
        if (session is not null && Math.Abs(bar.Speed - session.Speed) > 0.001) bar.Speed = session.Speed;
        var hands = KeyboardHands.Parse(learn?.Hands);
        if (bar.Hands != hands) bar.Hands = hands;
        if (learn is not null && bar.LookAheadSeconds != KeyboardModeSettings.NormalizeLookAhead(learn.LookAheadSeconds))
            bar.LookAheadSeconds = learn.LookAheadSeconds;
        bar.ShowNames = learn?.ShowNoteNames ?? true;
        bar.ShowFingers = learn?.ShowFingers ?? true;
        bar.HasFingers = _source?.HasFingers == true;

        // Track list follows the window's selection.
        if (project is not null)
        {
            var hash = new HashCode();
            foreach (var t in project.Tracks) hash.Add(t.Name);
            var sig = hash.ToHashCode() ^ (project.Tracks.Count * 397);
            var selected = Math.Clamp(track, 0, Math.Max(0, project.Tracks.Count - 1));
            if (sig != _syncSig || selected != _syncTrack || !ReferenceEquals(project, _syncProject))
            {
                _syncSig = sig;
                _syncTrack = selected;
                _syncProject = project;
                bar.SetTracks(project.Tracks.Select(t => t.Name), project.Tracks.Count == 0 ? -1 : selected);
            }
            else if (bar.TrackIndex != selected && project.Tracks.Count > 0)
            {
                bar.SetTracks(project.Tracks.Select(t => t.Name), selected);
            }
        }

        // MIDI list (Off / Any / devices) follows the hub; the heard dot follows what is open and listened to.
        var devices = _hubProvider?.Invoke()?.DeviceNames() ?? Array.Empty<string>();
        var midi = KeyboardModeSettings.NormalizeMidiInput(learn?.MidiInput);
        var heard = HeardDevices();
        var heardKey = string.Join("|", heard);
        if (!devices.SequenceEqual(_syncDevices) || midi != _syncMidi || heardKey != _heardKey)
        {
            _syncDevices = devices.ToArray();
            _syncMidi = midi;
            _heardKey = heardKey;
            bar.SetMidiDevices(devices, midi);
        }
        bar.SetHeard(heard.Count > 0, heard.Count == 0 ? KeyboardModeBar.NoDeviceText : string.Join(", ", heard));
    }

    private SongProject? _syncProject;
    private int _syncTrack = -1;
    private string[] _syncDevices = Array.Empty<string>();
    private string _heardKey = "";

    /// <summary>The devices Keyboard mode hears now: the open ones it listens to (empty while not listening).</summary>
    public IReadOnlyList<string> HeardDevices()
    {
        if (_midi is null || _hubProvider?.Invoke() is not { } hub) return Array.Empty<string>();
        var open = hub.OpenDeviceNames;
        return _midi.Device is { Length: > 0 } one ? (open.Contains(one) ? new[] { one } : Array.Empty<string>()) : open;
    }

    // ---- practice silence (practice track: silence only, never Mute) ----

    private int _practiceTrack = -1;

    private void ApplyPracticeSilence(int track)
    {
        if (track == _practiceTrack) return;
        _practiceTrack = track;
        if (_session?.Engine is PlaybackEngine engine) engine.SetPracticeSilence(track);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_document is not null) _document.TrackIndexChanged -= OnTrackIndexChanged;
        StopListening(clearPractice: true);
        DropWait();
    }
}
