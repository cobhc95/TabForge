using TabForge.Playback;

namespace TabForge.Views;

/// <summary>
/// The score editor's playback surface: what the transport tells the editor and the geometry the follow logic reads back.
/// Session members are called on start, stop, a document switch or a timeline change; the per-tick members are allocation-free.
/// </summary>
internal interface IScorePlayhead
{
    // ---- session ----

    /// <summary>True while the transport runs (playing or paused). Changing it repaints; it is set on start and stop only, never per tick.</summary>
    bool Active { get; set; }

    /// <summary>The timeline being played, the map from its source bars to the shown bars (or null) and the track whose notes are highlighted.</summary>
    void Bind(ScoreTimeline? timeline, int[]? barRemap, int trackIndex);

    /// <summary>Removes the playhead.</summary>
    void Clear();

    // ---- per tick ----

    /// <summary>Moves the playhead to a bar and cell; repaints only the systems whose playback state changed.</summary>
    void SetPlayhead(int measure, int cell);

    /// <summary>Fraction (0..1) through the playing bar.</summary>
    double Fraction { set; }

    /// <summary>Absolute playback time in milliseconds.</summary>
    double Ms { set; }

    /// <summary>True when a note starts or ends between two times, so the sounding-note overlay must repaint.</summary>
    bool NeedsRepaint(double fromMs, double toMs);

    // ---- follow geometry ----

    (double X, double Top, double Bottom)? PlayheadGeometry();

    IReadOnlyList<(double X, double EndX, double Top, double Bottom)> PlaybackDurationGeometries();

    (int SystemIndex, double SystemLeft, double SystemRight, double PlayheadX, double BarWidth)? PlaybackHorizontalGeometry(int measure, double fraction);
}
