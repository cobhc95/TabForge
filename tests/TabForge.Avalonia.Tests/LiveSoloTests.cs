using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Avalonia.Playback;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the live input's solo rule: one rule (MixerGroups.IsAudible with the live-solo flag) gates the engine levels and the MIDI audible mask alike,
/// a render ignores it, and it is cleared when live input turns off or its window closes (a queued sync is flushed first).
/// Does not own: the live strip's knobs (LiveStripBehaviourTests).
/// Tests: this class.
/// </summary>
public class LiveSoloTests
{
    private sealed class QueueUiThread : IUiThread
    {
        public readonly List<Action> Queue = new();
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, UiPriority priority = UiPriority.Normal) => Queue.Add(work);
        public Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) { work(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => Task.FromResult(work());
        public Task YieldAsync(UiPriority priority) => Task.CompletedTask;
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background) => new AvaloniaPlaybackSessionTests.FakeUiThread().CreateTimer(interval, priority);
    }

    private sealed class Host : ILiveInputSessionHost
    {
        public Host(AudioEngineClient engine) => Engine = engine;
        public PluginSettings Settings { get; } = new();
        public AudioEngineClient Engine { get; }
        public QueueUiThread Thread { get; } = new();
        public IUiThread UiThread => Thread;
        public int Syncs;
        public bool SoloAtLastSync;
        public void SyncAudioEngine() { Syncs++; SoloAtLastSync = Engine.LiveInput.Solo; }
        public void SaveSettings() { }
        public void SetStatus(string text) { }
        public (bool Restore, bool Remember) AskRestoreChain() => (false, false);
        public void ShowFxChain(Action closed) { }
    }

    [Fact]
    public void LiveSoloGatesEngineLevelsAndTheMidiMaskByOneRule()
    {
        var project = DemoSongFactory.Create();
        Assert.True(project.Tracks.Count >= 2);
        foreach (var t in project.Tracks) { t.Solo = false; t.Mute = false; }
        project.Tracks[1].Solo = false;
        Assert.All(project.Tracks, t => Assert.True(MixerGroups.IsAudible(project, t)));

        var levels = EngineMixLevels.Levels(project, 100, liveSolo: true);
        Assert.All(project.Tracks, t => Assert.Equal(0, levels(t).Volume));   // the engine-routed levels
        Assert.All(PlaybackEngine_Mask(project, true), audible => Assert.False(audible));   // the MIDI mask (Windows-MIDI tracks included)

        project.Tracks[0].Solo = true;   // a soloed song track plays beside the live solo, in both
        Assert.NotEqual(0, EngineMixLevels.Levels(project, 100, liveSolo: true)(project.Tracks[0]).Volume);
        Assert.True(PlaybackEngine_Mask(project, true)[0]);
        Assert.False(PlaybackEngine_Mask(project, true)[1]);

        foreach (var t in project.Tracks) t.Solo = false;
        Assert.All(PlaybackEngine_Mask(project, false), audible => Assert.True(audible));
        Assert.All(project.Tracks, t => Assert.NotEqual(0, EngineMixLevels.Levels(project, 100)(t).Volume));
    }

    [Fact]
    public void ASoloedTrackInAMutedGroupStaysSilent()
    {
        var project = DemoSongFactory.Create();
        foreach (var t in project.Tracks) { t.Solo = false; t.Mute = false; }
        var track = project.Tracks[0];
        track.Solo = true;
        var group = project.Mixer.Levels(MixerGroups.GroupOf(project, track));
        group.Mute = true;
        try { Assert.False(MixerGroups.IsAudible(project, track)); }   // the group's mute wins over the track's solo
        finally { group.Mute = false; }   // the levels object may outlive this project
    }

    private static bool[] PlaybackEngine_Mask(SongProject project, bool liveSolo) => project.Tracks.Select(t => MixerGroups.IsAudible(project, t, liveSolo)).ToArray();

    [Fact]
    public void ARenderSyncIgnoresTheLiveSolo()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var project = DemoSongFactory.Create();
        foreach (var t in project.Tracks) { t.Solo = false; t.Mute = false; }
        client.LiveInput.SetSolo(true);
        EngineSongSync.Apply(project, client, new PluginSettings(), 100, liveSolo: false);
        Assert.True(client.LiveInput.Solo);   // the flag itself is untouched; only the song's levels ignore it
        Assert.NotEqual(0, EngineMixLevels.Levels(project, 100, liveSolo: false)(project.Tracks[0]).Volume);
    }

    [Fact]
    public void TurningLiveInputOffClearsItsSolo()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new Host(client);
        using var session = new LiveInputSession(host);
        session.SetMidiInput("");
        session.Toggle();
        Assert.True(session.IsOn);
        session.SetSolo(true);
        Assert.True(session.Soloed);
        session.Toggle();
        Assert.False(session.IsOn);
        Assert.False(session.Soloed);
        Assert.False(host.SoloAtLastSync);
    }

    [Fact]
    public void DisposeClearsTheSoloAndFlushesAQueuedSync()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new Host(client);
        var session = new LiveInputSession(host);
        session.SetMidiInput("");
        session.Toggle();
        session.SetSolo(true);
        var before = host.Syncs;
        session.Dispose();
        Assert.False(client.LiveInput.Solo);
        Assert.True(host.Syncs > before);
        Assert.False(host.SoloAtLastSync);

        using var client2 = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host2 = new Host(client2);
        var session2 = new LiveInputSession(host2);
        session2.SetMidiInput("");
        session2.Toggle();
        session2.SetLevel(100, 64);   // queues a coalesced sync that has not run
        var queued = host2.Syncs;
        session2.Dispose();
        Assert.True(host2.Syncs > queued);
    }
}
