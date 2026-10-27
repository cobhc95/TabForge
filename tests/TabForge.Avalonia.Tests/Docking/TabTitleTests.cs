using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the tab text rule: file name with extension, the full path tooltip, the song title for an unsaved song, the imported file's name until a save.</summary>
public class TabTitleTests
{
    private static SongDocument Song(string title, string? path)
    {
        var song = new SongDocument(() => null) { Project = new SongProject { Title = title } };
        song.Path = path;
        return song;
    }

    [Fact]
    public void TabTextIsTheFileNameWithExtension() => Assert.Equal("aces_high_2.gp5", Song("Aces High", @"C:\Songs\aces_high_2.gp5").DisplayName);

    [Fact]
    public void TooltipIsFullPathThenTheTitleWhenItDiffers()
    {
        Assert.Equal(@"C:\Songs\aces_high_2.gp5" + "\n" + "Aces High", TabLabelPresenter.Tooltip(@"C:\Songs\aces_high_2.gp5", "aces_high_2.gp5", "Aces High", true));
        Assert.Equal(@"C:\Songs\Riff.tforge", TabLabelPresenter.Tooltip(@"C:\Songs\Riff.tforge", "Riff.tforge", "Riff", true));
        Assert.Equal(@"C:\In\a.gp5" + TitleMarkers.UnsavedNote, TabLabelPresenter.Tooltip(@"C:\In\a.gp5", "a.gp5", "a", false));
    }

    [Fact]
    public void UnsavedSongKeepsItsTitle() => Assert.Equal("Aces High", Song("Aces High", null).DisplayName);

    [Fact]
    public void ImportedFileNamesTheTabUntilSaveAsRenamesIt()
    {
        var song = Song("Aces High", null);
        song.ImportSourcePath = @"C:\In\aces_high.gp5";
        Assert.Equal("aces_high.gp5", song.DisplayName);
        song.Path = @"C:\Out\aces_high_v2.tforge";
        Assert.Equal("aces_high_v2.tforge", song.DisplayName);
        Assert.Null(song.ImportSourcePath);
    }
}
