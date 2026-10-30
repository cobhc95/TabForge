using System;
using System.Collections.Generic;
using System.Reflection;
using TabForge.Audio;
using TabForge.Avalonia;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The window's answers to the shared engine: a plug-in crash saves the quarantine and says the plug-in was switched off; a changed audio route
/// syncs the shown song once, and an unchanged one not at all.
/// Owns: these checks. Does not own: the controller (AvaloniaEngineEvents) or the engine client (AudioEngineClient).
/// Tests: this class.
/// </summary>
public class PluginEngineEventsTests
{
    [Fact]
    public void ACrashSavesTheQuarantineAndSaysThePluginWasSwitchedOff()
    {
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new EventHost();
        using var events = new AvaloniaEngineEvents(engine, host, new AvaloniaPlaybackSessionTests.FakeUiThread());
        Raise(engine, "PluginCrashed", "C:\\Plugins\\Crashy.dll");
        Assert.Equal(1, host.Saves);
        Assert.Equal("Crashy stopped working and was switched off; playback continues", Assert.Single(host.Status));
    }

    [Fact]
    public void AChangedAudioRouteSyncsTheSongOnceAndAnUnchangedOneSyncsNothing()
    {
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new EventHost();
        using var events = new AvaloniaEngineEvents(engine, host, new AvaloniaPlaybackSessionTests.FakeUiThread());
        host.Plugins.SampleRate = 44100;
        events.ApplyRouteSettings();
        events.ApplyRouteSettings();
        Assert.Equal(1, host.Syncs);
    }

    [Fact]
    public void AChangedSoundEnginePanicsOnceAndARepeatChangePanicsAgain()
    {
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new EventHost();
        using var events = new AvaloniaEngineEvents(engine, host, new AvaloniaPlaybackSessionTests.FakeUiThread());
        host.Plugins.SoundEngine = SoundEngines.None;
        events.ApplyRouteSettings();
        Assert.Equal(1, host.Panics);
        Assert.Equal(1, host.Syncs);
        events.ApplyRouteSettings();   // nothing changed: neither again
        Assert.Equal(1, host.Panics);
        Assert.Equal(1, host.Syncs);
        host.Plugins.SoundEngine = SoundEngines.WindowsGm;   // back: the stored play-all choice is untouched, behaviour restored
        host.Plugins.PlayAllThroughEngine = true;
        events.ApplyRouteSettings();
        Assert.Equal(2, host.Panics);
        Assert.Equal(2, host.Syncs);
        Assert.True(host.Plugins.PlayAllThroughEngine);
    }

    // Raises one of the client's events from the test (the client's own handlers are what the controller attached to).
    private static void Raise(AudioEngineClient engine, string eventName, params object[] args)
    {
        var handler = (Delegate?)typeof(AudioEngineClient).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(engine);
        handler?.DynamicInvoke(args);
    }

    private sealed class EventHost : IAvaloniaEngineEventHost
    {
        public IReadOnlyList<SongDocument> Songs { get; } = Array.Empty<SongDocument>();
        public SongDocument Active => throw new InvalidOperationException("No song is shown in this test.");
        public PluginSettings Plugins { get; } = new();
        public global::Avalonia.Controls.Window? Owner => null;
        public int Saves { get; private set; }
        public int Syncs { get; private set; }
        public int Panics { get; private set; }
        public List<string> Status { get; } = new();
        public void SaveSettings() => Saves++;
        public void PanicAll() => Panics++;
        public void ShowStatus(string text) => Status.Add(text);
        public void SyncActive() => Syncs++;
        public AvaloniaPlaybackSession? SessionOf(SongDocument song) => null;
    }
}
