using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Lyrics;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Lyrics pane as the app's text box of the song's lyrics (IN-14): it shows Project.Lyrics, and a commit is one undo step that marks the song dirty.
// Does not own: the per-bar syllable editor (LyricsPaneTests).
public class LyricsSongPaneTests
{
    private static SongDocument Song(string lyrics)
    {
        var document = new SongDocument(() => null) { Project = new SongProject { Title = "Lyrics", Lyrics = lyrics } };
        document.MarkClean();
        return document;
    }

    private static TextBox Box(LyricsSongPane pane) =>
        pane.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "LyricsBox");

    [AvaloniaFact]
    public void ThePaneShowsTheSongsLyricsAndACommitIsOneUndoStep()
    {
        var document = Song("first line");
        var pane = new LyricsSongPane(() => null);
        pane.Show(document);
        Assert.Equal("first line", Box(pane).Text);

        Box(pane).Text = "second line";
        pane.CommitText();

        Assert.Equal("second line", document.Project.Lyrics);
        Assert.True(document.HasUnsavedChanges);
        Assert.NotNull(DocumentEdits.Undo(document));
        Assert.Equal("first line", document.Project.Lyrics);
    }

    [AvaloniaFact]
    public void ACommitOfUnchangedTextChangesNothing()
    {
        var document = Song("same");
        var pane = new LyricsSongPane(() => null);
        pane.Show(document);

        pane.CommitText();

        Assert.False(document.HasUnsavedChanges);
        Assert.Null(DocumentEdits.Undo(document));
    }

    [AvaloniaFact]
    public void ANewSongShowsItsOwnLyrics()
    {
        var pane = new LyricsSongPane(() => null);
        pane.Show(Song("a"));
        pane.Show(Song("b"));

        Assert.Equal("b", Box(pane).Text);
    }
}
