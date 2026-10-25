using TabForge.Avalonia.Lyrics;
using TabForge.Documents;
using TabForge.Lyrics;
using TabForge.Presets;
using Avalonia.Headless.XUnit;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Lyrics panel rules: an edit of one bar's syllables changes the model and is one undo step, and the pane shows the shown song's lyrics.
// Does not own: the dock placement (DockDefaultLayoutTests), the playhead highlight (checked by hand in a playtest).
// Tests: this file.
public class LyricsPaneTests
{
    [Fact]
    public void EditingABarChangesTheModelAndUndoRestoresIt()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var before = LyricsController.RowsFor(document)[0].Text;
        var undoBefore = document.Undo.UndoCount;

        Assert.True(LyricsController.SetBarText(document, 0, "Hel lo"));

        Assert.Equal("Hel lo", LyricsController.RowsFor(document)[0].Text);
        Assert.Equal(undoBefore + 1, document.Undo.UndoCount);
        Assert.NotNull(DocumentEdits.Undo(document));
        Assert.Equal(before, LyricsController.RowsFor(document)[0].Text);
    }

    [AvaloniaFact]
    public void PaneShowsTheActiveSongsLyrics()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        Assert.True(LyricsController.SetBarText(document, 1, "alpha beta"));

        var pane = new LyricsPaneView();
        pane.Show(document, null);

        Assert.Contains("alpha beta", pane.ShownLyricTexts);
    }
}
