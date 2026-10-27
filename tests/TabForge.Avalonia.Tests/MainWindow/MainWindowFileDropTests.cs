using TabForge.Avalonia;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the song-file filter of a window file drop (which dropped paths open as tabs, in drop order).
// Does not own: the drag events and the opening of the files (MainWindowFileDrop, MainWindowFile).
// Tests: this file.
public sealed class MainWindowFileDropTests
{
    [Fact]
    public void SongPathsKeepOnlyOpenableScoresInDropOrder()
    {
        var kept = MainWindowFileDrop.SongPaths(new[] { @"C:\a\one.tforge", @"C:\a\clip.mp3", @"C:\a\two.GP5", @"C:\a\notes.txt" });
        Assert.Equal(new[] { @"C:\a\one.tforge", @"C:\a\two.GP5" }, kept);
    }

    [Fact]
    public void FirstDroppedSongReplacesSelectedTabUnlessDropOpensInNewTab()
    {
        Assert.True(MainWindowFileDrop.ReplacesSelectedTab(0, dropOpensInNewTab: false));
        Assert.False(MainWindowFileDrop.ReplacesSelectedTab(1, dropOpensInNewTab: false));
        Assert.False(MainWindowFileDrop.ReplacesSelectedTab(0, dropOpensInNewTab: true));
    }

    [Fact]
    public void SongsWinOverAudioAndAudioBeatsNothing()
    {
        Assert.Equal(MainWindowFileDrop.WindowDropKind.Songs, MainWindowFileDrop.Decide(new[] { "a.tforge" }, audioAccepted: true));
        Assert.Equal(MainWindowFileDrop.WindowDropKind.Audio, MainWindowFileDrop.Decide(new string[0], audioAccepted: true));
        Assert.Equal(MainWindowFileDrop.WindowDropKind.None, MainWindowFileDrop.Decide(new string[0], audioAccepted: false));
    }
}
