using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Views.Score;

// Owns: the page header of a score: the title, the artist and the words-by and music-by lines.
// Does not own: the tuning block (ScoreRenderer.DrawTuningBlock), the system drawing (ScoreRenderer.cs) or the text layout (ScoreText).
// Tests: no dedicated test class.

internal sealed partial class ScoreRenderer
{
    /// <summary>The page header: title, artist, authors and the tuning block.</summary>
    internal void DrawHeader(IDrawContext d, SongProject project, TrackModel track, UiColor ink, UiColor faint)
    {
        ScoreText.DrawCenteredIn(ScoreTextArea.Header, d, project.Title, _host.HeaderCentreX, 8, 22, ink, 600, "Segoe UI");
        if (!string.IsNullOrWhiteSpace(project.Artist))
            ScoreText.DrawCenteredIn(ScoreTextArea.Header, d, project.Artist, _host.HeaderCentreX, 34, 12, faint);
        var wordsBy = string.IsNullOrWhiteSpace(project.LyricsAuthor) ? "" : $"Words by {project.LyricsAuthor}";
        var musicBy = string.IsNullOrWhiteSpace(project.MusicAuthor) ? "" : $"Music by {project.MusicAuthor}";
        if (wordsBy.Length > 0) ScoreText.DrawIn(ScoreTextArea.Header, d, wordsBy, _host.GridLeft, 51, 9, faint);
        if (musicBy.Length > 0)
        {
            var text = ScoreText.MakeTextIn(d, ScoreTextArea.Header, musicBy, 9, faint);
            ScoreText.DrawIn(ScoreTextArea.Header, d, musicBy, _host.GridLeft + _host.GridWidth - text.Width, 51, 9, faint);
        }
        DrawTuningBlock(d, track, faint);
    }
}
