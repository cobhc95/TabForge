using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Save to .gp path that asks the "keep a full native copy" question (DocumentSaveFlow over DocumentController): asked once, after the plug-in
/// states are collected, answered from the same capture that is written; an edit made while it is open; cancel; a lossless song skips it.
/// Does not own: the dialogs (MainWindowSaveTests).
/// Tests: this class.
/// </summary>
public sealed class SavePreflightFlowTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-savepreflight-" + Guid.NewGuid().ToString("N"));

    public SavePreflightFlowTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private sealed class Asker(Func<GpPreflightReport, GpExportChoice> onPreflight) : ISaveInteractions
    {
        public int AudioAsked, PreflightAsked;
        public AudioDataSaveChoice? AskAudioDataChoice(string fileName) { AudioAsked++; return AudioDataSaveChoice.GpPlusDataFile; }
        public GpExportChoice AskGpPreflight(GpPreflightReport report, GpExportKind kind, string fileName) { PreflightAsked++; return onPreflight(report); }
        public ReplaceFileChoice AskReplaceFullCopy(string fileName) => throw new InvalidOperationException("no replace question expected");
    }

    /// <summary>A one-track song that has audio data (so the .gp save asks how) and, when <paramref name="lossy"/>, a reverb send a clean .gp cannot hold.</summary>
    private static SongDocument Song(bool lossy)
    {
        var project = new SongProject { Title = "start" };
        project.Tracks.Add(new TrackModel { Name = "Lead", Measures = TemplateFactory.Measures(1) });
        project.Mixer.Grouping = MixerGrouping.None;
        if (lossy) project.Tracks[0].Reverb = 60;
        var document = new SongDocument(() => null) { Project = project };
        document.Project.IsDirty = true;
        return document;
    }

    private async Task<SaveOutcome> Save(SongDocument document, string path, ISaveInteractions ask, Func<SongDocument, Task<StateCollection>> collect, DocumentController controller)
    {
        var flow = new DocumentSaveFlow(controller);
        SaveOutcome? outcome = null;
        Assert.True(await flow.RunClaimedAsync(async hold => { outcome = await flow.SaveAsync(document, path, ask, collect, hold); return true; }).WaitAsync(Limit));
        return outcome!;
    }

    [Fact]
    public async Task TheQuestionIsAskedOnceAfterTheStatesAreCollectedAndTheNativeCopyIsFromTheSameCapture()
    {
        var document = Song(lossy: true);
        var path = Path.Combine(_folder, "song.gp");
        var collected = false;
        var collectedBeforeQuestion = false;
        var ask = new Asker(_ => { collectedBeforeQuestion = collected; return GpExportChoice.KeepNativeCopy; });
        var outcome = await Save(document, path, ask, d => { d.Project.Title = "collected"; collected = true; return Task.FromResult(new StateCollection()); }, new DocumentController());

        Assert.Equal(1, ask.PreflightAsked);
        Assert.True(collectedBeforeQuestion);
        Assert.True(outcome.Saved, outcome.Message);
        var native = Path.Combine(_folder, "song (full copy).tforge");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(native));
        Assert.Equal("collected", ProjectService.Load(native).Title);
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public async Task AnEditMadeWhileTheQuestionIsOpenIsNotWrittenAndLeavesTheSongUnsaved()
    {
        var document = Song(lossy: true);
        var path = Path.Combine(_folder, "edit.gp");
        var ask = new Asker(_ =>
        {
            document.Project.Title = "edited while asking";
            document.Project.BumpEditVersion();
            return GpExportChoice.KeepNativeCopy;
        });
        var outcome = await Save(document, path, ask, _ => Task.FromResult(new StateCollection()), new DocumentController());

        Assert.False(outcome.Saved);
        Assert.Equal("start", ProjectService.Load(Path.Combine(_folder, "edit (full copy).tforge")).Title);
        Assert.True(document.HasUnsavedChanges);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public async Task CancellingTheQuestionWritesNothingReleasesTheClaimAndKeepsTheSongUnsaved()
    {
        var document = Song(lossy: true);
        var path = Path.Combine(_folder, "cancel.gp");
        var controller = new DocumentController();
        var outcome = await Save(document, path, new Asker(_ => GpExportChoice.Cancel), _ => Task.FromResult(new StateCollection()), controller);

        Assert.True(outcome.Cancelled);
        Assert.False(outcome.Saved);
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
        await controller.WhenIdle().WaitAsync(Limit);
        Assert.False(controller.IsSaving);
        Assert.True(document.HasUnsavedChanges);
    }

    [Fact]
    public async Task ALosslessSongSkipsTheQuestionAndWritesTheGpAndItsAudioDataFile()
    {
        var document = Song(lossy: false);
        var path = Path.Combine(_folder, "clean.gp");
        var ask = new Asker(_ => throw new InvalidOperationException("no preflight question expected"));
        var outcome = await Save(document, path, ask, _ => Task.FromResult(new StateCollection()), new DocumentController());

        Assert.Equal(0, ask.PreflightAsked);
        Assert.True(outcome.Saved, outcome.Message);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(AudioDataFile.PathFor(path)));
        Assert.False(document.HasUnsavedChanges);
    }
}
