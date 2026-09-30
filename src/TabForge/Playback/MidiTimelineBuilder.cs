using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Public entry point for score -> MIDI compilation. Thin facade over <see cref="ScoreToMidiCompiler"/>
/// so callers depend on one small surface.
/// </summary>
public static class MidiTimelineBuilder
{
    public static ScoreTimeline Build(SongProject project, PlaybackOptions opt) =>
        new ScoreToMidiCompiler(project, opt).Build();

    internal static ScoreTimeline Build(SongProject project, PlaybackOptions opt, IReadOnlyList<int> playbackOrder) =>
        new ScoreToMidiCompiler(project, opt, playbackOrder).Build();

    /// <summary>Bar indices (source bars) in performance order, repeats/endings expanded.</summary>
    public static List<int> BuildPlaybackOrder(SongProject project, PlaybackOptions opt) =>
        PlaybackOrder.Build(project, opt);

    public static (int startBar, int endBar) LoopRange(SongProject project, PlaybackOptions opt) =>
        PlaybackOrder.LoopRange(project, opt);
}
