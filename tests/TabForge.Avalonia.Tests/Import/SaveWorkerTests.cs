using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a save is captured on the calling thread and written on a worker: the thread is not blocked, an edit made while the file is written neither
/// corrupts the file nor is lost from the unsaved state, a failure leaves the song as it was, and a close waits for the running save.
/// Does not own: the save questions and messages (MainWindowSaveTests).
/// Tests: this class.
/// </summary>
public sealed class SaveWorkerTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-saveworker-" + Guid.NewGuid().ToString("N"));

    private SongDocument DemoDocument()
    {
        Directory.CreateDirectory(_folder);
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        document.Project.IsDirty = true;
        return document;
    }

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    [Theory]
    [InlineData(".tforge")]
    [InlineData(".gp")]
    public async Task SavingTheDemoSongBlocksTheCallingThreadForAtMostFiftyMilliseconds(string extension)
    {
        var controller = new DocumentController();
        await controller.SaveToFileAsync(DemoDocument(), Path.Combine(_folder, "warm" + extension));   // JIT warm-up
        var document = DemoDocument();
        var watch = Stopwatch.StartNew();
        var saving = controller.SaveToFileAsync(document, Path.Combine(_folder, "demo" + extension));
        var blocked = watch.Elapsed.TotalMilliseconds;
        await saving;
        output.WriteLine($"{extension}: calling thread blocked {blocked:0.0} ms of {watch.Elapsed.TotalMilliseconds:0.0} ms");
        Assert.True(blocked <= 50, $"the save blocked the calling thread for {blocked:0.0} ms");
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public async Task AnEditMadeWhileTheFileIsWrittenIsNotInTheFileAndLeavesTheSongUnsaved()
    {
        var document = DemoDocument();
        document.Project.Title = "before";
        var path = Path.Combine(_folder, "edit.tforge");
        var saving = new DocumentController().SaveToFileAsync(document, path);
        document.Project.Title = "after";   // the worker has not finished: the capture was taken when the save started
        document.Project.BumpEditVersion();
        await saving;
        Assert.Equal("before", ProjectService.Load(path).Title);
        Assert.Equal(path, document.Path);
        Assert.True(document.HasUnsavedChanges);
        Assert.True(document.IsDirty);
        document.Project.Title = "before";   // back to the saved content
        document.Project.BumpEditVersion();
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public async Task AFailedWriteLeavesTheSongUnsavedAndItsPathUnchanged()
    {
        var document = DemoDocument();
        var path = Path.Combine(_folder, "blocked.tforge");
        Directory.CreateDirectory(path);   // a folder where the file should go
        await Assert.ThrowsAnyAsync<Exception>(() => new DocumentController().SaveToFileAsync(document, path));
        Assert.Null(document.Path);
        Assert.True(document.HasUnsavedChanges);
    }

    [Fact]
    public async Task WhenIdleCompletesOnTheLastReleaseOfTheSaveClaim()
    {
        var controller = new DocumentController();
        Assert.True(controller.WhenIdle().IsCompleted);
        var hold = controller.TryBeginSave();
        Assert.NotNull(hold);
        Assert.Null(controller.TryBeginSave());
        var idle = controller.WhenIdle();
        Assert.False(idle.IsCompleted);
        hold.Dispose();
        await idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(controller.IsSaving);
    }

    [Fact]
    public async Task ClosingWaitsForTheRunningSaveBeforeItAsksAboutUnsavedSongs()
    {
        var controller = new DocumentController();
        var document = DemoDocument();
        var asked = 0;
        var tabs = new MainWindowTabs(document, _ => { asked++; return Task.FromResult(PromptButton.Cancel); }, _ => Task.FromResult(false), whenSaved: controller.WhenIdle);
        var hold = controller.TryBeginSave();
        Assert.True(tabs.IsSaving);
        var closing = tabs.MayCloseWindow();
        await Task.Delay(50);
        Assert.False(closing.IsCompleted);
        await controller.SaveToFileAsync(document, Path.Combine(_folder, "closing.tforge"));   // the running save finishes and leaves the song clean
        hold!.Dispose();
        Assert.True(await closing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, asked);
        Assert.False(tabs.IsSaving);
    }

    [Fact]
    public async Task TheExportAnalysisOfTheDemoSongRunsOnAWorkerAndIsMeasured()
    {
        var controller = new DocumentController();
        var document = DemoDocument();
        await controller.AnalyzeGuitarProExportAsync(document);   // JIT warm-up
        var watch = Stopwatch.StartNew();
        var analysing = controller.AnalyzeGuitarProExportAsync(document);
        var blocked = watch.Elapsed.TotalMilliseconds;
        var report = await analysing;
        var total = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        GpExportPreflight.Analyze(document.Project);
        output.WriteLine($"Analyze(demo song): {watch.Elapsed.TotalMilliseconds:0.0} ms on the UI thread; via worker: calling thread blocked {blocked:0.0} ms of {total:0.0} ms");
        Assert.NotNull(report);
        Assert.True(blocked <= 50, $"the analysis blocked the calling thread for {blocked:0.0} ms");
    }
}
