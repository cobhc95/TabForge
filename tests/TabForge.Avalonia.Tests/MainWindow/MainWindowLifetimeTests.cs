using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the main window's close flow, key routing table and document tabs (MainWindowLifetime, MainWindowKeyboard, MainWindowTabs).</summary>
public class MainWindowLifetimeTests
{
    private static SongDocument Song(string title, bool dirty)
    {
        var song = new SongDocument(() => null) { Project = new SongProject { Title = title } };
        song.MarkClean();
        if (dirty) MakeEdit(song);
        return song;
    }

    internal static void MakeEdit(SongDocument song) => DocumentEdits.Run(song, p => { p.Title += " (edited)"; return true; });

    private static (Window Window, List<SavePrompt> Asked) OpenWindow(SongDocument song, PromptButton answer)
    {
        var asked = new List<SavePrompt>();
        var window = new Window();
        var tabs = new MainWindowTabs(song, p => { asked.Add(p); return Task.FromResult(answer); }, _ => Task.FromResult(false));
        var commands = new MainWindowCommands(window, () => { });
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), commands), tabs);
        window.Show();
        return (window, asked);
    }

    [AvaloniaFact]
    public void DirtySongCancelKeepsWindowOpen()
    {
        var (window, asked) = OpenWindow(Song("Riff", true), PromptButton.Cancel);
        window.Close();
        Assert.True(window.IsVisible);
        Assert.Equal("Save changes to the current project?", Assert.Single(asked).Message);
    }

    [AvaloniaFact]
    public void DirtySongDontSaveCloses()
    {
        var (window, asked) = OpenWindow(Song("Riff", true), PromptButton.No);
        window.Close();
        Assert.False(window.IsVisible);
        Assert.Single(asked);
    }

    [AvaloniaFact]
    public void CleanSongClosesWithoutAsking()
    {
        var (window, asked) = OpenWindow(Song("Riff", false), PromptButton.Cancel);
        window.Close();
        Assert.False(window.IsVisible);
        Assert.Empty(asked);
    }

    [AvaloniaFact]
    public async Task CloseWaitsForAFinishingRecording()
    {
        var finishing = new TaskCompletionSource();
        var window = new Window();
        var tabs = new MainWindowTabs(Song("Riff", false), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs) { FinishBeforeClose = () => finishing.Task };
        window.Show();

        window.Close();
        Assert.True(window.IsVisible);   // the MP4 is still being finished
        finishing.SetResult();
        await Task.Yield();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task CloseGoesOnWhenASaveClaimNeverEnds()
    {
        var never = new TaskCompletionSource();
        var window = new Window();
        var tabs = new MainWindowTabs(Song("Riff", false), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), whenSaved: () => never.Task);
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs);
        window.Show();

        window.Close();
        for (var i = 0; i < 80 && window.IsVisible; i++) { await Task.Delay(100); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Assert.False(window.IsVisible);
    }

    private static async Task<bool> ClosesWithin(Window window, int ms)
    {
        window.Close();
        for (var i = 0; i < ms / 100 && window.IsVisible; i++) { await Task.Delay(100); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        return !window.IsVisible;
    }

    private static Window Open(Func<Task>? finish, Func<Task>? whenSaved = null, SongDocument? song = null, Func<SavePrompt, Task<PromptButton>>? ask = null, TimeSpan? timeout = null)
    {
        var window = new Window();
        var tabs = new MainWindowTabs(song ?? Song("Riff", false), ask ?? (_ => Task.FromResult(PromptButton.Cancel)), _ => Task.FromResult(false), whenSaved: whenSaved);
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs) { FinishBeforeClose = finish, FinishBeforeCloseTimeout = timeout ?? TimeSpan.FromSeconds(20) };
        window.Show();
        return window;
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task CloseAfterASaveFinishedIsImmediate() => Assert.True(await ClosesWithin(Open(null, () => Task.CompletedTask), 2000));

    [AvaloniaFact(Timeout = 15000)]
    public async Task CloseGoesOnWhenARecordingNeverFinishes()
    {
        var never = new TaskCompletionSource();
        Assert.True(await ClosesWithin(Open(() => never.Task, timeout: TimeSpan.FromSeconds(2)), 5000));
    }

    [AvaloniaFact]
    public void CloseWaitsLongerThanTheRecordingSaveWatchdog()
    {
        var lifetime = new MainWindowLifetime(new Window(), new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(new Window(), () => { })), new MainWindowTabs(Song("Riff", false), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false)));
        Assert.True(lifetime.FinishBeforeCloseTimeout >= TimeSpan.FromSeconds(20));   // the save watchdog is 15 s: a close must not cut a save short
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task CloseWithAnotherWindowOpenIsImmediate()
    {
        var window = Open(null);
        var tool = new Window();
        tool.Show(window);
        Assert.True(await ClosesWithin(window, 2000));
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task CancelInTheClosePromptLetsTheNextCloseAskAgain()
    {
        var asked = 0;
        var window = Open(null, null, Song("Riff", true), _ => { asked++; return Task.FromResult(PromptButton.Cancel); });
        window.Close();
        for (var i = 0; i < 20; i++) { await Task.Delay(50); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        window.Close();
        for (var i = 0; i < 20; i++) { await Task.Delay(50); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(2, asked);
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public void CatalogueGesturesReachTheirCommands()
    {
        var ran = new List<string>();
        var commands = new MainWindowCommands(new Window(), () => { });
        commands.Add("Tab.New", () => ran.Add("Tab.New"));
        commands.Add("Tab.Close", () => ran.Add("Tab.Close"));
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands);
        Assert.Equal("File.Save", keyboard.CommandFor(Key.S, KeyModifiers.Control));
        Assert.True(keyboard.Route(Key.T, KeyModifiers.Control | KeyModifiers.Alt, null));
        Assert.True(keyboard.Route(Key.W, KeyModifiers.Control, null));
        Assert.False(keyboard.Route(Key.S, KeyModifiers.Control, null)); // Save is not ported: left to the focused control.
        Assert.True(keyboard.Route(Key.W, KeyModifiers.Control, new TextBox())); // Tab.Close is safe while typing
        Assert.Equal(new[] { "Tab.New", "Tab.Close", "Tab.Close" }, ran);
    }

    [AvaloniaFact]
    public void KeyTableFollowsRebuiltBindings()
    {
        var commands = new MainWindowCommands(new Window(), () => { });
        var hotkeys = new HotkeySettings();
        var keyboard = new MainWindowKeyboard(hotkeys, commands);
        Assert.Null(keyboard.CommandFor(Key.K, KeyModifiers.Control | KeyModifiers.Alt));
        hotkeys["File.Save"] = "Ctrl+Alt+K";
        keyboard.Rebuild(hotkeys);
        Assert.Equal("File.Save", keyboard.CommandFor(Key.K, KeyModifiers.Control | KeyModifiers.Alt));
    }

    [AvaloniaFact]
    public async Task TabsNewSwitchAndCloseDirtyAsks()
    {
        var asked = new List<SavePrompt>();
        var answer = PromptButton.Cancel;
        var tabs = new MainWindowTabs(Song("Riff", false), p => { asked.Add(p); return Task.FromResult(answer); }, _ => Task.FromResult(false));
        tabs.New();
        Assert.Equal(2, tabs.Songs.Count);
        Assert.Equal(1, tabs.ActiveIndex);
        MakeEdit(tabs.Active);
        Assert.False(await tabs.CloseActive());
        Assert.Equal(2, tabs.Songs.Count);
        answer = PromptButton.Yes; // the save function here fails, so the song stays open
        Assert.False(await tabs.CloseActive());
        answer = PromptButton.No;
        Assert.True(await tabs.CloseActive());
        Assert.Equal(3, asked.Count);
        Assert.Single(tabs.Songs);
        tabs.Switch(0);
        Assert.True(await tabs.CloseActive()); // a clean song closes without asking; the last tab becomes a new one
        Assert.Equal(3, asked.Count);
        Assert.Single(tabs.Songs);
    }
}
