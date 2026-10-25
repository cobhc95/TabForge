using TabForge.Avalonia;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the Section.Edit write, the section name of the cursor bar set on that bar of every track as one undo step.</summary>
public class MainWindowSectionEditTests
{
    [Fact]
    public void SetSectionNameWritesTheBarInEveryTrack()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        Assert.True(MainWindowSectionEdit.SetSectionName(document, 0, "Section under test"));
        Assert.All(document.Project.Tracks, track => Assert.Equal("Section under test", track.Measures[0].SectionName));
    }
}
