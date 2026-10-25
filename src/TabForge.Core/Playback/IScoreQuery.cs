using TabForge.Models;

namespace TabForge.Playback;

// Owns: the narrow read-only view of a song that the scheduler thread and the pool threads (start, seek and arrangement compiles) are allowed to use.
// Does not own: the song itself (SongSnapshot holds the immutable copy) or the live song (owner thread only, see PlaybackEngine.LiveProject).
// Tests: PlaybackScoreQueryGateTests, PlaybackSnapshotIsolationTests.

/// <summary>What background playback work asks of a song: revisions, track ids, audibility, the compile and the loop bounds. Never the live song.</summary>
internal interface IScoreQuery
{
    /// <summary>The song's <see cref="SongProject.TimelineRevision"/> when the view was taken.</summary>
    int Revision { get; }

    /// <summary>The song's <see cref="SongProject.ContentRevision"/> when the view was taken.</summary>
    int ContentRevision { get; }

    /// <summary>The ids of the tracks in the timeline's index space.</summary>
    Guid[] TrackIds { get; }

    /// <summary>Per track, whether mute and solo let it sound.</summary>
    bool[] AudibleMask(bool liveSolo = false);

    /// <summary>Compiles the timeline for <paramref name="options"/> (see <see cref="PlaybackSeekRebuild.CompilerOptionsFor"/>); <paramref name="loopCompileStart"/> is the bar a loop was compiled from, or -1.</summary>
    ScoreTimeline Compile(PlaybackOptions options, out int loopCompileStart, CancellationToken cancel = default);

    /// <summary>The song ms where playback begins (see <see cref="PlaybackSeekRebuild.LoopEntryMs"/>).</summary>
    double LoopEntryMs(ScoreTimeline timeline, PlaybackOptions options, int loopCompileStart, double fallbackMs);

    /// <summary>The ms range of the loop in <paramref name="timeline"/> and whether it exists.</summary>
    (double startMs, double endMs, bool available) LoopBounds(ScoreTimeline timeline, PlaybackOptions options);

    /// <summary>The future of a running timeline recompiled for a changed arrangement.</summary>
    ArrangementRefreshCompiler.Plan CompileFuture(ScoreTimeline current, PlaybackOptions options, int[] baseToCurrentBar, ScoreBar activeBar, int? continueAtBar);

    /// <summary>The whole song compiled again for the next loop wrap after an arrangement refresh.</summary>
    ScoreTimeline CompileLoop(int[] baseToCurrentBar, ArrangementRefreshCompiler.Plan plan);
}
