using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The section lock row flips the lock of the section the right-click named; a second flip restores it.</summary>
public sealed class MainWindowSectionLockTests
{
    [Fact]
    public void LockRowFlipsTheSectionLockAndASecondFlipRestoresIt()
    {
        var song = new SongDocument(() => null) { Project = new SongProject() };
        song.Project.Markers.Add(new MarkerModel { MeasureIndex = 0 });

        MainWindowSectionLock.Toggle(song, 0);
        Assert.True(song.Project.Markers[0].LockPosition);

        MainWindowSectionLock.Toggle(song, 0);
        Assert.False(song.Project.Markers[0].LockPosition);
    }
}
