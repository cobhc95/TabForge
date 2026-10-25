using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Playback;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check of Transport Record with the engine not running (offline): nothing armed asks for an armed track, an armed audio track
/// reports that recording could not start and does not record.
/// A finished take lands on a free lane of the owning song's track, the newest playing.
/// Recording while the song plays starts at the playhead: the MIDI take is anchored at the song second there and the engine's song
/// clock is told the timeline position once, before the record command; its notes follow the live song second as playback moves on.
/// Does not own: the engine's recording itself (it needs an audio device).
/// Tests: this class.
/// </summary>
public class TransportRecordingTests
{
    [Fact]
    public void AFinishedTakeLandsOnANewLaneOfTheOwningSongAndPlays()
    {
        var song = new TabForge.Documents.SongDocument(() => null);
        var track = new TrackModel();
        song.Project.Tracks.Add(track);
        track.AudioClips.Add(new AudioClip { File = "old.wav", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4 });
        var status = MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 2);
        Assert.Equal(RecordedTakes.StatusText(track, 1, 2), status);
        Assert.Equal(2, track.AudioClips.Count);
        Assert.Equal(1, track.AudioClips[^1].Lane);
        Assert.Null(MainWindowTransportRecord.LandTake(new[] { song }, new TrackModel(), "other.wav", 0, 1));
    }

    [Fact]
    public void RecordWithNothingArmedAsksForAnArmedTrack()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var status = recording.Toggle(new List<TrackModel> { new TrackModel() }, "unused");
        Assert.Equal(TransportRecording.NothingArmedText, status);
        Assert.False(recording.IsRecording);
    }

    [Fact]
    public void RecordWithAnArmedAudioTrackAndNoEngineReportsItCouldNotStart()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var status = recording.Toggle(new List<TrackModel> { new TrackModel { RecordArm = true } }, "unused");
        Assert.Equal(TransportRecording.CouldNotStartText, status);
        Assert.False(recording.IsRecording);
    }

    [Fact]
    public void ArmedAudioTrackWithAnUnusableFolderReportsTheFolderNotTheEngine()
    {
        var blocked = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-record-blocked-" + System.Guid.NewGuid().ToString("N"));
        System.IO.File.WriteAllText(blocked, "a file where the folder should be");
        try
        {
            using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
            client.AttachFakeForTest();   // the engine runs: the folder is what refuses the start
            var recording = new TransportRecording(client, client.MidiInput.CreateClient());
            var status = recording.Toggle(new List<TrackModel> { new TrackModel { RecordArm = true } }, blocked);
            Assert.StartsWith(TransportRecording.FolderNotUsableText, status);
            Assert.NotEqual(TransportRecording.CouldNotStartText, status);
            Assert.False(recording.IsRecording);
        }
        finally { try { System.IO.File.Delete(blocked); } catch (System.IO.IOException) { } }
    }

    [Fact]
    public void EngineSyncOfAnArmedTrackKeepsTheArmAndLetsRecordStart()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        using var sessions = new TabForge.Avalonia.Transport.AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), null) { Engine = client };
        var document = new TabForge.Documents.SongDocument(() => null);
        var track = new TrackModel { RecordArm = true };
        document.Project.Tracks.Add(track);
        sessions.SyncEngine(document, new TabForge.Services.PluginSettings());
        Assert.True(track.RecordArm);
        var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var status = recording.Toggle(document.Project.Tracks, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-sync-" + System.Guid.NewGuid().ToString("N")));
        Assert.Equal(!recording.IsRecording, status == TransportRecording.CouldNotStartText);
        if (recording.IsRecording) recording.Toggle(document.Project.Tracks, "unused");
    }
    [Fact]
    public void RecordingsOfASavedSongGoToItsMediaFolder()
    {
        var song = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Riff.tabforge");
        Assert.Equal(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Riff Media"), TabForge.Services.MediaFolders.ForSong(song));
        Assert.EndsWith(TabForge.Services.MediaFolders.UnsavedRecordingsFolderName, TabForge.Services.MediaFolders.ForSong(null));
    }
    [Fact]
    public void RecordKeepsWhereTheCursorWasSoStopCanPutItBack()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var document = new TabForge.Documents.SongDocument(() => null) { CursorBar = 3, CursorCell = 2, CursorString = 1 };
        document.Project.Tracks.Add(new TrackModel { RecordArm = true });
        using var session = new TabForge.Avalonia.Transport.AvaloniaPlaybackSession(new AvaloniaPlaybackSessionTests.FakeEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => document.Project);
        var start = new RecordingPresenter.RecordStart();
        var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        MainWindowTransportRecord.Toggle(recording, session, document, _ => { }, start);
        Assert.Equal((3, 2, 1), (start.Bar, start.Cell, start.String));
        if (recording.IsRecording) MainWindowTransportRecord.Toggle(recording, session, document, _ => { }, start);
    }

    [Fact]
    public void RecordFromAStopStartsWhereTheSongWasLastNavigatedTo()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var document = new TabForge.Documents.SongDocument(() => null) { CursorBar = 3, CursorCell = 2, CursorString = 1 };
        document.Project.Tracks.Add(new TrackModel { RecordArm = true });
        using var session = new TabForge.Avalonia.Transport.AvaloniaPlaybackSession(new AvaloniaPlaybackSessionTests.FakeEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => document.Project);
        session.Seek(6, 0);   // an arrangement or section click seeks the playback; the document's cursor is not moved
        var start = new RecordingPresenter.RecordStart();
        var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        MainWindowTransportRecord.Toggle(recording, session, document, _ => { }, start);
        Assert.Equal((6, 0), (start.Bar, start.Cell));
        if (recording.IsRecording) MainWindowTransportRecord.Toggle(recording, session, document, _ => { }, start);
    }

    [Fact]
    public void RecordingWhileTheSongPlaysAnchorsTheMidiTakeAtThePlayhead()
    {
        var song = new TabForge.Documents.SongDocument(() => null);
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true };
        song.Project.Tracks.Add(track);
        song.CursorBar = 1;   // left behind by an earlier click, not where the playhead is
        song.CursorCell = 0;
        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        using var sessions = new TabForge.Avalonia.Transport.AvaloniaPlaybackSessions(ui, () => playback);
        var session = sessions.For(song)!;
        session.PlayOrPause();
        playback.Report(new PlaybackPosition { Bar = 3, Cell = 2, BarFraction = 0.25, ElapsedMs = 5000 });
        ui.FrameTimer.Fire();

        var device = new FakeMidiDevice();
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var recording = new TransportRecording(client, new MidiInputHub(device).CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);

        Assert.True(recording.IsRecording);
        Assert.Equal((3, 2), (start.Bar, start.Cell));   // the take starts at the playhead, not at the stale cursor
        device.Play(0x90, 60, 100, Stopwatch.GetTimestamp());
        device.Play(0x80, 60, 0, Stopwatch.GetTimestamp());
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);

        var clip = Assert.Single(track.AudioClips);
        Assert.Equal(5.0, clip.StartSec, 6);
    }

    [Fact]
    public void RecordingWhilePlayingMidSongLandsTheMidiTakeOnTheSongClock()
    {
        var song = new TabForge.Documents.SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true };
        song.Project.Tracks.Add(track);
        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        using var sessions = new TabForge.Avalonia.Transport.AvaloniaPlaybackSessions(ui, () => playback);
        var session = sessions.For(song)!;
        session.PlayOrPause();
        // A timeline compiled from bar 8: its own seconds run behind the song clock by the dropped span.
        var timeline = MidiTimelineBuilder.Build(song.Project, new PlaybackOptions { StartBar = 8, RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        playback.RaiseTimelineChanged(timeline);
        var recordMs = timeline.Bars[2].StartMs + 120;
        playback.Report(new PlaybackPosition { Bar = timeline.Bars[2].Bar, Cell = 0, BarFraction = 0, ElapsedMs = recordMs });
        ui.FrameTimer.Fire();

        var device = new FakeMidiDevice();
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var recording = new TransportRecording(client, new MidiInputHub(device).CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);
        Assert.True(recording.IsRecording);

        device.Play(0x90, 60, 100, Stopwatch.GetTimestamp());
        device.Play(0x80, 60, 0, Stopwatch.GetTimestamp());
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
        MainWindowTransportRecord.LandMidiTakes(recording, song);

        var landed = Assert.Single(track.AudioClips);
        // The take lands at the song bar being played, not at the timeline second (which would be bar 2).
        Assert.Equal(timeline.Bars[2].Bar, AudioClipTimelineRules.BarAtSeconds(song.Project, landed.StartSec).Bar);
        Assert.True(landed.StartSec > recordMs / 1000.0 + 10, $"take at {landed.StartSec:0.00} s, timeline second {recordMs / 1000.0:0.00} s");
    }

    [Fact]
    public void RecordingWhilePlayingAnchorsTheEngineBeforeTheRecordCommand()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        using var sessions = new TabForge.Avalonia.Transport.AvaloniaPlaybackSessions(ui, () => playback) { Engine = client };
        var song = new TabForge.Documents.SongDocument(() => null);
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.PlayOrPause();
        playback.Report(new PlaybackPosition { Bar = 4, Cell = 1, BarFraction = 0.5, ElapsedMs = 5000 });
        ui.FrameTimer.Fire();

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);

        var owner = client.OwnerIdOf(song);
        Assert.Equal((false, 5.0), client.PositionSentForTest(owner));   // the clock is held at the playhead second: this app sends no later positions
        Assert.True(recording.IsRecording);
        var position = commands.IndexOf(EngineCommand.SetPosition);
        var record = commands.IndexOf(EngineCommand.Record);
        Assert.True(position >= 0 && record > position, $"position at {position}, record at {record}");
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
    }

    [Fact]
    public void MovingPlayheadStampsNotesAtTheLiveSchedulerPosition()
    {
        var song = new TabForge.Documents.SongDocument(() => null);
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true };
        song.Project.Tracks.Add(track);
        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        using var sessions = new TabForge.Avalonia.Transport.AvaloniaPlaybackSessions(ui, () => playback);
        var session = sessions.For(song)!;
        session.PlayOrPause();
        playback.Report(new PlaybackPosition { Bar = 3, Cell = 2, BarFraction = 0.25, ElapsedMs = 5000 });
        ui.FrameTimer.Fire();

        var device = new FakeMidiDevice();
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var recording = new TransportRecording(client, new MidiInputHub(device).CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);
        Assert.True(recording.IsRecording);

        // Playback moves on while recording: the notes must follow the live song position, not the press-time one.
        playback.Report(new PlaybackPosition { Bar = 4, Cell = 0, BarFraction = 0, ElapsedMs = 7000 });
        ui.FrameTimer.Fire();
        device.Play(0x90, 60, 100, Stopwatch.GetTimestamp());
        device.Play(0x80, 60, 0, Stopwatch.GetTimestamp());
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);

        var clip = Assert.Single(track.AudioClips);
        var note = Assert.Single(clip.Notes!);
        Assert.Equal(5.0, clip.StartSec, 6);   // the take still starts where Record was pressed
        Assert.Equal(7.0, clip.StartSec + note.StartSec, 1);   // the note lands where the song was when heard
    }

    [Fact]
    public void AudioTakeStartedAt5sTracksTheLiveSchedulerAsItAdvances()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        using var sessions = new TabForge.Avalonia.Transport.AvaloniaPlaybackSessions(ui, () => playback) { Engine = client };
        var song = new TabForge.Documents.SongDocument(() => null);
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        // A compiled timeline: the drawn playhead runs ahead of the engine's last report by wall time, as in the app.
        // (Raised after the start, as the engine hands its timeline over once the start compiles and clears StartPending.)
        var timeline = new ScoreTimeline { TotalMs = 8000, CountInMs = 0, PlayFromMs = 0 };
        for (var bar = 0; bar < 4; bar++) timeline.Bars.Add(new ScoreBar(bar, bar * 2000, (bar + 1) * 2000, 8, 120));
        session.PlayOrPause();
        playback.RaiseTimelineChanged(timeline);
        playback.Report(new PlaybackPosition { Bar = 3, Cell = 2, BarFraction = 0.25, ElapsedMs = 5000 });
        ui.FrameTimer.Fire();

        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var hooks = new RecordingPresenter.EngineHooks(client, tabs, _ => { });
        try
        {
            using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
            var start = new RecordingPresenter.RecordStart();
            MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, hooks, playback: sessions);
            Assert.True(recording.IsRecording);

            ui.FrameTimer.Fire();   // the first live frame starts the take at the live scheduler second, not the drawn playhead
            var take = Assert.Single(hooks.Live.All);
            Assert.Equal(5.0, take.StartSec, 6);
            Assert.Equal(5.0, take.EndSec, 6);

            // Playback moves on while recording: the take extends to the live scheduler second (the drawn playhead leads it by wall time).
            playback.Report(new PlaybackPosition { Bar = 4, Cell = 0, BarFraction = 0, ElapsedMs = 7000 });
            ui.FrameTimer.Fire();
            // Frames keep running while the engine has nothing new to report: the take still ends at the live scheduler second.
            System.Threading.Thread.Sleep(60);
            ui.FrameTimer.Fire();
            Assert.Equal(5.0, take.StartSec, 6);
            Assert.Equal(7.0, take.EndSec, 6);
            Assert.Equal(7.0, take.EndSec, 6);

            MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, hooks);

            // The take lands over the live span: press-anchored start, live-tracked end.
            var status = MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 5.0, 2.0);
            Assert.Equal(RecordedTakes.StatusText(track, 1, 2.0), status);
            var landed = Assert.Single(track.AudioClips);
            Assert.Equal(5.0, landed.StartSec, 6);
            Assert.Equal(7.0, landed.EndSec, 6);

            // One undo step removes the landed take again.
            Assert.NotNull(DocumentEdits.Undo(song));
            Assert.Empty(song.Project.Tracks.Single(candidate => candidate.Name == track.Name).AudioClips);
        }
        finally
        {
            hooks.Dispose();
            tabs.Dispose();
        }
    }

    /// <summary>A MIDI input device the test plays by hand: it opens when a client holds it and passes each message on as the driver would.</summary>
    private sealed class FakeMidiDevice : IMidiInputSource
    {
        private bool _open;

        public event Action<int, int, int, long>? Message;
        public bool IsOpen => _open;

        public int Open()
        {
            _open = true;
            return 1;
        }

        public void Close() => _open = false;

        public void Play(int status, int pitch, int velocity, long stamp) => Message?.Invoke(status, pitch, velocity, stamp);

        public void Dispose() => Close();
    }
}
