using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what the Load FX chain menu item does on the UI thread before and while its picker is up: it returns at once (the picker is awaited, never a nested
/// message loop), the window stays responsive while the picker is open, and the load finishes when the pick arrives.
/// Does not own: the load itself (FxChainWindowTests) or the picker's start folder (FileDialogHangTests).
/// Tests: this class.
/// </summary>
public class FxChainLoadMenuTests
{
    private readonly ITestOutputHelper _out;
    public FxChainLoadMenuTests(ITestOutputHelper output) => _out = output;

    /// <summary>A picker that stays open until the test answers it, as a real dialog does; it counts the synchronous calls (which would hold the UI thread).</summary>
    private sealed class OpenPicker : IFileDialogs
    {
        public TaskCompletionSource<string[]?> Answer { get; } = new();
        public int SyncCalls { get; private set; }
        public int AsyncCalls { get; private set; }
        public string? OpenFile(FileDialogOptions options, object? owner) { SyncCalls++; return null; }
        public string[]? OpenFiles(FileDialogOptions options, object? owner) { SyncCalls++; return null; }
        public Task<string[]?> OpenFilesAsync(FileDialogOptions options, object? owner) { AsyncCalls++; return Answer.Task; }
        public string? SaveFile(FileDialogOptions options, object? owner) { SyncCalls++; return null; }
        public Task<string?> SaveFileAsync(FileDialogOptions options, object? owner) { AsyncCalls++; return new TaskCompletionSource<string?>().Task; }
        public string? PickFolder(FileDialogOptions options, object? owner) => null;
        public string[]? PickFolders(FileDialogOptions options, object? owner) => null;
    }

    [AvaloniaFact]
    public void TheMenuItemReturnsWhileThePickerIsOpenAndTheWindowStaysResponsive()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-chain-{Guid.NewGuid():N}.tfchain");
        try
        {
            PluginLibrary.SaveChain(file, FxChainWindow.SampleChain());
            var track = FxTestKit.Track();
            var picker = new OpenPicker();
            var window = FxTestKit.Window(track, files: picker);
            window.Show();
            DialogTestKit.RunQueuedUiWork();

            var clock = Stopwatch.StartNew();
            FxTestKit.Choose(window, "Load FX chain…");
            var returned = clock.ElapsedMilliseconds;
            _out.WriteLine($"the menu click returned after {returned} ms");

            Assert.Equal(0, picker.SyncCalls);
            Assert.Equal(1, picker.AsyncCalls);
            Assert.True(returned < 500, $"the menu click held the UI thread for {returned} ms");

            // The UI thread is free: queued work still runs while the picker is open.
            var ran = false;
            Dispatcher.UIThread.Post(() => ran = true);
            DialogTestKit.RunQueuedUiWork();
            Assert.True(ran);
            Assert.Empty(track.Rig.Plugins);   // nothing changed while the picker is open

            picker.Answer.SetResult(new[] { file });
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, track.Rig.Plugins.Select(s => s.Name));
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void ACancelledPickerLeavesTheChainAndTheWindowAlone()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var picker = new OpenPicker();
        var window = FxTestKit.Window(track, files: picker);
        window.Show();
        FxTestKit.Choose(window, "Load FX chain…");
        picker.Answer.SetResult(null);
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(3, track.Rig.Plugins.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void TheRealDialogsReturnFromTheMenuClickWithoutHangingTheUiThread()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var window = FxTestKit.Window(FxTestKit.Track(FxChainWindow.SampleChain()));   // the window's own AvaloniaFileDialogs
        window.Dialogs.Files = new TabForge.Avalonia.Platform.AvaloniaFileDialogs(null);
        window.Show();
        DialogTestKit.RunQueuedUiWork();

        var clock = Stopwatch.StartNew();
        FxTestKit.Choose(window, "Load FX chain…");
        var returned = clock.ElapsedMilliseconds;
        // The headless platform has no picker to show: the call may end quietly or with a refusal, but it must not hold the UI thread.
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 300) { DialogTestKit.RunQueuedUiWork(); Task.Delay(20).Wait(); }
        _out.WriteLine($"click returned after {returned} ms; the UI thread kept running for {deadline.ElapsedMilliseconds} ms afterwards");
        Assert.True(returned < 1500, $"the menu click held the UI thread for {returned} ms");
        window.Close();
    }
}
