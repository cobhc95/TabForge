using System;
using System.Linq;
using TabForge.Models;

namespace TabForge.Playback;

// Owns: one document's transport over an IPlaybackEngine (play, stop, pause and resume, seek, loop, speed), the newest scheduler report handed to
//     the UI frame, and the playhead fields a frame draws (time, bar, cell, fraction).
// Does not own: the frame timer (the host calls ApplyFrame once per frame), the song (passed in per call), MIDI, the views.
// Tests: TabForge.Avalonia.Tests.PlaybackTransportTests (fake engine).
// Threads: transport commands, ApplyFrame and Dispose run on the UI thread. ReportPosition and MarkFinished (the engine's callbacks) run on
//     the scheduler thread; they reach the UI only through PendingPlayheadSlot's lock. ApplyFrame allocates nothing in steady state.

/// <summary>The toolkit-free transport and playhead of one document.</summary>
public sealed class PlaybackTransport : IDisposable
{
    /// <summary>The playback speed of a song played at its own tempo.</summary>
    public const double DefaultSpeed = 1.0;
    /// <summary>The slowest playback speed the transport accepts.</summary>
    public const double MinSpeed = 0.25;
    /// <summary>The fastest playback speed the transport accepts.</summary>
    public const double MaxSpeed = 2.0;
    private readonly IPlaybackEngine _engine;
    private readonly PendingPlayheadSlot _pending = new();
    private readonly PlaybackPosition _audibleFrame = new();
    private readonly Action<PlaybackPosition> _onPosition;
    private readonly Action _onFinished;
    private Action<PlaybackOptions>? _startTuning;   // the app's audio settings for every start; null applies none
    private readonly Action<ScoreTimeline> _onTimelineRevised;
    private int _startBar;
    private int _startCell;
    private int? _startOccurrence;   // the pass-aware start's occurrence of _startBar; null is the unchanged start
    private (int Bar, int Cell)? _seekPosition;
    private PlaybackLoop _loop = PlaybackLoop.Off;   // the loop as the owner (SelectionLoopLink, from SelectionModel) last pushed it; UI thread only
    private bool _metronome;
    private bool _countIn;
    private double _speed = DefaultSpeed;
    private bool _disposed;

    public PlaybackTransport(IPlaybackEngine engine)
    {
        _engine = engine;
        // Cached delegates: the engine is handed the same two callbacks for every start, so starting allocates nothing here.
        _onPosition = _pending.Report;
        _onFinished = _pending.MarkFinished;
        _onTimelineRevised = _pending.ReportTimeline;
        _engine.TimelineChanged += OnTimelineChanged;
        _engine.TimelineRevised += _onTimelineRevised;
    }

    public bool IsPlaying => _engine.IsPlaying;
    public bool IsPaused => _engine.IsPaused;
    public bool Loop => _loop.On;

    /// <summary>The loop as last pushed (<see cref="ApplyLoop"/>).</summary>
    public PlaybackLoop LoopState => _loop;
    public bool Metronome => _metronome;
    public bool CountIn => _countIn;
    /// <summary>The relative playback speed the song plays at; DefaultSpeed is the song's own tempo.</summary>
    public double Speed => _speed;

    /// <summary>The bar and cell of the latest <see cref="Seek"/> (where a stopped song starts from), or null before any seek.</summary>
    public (int Bar, int Cell)? SeekPosition => _seekPosition;

    /// <summary>The song time the playhead shows, in milliseconds.</summary>
    public double PlayheadMs { get; private set; }
    /// <summary>The bar the playhead is in, or -1 when nothing plays.</summary>
    public int PlayheadBar { get; private set; } = -1;
    public int PlayheadCell { get; private set; }
    public double PlayheadFraction { get; private set; }
    /// <summary>The timeline the engine last compiled, or null when none.</summary>
    public ScoreTimeline? Timeline { get; private set; }
    /// <summary>True from a start until the engine hands over its compiled timeline: <see cref="Timeline"/> is still the previous run's meanwhile.</summary>
    public bool StartPending { get; private set; }

    /// <summary>
    /// Starts from the last seek position (bar 0 at first), looping over the whole song when looping is on. It returns at once: the song
    /// compiles on a worker and the audio begins when it is ready, with the playhead held at the start meanwhile. A repeated Play while
    /// the start still compiles is no start (idempotent): mashing Play would otherwise restart the compile each time and the song would
    /// never begin. A pause, resume or seek while it compiles still applies.
    /// </summary>
    public void Play(SongProject project)
    {
        if (StartPending) return;
        BeginStart(project, StartOptions(project, _startBar, _startCell, _countIn), startPaused: false);
    }

    private PlaybackOptions StartOptions(SongProject project, int bar, int cell, bool countIn)
    {
        var options = new PlaybackOptions
        {
            StartBar = bar,
            StartCell = cell,
            StartOccurrence = _loop.On ? null : _startOccurrence,   // T1 enters a loop by bar/cell, not by a pass
            Loop = _loop.On,
            LoopStartBar = _loop.StartBar ?? 0,
            LoopEndBar = _loop.EndBar ?? LastBar(project),
            LoopStartCell = _loop.StartCell,
            LoopEndCell = _loop.EndCell,
            Metronome = _metronome,
            CountIn = countIn,
            Speed = _speed,
        };
        // The start replaces the engine's options, so the app's audio settings are applied to each start here.
        _startTuning?.Invoke(options);
        // A start from the middle of the song (a speed change while playing) never replays the count-in, whatever the settings say.
        if (!countIn) options.CountIn = false;
        return options;
    }

    private void BeginStart(SongProject project, PlaybackOptions options, bool startPaused)
    {
        _engine.SetSectionStarts(project.Markers.Select(marker => marker.MeasureIndex).Where(bar => bar > 0));
        StartPending = true;   // set first: an engine that starts at once hands its timeline over inside BeginStart
        _engine.BeginStart(project, options, _onPosition, _onFinished, startPaused);
    }

    /// <summary>Applies the app's audio settings (count-in, click, let-ring) to every start from now on; null applies none.</summary>
    public void SetStartTuning(Action<PlaybackOptions>? tuning) => _startTuning = tuning;

    /// <summary>Stops the engine and clears the playhead. The seek position stays for the next start.</summary>
    public void Stop()
    {
        _engine.Stop();
        ClearPlayhead();
    }

    /// <summary>Pauses a playing song; resumes a paused one. Does nothing when stopped.</summary>
    public void PauseOrResume()
    {
        if (!_engine.IsPlaying) return;
        if (_engine.IsPaused) _engine.Resume();
        else _engine.Pause();
    }

    /// <summary>
    /// Moves the playhead to a bar and cell: a playing song jumps there, and the playhead shows the target at once while the engine resumes
    /// there; a stopped one starts there next time.
    /// </summary>
    public void Seek(SongProject project, int bar, int cell) => Seek(project, bar, cell, null);

    /// <summary>
    /// Seeks to a pass-aware start: the <paramref name="occurrence"/>-th performance of <paramref name="bar"/> (1-based),
    /// or the unchanged start when it is null/1. The next play compiles from that performance.
    /// </summary>
    public void Seek(SongProject project, int bar, int cell, int? occurrence)
    {
        _startBar = Math.Max(0, bar);
        _startCell = Math.Max(0, cell);
        // T1: an active loop compiles whole passes and enters by bar/cell, so a pass-aware occurrence does not apply there.
        _startOccurrence = occurrence is > 1 && !_loop.On ? occurrence : null;
        _seekPosition = (_startBar, _startCell);
        if (_engine.IsPlaying) _engine.Seek(project, _startBar, _startCell, _startOccurrence);
        PlayheadBar = _startBar;
        PlayheadCell = _startCell;
        PlayheadFraction = 0;
    }

    /// <summary>
    /// Sets the range a loop wraps within, both bars inclusive (null bars mean the song's first and last bar), from <paramref name="startCell"/> of the first
    /// bar to <paramref name="endCell"/> of the last (-1: the end of that bar). A running song wraps at the new range at once, without a restart.
    /// </summary>
    public void SetLoopRange(SongProject project, int? startBar, int? endBar, int startCell = 0, int endCell = -1) =>
        ApplyLoop(project, _loop with { StartBar = startBar, EndBar = endBar, StartCell = Math.Max(0, startCell), EndCell = endCell });

    /// <summary>
    /// Takes the whole loop in one push (on/off and range); the engine hears only what changed. The one writer is the window's loop link, which reads
    /// SelectionModel (the one source of the loop), so nothing here is a second copy anyone else edits.
    /// </summary>
    public void ApplyLoop(SongProject project, PlaybackLoop loop)
    {
        var before = _loop;
        _loop = loop with { StartCell = Math.Max(0, loop.StartCell) };
        if ((before.StartBar, before.EndBar, before.StartCell, before.EndCell) != (_loop.StartBar, _loop.EndBar, _loop.StartCell, _loop.EndCell) && _engine.IsPlaying)
            _engine.SetLoopRange(_loop.StartBar ?? 0, _loop.EndBar ?? LastBar(project), _loop.StartCell, _loop.EndCell);
        if (before.On == _loop.On) return;
        if (_loop.On) _startOccurrence = null;   // a loop enters by bar/cell; turning it off later must not revive an old pass-aware start
        _engine.SetLoop(_loop.On);
    }

    private static int LastBar(SongProject project) =>
        project.Tracks.Count == 0 ? 0 : Math.Max(0, project.Tracks.Max(t => t.Measures.Count) - 1);

    public void SetLoop(bool loop)
    {
        _loop = _loop with { On = loop };
        if (loop) _startOccurrence = null;   // a loop enters by bar/cell; turning it off later must not revive an old pass-aware start
        _engine.SetLoop(loop);
    }

    /// <summary>Turns the metronome click on or off; a running song picks the change up at once.</summary>
    public void SetMetronome(SongProject project, bool on)
    {
        _metronome = on;
        _engine.UpdateOptions(project, options => options.Metronome = on);
    }

    /// <summary>Turns the count-in on or off; a running song keeps playing, and the count-in applies from the next start from silence.</summary>
    public void SetCountIn(SongProject project, bool on)
    {
        _countIn = on;
        if (!_engine.IsPlaying) _engine.UpdateOptions(project, options => options.CountIn = on);
    }

    /// <summary>
    /// Sets the relative playback speed, clamped to MinSpeed and MaxSpeed; the next start plays at it. A running song starts again at its
    /// position at the new speed, compiled on a worker like Play (no count-in), keeping its paused state.
    /// </summary>
    public void SetSpeed(SongProject project, double speed)
    {
        var clamped = Math.Clamp(speed, MinSpeed, MaxSpeed);
        var changed = !clamped.Equals(_speed);
        _speed = clamped;
        if (!_engine.IsPlaying || !changed)
        {
            _engine.UpdateOptions(project, options => options.Speed = _speed);
            return;
        }
        var at = _engine.Playhead();
        var occurrence = _loop.On ? null : _engine.LiveOccurrence(project);   // the pass playing now, read before the start moves
        _startOccurrence = null;   // the seek's pass-aware occurrence is spent; the restart carries the live one below
        var options = StartOptions(project, Math.Max(0, at.Bar), Math.Max(0, at.Cell), countIn: false);
        options.StartOccurrence = occurrence;
        BeginStart(project, options, _engine.IsPaused);
    }

    /// <summary>
    /// One rendered frame: applies the finish flag, or the newest scheduler report (or, while the song plays live, the audible position).
    /// Returns true when the playhead fields changed this frame.
    /// </summary>
    public bool ApplyFrame()
    {
        if (_pending.TakeTimeline(out var revised)) Timeline = revised;
        if (_pending.TakeFinished())
        {
            ClearPlayhead();
            return true;
        }
        var live = PlaybackFrameRules.DrawsLiveClock(_engine.IsPlaying, _engine.IsPaused);
        var hasReport = _pending.TryTake(out var position);
        if (live) position = _engine.AudiblePlayhead(_audibleFrame);
        else if (!hasReport) return false;
        if (position is null) return false;

        PlayheadMs = position.ElapsedMs;
        PlayheadBar = position.Bar;
        PlayheadCell = position.Cell;
        PlayheadFraction = position.BarFraction;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.TimelineChanged -= OnTimelineChanged;
        _engine.TimelineRevised -= _onTimelineRevised;
        if (_engine.IsPlaying) _engine.Stop();
        _engine.Dispose();
        _pending.Clear();
        Timeline = null;
    }

    private void ClearPlayhead()
    {
        StartPending = false;
        _pending.Clear();
        PlayheadMs = 0;
        PlayheadBar = -1;
        PlayheadCell = 0;
        PlayheadFraction = 0;
    }

    private void OnTimelineChanged(ScoreTimeline timeline)
    {
        StartPending = false;
        Timeline = timeline;
        PlayheadMs = timeline.PlayFromMs;
        PlayheadBar = -1;
        PlayheadCell = 0;
        PlayheadFraction = 0;
    }
}

/// <summary>The loop the transport plays: on/off and the bars (null: the song's first/last bar) and cells it wraps within (EndCell -1: the end of that bar).</summary>
public readonly record struct PlaybackLoop(bool On, int? StartBar, int? EndBar, int StartCell, int EndCell)
{
    public static PlaybackLoop Off => new(false, null, null, 0, -1);
}
