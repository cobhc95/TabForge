using System;
using System.Collections.Generic;
using System.Diagnostics;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: recording from a stop with the count-in on: nothing is captured during the count-in, the take starts at the
///     record position (bar 5 here), and a start without the count-in keeps starting at once.
/// Does not own: recording while the song already plays (TransportRecordingTests) or the click output (TransportLoopCountInTests).
/// Tests: this class.
/// </summary>
public class CountInRecordingTests
{
    private const int NoteOn = 0x90, NoteOff = 0x80;
    private const int FastTempo = 480;   // one 4/4 bar is half a second
    private const int Bars = 6;
    private const int StartBar = 4;      // bar 5 (0-based)

    [Fact]
    public void CountInFromStopStartsTheAudioTakeOnlyWhenTheSchedulerReachesTheStart()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine) { Engine = client };
        var song = new SongDocument(() => null) { Project = new SongProject { Tempo = FastTempo } };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.SetCountIn(true);
        session.Seek(StartBar, 0);

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);

        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);   // the count-in is a pre-roll: no capture yet

        // The count-in plays before the music (timeline second 0.5 is the start of bar 5's audio).
        var timeline = new ScoreTimeline { CountInMs = 500, PlayFromMs = 500, TotalMs = 4000 };
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = 0, Cell = 0, ElapsedMs = 100 });
        session.PumpFrame();
        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);

        engine.Report(new PlaybackPosition { Bar = StartBar, Cell = 0, ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();

        Assert.True(recording.IsRecording);
        Assert.Equal(StartBar, start.Bar);
        var at = AudioClipTimelineRules.SecondsAtBar(song.Project, start.Bar, 0);
        Assert.Equal((false, at), client.PositionSentForTest(client.OwnerIdOf(song)));
        var position = commands.IndexOf(EngineCommand.SetPosition);
        var record = commands.IndexOf(EngineCommand.Record);
        Assert.True(position >= 0 && record > position, $"position at {position}, record at {record}");
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
    }

    [Fact]
    public void WithoutCountInRecordFromStopStillStartsTheTakeAtOnce()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine) { Engine = client };
        var song = new SongDocument(() => null) { Project = new SongProject { Tempo = FastTempo } };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.SetCountIn(false);
        session.Seek(StartBar, 0);

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);

        Assert.True(recording.IsRecording);
        Assert.Contains(EngineCommand.Record, commands);
        var at = AudioClipTimelineRules.SecondsAtBar(song.Project, start.Bar, 0);
        Assert.Equal((false, at), client.PositionSentForTest(client.OwnerIdOf(song)));
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
    }

    [Fact]
    public void CountInFromStopDropsMidiPlayedDuringItAndStartsTheTakeAtTheStartBar()
    {
        var project = Song();
        var track = project.Tracks[0];
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.SetCountIn(true);
        session.Seek(StartBar, 0);
        var song = new SongDocument(() => null) { Project = project };
        var device = new FakeMidiDevice();
        using var recording = new TransportRecording(new AudioEngineClient(new TabForge.Plugins.PluginTrust()), new MidiInputHub(device).CreateClient());
        var start = new RecordingPresenter.RecordStart();

        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
        Assert.False(recording.IsRecording);

        // Heard while the count-in clicks: it belongs to the pre-roll and must not reach the take.
        device.Play(NoteOn, 40, 100, Stopwatch.GetTimestamp());
        device.Play(NoteOff, 40, 0, Stopwatch.GetTimestamp());

        var timeline = new ScoreTimeline { CountInMs = 500, PlayFromMs = 500, TotalMs = 4000 };
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = StartBar, Cell = 0, ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();
        Assert.True(recording.IsRecording, "the count-in never ended");

        device.Play(NoteOn, 64, 100, Stopwatch.GetTimestamp());
        device.Play(NoteOff, 64, 0, Stopwatch.GetTimestamp());
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
        MainWindowTransportRecord.LandMidiTakes(recording, song);

        var at = AudioClipTimelineRules.SecondsAtBar(project, start.Bar, 0);
        var clip = Assert.Single(track.AudioClips);
        var note = Assert.Single(clip.Notes!);
        Assert.Equal(at, clip.StartSec, 3);
        Assert.Equal(64, note.Pitch);   // the count-in note (40) was dropped
        Assert.InRange(clip.StartSec + note.StartSec, at, at + 0.3);
    }

    [Fact]
    public void ALoopRecordingStartingAtTheCountInEndKeepsItsFirstPassAtThatPosition()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine) { Engine = client };
        var song = new SongDocument(() => null) { Project = new SongProject { Tempo = FastTempo } };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.SetCountIn(true);
        session.Seek(StartBar, 0);
        var at = AudioClipTimelineRules.SecondsAtBar(song.Project, StartBar, 0);
        var loop = (at, at + 1.0);

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, loop: loop, playback: sessions);

        Assert.False(recording.IsRecording);   // fails on the old immediate start: the pre-roll captures nothing
        Assert.DoesNotContain(EngineCommand.Record, commands);

        var timeline = new ScoreTimeline { CountInMs = 500, PlayFromMs = 500, TotalMs = 4000 };
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = 0, Cell = 0, ElapsedMs = 100 });
        session.PumpFrame();
        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);

        engine.Report(new PlaybackPosition { Bar = StartBar, Cell = 0, ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();

        Assert.True(recording.IsRecording);
        Assert.Equal(StartBar, start.Bar);
        Assert.Equal(loop, recording.RecordLoop!.Value);
        Assert.Equal((false, at), client.PositionSentForTest(client.OwnerIdOf(song)));
        var takes = RecordedTakes.FromRecording("take.wav", at, 3.0, recording.RecordLoop);
        Assert.Equal(at, takes[0].StartSec, 6);   // the first pass sits at P, not at the count-in start
        Assert.Equal(0.0, takes[0].OffsetSec, 6);
        Assert.Equal(at, takes[1].StartSec, 6);
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start);
    }

    [Fact]
    public void StopDuringCountInCancelsThePendingRecord()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine) { Engine = client };
        var song = new SongDocument(() => null) { Project = new SongProject { Tempo = FastTempo } };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.SetCountIn(true);
        session.Seek(StartBar, 0);

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);
        Assert.False(recording.IsRecording);

        session.Stop();   // what Transport.Stop runs after its EndRecording no-op while a record is pending
        Assert.False(session.IsPlaying);
        engine.Report(new PlaybackPosition { Bar = StartBar, Cell = 0, ElapsedMs = 500 });
        session.PumpFrame();

        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);   // nothing recorded, no take
    }

    [Fact]
    public void SeekDuringCountInCancelsThePendingRecord()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine) { Engine = client };
        var song = new SongDocument(() => null) { Project = new SongProject { Tempo = FastTempo } };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.SetCountIn(true);
        session.Seek(StartBar, 0);

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);
        Assert.False(recording.IsRecording);

        session.Seek(0, 0);   // a moved start invalidates the waiting take second
        engine.Report(new PlaybackPosition { Bar = StartBar, Cell = 0, ElapsedMs = 500 });
        session.PumpFrame();

        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);
    }

    [Fact]
    public void RecordPressedAgainDuringCountInCancelsThePendingRecord()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine) { Engine = client };
        var song = new SongDocument(() => null) { Project = new SongProject { Tempo = FastTempo } };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        sessions.SyncEngine(song, new PluginSettings());
        var session = sessions.For(song)!;
        session.SetCountIn(true);
        session.Seek(StartBar, 0);

        var commands = new List<EngineCommand>();
        client.SentForTest = command => commands.Add(command);
        using var recording = new TransportRecording(client, client.MidiInput.CreateClient());
        var start = new RecordingPresenter.RecordStart();
        MainWindowTransportRecord.Toggle(recording, session, song, _ => { }, start, playback: sessions);
        Assert.False(recording.IsRecording);

        var messages = new List<string>();
        MainWindowTransportRecord.Toggle(recording, session, song, messages.Add, start);
        Assert.Equal(TransportRecording.StoppedText, Assert.Single(messages));
        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);

        engine.Report(new PlaybackPosition { Bar = StartBar, Cell = 0, ElapsedMs = 500 });
        session.PumpFrame();
        Assert.False(recording.IsRecording);
        Assert.DoesNotContain(EngineCommand.Record, commands);
    }

    // Six quarter-note bars, each one note on its first beat, at a fast tempo: one bar is 0.5 s.
    private static SongProject Song()
    {
        var project = new SongProject { Tempo = FastTempo };
        var track = new TrackModel
        {
            Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
        };
        for (var bar = 0; bar < Bars; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar, MidiValue = 60 + bar });
            measure.Cells[0].DurationDenominator = 4;
            track.Measures.Add(measure);
        }
        project.Tracks.Add(track);
        return project;
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
