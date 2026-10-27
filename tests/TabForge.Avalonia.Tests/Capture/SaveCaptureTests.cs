using System;
using System.Diagnostics;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the save capture (SaveCapture): a worker's rebuilt song writes the same file and hash as the live one, capture is cheap, and the live song is
/// never touched or read by the worker.
/// Does not own: the save sequencing (DocumentSaveFlow tests in MainWindowSaveTests).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]   // the capture-cost budget is wall-clock: run alone so parallel tests cannot inflate it
public sealed class SaveCaptureTests(ITestOutputHelper output)
{
    internal static SongProject SongWithOmittedFields()
    {
        var project = FullDemoSongFactory.Create();
        project.Tracks[0].MonitorInput = false;
        project.Tracks[0].AudioInput = "Input 3";
        project.Tracks[^1].AudioInput = AudioInputs.Stereo;
        project.BandLayout = new BandLayoutData { Order = { project.Tracks[1].Id, project.Tracks[0].Id }, Shown = { project.Tracks[0].Id }, RowsPerScreen = 3 };
        project.BandLayout.Heights[project.Tracks[0].Id] = 77;
        project.Mixer.CollapsedGroups.Add("Drums");
        return project;
    }

    private static SongDocument Document(SongProject project) => new(() => null) { Project = project };

    [Fact]
    public void ARebuiltSongWritesTheSameFileAndHashAsTheLiveOne()
    {
        foreach (var project in new[] { FullDemoSongFactory.Create(), SongWithOmittedFields() })
        {
            var document = Document(project);
            var capture = SaveCapture.Take(document);
            var rebuilt = capture.Rebuild();
            var liveFile = Path.Combine(Path.GetTempPath(), "tf-cap-live-" + System.Guid.NewGuid().ToString("N") + ".tforge");
            var rebuiltFile = Path.Combine(Path.GetTempPath(), "tf-cap-copy-" + System.Guid.NewGuid().ToString("N") + ".tforge");
            try
            {
                Assert.Equal(ProjectService.ContentHash(project), ProjectService.ContentHash(rebuilt));
                var liveHash = ProjectService.Save(liveFile, project);
                Assert.Equal(liveHash, ProjectService.Save(rebuiltFile, rebuilt));
                Assert.Equal(File.ReadAllBytes(liveFile), File.ReadAllBytes(rebuiltFile));
                Assert.Equal(project.FormatVersion, rebuilt.FormatVersion);
            }
            finally { File.Delete(liveFile); File.Delete(rebuiltFile); }
        }
    }

    [Fact]
    public void TheCaptureIsIndependentOfLaterChangesAndMatchesTheUnchangedSong()
    {
        var project = SongWithOmittedFields();
        var document = Document(project);
        var capture = SaveCapture.Take(document);
        Assert.True(capture.Matches(document));
        var hash = ProjectService.ContentHash(capture.Rebuild());
        project.BandLayout!.RowsPerScreen = 9;   // view state is part of the capture check (the file would lack it) but not of the unsaved content
        project.Mixer.CollapsedGroups.Clear();
        Assert.Equal(hash, ProjectService.ContentHash(capture.Rebuild()));
        Assert.True(capture.Matches(document));   // view state is outside the unsaved-changes content: it never makes the song unsaved
        project.Tracks[0].MonitorInput = true;
        Assert.False(capture.Matches(document));
        project.Tracks[0].MonitorInput = false;
        project.Title = "changed";
        Assert.False(capture.Matches(document));
    }

    [Fact]
    public void CaptureCostOfTheDemoSong()
    {
        SaveCapture.Take(Document(FullDemoSongFactory.Create()));   // JIT warm-up: the cold figure below is a first capture of a song, not the first call of the code
        var document = Document(FullDemoSongFactory.Create());
        var watch = Stopwatch.StartNew();
        var cold = SaveCapture.Take(document);
        var coldMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var warmMs = double.MaxValue;   // best of five: the suite runs other tests beside this one
        SaveCapture warm = cold;
        for (var run = 0; run < 5; run++)
        {
            watch.Restart();
            warm = SaveCapture.Take(document);
            warmMs = Math.Min(warmMs, watch.Elapsed.TotalMilliseconds);
        }
        watch.Restart();
        warm.Rebuild();
        output.WriteLine($"capture cold {coldMs:0.0} ms, warm {warmMs:0.0} ms, rebuild {watch.Elapsed.TotalMilliseconds:0.0} ms");
        Assert.True(cold.State.ContentEquals(warm.State));
        Assert.True(warmMs <= 30, $"warm capture took {warmMs:0.0} ms");
    }
}
