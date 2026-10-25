namespace TabForge.Playback;

// Owns: the per-frame playback decisions: which clock the frame draws (live audible clock or the newest report), the bar remap, and which
//     views one frame refreshes (bar change, note boundary, score and instrument fallback cadences); the audible-time maths.
// Does not own: the views, the engine clock, the pending report slot (PendingPlayheadSlot).
// Tests: CheckMixFadeAndAudiblePlayhead.
// Threads: the static members are pure and run on any thread; an instance belongs to one window's UI thread (the frame tick) and is not
//     locked. Decide allocates nothing.

/// <summary>What one rendered frame refreshes.</summary>
public readonly record struct PlaybackFrameDecision(bool BarChanged, bool RepaintScore, bool RefreshInstrument);

/// <summary>The toolkit-free rules one playback frame follows; holds the previous frame's bar, cell, time and refresh stamps.</summary>
public sealed class PlaybackFrameRules
{
    /// <summary>Fallback score repaint cadence while nothing else changes.</summary>
    public const double ScoreCadenceMs = 250;
    /// <summary>Fallback instrument refresh cadence while nothing else changes.</summary>
    public const double InstrumentCadenceMs = 200;

    private int _lastBar = -1;
    private int _lastCell = -1;
    private DateTime _lastScoreUpdate = DateTime.MinValue;
    private DateTime _lastInstrumentUpdate = DateTime.MinValue;

    /// <summary>The song time the previous frame drew (the start of the next note-boundary check).</summary>
    public double LastMs { get; private set; }

    /// <summary>While it plays, every frame draws what is heard now: the scheduler reports on a song-time grid coarser than the display.</summary>
    public static bool DrawsLiveClock(bool isPlaying, bool isPaused) => isPlaying && !isPaused;

    /// <summary>The shown bar for a played bar: the remap entry when it has one, else the bar itself.</summary>
    public static int RemapBar(int[]? remap, int bar) => remap is not null && bar >= 0 && bar < remap.Length ? remap[bar] : bar;

    /// <summary>
    /// Song position being heard: the scheduler position minus the output latency. The latency is wall-clock time and the timeline is
    /// already compiled at the playback speed, so it converts at the clock rate (speed trainer) only.
    /// </summary>
    public static double AudibleStreamMs(double schedulerMs, double latencyWallMs, double clockRate) =>
        schedulerMs - Math.Max(0, latencyWallMs) * Math.Clamp(clockRate, 0.1, 4);

    /// <summary>Forgets the previous frame, so the next one counts as a bar change; <paramref name="lastMs"/> starts the note-boundary check.</summary>
    public void Reset(double lastMs)
    {
        _lastBar = -1;
        _lastCell = -1;
        LastMs = lastMs;
    }

    /// <summary>Decides what the frame at (<paramref name="bar"/>, <paramref name="cell"/>) refreshes and stamps the cadences it uses.</summary>
    public PlaybackFrameDecision Decide(int bar, int cell, bool noteBoundary, bool instrumentAnimating, DateTime now)
    {
        var barChanged = bar != _lastBar || cell != _lastCell;
        var repaintScore = barChanged || noteBoundary || (now - _lastScoreUpdate).TotalMilliseconds >= ScoreCadenceMs;
        if (repaintScore) _lastScoreUpdate = now;
        // A drum hit starts at a note boundary; per-frame refreshes run only while its glow is fading.
        var refreshInstrument = instrumentAnimating || barChanged || noteBoundary || (now - _lastInstrumentUpdate).TotalMilliseconds >= InstrumentCadenceMs;
        if (refreshInstrument) _lastInstrumentUpdate = now;
        return new PlaybackFrameDecision(barChanged, repaintScore, refreshInstrument);
    }

    /// <summary>Records the frame just drawn.</summary>
    public void Commit(int bar, int cell, double ms)
    {
        _lastBar = bar;
        _lastCell = cell;
        LastMs = ms;
    }
}
