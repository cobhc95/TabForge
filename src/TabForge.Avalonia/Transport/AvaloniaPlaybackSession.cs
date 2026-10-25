using System;
using System.Collections.Generic;
using System.Diagnostics;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Platform;

namespace TabForge.Avalonia.Transport;

// Owns: one document's playback for the Avalonia shell: the PlaybackTransport over the document's engine, frame source
//     (one tick per screen refresh) that pumps the newest position through the PlayheadClock while the song plays (stopped while paused or stopped), and the two change
//     events the views follow (play, pause or loop state; a moved playhead).
// Does not own: the engine's creation (passed in), the song (read through the project delegate), the views that draw the playhead.
// Tests: TabForge.Avalonia.Tests.AvaloniaPlaybackSessionTests, TabForge.Avalonia.Tests.TransportRecordingTests.
// Threads: every public member runs on the UI thread; the frame source ticks on it.
public sealed class AvaloniaPlaybackSession : IDisposable
{
    private readonly PlaybackTransport _transport;
    private readonly IPlaybackEngine _engine;
    private readonly Func<SongProject> _project;
    private readonly IFrameSource _frames;
    private readonly PlayheadClock _clock = new();
    private bool _lastPlaying;
    private bool _lastPaused;
    private bool _disposed;
    private Action? _afterCountIn;   // a recording waiting for the start's count-in to end; UI thread only
    private TabForge.Services.AudioSettings? _audio;   // the audio settings ApplyClickOptions was given (the shared object); UI thread only
    private double? _performedSeekSec;   // the performed second of the last pass-aware seek, the timeline-to-song mapping's pass hint; UI thread only

    /// <param name="frames">The per-frame tick (screen refresh); null uses a timer on <paramref name="ui"/>.</param>
    public AvaloniaPlaybackSession(IPlaybackEngine engine, IUiThread ui, Func<SongProject> project, IFrameSource? frames = null)
    {
        _engine = engine;
        _transport = new PlaybackTransport(engine);
        _project = project;
        _frames = frames ?? new TimerFrameSource(ui);
        _frames.Frame += PumpFrame;
    }

    /// <summary>The transport the playhead views read (the loop flag, the timeline and the engine's own position).</summary>
    public PlaybackTransport Transport => _transport;

    /// <summary>The engine the transport runs on (practice silence reads it); null-shape for engines without one.</summary>
    internal IPlaybackEngine Engine => _engine;

    /// <summary>The position to draw this frame: the engine position carried forward by the elapsed time, so it moves at every screen frame.</summary>
    public PlaybackPosition Playhead => _clock.Position;

    /// <summary>
    /// The scheduler's song time in seconds: where a recording starts. The drawn <see cref="Playhead"/> runs up to a frame ahead of it, so a
    /// take anchored on it would begin late. Reads the engine's last reported position (one allocation, for a user action, never a frame).
    /// </summary>
    public double SchedulerSec => _engine.Playhead().ElapsedMs / 1000.0;

    /// <summary>Note-ons the engine skipped this playthrough because its scheduler fell behind; 0 for an engine that does not count them.</summary>
    public int LateNotesDropped => _engine is PlaybackEngine engine ? engine.LateNotesDropped : 0;

    public bool IsPlaying => _transport.IsPlaying;
    public bool IsPaused => _transport.IsPaused;
    public bool Loop => _transport.Loop;
    public bool Metronome => _transport.Metronome;
    public bool CountIn => _transport.CountIn;

    /// <summary>Raised on the UI thread when play, pause or loop state changes (tab strip playing icon, transport lit buttons).</summary>
    public event Action? StateChanged;

    /// <summary>Raised on the UI thread just before a stopped song starts, so the engine takes the document's edits first.</summary>
    public event Action? PlayStarting;

    /// <summary>Reports the song position to the audio engine's song transport: the engine's audio clips only play while their song
    /// is told playing with its position (the WPF SongClock's anchors). Null keeps positions local. Set by the sessions host.</summary>
    public Action<bool, double, double>? ReportEnginePosition { get; set; }

    /// <summary>The routed MIDI output the engine plays through (the document's MIDI routes are set on it); null when the engine has none.</summary>
    public TabForge.Audio.RoutedMidiOutput? Routing => _engine.Routing;

    /// <summary>Applies mute and solo to the running playback at once, without a restart.</summary>
    public void ApplyMuteSolo(SongProject project, bool liveSolo = false)
    {
        ThrowIfDisposed();
        _liveSolo = liveSolo;
        _engine.SetMuteSolo(project, liveSolo);
    }

    private bool _liveSolo;

    /// <summary>The live input's solo reaches the MIDI mask too (Windows-MIDI tracks included); nothing is sent unless it changed.</summary>
    public void ApplyLiveSolo(SongProject project, bool liveSolo)
    {
        if (liveSolo != _liveSolo) ApplyMuteSolo(project, liveSolo);
    }

    /// <summary>Sets the master percent the playback plays at, at once.</summary>
    public void ApplyMasterVolume(SongProject project, int percent)
    {
        ThrowIfDisposed();
        _engine.SetMasterVolume(project, percent);
    }

    /// <summary>Raised on the UI thread when the played bar or cell moved: a frame that entered another cell, a seek, a stop or the end.</summary>
    public event Action? PlayheadBarChanged;

    /// <summary>Raised on the UI thread on every live frame, after the playhead has moved (the instrument panes read the note lists from it).</summary>
    public event Action? PlayheadFrame;

    /// <summary>Starts a stopped song; resumes a paused one; pauses a playing one (including a start that still compiles, which then begins paused).</summary>
    public void PlayOrPause()
    {
        ThrowIfDisposed();
        if (!_transport.IsPlaying)
        {
            _afterCountIn = null;   // a new start supersedes a recording still waiting for the old one's count-in
            PlayStarting?.Invoke();
            _transport.Play(_project());
        }
        else _transport.PauseOrResume();
        _clock.Update(_transport);
        SyncState();
        ReportPosition();
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _afterCountIn = null;
        var sec = EngineSongSec();   // before the pass hint clears: a stop on pass 2 holds pass 2's second
        _performedSeekSec = null;
        var wasActive = _transport.IsPlaying;
        _transport.Stop();
        _clock.Update(_transport);
        SyncState();
        PlayheadBarChanged?.Invoke();
        // The engine's song transport stops where the song was (the WPF clock's Stopped): the cleared playhead reads 0 afterwards.
        if (wasActive) Anchor(false, sec);
    }

    public void Seek(int bar, int cell)
    {
        ThrowIfDisposed();
        _afterCountIn = null;   // a moved start invalidates the take second a waiting recording was given
        _performedSeekSec = null;   // a plain bar/cell seek keeps today's first-occurrence mapping
        _transport.Seek(_project(), bar, cell);
        _clock.Update(_transport);
        PlayheadBarChanged?.Invoke();
        // A playing song jumps there at once (the WPF clock's jump anchor): the transport's playhead fields have no seconds yet.
        if (_transport.IsPlaying && !_transport.StartPending)
        {
            var song = _project();
            var slots = Math.Max(1, Services.MusicTime.BarSlots(song, Math.Max(0, bar)));
            AnchorSeek(Presenters.AudioClipTimelineRules.SecondsAtBar(song, Math.Max(0, bar), Math.Clamp(cell, 0, slots) / (double)slots));
        }
    }

    // A seek re-anchors the engine's song transport at the new second; a paused song stays held there (the engine's audio clips must not
    // run while the transport is paused: the transport reports IsPlaying while paused), and the resume anchors it running.
    private void AnchorSeek(double sec) => Anchor(!_transport.IsPaused, sec);

    /// <summary>
    /// Seeks to a pass-aware position over a repeat (RV12): the written bar, the fraction through that play, and which
    /// performance (pass) of it. The performed second comes from <see cref="RepeatMap.ToPerformed"/>; a pass of 1 (or a
    /// bar outside a repeat) is the unchanged bar/cell seek. A play that does not exist is ignored. A transport loop keeps
    /// its unchanged T1 entry (the pass does not apply there). UI thread only.
    /// </summary>
    public void Seek(RepeatPosition position)
    {
        ThrowIfDisposed();
        var song = _project();
        var performed = Services.SongExtent.Timing(song).Performed;
        var performedSec = RepeatMap.ToPerformed(performed, position);
        if (double.IsNaN(performedSec)) return;
        var slots = Math.Max(1, Services.MusicTime.BarSlots(song, Math.Max(0, position.WrittenBar)));
        var cell = Math.Clamp((int)Math.Round(position.Fraction * slots), 0, slots - 1);
        var looping = _transport.Loop;   // T1: a loop compiles whole passes and enters by bar/cell, so the pass is not used
        var startSec = looping
            ? Presenters.AudioClipTimelineRules.SecondsAtBar(song, Math.Max(0, position.WrittenBar), position.Fraction)
            : performedSec;
        _afterCountIn = null;   // a moved start invalidates the take second a waiting recording was given
        _performedSeekSec = looping ? null : performedSec;
        _transport.Seek(song, position.WrittenBar, cell, looping ? null : position.Pass);
        _clock.Update(_transport);
        PlayheadBarChanged?.Invoke();
        // A playing song jumps there at once, anchored on the selected performed second (the pass's own occurrence, not the bar's first).
        if (_transport.IsPlaying && !_transport.StartPending) AnchorSeek(startSec);
    }

    /// <summary>
    /// The performed (song) second the scheduler has reached, mapped through <see cref="TimelineSongClock.SongSecAt"/>
    /// exactly as a recording's landing does. A pass-aware seek supplies the pass hint so a repeat's later play maps to
    /// its own occurrence. UI thread only.
    /// </summary>
    public double PerformedSec
    {
        get
        {
            ThrowIfDisposed();
            return PerformedSecAtScheduler(_project());
        }
    }

    /// <summary>The written bar, fraction and pass at the playing performed second. UI thread only.</summary>
    public RepeatPosition CurrentRepeatPosition
    {
        get
        {
            ThrowIfDisposed();
            var song = _project();
            var performed = Services.SongExtent.Timing(song).Performed;
            return RepeatMap.FromPerformed(performed, PerformedSecAtScheduler(performed));
        }
    }

    private double PerformedSecAtScheduler(SongProject song) =>
        PerformedSecAtScheduler(Services.SongExtent.Timing(song).Performed);

    private double PerformedSecAtScheduler(PerformedBar[] performed) =>
        TimelineSongClock.SongSecAt(performed, _transport.Timeline, SchedulerSec * 1000.0, _performedSeekSec);

    public void SetLoop(bool loop)
    {
        ThrowIfDisposed();
        _performedSeekSec = null;
        _transport.SetLoop(loop);
        StateChanged?.Invoke();
    }

    /// <summary>Takes the whole loop (on/off and range) in one push from the loop link; the lit Loop button follows.</summary>
    public void ApplyLoop(PlaybackLoop loop)
    {
        ThrowIfDisposed();
        var flipped = loop.On != _transport.Loop;
        if (flipped) _performedSeekSec = null;
        _transport.ApplyLoop(_project(), loop);
        if (flipped) StateChanged?.Invoke();
    }

    /// <summary>Sets the bars (null: the whole song) and cells a loop wraps within; a running song wraps at the new range at once.</summary>
    public void SetLoopRange(int? startBar, int? endBar, int startCell = 0, int endCell = -1)
    {
        ThrowIfDisposed();
        _transport.SetLoopRange(_project(), startBar, endBar, startCell, endCell);
    }

    /// <summary>Turns the metronome click on or off for this document's playback (the transport's lit button follows).</summary>
    public void SetMetronome(bool on)
    {
        ThrowIfDisposed();
        _transport.SetMetronome(_project(), on);
        StateChanged?.Invoke();
    }

    /// <summary>Turns the count-in on or off for the next start from silence.</summary>
    public void SetCountIn(bool on)
    {
        ThrowIfDisposed();
        _transport.SetCountIn(_project(), on);
        ApplyLoopPreferences();   // the count-in before every section follows the count-in switch
        StateChanged?.Invoke();
    }

    /// <summary>The relative playback speed of this document's playback; 1 is the song's own tempo.</summary>
    public double Speed => _transport.Speed;

    /// <summary>Sets the playback speed; a running song restarts at its position at the new speed, and the next start plays at it.</summary>
    public void SetSpeed(double speed)
    {
        ThrowIfDisposed();
        // The restart plays on from the pass playing now: that second is the new timeline's pass hint (a loop enters by bar and cell instead).
        if (_transport.IsPlaying) _performedSeekSec = _transport.Loop ? null : EngineSongSec();
        _transport.SetSpeed(_project(), speed);
    }

    /// <summary>
    /// Writes the click volumes, click notes, subdivision and count-in bars into the engine's options, and the loop behaviour (number of loops,
    /// count-in before each repeat, speed trainer) and the section count-in into its preferences.
    /// </summary>
    public void ApplyClickOptions(TabForge.Services.AudioSettings audio)
    {
        ThrowIfDisposed();
        _audio = audio;
        ApplyLoopPreferences();
        _engine.UpdateOptions(_project(), options =>
        {
            options.MetronomeVolume = audio.MetronomeVolume;
            options.MetronomeAccentVolume = audio.MetronomeAccentVolume;
            options.MetronomeClickVolume = audio.MetronomeClickVolume;
            options.MetronomeAccentNote = audio.MetronomeAccent;
            options.MetronomeClickNote = audio.MetronomeClick;
            options.MetronomeSubdivision = audio.MetronomeSubdivision;
            options.CountInBars = audio.CountInBars;
        });
        var notes = TabForge.Playback.CountInSoundNotes.Parse(audio.CountInSound);
        _engine.SetPreferences(preferences =>
        {
            preferences.CountInVolume = audio.CountInVolume;
            preferences.MetronomeBoost = audio.MetronomeBoost;
            preferences.CountInAccentNote = notes.AccentNote;
            preferences.CountInClickNote = notes.ClickNote;
        });
        // Every later start takes the same settings (the engine's options are replaced at each start), as the WPF transport's BuildOptions does.
        _transport.SetStartTuning(options =>
        {
            options.CountIn = options.CountIn && (!audio.CountInOnlyAtSongStart || options.StartBar == 0);
            options.CountInBars = audio.CountInBars;
            options.MetronomeAccentNote = audio.MetronomeAccent;
            options.MetronomeClickNote = audio.MetronomeClick;
            options.MetronomeVolume = audio.MetronomeVolume;
            options.MetronomeAccentVolume = audio.MetronomeAccentVolume;
            options.MetronomeClickVolume = audio.MetronomeClickVolume;
            options.MetronomeSubdivision = audio.MetronomeSubdivision;
            options.LetRingCapMs = audio.LetRingCapMs;
        });
    }

    // The section count-in plays only while the count-in is on, as the WPF transport sets it.
    private void ApplyLoopPreferences()
    {
        if (_audio is not { } audio) return;
        var countInEachSection = _transport.CountIn && audio.CountInEachSection;
        _engine.SetPreferences(preferences => (preferences.Loop, preferences.CountInEachSection, preferences.CountInOnSeek) = (LoopSettingsRules.Behaviour(audio), countInEachSection, _transport.CountIn));
    }

    /// <summary>A section moved while this song plays: the playing song takes the new order from the next bar (no restart),
    /// as the WPF section move does. Nothing when this song is not playing.</summary>
    /// <summary>Tracks deleted while this song plays fall silent at once; nothing when it is not playing.</summary>
    internal void SilenceTracks(IReadOnlyCollection<Guid> removed)
    {
        ThrowIfDisposed();
        if (_engine is PlaybackEngine engine && _transport.IsPlaying) engine.SilenceTracks(removed);
    }

    internal void RefreshArrangementLive(SongProject project, int[] baseToCurrentBar)
    {
        ThrowIfDisposed();
        if (_engine is PlaybackEngine engine && _transport.IsPlaying)
            _ = engine.RefreshArrangementInBackground(project, baseToCurrentBar);
    }

    /// <summary>
    /// Runs <paramref name="start"/> once the start now compiling has finished its count-in: the scheduler clock has
    /// reached <see cref="ScoreTimeline.PlayFromMs"/> and is no longer held. Nothing captured before that belongs to the take.
    /// UI thread only.
    /// </summary>
    internal void StartAfterCountIn(Action start) => _afterCountIn = start;

    /// <summary>Drops a start waiting for its count-in (a second Record press or a moved transport); true when one was waiting.</summary>
    internal bool CancelAfterCountIn()
    {
        if (_afterCountIn is null) return false;
        _afterCountIn = null;
        return true;
    }

    // A recording must not start capturing during the count-in: its sample 0 and its take second are the start position P.
    private void TryStartAfterCountIn()
    {
        if (_afterCountIn is not { } start) return;
        // A start whose timeline is still compiling is not a stop: between the compile hand-back and the scheduler
        // setting its running flag the engine briefly reports not playing, and treating that as a stop would drop the
        // pending record so its count-in could never end.
        if (_transport.StartPending) return;
        if (!_transport.IsPlaying)
        {
            _afterCountIn = null;   // stopped before the count-in ended: nothing to record
            return;
        }
        // The compiled count-in plays before the music with the clock running from the timeline origin; a loop or inside-bar
        // start holds the clock at its entry instead. Wait for the timeline, then for the hold to end and the clock to reach P.
        if (_transport.IsPaused || _transport.Timeline is not { } timeline) return;
        if (_engine is PlaybackEngine engine && engine.StartSettling) return;
        if (_engine.Playhead().ElapsedMs + 0.5 < timeline.PlayFromMs) return;
        _afterCountIn = null;
        start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _afterCountIn = null;
        _frames.Stop();
        _frames.Frame -= PumpFrame;
        _transport.Dispose();
    }

    /// <summary>One frame now: applies the newest position and raises the events (the timer calls it; captures call it to show a held moment).</summary>
    internal void PumpFrame()
    {
        var bar = _transport.PlayheadBar;
        var cell = _transport.PlayheadCell;
        _transport.ApplyFrame();
        _clock.Update(_transport);
        // A live frame always draws a position, so the event follows the bar and cell rather than every frame.
        if (_transport.PlayheadBar != bar || _transport.PlayheadCell != cell) PlayheadBarChanged?.Invoke();
        PlayheadFrame?.Invoke();
        SyncState();
        ReportPosition();
        // After the position report, so the engine is told the take's start second before the Record command reads it.
        TryStartAfterCountIn();
    }

    // The engine's song position follows the transport's playhead (the WPF SongClock's anchors): a new anchor on a play-state
    // change or a drift past the threshold, so the engine's audio clips start, follow jumps and stop with the song. Nothing while
    // the start is still compiling (the playhead is held at the old position): the first real frame anchors it. Never per frame.
    private bool? _reportedPlaying = false;
    private double _reportedSec;
    private long _reportedStamp;
    private double _reportedSpeed = PlaybackTransport.DefaultSpeed;

    /// <summary>A play-state change or drift the engine has not been told yet, in song seconds (the WPF clock's 25 ms anchor rule).</summary>
    private const double AnchorDriftSec = 0.025;

    private void ReportPosition()
    {
        if (ReportEnginePosition is null) return;
        var playing = _transport.IsPlaying && !_transport.IsPaused;
        if (playing && _transport.StartPending) return;
        var map = SongMap();
        var sec = map.SongSecAt(_transport.PlayheadMs);
        // The count-in plays before the start point: the engine waits there (not running) and starts once, as the count-in ends.
        var running = playing && !map.InCountIn(_transport.PlayheadMs);
        if (_reportedPlaying != running || (playing && !running && sec != _reportedSec))   // a count-in also tells the engine where to wait, once
        {
            Anchor(running, sec);
            return;
        }
        if (!running) return;
        var now = Stopwatch.GetTimestamp();
        var expected = _reportedSec + (now - _reportedStamp) * _reportedSpeed / Stopwatch.Frequency;
        if (Math.Abs(sec - expected) < AnchorDriftSec && _transport.Speed.Equals(_reportedSpeed)) return;   // a speed change re-anchors once, at the same second
        Anchor(true, sec);
    }

    private TimelineSongClock.Mapping? _songMap;   // the playing timeline's mapping to performed seconds, rebuilt when the timeline or pass hint changes
    private ScoreTimeline? _songMapTimeline;
    private double? _songMapHint;

    /// <summary>
    /// The engine's song position: the playhead's timeline milliseconds as performed song seconds (count-in held at the start, speed
    /// unscaled, repeats and the pass hint applied). Every report to the engine goes through it; the clips are placed on this clock.
    /// </summary>
    private double EngineSongSec() => SongMap().SongSecAt(_transport.PlayheadMs);

    private TimelineSongClock.Mapping SongMap()
    {
        var timeline = _transport.Timeline;
        if (_songMap is null || !ReferenceEquals(timeline, _songMapTimeline) || _songMapHint != _performedSeekSec)
        {
            _songMap = new TimelineSongClock.Mapping(Services.SongExtent.Timing(_project()).Performed, timeline, _performedSeekSec);
            _songMapTimeline = timeline;
            _songMapHint = _performedSeekSec;
        }
        return _songMap;
    }

    private void Anchor(bool playing, double sec)
    {
        _reportedPlaying = playing;
        _reportedSec = Math.Max(0, sec);
        _reportedStamp = Stopwatch.GetTimestamp();
        _reportedSpeed = _transport.Speed;
        ReportEnginePosition?.Invoke(playing, _reportedSec, _reportedSpeed);
    }

    // Keeps the frame timer running only while the song plays, and raises StateChanged when play or pause changed.
    private void SyncState()
    {
        var running = _transport.IsPlaying && !_transport.IsPaused;
        if (running && !_frames.IsRunning) _frames.Start();
        else if (!running && _frames.IsRunning) _frames.Stop();
        if (_transport.IsPlaying == _lastPlaying && _transport.IsPaused == _lastPaused) return;
        _lastPlaying = _transport.IsPlaying;
        _lastPaused = _transport.IsPaused;
        StateChanged?.Invoke();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AvaloniaPlaybackSession));
    }
}
