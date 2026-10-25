using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Playback;

// Owns: the immutable thread-safe song copy for background compile and clock mapping (undo-state backed, decoded on first use).
// Does not own: the live song (ProjectState) or the encoder (ProjectStateEncoder).
// Tests: SongSnapshotCostTests, ExportFromCopyTests.

/// <summary>
/// An immutable copy of a song, taken on the thread that owns the song (the thread that edits it) and readable from any thread.
/// Work that runs elsewhere (compiling a timeline in the background, mapping song time for the audio clock) reads the copy instead of
/// the live song, so an edit made meanwhile can never show up half-applied. The copy is the undo state of the song
/// (<see cref="ProjectState"/>): it holds everything that sounds, and it is decoded on first use.
/// </summary>
internal sealed class SongSnapshot : IScoreQuery
{
    private readonly Lazy<SongProject> _copy;

    private SongSnapshot(ProjectState state, RuntimeState runtime, int revision, int contentRevision)
    {
        Revision = revision;
        ContentRevision = contentRevision;
        // Decoded by whichever thread asks first; a decoder of its own keeps the owner's encoder (and its baseline) untouched.
        _copy = new Lazy<SongProject>(() => runtime.ApplyTo(new ProjectStateEncoder().Restore(state, null, validate: false)), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The song's <see cref="SongProject.TimelineRevision"/> when the copy was taken.</summary>
    public int Revision { get; }

    /// <summary>The song's <see cref="SongProject.ContentRevision"/> when the copy was taken: any later edit makes the live song differ from the copy.</summary>
    public int ContentRevision { get; }

    /// <summary>The copy. Never edit it. Playback code outside the snapshot and the compiler reads it through <see cref="IScoreQuery"/>, not directly.</summary>
    internal SongProject Song => _copy.Value;

    public Guid[] TrackIds => Song.Tracks.Select(track => track.Id).ToArray();

    public bool[] AudibleMask(bool liveSolo = false) => PlaybackEngine.AudibleMask(Song, liveSolo);

    public ScoreTimeline Compile(PlaybackOptions options, out int loopCompileStart, CancellationToken cancel = default) =>
        MidiTimelineBuilder.Build(Song, PlaybackSeekRebuild.CompilerOptionsFor(Song, options, out loopCompileStart), cancel);

    public double LoopEntryMs(ScoreTimeline timeline, PlaybackOptions options, int loopCompileStart, double fallbackMs) =>
        PlaybackSeekRebuild.LoopEntryMs(Song, timeline, options, loopCompileStart, fallbackMs);

    public (double startMs, double endMs, bool available) LoopBounds(ScoreTimeline timeline, PlaybackOptions options) => PlaybackOrder.LoopBounds(timeline, Song, options);

    public ArrangementRefreshCompiler.Plan CompileFuture(ScoreTimeline current, PlaybackOptions options, int[] baseToCurrentBar, ScoreBar activeBar, int? continueAtBar) =>
        ArrangementRefreshCompiler.CompileFuture(current, Song, options, baseToCurrentBar, activeBar, continueAtBar);

    public ScoreTimeline CompileLoop(int[] baseToCurrentBar, ArrangementRefreshCompiler.Plan plan) => ArrangementRefreshCompiler.CompileLoop(Song, baseToCurrentBar, plan);

    /// <summary>Copies <paramref name="live"/> now (owner thread). A long-lived <paramref name="encoder"/> shares unchanged bars between successive snapshots.</summary>
    public static SongSnapshot Take(SongProject live, ProjectStateEncoder? encoder = null)
    {
        var revision = live.TimelineRevision;
        var contentRevision = live.ContentRevision;
        return new SongSnapshot((encoder ?? new ProjectStateEncoder()).Encode(live), RuntimeState.Read(live), revision, contentRevision);
    }

    /// <summary>
    /// The runtime-only state the compile reads (never saved, so not in the undo state): the app-wide mixer rules, each track's manual GM-off and each
    /// plug-in's bypassed-by-container and engine-unavailable flags. Read once on the owner thread at <see cref="Take"/>, matched to the copy by track and plug-in id.
    /// </summary>
    private sealed record RuntimeState(MixerAppRules? App, Dictionary<Guid, TrackRuntime> Tracks)
    {
        public static RuntimeState Read(SongProject live) => new(live.Mixer.App, live.Tracks.DistinctBy(track => track.Id).ToDictionary(track => track.Id, track => new TrackRuntime(
            track.MidiSoundManualOff, track.Rig.Plugins.DistinctBy(plugin => plugin.Id).ToDictionary(plugin => plugin.Id, plugin => (plugin.GroupBypassed, plugin.Unavailable)))));

        public SongProject ApplyTo(SongProject copy)
        {
            copy.Mixer.App = App;
            foreach (var track in copy.Tracks)
            {
                if (!Tracks.TryGetValue(track.Id, out var runtime)) continue;
                track.MidiSoundManualOff = runtime.ManualOff;
                foreach (var plugin in track.Rig.Plugins)
                    if (runtime.Plugins.TryGetValue(plugin.Id, out var flags)) (plugin.GroupBypassed, plugin.Unavailable) = flags;
            }
            return copy;
        }
    }

    private sealed record TrackRuntime(bool ManualOff, Dictionary<string, (bool GroupBypassed, bool Unavailable)> Plugins);
}
