using System;
using TabForge.Audio;
using TabForge.Avalonia.Playback;
using TabForge.LiveInput;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check of live input's arm state through the Core session offline: with no input chosen a press asks for the menu and stays off;
/// with a MIDI choice it arms the program's stand-in and a second press disarms it; the restore question is pending only under Ask with a
/// saved chain; the transport bar's live input button runs Transport.LiveInput.
/// Does not own: the engine's monitoring (it needs an audio device), the menu's look.
/// Tests: this class.
/// </summary>
public class LiveInputSessionTests
{
    private sealed class FakeHost : ILiveInputSessionHost
    {
        public FakeHost(AudioEngineClient engine) => Engine = engine;
        public PluginSettings Settings { get; } = new();
        public AudioEngineClient Engine { get; }
        public IUiThread UiThread { get; } = new AvaloniaPlaybackSessionTests.FakeUiThread();
        public int Syncs, Saves;
        public string Status = "";
        public void SyncAudioEngine() => Syncs++;
        public void SaveSettings() => Saves++;
        public void SetStatus(string text) => Status = text;
        public (bool Restore, bool Remember) AskRestoreChain() => (false, false);
        public void ShowFxChain(Action closed) { }
    }

    [Fact]
    public void APressArmsAndDisarmsLiveInputAndNoInputAsksForTheMenu()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new FakeHost(client);
        host.Settings.LiveInput.Input = "";
        host.Settings.LiveInput.MidiDevice = null;
        using var session = new LiveInputSession(host);
        var menus = 0; var changes = 0;
        session.MenuRequested += () => menus++;
        session.Changed += () => changes++;

        session.Toggle();
        Assert.False(session.IsOn);
        Assert.Equal(1, menus);

        session.SetMidiInput("");
        session.Toggle();
        Assert.True(session.IsOn);
        Assert.True(client.LiveInput.Track.RecordArm);
        Assert.True(host.Syncs > 0);

        session.Toggle();
        Assert.False(session.IsOn);
        Assert.Equal(LiveInputSession.OffText, host.Status);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void TheRestoreQuestionIsPendingOnlyUnderAskWithASavedChain()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new FakeHost(client);
        using var session = new LiveInputSession(host);
        Assert.False(session.RestoreQuestionPending);
        host.Settings.LiveInput.Chain.Plugins.Add(new TabForge.Plugins.PluginSlot());
        host.Settings.LiveInput.Restore = LiveInputSettings.RestoreAsk;
        Assert.True(session.RestoreQuestionPending);
        host.Settings.LiveInput.Restore = LiveInputSettings.RestoreNever;
        Assert.False(session.RestoreQuestionPending);
    }

    [Fact]
    public void TheTransportLiveInputButtonRunsTheLiveInputCommand()
    {
        Assert.Equal(MainWindowLiveInput.CommandId, TabForge.Avalonia.Platform.TransportBarView.LiveInputItem.CommandId);
        Assert.Contains(MainWindowLiveInput.CommandId, MainWindowLiveInput.Commands);
    }
}
