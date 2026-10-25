using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the test of the track row menu's Duplicate row (MainWindowTrackRowEdits.Duplicate).
// Does not own: the menu rows (TrackRowMenus) or the clipboard (ClipboardService), which have their own tests.
/// <summary>The track row menu's Duplicate row inserts a copy after the track under a unique name, through DocumentEdits.</summary>
public sealed class MainWindowTrackRowMenuTests
{
    [Fact]
    public void DuplicateInsertsACopyAfterTheTrackUnderAUniqueName()
    {
        var song = new SongDocument(() => null) { Project = new SongProject() };
        MainWindowTrackMenu.AddTrack(song, TrackKind.Bass);
        var index = song.Project.Tracks.Count - 1;
        var name = song.Project.Tracks[index].Name;
        var before = song.Project.Tracks.Count;

        MainWindowTrackRowEdits.Duplicate(song, index);

        Assert.Equal(before + 1, song.Project.Tracks.Count);
        Assert.Equal($"{name} copy", song.Project.Tracks[index + 1].Name);
    }
}
