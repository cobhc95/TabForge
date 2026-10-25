using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: File > Save / Save As of the Avalonia window and the close question's Save (MainWindowSaving through DocumentSaveFlow).</summary>
public sealed class MainWindowSaveTests : IDisposable
{
    /// <summary>The bytes the app's save path writes for <see cref="CharacterizationSong"/> (the same constant as the app's TestSaveFlowCharacterization).</summary>
    private const string AppTforgeSha256 = "22BD95662359DD89C07CFB21865BFCEC6B453F8F3C0814AF4890DC62889143B9";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-avsave-{Guid.NewGuid():N}");

    public MainWindowSaveTests() => Directory.CreateDirectory(_folder);

    /// <summary>A window without an audio engine: a save reads no plug-in states.</summary>
    private static SongPluginStates NoEngine => SongPluginStates.ForEngine(null, () => null);

    /// <summary>The Default save format setting when it is .tforge.</summary>
    private static Func<string> TforgeFormat => () => "tforge";

    /// <summary>A save that must not ask a question: the test fails if one is asked.</summary>
    private static Func<SavePrompt, Task<PromptButton>> NoQuestions => _ => throw new InvalidOperationException("no question expected");

    /// <summary>The process is not degraded (no unexpected error happened).</summary>
    private static Func<DegradedSaveGate> NoDegradedGate => () => new DegradedSaveGate();

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private sealed class SaveTo : IFileDialogs
    {
        public SaveTo(string? path) => Path = path;
        public string? Path { get; }
        public List<FileDialogOptions> Asked { get; } = new();
        public string? SaveFile(FileDialogOptions options, object? owner) { Asked.Add(options); return Path; }
        public string? OpenFile(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no open dialog expected");
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no open dialog expected");
        public string? PickFolder(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no folder dialog expected");
        public string[]? PickFolders(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no folder dialog expected");
    }

    /// <summary>The app's characterization song: two one-bar tracks with fixed ids, one note, lyrics.</summary>
    private static SongDocument CharacterizationSong()
    {
        var project = new SongProject { Title = "Characterization", Lyrics = "la la" };
        foreach (var name in new[] { "Lead", "Bass" }) project.Tracks.Add(new TrackModel { Name = name, Measures = TemplateFactory.Measures(1) });
        for (var i = 0; i < project.Tracks.Count; i++) project.Tracks[i].Id = new Guid($"5a1e0000-0000-4000-8000-00000000000{i}");
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        DocumentEdits.Run(document, p => { p.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 5 }); return true; });
        return document;
    }

    [AvaloniaFact]
    public async Task ExportGuitarProWritesTheGpFileAndLeavesTheSongAsItWas()
    {
        var path = Path.Combine(_folder, "song.gp");
        var reports = new List<string>();
        var document = CharacterizationSong();
        var dirty = document.IsDirty;
        var exported = await new MainWindowSaving(new SaveTo(path), null, reports.Add, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).ExportGuitarPro(document, (_, _) => Task.FromResult(GpExportChoice.ExportCompatible));
        Assert.True(exported, string.Join("; ", reports));
        Assert.True(File.Exists(path));
        Assert.Equal(dirty, document.IsDirty);
        Assert.Null(document.Path);
    }

    [AvaloniaFact]
    public async Task ExportGuitarProCancelledInThePreflightWritesNothing()
    {
        var path = Path.Combine(_folder, "lossy.gp");
        var document = CharacterizationSong();
        var asked = false;
        var exported = await new MainWindowSaving(new SaveTo(path), null, _ => { }, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).ExportGuitarPro(document, (_, _) => { asked = true; return Task.FromResult(GpExportChoice.Cancel); });
        Assert.Equal(GpExportPreflight.Analyze(document.Project).ShouldAskFor(GpExportKind.Export), asked);
        Assert.Equal(!asked, exported);
        Assert.Equal(!asked, File.Exists(path));
    }

    [AvaloniaFact]
    public async Task EditThenSaveWritesTheAppsBytesAndLoadsBackEqual()
    {
        var path = Path.Combine(_folder, "song.tforge");
        var dialogs = new SaveTo(path);
        var reports = new List<string>();
        var document = CharacterizationSong();
        Assert.True(document.HasUnsavedChanges);

        Assert.True(await new MainWindowSaving(dialogs, null, reports.Add, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).Save(document));

        Assert.Single(dialogs.Asked);   // no file yet: Save asks where
        Assert.Equal(AppTforgeSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        Assert.Equal(ProjectService.ContentHash(document.Project), ProjectService.ContentHash(ProjectService.Load(path)));
        Assert.False(document.HasUnsavedChanges);
        Assert.Equal(path, document.Path);
        Assert.Equal(new[] { "Saved song.tforge" }, reports);

        MainWindowLifetimeTests.MakeEdit(document);
        Assert.True(await new MainWindowSaving(dialogs, null, reports.Add, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).Save(document));
        Assert.Single(dialogs.Asked);   // the song has its file now: Save writes in place
        Assert.False(document.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task FailedSaveIsReportedAndKeepsTheSongUnsaved()
    {
        var path = Path.Combine(_folder, "locked.tforge");
        File.WriteAllText(path, "old");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var reports = new List<string>();
            var document = CharacterizationSong();
            Assert.False(await new MainWindowSaving(new SaveTo(path), null, reports.Add, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).Save(document));
            Assert.True(document.HasUnsavedChanges);
            Assert.Equal("old", File.ReadAllText(path));
            Assert.StartsWith("Save failed: ", Assert.Single(reports));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [AvaloniaFact]
    public void ClosePromptSaveSavesThenCloses()   // the save runs on a worker: the dispatcher is pumped until the window has closed
    {
        var path = Path.Combine(_folder, "closing.tforge");
        var window = new Window();
        var saving = new MainWindowSaving(new SaveTo(path), window, _ => { }, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate);
        var document = CharacterizationSong();
        var tabs = new MainWindowTabs(document, _ => Task.FromResult(PromptButton.Yes), saving.Save);
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs);
        window.Show();

        window.Close();
        for (var wait = Stopwatch.StartNew(); window.IsVisible && wait.Elapsed < TimeSpan.FromSeconds(20);) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }

        Assert.False(window.IsVisible);
        Assert.True(File.Exists(path));
        Assert.False(document.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task SaveAsStartsOnGpAndWritesTheChosenGpFileWhenTheDefaultSaveFormatIsGp()
    {
        var path = Path.Combine(_folder, "chosen.gp");
        var dialogs = new SaveTo(path);
        var document = CharacterizationSong();

        Assert.True(await new MainWindowSaving(dialogs, null, _ => { }, NoEngine, () => "gp", NoQuestions, NoDegradedGate).SaveAs(document));

        var options = Assert.Single(dialogs.Asked);
        Assert.Equal(".gp", options.DefaultExt);
        Assert.StartsWith(".gp file", options.Filter, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
        Assert.Equal(path, document.Path);
        Assert.False(document.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task SaveAsStartsOnTforgeWhenTheDefaultSaveFormatIsTforge()
    {
        var dialogs = new SaveTo(null);
        var document = CharacterizationSong();

        Assert.False(await new MainWindowSaving(dialogs, null, _ => { }, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).SaveAs(document));

        var options = Assert.Single(dialogs.Asked);
        Assert.Equal(".tforge", options.DefaultExt);
        Assert.StartsWith("TabForge project", options.Filter, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SaveAfterAnUnexpectedErrorCancelledLeavesTheExistingFileAsItWas()
    {
        var path = Path.Combine(_folder, "degraded-cancel.tforge");
        File.WriteAllText(path, "old");
        var document = CharacterizationSong();
        document.Path = path;
        var gate = new DegradedSaveGate();
        gate.MarkDegraded();
        var asked = new List<SavePrompt>();
        var saving = new MainWindowSaving(new SaveTo(path), null, _ => { }, NoEngine, TforgeFormat,
            prompt => { asked.Add(prompt); return Task.FromResult(PromptButton.Cancel); }, () => gate);

        Assert.False(await saving.Save(document));

        Assert.Single(asked);
        Assert.Equal("old", File.ReadAllText(path));
        Assert.True(document.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task SaveAfterAnUnexpectedErrorOverwriteWritesOnceThenAsksNoMore()
    {
        var path = Path.Combine(_folder, "degraded-overwrite.tforge");
        File.WriteAllText(path, "old");
        var document = CharacterizationSong();
        document.Path = path;
        var gate = new DegradedSaveGate();
        gate.MarkDegraded();
        var asked = new List<SavePrompt>();
        var saving = new MainWindowSaving(new SaveTo(path), null, _ => { }, NoEngine, TforgeFormat,
            prompt => { asked.Add(prompt); return Task.FromResult(PromptButton.No); }, () => gate);

        Assert.True(await saving.Save(document));
        MainWindowLifetimeTests.MakeEdit(document);
        Assert.True(await saving.Save(document));

        Assert.Single(asked);
        Assert.Equal(ProjectService.ContentHash(document.Project), ProjectService.ContentHash(ProjectService.Load(path)));
    }

    [AvaloniaFact]
    public async Task SaveWritesThePluginStatesTheEngineReadsForEveryRigIncludingTheMonitorChain()
    {
        var path = Path.Combine(_folder, "chained.tforge");
        var document = CharacterizationSong();
        var synth = new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument };
        document.Project.Tracks[0].Rig.Plugins.Add(synth);
        var monitor = new BusChain();
        var speakers = new PluginSlot { Name = "Speakers", Path = @"C:\NoSuch\Speakers.vst3", Format = "VST3" };
        monitor.Rig.Plugins.Add(speakers);
        var askedSlots = new List<PluginSlot>();
        var timeouts = new List<int>();
        var states = SongPluginStates.ForReader((tracks, timeoutMs) =>
        {
            var slots = tracks.SelectMany(t => t.Rig.Plugins).ToList();
            askedSlots.AddRange(slots);
            timeouts.Add(timeoutMs);
            foreach (var slot in slots) slot.State = $"state of {slot.Name}";
            return Task.FromResult(new StateCollection());
        }, () => monitor);

        Assert.True(await new MainWindowSaving(new SaveTo(path), null, _ => { }, states, TforgeFormat, NoQuestions, NoDegradedGate).Save(document));

        Assert.Contains(synth, askedSlots);
        Assert.Contains(speakers, askedSlots);
        Assert.Equal(new[] { SongPluginStates.CollectTimeoutMs }, timeouts);
        Assert.Equal("state of Synth", ProjectService.Load(path).Tracks[0].Rig.Plugins[0].State);
    }

    [AvaloniaFact]
    public void ClosePromptSaveCancelledKeepsTheWindow()
    {
        var window = new Window();
        var tabs = new MainWindowTabs(CharacterizationSong(), _ => Task.FromResult(PromptButton.Yes), new MainWindowSaving(new SaveTo(null), window, _ => { }, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate).Save);
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs);
        window.Show();

        window.Close();

        Assert.True(window.IsVisible);
        Assert.True(tabs.Active.HasUnsavedChanges);
    }

    private sealed class DialogProbe(Action onSave) : IFileDialogs
    {
        public string? SaveFile(FileDialogOptions options, object? owner) { onSave(); return null; }
        public string? OpenFile(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no open dialog expected");
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no open dialog expected");
        public string? PickFolder(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no folder dialog expected");
        public string[]? PickFolders(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no folder dialog expected");
    }

    [AvaloniaFact]
    public async Task SaveAsAndExportHoldTheSaveClaimWhileTheirDialogIsOpen()
    {
        MainWindowSaving? saving = null;
        var idleDuringDialog = new List<bool>();
        saving = new MainWindowSaving(new DialogProbe(() => idleDuringDialog.Add(saving!.WhenIdle().IsCompleted)), null, _ => { }, NoEngine, TforgeFormat, NoQuestions, NoDegradedGate);
        var document = CharacterizationSong();
        Assert.False(await saving.SaveAs(document));
        Assert.False(await saving.ExportGuitarPro(document, (_, _) => Task.FromResult(GpExportChoice.Cancel)));
        Assert.Equal(new[] { false, false }, idleDuringDialog);
        Assert.True(saving.WhenIdle().IsCompleted);
    }

    [AvaloniaFact]
    public void ASecondCloseWhileTheSaveQuestionIsOpenDoesNotAskAgain()
    {
        var window = new Window();
        var asked = 0;
        var answer = new TaskCompletionSource<PromptButton>();
        var tabs = new MainWindowTabs(CharacterizationSong(), _ => { asked++; return answer.Task; }, _ => Task.FromResult(false));
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs);
        window.Show();

        window.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, asked);
        Assert.True(window.IsVisible);
        answer.SetResult(PromptButton.Cancel);
    }
}
