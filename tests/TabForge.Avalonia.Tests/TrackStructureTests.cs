using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine following the track list: deleting or adding a track, and Undo of either, bring the engine in line at once (a deleted track
/// stops sounding), and a full engine tells the user instead of failing the sync.
/// Tests: this class.
/// </summary>
public sealed class TrackStructureTests
{
    private static SongDocument SongOfPluginTracks(int count)
    {
        var project = new SongProject();
        project.Tracks.Clear();
        for (var i = 0; i < count; i++)
        {
            var track = NewTrackFactory.CreateTrack(project, TrackKind.Guitar);
            track.SoundSource = SoundSources.Plugins;
            project.Tracks.Add(track);
        }
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        return song;
    }

    [Fact]
    public void DeleteAddAndTheirUndoSyncTheEngine()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine()) { Engine = client };
        var song = SongOfPluginTracks(3);
        var plugins = new PluginSettings();
        using var structure = new MainWindowTrackStructure(sessions, () => plugins);
        structure.Bind(song);
        sessions.SyncEngine(song, plugins);
        var all = client.SentChainCountForTest;   // the three tracks and the song buses

        MainWindowTrackMenu.RemoveTrack(song, 1);
        Assert.Equal(all - 1, client.SentChainCountForTest);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(all, client.SentChainCountForTest);

        MainWindowTrackMenu.AddTrack(song, TrackKind.Guitar, track => track.SoundSource = SoundSources.Plugins);
        sessions.SyncEngine(song, plugins);   // the first play or arm of the new track
        Assert.Equal(all + 1, client.SentChainCountForTest);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(all, client.SentChainCountForTest);
    }

    [AvaloniaFact]   // the event is raised on the UI thread: a plain Fact thread is the UI thread only when it touched the dispatcher first
    public void AnyTrackListChangeRaisesStructureChangedOnce()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine());
        var song = SongOfPluginTracks(3);
        var plugins = new PluginSettings();
        using var structure = new MainWindowTrackStructure(sessions, () => plugins);
        structure.Bind(song);
        var raised = 0;
        structure.StructureChanged += () => raised++;

        MainWindowTrackMenu.RemoveTrack(song, 1);
        Assert.Equal(1, raised);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(2, raised);
    }

    [AvaloniaFact]
    public void MoreTracksThanSlotsLeavesTheExtraSilentAndTellsTheUser()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var told = 0;
        var limitSeen = -1;
        client.TrackLimitReached += limit => { limitSeen = limit; told++; };
        var song = SongOfPluginTracks(MixerBuses.BusBase + 3);

        client.Sync(song.Project.Tracks, new PluginSettings(), project: song.Project, owner: song);

        Assert.True(System.Threading.SpinWait.SpinUntil(() => { DialogTestKit.RunQueuedUiWork(); return told > 0; }, 5000), "the limit notice is posted to the UI context");   // posted, so it arrives a moment after Sync returns
        Assert.Equal(1, told);
        Assert.Equal(MixerBuses.BusBase, limitSeen);
        Assert.Equal(MixerBuses.BusBase, client.SentChainCountForTest);
    }
}
