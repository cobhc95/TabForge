using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// An immutable copy of a song, taken on the thread that owns the song (the thread that edits it) and readable from any thread.
/// Work that runs elsewhere (compiling a timeline in the background, mapping song time for the audio clock) reads the copy instead of
/// the live song, so an edit made meanwhile can never show up half-applied. The copy is the undo state of the song
/// (<see cref="ProjectState"/>): it holds everything that sounds, and it is decoded on first use.
/// </summary>
internal sealed class SongSnapshot
{
    private readonly Lazy<SongProject> _copy;

    private SongSnapshot(ProjectState state, int revision)
    {
        Revision = revision;
        // Decoded by whichever thread asks first; a decoder of its own keeps the owner's encoder (and its baseline) untouched.
        _copy = new Lazy<SongProject>(() => new ProjectStateEncoder().Restore(state, null, validate: false), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The song's <see cref="SongProject.TimelineRevision"/> when the copy was taken.</summary>
    public int Revision { get; }

    /// <summary>The copy. Never edit it.</summary>
    public SongProject Song => _copy.Value;

    /// <summary>Copies <paramref name="live"/> now (owner thread). A long-lived <paramref name="encoder"/> shares unchanged bars between successive snapshots.</summary>
    public static SongSnapshot Take(SongProject live, ProjectStateEncoder? encoder = null)
    {
        var revision = live.TimelineRevision;
        return new SongSnapshot((encoder ?? new ProjectStateEncoder()).Encode(live), revision);
    }
}
