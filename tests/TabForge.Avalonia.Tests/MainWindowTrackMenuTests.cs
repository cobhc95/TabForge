using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The Track menu's add command appends a track of the kind it names, through DocumentEdits.</summary>
public sealed class MainWindowTrackMenuTests
{
    [Fact]
    public void AddBassAppendsOneBassTrack()
    {
        var song = new SongDocument(() => null) { Project = new SongProject() };
        var before = song.Project.Tracks.Count;

        MainWindowTrackMenu.AddTrack(song, TrackKind.Bass);

        Assert.Equal(before + 1, song.Project.Tracks.Count);
        Assert.Equal(TrackKind.Bass, song.Project.Tracks[^1].Kind);
    }
}
