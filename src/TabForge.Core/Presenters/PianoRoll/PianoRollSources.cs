using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the contract between the piano roll and where a track's notes live: its clock, reading its notes as piano-roll notes, and writing an
//       edited note list back inside the caller's undo step; and the choice of source for a track (MIDI clips or written tab).
// Does not own: the sources themselves (PianoRollClipSource, PianoRollTabSource) or the undo step (PianoRollPresenter through DocumentEdits).
// Tests: TabForge.Avalonia.Tests.PianoRollSourceTests.
public interface IPianoRollSource
{
    /// <summary>True when edits rewrite written tab (pitch and timing edits then ask once per visit).</summary>
    bool IsTab { get; }

    /// <summary>The clock the track's notes are laid out on.</summary>
    PianoRollTimeMap Map(SongProject song);

    /// <summary>The track's notes in a stable order (by start, then pitch).</summary>
    List<PianoRollNote> Read(SongProject song, TrackModel track, PianoRollTimeMap map);

    /// <summary>Stores <paramref name="after"/> as the track's notes; false when nothing changed. Runs inside the caller's undo step.</summary>
    bool Write(SongProject song, TrackModel track, PianoRollTimeMap map, IReadOnlyList<PianoRollNote> after);
}

public static class PianoRollSources
{
    private static readonly IPianoRollSource Clips = new PianoRollClipSource();
    private static readonly IPianoRollSource Tab = new PianoRollTabSource();

    /// <summary>The source of <paramref name="track"/>'s notes: its MIDI clips for a MIDI track, else its written tab.</summary>
    public static IPianoRollSource For(TrackModel track) => PianoRollViewRules.UsesClips(track) ? Clips : Tab;

    /// <summary>The notes in a stable order: by start, then pitch, then length.</summary>
    public static List<PianoRollNote> Sorted(IEnumerable<PianoRollNote> notes) =>
        notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ThenBy(n => n.Length).ToList();
}
