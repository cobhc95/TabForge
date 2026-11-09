using TabForge.Models;

namespace TabForge.Playback;

// Owns: the seam through which the app humanizes a compiled timeline for playback and render (PlaybackOptions.Humanizer).
// Does not own: the pass (the app's Transport/SynthHumanizePass), the compile (ScoreToMidiCompiler), the event order (SustainResolver).
// Tests: HumanizerTests.

/// <summary>Moves the notes of the tracks it plays to how a player would play them. Null on the options: notes play as written.</summary>
public interface ITimelineHumanizer
{
    /// <summary>What the pass would do now (the settings it reads); null when it is off. A schedule compiled under another key is not reused.</summary>
    string? Key { get; }

    /// <summary>Changes note-on / note-off times and velocities (and the times of their channels' controllers) in place; the compiler sorts afterwards.</summary>
    void Apply(ScoreTimeline timeline, SongProject project);
}
