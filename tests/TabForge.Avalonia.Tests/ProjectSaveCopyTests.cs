using System.Diagnostics;
using System.IO;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: Save, Snapshot and CloneProject repair a copy of the song, never the open one; load still repairs.
/// Does not own: the individual validator rules (ProjectValidator).
/// Tests: this class.
/// </summary>
public sealed class ProjectSaveCopyTests(ITestOutputHelper output)
{
    private static SongProject PitchedTrackOnDrumChannel()
    {
        var project = new SongProject { Title = "repair" };
        project.Tracks.Add(new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, MidiChannel = 9, Measures = TemplateFactory.Measures(1) });
        return project;
    }

    [Fact]
    public void SaveRepairsTheFileButNotTheOpenSong()
    {
        var project = PitchedTrackOnDrumChannel();
        var path = Path.Combine(Path.GetTempPath(), "tf-save-copy-" + System.Guid.NewGuid().ToString("N") + ".tforge");
        try
        {
            var hashBefore = ProjectService.ContentHash(project);
            ProjectService.Save(path, project);

            Assert.Equal(9, project.Tracks[0].MidiChannel);
            Assert.Equal(hashBefore, ProjectService.ContentHash(project));
            Assert.NotEqual(9, ProjectService.Load(path).Tracks[0].MidiChannel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SnapshotAndCloneLeaveTheOpenSongAsItIs()
    {
        var project = PitchedTrackOnDrumChannel();

        var clone = ProjectService.CloneProject(project);
        ProjectService.Snapshot(project);

        Assert.Equal(9, project.Tracks[0].MidiChannel);
        Assert.NotEqual(9, clone.Tracks[0].MidiChannel);
    }

    [Fact]
    public void CloneCostOfTheDemoSong()
    {
        var demo = FullDemoSongFactory.Create();
        ProjectService.CloneProject(demo);
        var watch = Stopwatch.StartNew();
        ProjectService.CloneProject(demo);
        output.WriteLine($"CloneProject(demo song): {watch.ElapsedMilliseconds} ms");
        var path = Path.Combine(Path.GetTempPath(), "tf-save-time-" + System.Guid.NewGuid().ToString("N") + ".tforge");
        try
        {
            ProjectService.Save(path, demo);
            watch.Restart();
            ProjectService.Save(path, demo);
            output.WriteLine($"Save(demo song): {watch.ElapsedMilliseconds} ms");
            watch.Restart();
            ProjectService.ContentHash(demo);
            output.WriteLine($"ContentHash(demo song): {watch.ElapsedMilliseconds} ms");
        }
        finally { File.Delete(path); }
    }
}
