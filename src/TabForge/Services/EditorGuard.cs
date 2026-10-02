using TabForge.Models;

namespace TabForge.Services;

// Owns: the one answer to "may notation be edited on this track?" (an audio track has no notation).
// Does not own: the editing commands themselves; they ask here before they change anything.
// Tests: TestAudioTrackEditorGuards.
/// <summary>The editor guard: note entry, paste, transpose and note preview do nothing on an audio track.</summary>
public static class EditorGuard
{
    /// <summary>The words shown in the score area for an audio track.</summary>
    public const string Message = "Audio track — no notation";

    /// <summary>Why nothing happened (status bar text).</summary>
    public const string Hint = "Audio track — no notation: it holds audio and MIDI clips; add notes on an instrument track";

    /// <summary>True when notation can be written on <paramref name="track"/> (false for no track and for an audio track).</summary>
    public static bool CanEdit(TrackModel? track) => track is not null && !track.IsAudio;

    /// <summary>True when the track at <paramref name="index"/> of <paramref name="project"/> takes notation.</summary>
    public static bool CanEdit(SongProject? project, int index) =>
        project is not null && index >= 0 && index < project.Tracks.Count && CanEdit(project.Tracks[index]);

    /// <summary>True when the track at <paramref name="index"/> exists and is an audio track (the edit is refused, not just impossible).</summary>
    public static bool Blocks(SongProject? project, int index) =>
        project is not null && index >= 0 && index < project.Tracks.Count && project.Tracks[index].IsAudio;
}
