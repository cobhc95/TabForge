namespace TabForge.Playback;

/// <summary>
/// Settings-driven playback behaviour every engine reads: metronome boost, count-in sound and level, section
/// count-in and loop behaviour. The application's <c>AppOptions</c> owns the shared instance; a window's document
/// manager hands it to the engine of every document it holds (an engine built on its own starts with a private one), and the settings applier updates it.
/// </summary>
public sealed class PlaybackPreferences
{
    /// <summary>Layers each click with extra sharp percussion hits so it cuts through a loud mix.</summary>
    public bool MetronomeBoost { get; set; } = true;
    /// <summary>Count-in click level (0-100 %), separate from the metronome; boosted like the metronome.</summary>
    public int CountInVolume { get; set; } = 70;
    /// <summary>Count-in sound; -1 = use the metronome notes.</summary>
    public int CountInAccentNote { get; set; } = -1;
    public int CountInClickNote { get; set; } = -1;
    /// <summary>Count-in before every section start while playing (bars from the song's markers).</summary>
    public bool CountInEachSection { get; set; }
    /// <summary>Loop behaviour read by the scheduler at every wrap (cheap: no recompile, no restart).</summary>
    public PlaybackEngine.LoopBehaviour Loop { get; set; } = new();
}
