using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Autosave;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the window's autosave (MainWindowAutosave): the copy of a dirty song, the failing-autosave notice and the start-up recovery offer.</summary>
public sealed class MainWindowAutosaveTests : IDisposable
{
    private const string OrphanPid = "999999";   // no such process: a copy under it is what a crashed session leaves behind
    private const string OrphanId = "0123456789abcdef0123456789abcdef";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-autosave-{Guid.NewGuid():N}");

    public MainWindowAutosaveTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    /// <summary>A window's songs and its answers, without a window.</summary>
    private sealed class FakeHost : MainWindowAutosave.IHost
    {
        public IReadOnlyList<SongDocument> Songs { get; set; } = Array.Empty<SongDocument>();
        public IReadOnlyList<SongDocument> SongsInAllWindows => Songs;
        public int AutosaveMinutes { get; set; } = AutosaveService.DefaultMinutes;
        public bool Answer { get; set; }
        public List<int> Asked { get; } = new();
        public List<SongDocument> Opened { get; } = new();
        public List<string> Reports { get; } = new();
        public bool IsPlaying(SongDocument song) => false;
        public Task<bool> ConfirmRecoveryAsync(int songCount) { Asked.Add(songCount); return Task.FromResult(Answer); }
        public void OpenRecoveredSong(SongDocument song) => Opened.Add(song);
        public void Report(string text) => Reports.Add(text);
    }

    private static SongDocument DirtySong(string title)
    {
        var document = new SongDocument(() => null) { Project = new SongProject { Title = title } };
        document.MarkClean();
        MainWindowLifetimeTests.MakeEdit(document);
        return document;
    }

    private static MainWindowAutosave Controller(FakeHost host, string folder, AutosaveRegistry registry) =>
        new(host, registry, folder, new AvaloniaUiThread(Dispatcher.UIThread));

    /// <summary>A recovery copy as a crashed session left it: a real .tforge under a process id that is not running.</summary>
    private void WriteOrphanCopy(string title)
    {
        var project = new SongProject { Title = title };
        RecoveryCopyWriter.Write(project, Path.Combine(_folder, $"autosave-{OrphanPid}-{OrphanId}-{title}.tforge"));
    }

    [AvaloniaFact]
    public async Task CopyDirtySongsWritesARecoveryCopyOfEachDirtySongAndKeepsTheSongDirty()
    {
        var song = DirtySong("Dirty");
        var host = new FakeHost { Songs = new[] { song } };
        var registry = new AutosaveRegistry();
        var controller = Controller(host, _folder, registry);
        try
        {
            var result = await controller.CopyDirtySongsAsync();

            Assert.Equal(1, result!.Written);
            var copy = Assert.Single(Directory.GetFiles(_folder, "autosave-*.tforge"));
            Assert.Equal("Dirty (edited)", ProjectService.Load(copy).Title);   // the test's edit appends to the title
            Assert.True(song.HasUnsavedChanges);   // the copy is not a save
            Assert.False(controller.Health.Failing);
            Assert.False(controller.Notice.IsVisible);
        }
        finally { controller.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowRemovesTheCopiesOfSongsDiscardedFromItsTabs()
    {
        var kept = DirtySong("Kept");
        var discarded = DirtySong("Discarded");
        var host = new FakeHost { Songs = new[] { kept, discarded } };
        var controller = Controller(host, _folder, new AutosaveRegistry());
        await controller.CopyDirtySongsAsync();
        Assert.Equal(2, Directory.GetFiles(_folder, "autosave-*.tforge").Length);

        host.Songs = new[] { kept };   // the discarded tab is gone; the window closes
        controller.Dispose();

        Assert.Empty(Directory.GetFiles(_folder, "autosave-*.tforge"));
    }

    [AvaloniaFact]
    public async Task FailingCopyShowsTheNoticeWithTheReasonAndKeepsTheSongUnsaved()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "a file where the Recovery folder should be");   // the copy's folder cannot be created
        var song = DirtySong("Stuck");
        var host = new FakeHost { Songs = new[] { song } };
        var controller = Controller(host, blocked, new AutosaveRegistry());
        try
        {
            await controller.CopyDirtySongsAsync();

            Assert.True(controller.Health.Failing);
            Assert.True(controller.Notice.IsVisible);
            Assert.True(song.HasUnsavedChanges);
        }
        finally { controller.Dispose(); }
    }

    [AvaloniaFact]
    public async Task RetryProbesTheFolderFirstAndReportsItsDetail()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "a file where the Recovery folder should be");   // the copy's folder cannot be created
        var song = DirtySong("Stuck");
        var host = new FakeHost { Songs = new[] { song } };
        var controller = Controller(host, blocked, new AutosaveRegistry());
        try
        {
            await controller.CopyDirtySongsAsync();
            Assert.True(controller.Health.Failing);

            controller.Retry();   // the probe fails: the notice reports the probe's detail, no pass starts

            Assert.True(controller.Health.Failing);
            Assert.NotNull(controller.Health.ProbeDetail);
            Assert.Equal(blocked, controller.Health.Folder);
            var notice = controller.Health.Describe(DateTime.UtcNow);
            Assert.True(notice is not null && notice.Contains("Folder check:"));
            Assert.True(controller.Notice.IsVisible);
            Assert.True(song.HasUnsavedChanges);
        }
        finally { controller.Dispose(); }
    }

    [AvaloniaFact]
    public async Task RecoveryOfferOpensEachOrphanCopyAsAnUnsavedTabAndKeepsItUntilSaved()
    {
        WriteOrphanCopy("Crashed");
        var host = new FakeHost { Answer = true };
        var controller = Controller(host, _folder, new AutosaveRegistry());
        try
        {
            await controller.OfferRecoveryAsync();

            Assert.Equal(new[] { 1 }, host.Asked);
            var song = Assert.Single(host.Opened);
            Assert.Equal("Crashed", song.Project.Title);
            Assert.Null(song.Path);
            Assert.True(song.IsNew);
            Assert.True(song.HasUnsavedChanges);
            Assert.Single(Directory.GetFiles(_folder, "autosave-*.tforge"));   // the copy stays until the song is saved
        }
        finally { controller.Dispose(); }
    }

    [AvaloniaFact]
    public async Task RecoveryOfferDeclinedDiscardsTheOrphanCopies()
    {
        WriteOrphanCopy("Discarded");
        var host = new FakeHost { Answer = false };
        var controller = Controller(host, _folder, new AutosaveRegistry());
        try
        {
            await controller.OfferRecoveryAsync();

            Assert.Empty(host.Opened);
            Assert.Empty(Directory.GetFiles(_folder, "autosave-*.tforge"));
        }
        finally { controller.Dispose(); }
    }

    [AvaloniaFact]
    public async Task RecoveryIsOfferedOnceForTheProcess()
    {
        WriteOrphanCopy("Once");
        var registry = new AutosaveRegistry();
        var host = new FakeHost { Answer = false };
        var first = Controller(host, _folder, registry);
        var second = Controller(host, _folder, registry);
        try
        {
            await first.OfferRecoveryAsync();
            await second.OfferRecoveryAsync();

            Assert.Equal(new[] { 1 }, host.Asked);
        }
        finally { first.Dispose(); second.Dispose(); }
    }
}
