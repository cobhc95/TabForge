using TabForge.Models;

// Owns: the score information texts of the Bar menu's Score information dialog (title, subtitle, artist, album, the authors, copyright, instructions, notice), read from a song and written back to it.
// Does not own: the dialog (Views/ScoreInfoWindow), the edit that runs the write (ScorePreviewView.Edit), the status text.
// Tests: TabForge.Avalonia.Tests.ScoreInfoWindowTests.
namespace TabForge.Presenters;

/// <summary>The score information texts of a song, as the Score information dialog edits them.</summary>
public sealed record ScoreInfoValues(
    string Title, string Subtitle, string Artist, string Album,
    string MusicAuthor, string LyricsAuthor, string Copyright, string TabAuthor,
    string Instructions, string Notice)
{
    /// <summary>The texts the song holds now.</summary>
    public static ScoreInfoValues From(SongProject project) => new(
        project.Title, project.Subtitle, project.Artist, project.Album,
        project.MusicAuthor, project.LyricsAuthor, project.Copyright, project.TabAuthor,
        project.Instructions, project.Notice);

    /// <summary>Writes the texts to the song and marks it changed, as the WPF dialog does on OK.</summary>
    public void ApplyTo(SongProject project)
    {
        project.Title = Title;
        project.Subtitle = Subtitle;
        project.Artist = Artist;
        project.Album = Album;
        project.MusicAuthor = MusicAuthor;
        project.LyricsAuthor = LyricsAuthor;
        project.Copyright = Copyright;
        project.TabAuthor = TabAuthor;
        project.Instructions = Instructions;
        project.Notice = Notice;
    }
}
