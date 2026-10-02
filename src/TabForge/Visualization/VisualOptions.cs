namespace TabForge.Visualization;

/// <summary>
/// Settings that shape the code-drawn views: the fretboard's score-following layout and the track-row tint. The
/// builders take an instance (the application's <c>AppOptions.Visual</c>), which the settings applier updates.
/// </summary>
public sealed class VisualOptions
{
    /// <summary>Null = TabForge look-ahead; otherwise the score-following "Show [Beat] / [Bar]" layout.</summary>
    public Gp5FretboardMode? Gp5Mode { get; set; }

    /// <summary>Track colour tint strength (0..0.6), from Settings > Appearance.</summary>
    public double TrackTint { get; set; } = 0.2;

    /// <summary>How strongly an explicitly muted track is greyed in its row and lane (0..1; 0 = not at all), from Settings > Appearance.</summary>
    public double MutedDim { get; set; } = 0.8;
}
