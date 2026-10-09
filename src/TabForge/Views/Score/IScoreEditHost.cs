using TabForge.Models;

namespace TabForge.Views.Score;

/// <summary>
/// The one way an editor command changes the song: the host runs <paramref name="edit"/> on the displayed document through
/// <c>DocumentEdits.Run</c>, so the edit is one undo step, one dirty change and one timeline invalidation. The window that
/// hosts the editor implements it.
/// </summary>
public interface IScoreEditHost
{
    /// <summary>
    /// Runs <paramref name="edit"/> on the displayed document's song; it returns whether it changed anything, and when it did not
    /// nothing is stored. <paramref name="invalidatesTimeline"/> is false when the edit already invalidated the playback timeline.
    /// Returns whether the song changed.
    /// </summary>
    bool Run(Func<SongProject, bool> edit, bool invalidatesTimeline = true);

    /// <summary>As <see cref="Run(Func{SongProject, bool}, bool)"/>; <paramref name="continuesLastStep"/>: the edit joins the latest undo step (the second digit of a two-digit fret).</summary>
    bool Run(Func<SongProject, bool> edit, bool invalidatesTimeline, bool continuesLastStep) => Run(edit, invalidatesTimeline);
}
