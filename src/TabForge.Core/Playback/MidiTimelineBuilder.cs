using TabForge.Models;

namespace TabForge.Playback;

// Owns: the public entry point that compiles a score into the MIDI timeline (a thin facade).
// Does not own: the compile rules (ScoreToMidiCompiler).
// Tests: no dedicated test class.

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

    /// <summary>Compiles like <see cref="Build(SongProject, PlaybackOptions)"/>; a cancelled <paramref name="cancel"/> throws OperationCanceledException at the next bar.</summary>
    internal static ScoreTimeline Build(SongProject project, PlaybackOptions opt, CancellationToken cancel) =>
        new ScoreToMidiCompiler(project, opt, null, cancel).Build();

    /// <summary>Bar indices (source bars) in performance order, repeats/endings expanded.</summary>
    public static List<int> BuildPlaybackOrder(SongProject project, PlaybackOptions opt) =>
        PlaybackOrder.Build(project, opt);

    public static (int startBar, int endBar) LoopRange(SongProject project, PlaybackOptions opt) =>
        PlaybackOrder.LoopRange(project, opt);
}
