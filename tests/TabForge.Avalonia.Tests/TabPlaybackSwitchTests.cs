using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the playback of a playing song across a tab add and a switch round trip: adding a tab never stops it, whatever the switch
//   preference says, and switching away and back finds the same session still playing with its engine track loaded.
// Does not own: the transport (AvaloniaPlaybackSessionTests) or the engine sync rules (AudioEngineClient).
// Tests: MainWindowTabs, TabPlaybackSwitchPolicy, AvaloniaPlaybackSessions, AudioEngineClient.IsOwnerPlaying.
public sealed class TabPlaybackSwitchTests
{
    private static Task<PromptButton> NoAnswer(SavePrompt _) => Task.FromResult(PromptButton.Cancel);

    private static Task<bool> NoSave(SongDocument _) => Task.FromResult(false);

    /// <summary>A song with one plug-in track, so the shared engine holds a slot for it.</summary>
    private static SongDocument EngineTrackSong()
    {
        var project = TemplateFactory.Blank();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Guitar);
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
        project.Tracks.Add(track);
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        return song;
    }

    [Fact]
    public void AddingATabLeavesThePlayingSongAndItsEngineTrackAlone()
    {
        var engines = new List<AvaloniaPlaybackSessionTests.FakeEngine>();
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        client.WarmIdle = System.TimeSpan.Zero;   // an owner that is not kept live is removed at once, not merely parked
        using var playback = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(),
            () => { var engine = new AvaloniaPlaybackSessionTests.FakeEngine(); engines.Add(engine); return engine; }) { Engine = client };
        var songA = EngineTrackSong();
        var settings = new AppSettings();
        settings.Tabs.PlaybackOnTabSwitch = TabPlaybackActions.StopPrevious;   // adding must ignore even Stop
        var tabs = new MainWindowTabs(songA, NoAnswer, NoSave, settings: settings, playback: playback);
        var sessionA = playback.For(songA)!;
        sessionA.PlayOrPause();
        var trackA = songA.Project.Tracks[0];
        Assert.True(client.SlotOf(trackA) >= 0, "A's track is in the engine before the new tab");

        tabs.New();
        _ = playback.For(tabs.Active);   // the window's panes create the new tab's session when it becomes shown

        Assert.True(sessionA.IsPlaying);
        Assert.False(engines[0].Stopped);
        Assert.True(client.SlotOf(trackA) >= 0, "A's engine track must stay loaded beside the new tab");
    }

    [Fact]
    public void SwitchingAwayAndBackFindsTheSameSessionStillPlaying()
    {
        var engines = new List<AvaloniaPlaybackSessionTests.FakeEngine>();
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        client.WarmIdle = System.TimeSpan.Zero;
        using var playback = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(),
            () => { var engine = new AvaloniaPlaybackSessionTests.FakeEngine(); engines.Add(engine); return engine; }) { Engine = client };
        var songA = EngineTrackSong();
        var songB = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(songA, NoAnswer, NoSave, settings: new AppSettings(), playback: playback);
        var sessionA = playback.For(songA)!;
        sessionA.PlayOrPause();
        var trackA = songA.Project.Tracks[0];

        tabs.Open(songB);                 // add beside A and show B
        _ = playback.For(tabs.Active);
        tabs.Switch(0);                   // back to A
        tabs.Switch(1);                   // away to B
        _ = playback.For(tabs.Active);
        tabs.Switch(0);                   // back to A again

        Assert.Same(songA, tabs.Active);
        Assert.Same(sessionA, playback.Existing(songA));
        Assert.True(sessionA.IsPlaying);
        Assert.False(engines[0].Stopped);
        Assert.True(client.SlotOf(trackA) >= 0);
    }
}
