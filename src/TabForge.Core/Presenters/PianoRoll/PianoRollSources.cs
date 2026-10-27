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

/// <summary>Which notes the piano roll edits on a track: Auto (the track decides), the written Tab, or one MIDI clip Lane (every take on it; a Fresh lane is shown even with no take, and its first note creates one).</summary>
public enum PianoRollSourceKind { Auto, Tab, Lane }

public readonly record struct PianoRollSourceChoice(PianoRollSourceKind Kind = PianoRollSourceKind.Auto, int Lane = 0, bool Fresh = false)
{
    public static PianoRollSourceChoice OnTab => new(PianoRollSourceKind.Tab);
    public static PianoRollSourceChoice OnLane(int lane) => new(PianoRollSourceKind.Lane, lane);

    /// <summary>A lane that may hold no take yet (an empty take lane was clicked): it shows only that lane, and the first note written there creates a take on it.</summary>
    public static PianoRollSourceChoice OnFreshLane(int lane) => new(PianoRollSourceKind.Lane, lane, true);
}

public static class PianoRollSources
{
    private static readonly IPianoRollSource Clips = new PianoRollClipSource();
    private static readonly IPianoRollSource Tab = new PianoRollTabSource();

    /// <summary>The source of <paramref name="track"/>'s notes: its MIDI clips for a MIDI track, else its written tab.</summary>
    public static IPianoRollSource For(TrackModel track) => PianoRollViewRules.UsesClips(track) ? Clips : Tab;

    /// <summary>The source for <paramref name="choice"/>: the chosen tab or clip lane (a lane the track no longer has falls back to Auto).</summary>
    public static IPianoRollSource For(TrackModel track, PianoRollSourceChoice choice) => choice.Kind switch
    {
        PianoRollSourceKind.Tab => Tab,
        PianoRollSourceKind.Lane when choice.Fresh => new PianoRollClipSource(choice.Lane, strict: true),
        PianoRollSourceKind.Lane when track.AudioClips.Any(clip => clip.IsMidi && clip.Lane == choice.Lane) => new PianoRollClipSource(choice.Lane),
        _ => For(track),
    };

    /// <summary>The notes in a stable order: by start, then pitch, then length.</summary>
    public static List<PianoRollNote> Sorted(IEnumerable<PianoRollNote> notes) =>
        notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ThenBy(n => n.Length).ToList();
}
