using TabForge.Avalonia;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the uri-list parsing behind the Linux file drop fallback (percent-decoding, spaces, non-ASCII, several files, a folder, comment and copy lines).
// Does not own: the drag events (MainWindowFileDrop).
// Tests: this file.
public sealed class DroppedFileUrisTests
{
    [Fact]
    public void DecodesSpacesAndNonAsciiAndKeepsOrder()
    {
        var paths = DroppedFileUris.Parse("file:///home/u/My%20Song.gp5\r\nfile:///home/u/caf%C3%A9/%E6%AD%8C.mid\r\n");
        Assert.Equal(new[] { "/home/u/My Song.gp5", "/home/u/café/歌.mid" }, paths);
    }

    [Fact]
    public void SkipsCommentsBlankLinesAndTheCopyWordAndRemoteHosts()
    {
        var paths = DroppedFileUris.Parse("copy\nfile://localhost/tmp/a.mp3\n# note\n\nfile://other/tmp/b.mp3\nfile:///tmp/dir%20x\n");
        Assert.Equal(new[] { "/tmp/a.mp3", "/tmp/dir x" }, paths);
    }

    [Fact]
    public void EmptyOrNullGivesNothing()
    {
        Assert.Empty(DroppedFileUris.Parse(null));
        Assert.Empty(DroppedFileUris.Parse("  "));
    }
}
