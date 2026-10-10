namespace TabForge.KeyboardMode;

// Owns: one Keyboard mode run on a keyboard track: the judge and score for the song's notes, fed with the keys the listener queued and advanced with the song position each frame;
//   a fresh run when the source changes, the song restarts, a seek happens or a loop wraps.
// Does not own: where the song position comes from (KeyboardModeFrameController), the MIDI devices (KeyboardModeMidiListener / MidiInputHub), the drawing, or any change to the song or its arm.
// Tests: TestKeyboardModeMidiListener.
internal sealed class KeyboardModeKeyboardSession : IDisposable
{
    /// <summary>A note this far behind the new start position is still played; the judge's good window needs it.</summary>
    private const double StartSlackSec = 0.05;
    /// <summary>Misses are declared this long late so a key that is still on its way from the driver thread is not turned into one.</summary>
    private const double MissGraceSec = 0.02;

    private readonly KeyboardModeMidiListener _listener;
    private KeyboardNoteSource? _source;
    private bool _wasRunning;
    private int _wraps;

    public KeyboardModeKeyboardSession(KeyboardModeMidiListener listener) { _listener = listener; }

    /// <summary>The totals of the current run.</summary>
    public KeyboardModeScore Score { get; } = new();
    /// <summary>The judge of the current run (its results, in onset order, are the last results); null until a source is set.</summary>
    public KeyboardModeJudge? Judge { get; private set; }
    /// <summary>How many fresh runs were started (a self-test counter).</summary>
    public int Runs { get; private set; }
    public bool IsListening => _listener.IsListening;
    /// <summary>Wait mode of this run (set by the controller; null or disabled: the song never waits).</summary>
    public KeyboardModeWaitMode? Wait { get; set; }
    /// <summary>How forgiving the judge is; a change starts a fresh run.</summary>
    public KeyboardModeTolerances Tolerances
    {
        get => _tol;
        set { if (_tol == value) return; _tol = value; Judge = null; }
    }
    private KeyboardModeTolerances _tol = KeyboardModeTolerances.Default;
    /// <summary>Which hand is judged and awaited; a change starts a fresh run.</summary>
    public KeyboardHandsFilter Hands
    {
        get => _hands;
        set { if (_hands == value) return; _hands = value; Judge = null; }
    }
    private KeyboardHandsFilter _hands;

    /// <summary>What the view shows about the player: held keys and the latest grade flash.</summary>
    public KeyboardFeedback Feedback { get; } = new();

    /// <summary>The keyboard source of the selected track, or null when it is not a keyboard track; <paramref name="listen"/> turns the MIDI subscription on or off.</summary>
    public void SetSource(KeyboardNoteSource? source, bool listen)
    {
        if (!ReferenceEquals(source, _source)) { _source = source; _offsets.Clear(); _biasSec = 0; Wait?.Rearm(); Judge = null; Score.Reset(); Feedback.Reset(); _wasRunning = false; }
        if (source is not null && listen) _listener.Start(); else _listener.Stop();
    }

    /// <summary>One frame. <paramref name="songMs"/> is the playing position; <paramref name="jumped"/> a seek and <paramref name="wraps"/> the loop passes (as <see cref="KeyboardModeClock"/> reports them).</summary>
    public void Update(double songMs, bool running, bool paused, KeyboardModeLoop? loop, int wraps, bool jumped, double latencySec)
    {
        if (_source is null) return;
        _listener.KeepUnstamped = true;   // a key pressed while stopped or paused still shows held; the judge ignores its unknown time, wait mode reads its pitch
        if (running && (!_wasRunning || jumped || wraps != _wraps || Judge is null)) StartRun(songMs, loop);
        _wasRunning = running;
        _wraps = wraps;
        if (!running) Wait?.Drop(); else if (Judge is null) Wait?.Rearm();
        if (!running || Judge is null) { _listener.Drain(e => Feedback.Played(e, KeyHold.Held)); return; }
        _listener.Drain(e => { e = Calibrated(e); var hit = Judge.Feed(e); if (hit is null) Wait?.Press(e); Feedback.Played(e, hit is not null || Wait?.Awaits(e.Midi) == true); });
        if (Wait is { Enabled: true } wait) { wait.Step(Judge, songMs / 1000, running); paused |= wait.IsWaiting; }
        if (!paused) Judge.Advance(KeyboardModeJudge.Compensate(songMs / 1000, latencySec) - MissGraceSec - Math.Max(0, _biasSec));
        Score.Absorb(Judge);
        Feedback.Scan(Judge, Environment.TickCount64);
    }

    /// <summary>Leaves the MIDI input (the pane is hidden); the run's results stay.</summary>
    public void Stop() { _listener.Stop(); _wasRunning = false; Wait?.Rearm(); Feedback.Reset(); }

    /// <summary>The listener (its device choice and the Thru messages for the practice track).</summary>
    public KeyboardModeMidiListener Listener => _listener;

    public void Dispose() { Wait?.Rearm(); _listener.Dispose(); }

    /// <summary>A press with the player's steady lead or lag taken out: when the recent presses of the same keys were all about the same distance from their notes (a clock or latency offset, not sloppy playing),
    /// that distance is removed from the next presses, so the song and the keys line up whatever the cause.</summary>
    private KeyboardModePlayed Calibrated(KeyboardModePlayed e)
    {
        if (!e.On || double.IsNaN(e.TimeSec) || Judge is null) return e;
        var raw = Judge.NearestOffsetMs(new KeyboardModePlayed(e.Midi, true, e.TimeSec + _biasSec));
        if (!double.IsNaN(raw))
        {
            _offsets.Enqueue(raw + _biasSec * 1000);   // the offset the press would have had without any bias
            while (_offsets.Count > OffsetWindow) _offsets.Dequeue();
            _biasSec = BiasFrom(_offsets);
        }
        return e with { TimeSec = e.TimeSec - _biasSec };
    }

    private static double BiasFrom(Queue<double> offsets)
    {
        if (offsets.Count < MinOffsets) return 0;
        var sorted = offsets.OrderBy(x => x).ToArray();
        var median = sorted[sorted.Length / 2];
        var spread = sorted[sorted.Length - 1 - sorted.Length / 4] - sorted[sorted.Length / 4];
        return spread <= MaxSpreadMs && Math.Abs(median) >= MinBiasMs ? Math.Clamp(median, -MaxBiasMs, MaxBiasMs) / 1000 : 0;
    }

    private const int OffsetWindow = 8, MinOffsets = 3;
    private const double MaxSpreadMs = 160, MinBiasMs = 30, MaxBiasMs = 700;
    private readonly Queue<double> _offsets = new();
    private double _biasSec;

    private void StartRun(double songMs, KeyboardModeLoop? loop)
    {
        var from = (loop is { IsUsable: true } l && songMs < l.StartMs ? l.StartMs : songMs) / 1000 - StartSlackSec;
        var to = loop is { IsUsable: true } l2 ? l2.EndMs / 1000 : double.MaxValue;
        Judge = new KeyboardModeJudge(_source!.Expected(_hands).Where(x => x.OnsetSec >= from && x.OnsetSec < to), _tol);
        Score.Reset();
        Feedback.Reset();
        _listener.Drain(_ => { });   // keys from before the start belong to no run
        Runs++;
    }
}
