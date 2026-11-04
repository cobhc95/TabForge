using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the file dialogs never wait unbounded for the shell: a slow start folder falls back fast, a fast remembered folder is kept, the remembered-folder
/// memory still stores and reuses its pick off the UI thread, the picker options keep their mapping, and a cancel or a refused path still reads as null.
/// Does not own: the native picker (never shown here) or the open/save flows after the pick (MainWindowDocumentsTests, MainWindowSaveTests).
/// Tests: this class.
/// </summary>
public class FileDialogHangTests
{
    private const string Documents = @"C:\Users\Test\Documents";
    private static readonly IAppFolders Folders = AppFolders.Current;

    private static string Scratch(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "TabForgeFileDialogHang", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool Slow() { Thread.Sleep(500); return true; }

    [AvaloniaFact]
    public async Task AnUnreachableStartFolderFallsBackFast()
    {
        const string stalled = @"Z:\stalled-share\songs";
        var remembered = new Dictionary<string, string> { [DialogKeys.ProjectOpen] = stalled };
        var elapsed = Stopwatch.StartNew();

        var start = await DialogFolders.StartFolderBoundedAsync(remembered, DialogKeys.ProjectOpen, null,
            path => path == stalled ? Slow() : false, Folders, Documents, TimeSpan.FromMilliseconds(50));

        elapsed.Stop();
        Assert.Null(start);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3), $"The stalled start folder took {elapsed.Elapsed} to give up.");
    }

    [AvaloniaFact]
    public async Task AFastRememberedFolderIsStillChosen()
    {
        var songs = Scratch("songs");
        var remembered = new Dictionary<string, string> { [DialogKeys.ProjectOpen] = songs };

        var start = await DialogFolders.StartFolderBoundedAsync(remembered, DialogKeys.ProjectOpen, null,
            Directory.Exists, Folders, Documents, TimeSpan.FromSeconds(2));

        Assert.Equal(songs, start);
    }

    [AvaloniaFact]
    public async Task TheMemoryStillStoresAndReusesAFolder()
    {
        var settings = new AppSettings();
        var songs = Scratch("songs");
        var memory = new DialogFolderMemory(() => settings, () => { }, Folders, Documents);
        memory.Remember(DialogKeys.ProjectOpen, Path.Combine(songs, "a.tforge"), isFolder: false);

        Assert.Equal(songs, await memory.StartFolderAsync(DialogKeys.ProjectOpen, null));

        var gone = Scratch("gone");
        Directory.Delete(gone);
        settings.RememberedFolders[DialogKeys.ProjectOpen] = gone;
        Assert.Equal(songs, await memory.StartFolderAsync(DialogKeys.ProjectOpen, songs));
    }

    [AvaloniaFact]
    public void ThePickerOptionsKeepTheirMapping()
    {
        var open = AvaloniaFileDialogs.OpenOptions(new FileDialogOptions
        {
            Title = "Open TabForge or .gp file",
            Filter = "Scores|*.tforge;*.gp|All files|*.*",
            Multiselect = true,
        }, null);
        Assert.Equal("Open TabForge or .gp file", open.Title);
        Assert.True(open.AllowMultiple);
        Assert.Equal(new[] { "Scores", "All files" }, open.FileTypeFilter!.Select(type => type.Name));
        Assert.Null(open.SuggestedStartLocation);

        var save = AvaloniaFileDialogs.SaveOptions(new FileDialogOptions
        {
            Title = "Export MIDI", FileName = "Song", DefaultExt = ".mid", Filter = "MIDI file (*.mid)|*.mid", OverwritePrompt = false,
        }, null);
        Assert.Equal("Song", save.SuggestedFileName);
        Assert.Equal(".mid", save.DefaultExtension);
        Assert.False(save.ShowOverwritePrompt);
        Assert.Equal("MIDI file (*.mid)", save.FileTypeChoices!.Single().Name);
        Assert.Null(save.SuggestedStartLocation);

        var folder = AvaloniaFileDialogs.FolderOptions(new FileDialogOptions { Title = "Choose the render folder" }, null);
        Assert.Equal("Choose the render folder", folder.Title);
        Assert.False(folder.AllowMultiple);
        Assert.Null(folder.SuggestedStartLocation);
    }

    /// <summary>A picker that is still up: the sync call must never be used by File > Open, the async one stays pending until released.</summary>
    private sealed class PendingPicker : IFileDialogs
    {
        public readonly TaskCompletionSource<string[]?> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AsyncCalls;
        public string? OpenFile(FileDialogOptions options, object? owner) => throw new InvalidOperationException("sync open");
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => throw new InvalidOperationException("File > Open used the blocking picker.");
        public Task<string[]?> OpenFilesAsync(FileDialogOptions options, object? owner) { AsyncCalls++; return Release.Task; }
        public string? SaveFile(FileDialogOptions options, object? owner) => throw new InvalidOperationException();
        public string? PickFolder(FileDialogOptions options, object? owner) => throw new InvalidOperationException();
        public string[]? PickFolders(FileDialogOptions options, object? owner) => throw new InvalidOperationException();
    }

    [AvaloniaFact]
    public async Task FileOpenReturnsToTheMessageLoopWhileThePickerIsUp()
    {
        var picker = new PendingPicker();
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var open = new MainWindowFile(picker, null, tabs, _ => { }).Open();

        Assert.False(open.IsCompleted);          // the command returned to its caller; nothing is blocked or pumping a nested loop
        Assert.Equal(1, picker.AsyncCalls);
        Assert.Single(tabs.Songs);

        picker.Release.SetResult(null);          // the person cancels
        await open.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(tabs.Songs);
    }

    [AvaloniaFact]
    public async Task TheDefaultAsyncOpenKeepsTheSynchronousAnswer()
    {
        var dialogs = new SyncOnly();
        Assert.Equal(new[] { "a.tforge" }, await ((IFileDialogs)dialogs).OpenFilesAsync(new FileDialogOptions(), null));
    }

    private sealed class SyncOnly : IFileDialogs
    {
        public string? OpenFile(FileDialogOptions options, object? owner) => null;
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => new[] { "a.tforge" };
        public string? SaveFile(FileDialogOptions options, object? owner) => null;
        public string? PickFolder(FileDialogOptions options, object? owner) => null;
        public string[]? PickFolders(FileDialogOptions options, object? owner) => null;
    }

    [AvaloniaFact]
    public async Task AnOwnerlessAsyncOpenFailsAsATaskNotABlock()
    {
        var dialogs = new AvaloniaFileDialogs();
        await Assert.ThrowsAsync<ArgumentException>(() => dialogs.OpenFilesAsync(new FileDialogOptions(), null));
    }

    [AvaloniaFact]
    public void ACancelOrARefusedPathStillReadsAsNull()
    {
        Assert.Null(AvaloniaFileDialogs.CheckedFiles(Array.Empty<string?>()));
        Assert.Null(AvaloniaFileDialogs.CheckedFiles(new string?[] { null }));
        Assert.Null(AvaloniaFileDialogs.CheckedFiles(new[] { @"\\?\C:\Windows\notepad.exe" }));

        var picked = Path.Combine(Scratch("picked"), "a.tforge");
        Assert.Equal(new[] { picked }, AvaloniaFileDialogs.CheckedFiles(new[] { picked }));
    }
}
