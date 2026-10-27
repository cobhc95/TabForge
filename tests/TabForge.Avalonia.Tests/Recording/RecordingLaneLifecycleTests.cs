using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: lane allocation and lifecycle of recorded and edited clips: the first lane free over the whole span (a new lane below when all are
/// busy), one undo step for a multi-take placement, disarm trimming trailing empty lanes, compact removing empty lanes only on unarmed tracks,
/// a landed take closing the lane its growing box opened but did not need, looped recording landing one separate take per lap with the newest
/// playing (audio and MIDI), lane bands drawn for empty lanes, and a clip edit syncing the engine down the normal path.
/// Does not own: the lane rules themselves (ClipLanes), the recording capture (TransportRecordingTests/MidiTakeCaptureTests), the live take
/// overlay (RecordingLiveTakeTests) or the engine routing (AvaloniaEngineRoutingTests).
/// Tests: this class.
/// </summary>
public class RecordingLaneLifecycleTests
{
    private static (SongDocument Song, TrackModel Track) SongWithTrack()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    private static AudioClip Clip(double start, double length, int lane = 0) =>
        new() { File = "take.wav", Name = "take", StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane };

    [AvaloniaFact]
    public void FreeLanePicksTheFirstLaneFreeOverTheWholeSpan()
    {
        var (_, track) = SongWithTrack();
        track.AudioClips.Add(Clip(0, 10, lane: 0));

        Assert.Equal(0, ClipLanes.FreeLane(track, 11, 12));   // past the clip: lane 0 is free over the whole span
        Assert.Equal(1, ClipLanes.FreeLane(track, 2, 4));     // inside it: the first lane free over the whole span is 1
        Assert.Equal(1, ClipLanes.FreeLane(track, 9, 15));    // crossing its edge: still 1
    }

    [AvaloniaFact]
    public void ACollidingTakeGoesToANewLaneBelowAndTheNewestPlays()
    {
        var (_, track) = SongWithTrack();
        track.AudioClips.Add(Clip(0, 10, lane: 0));

        RecordedTakes.Place(track, new List<AudioClip> { Clip(2, 3) }, midi: false);

        Assert.Equal(2, track.AudioClips.Count);
        var take = track.AudioClips[^1];
        Assert.Equal(1, take.Lane);
        Assert.True(track.Lanes.Count >= 2);
        Assert.True(ClipLanes.Plays(track, 1));
        Assert.False(ClipLanes.Plays(track, 0));
    }

    [AvaloniaFact]
    public void ALandedTakeClosesTheLaneItsGrowingBoxOpenedButDidNotNeed()
    {
        var (_, track) = SongWithTrack();
        track.RecordArm = true;                      // a track left armed after recording is never compacted
        track.AudioClips.Add(Clip(12, 4, lane: 0));  // a clip that starts where the recording ends

        // The growing box is drawn at the raw song second while the landed take is stamped at that second less the engine's
        // alignment, so while it grew past the clip's start it needed lane 1; the song's lanes stay untouched (the overlay grows the lane).
        var live = new LiveTakes();
        live.Frame(track, 2, 0.5f, midi: false);
        live.Frame(track, 12.03, 0.5f, midi: false);
        Assert.Equal(1, live[0].Lane);
        Assert.Single(track.Lanes);

        // The take itself ends at 12, before the clip: it lands on lane 0 and the band it never needed closes again.
        RecordedTakes.Place(track, new List<AudioClip> { Clip(2, 10) }, midi: false);

        Assert.Equal(0, track.AudioClips[^1].Lane);
        Assert.Single(track.Lanes);
        Assert.Equal(1, ClipLanes.LaidOutCount(track));
    }

    [AvaloniaFact]
    public void DisarmingATrackTrimsItsTrailingEmptyLanes()
    {
        var (song, track) = SongWithTrack();
        song.TrackIndex = song.Project.Tracks.IndexOf(track);
        track.RecordArm = true;
        track.AudioClips.Add(Clip(0, 2, lane: 0));
        ClipLanes.Ensure(track, 3);   // two trailing empty lanes

        MainWindowTrackArmKeys.ToggleArm(song);

        Assert.False(track.RecordArm);
        Assert.Single(track.Lanes);
    }

    [AvaloniaFact]
    public void CompactRemovesEmptyLanesButNeverOnAnArmedTrack()
    {
        var (_, track) = SongWithTrack();
        track.AudioClips.Add(Clip(0, 2, lane: 0));
        track.AudioClips.Add(Clip(3, 2, lane: 2));
        ClipLanes.Ensure(track, 3);

        Assert.True(ClipLanes.Compact(track));
        Assert.Equal(2, track.Lanes.Count);
        Assert.Equal(1, track.AudioClips.Single(c => c.StartSec == 3).Lane);   // lane 2 closes over the empty lane 1

        track.RecordArm = true;
        Assert.False(ClipLanes.Compact(track));                                  // an armed track is never compacted
        Assert.Equal(2, track.Lanes.Count);
    }

    [AvaloniaFact]
    public void ALoopedAudioRecordingLandingIsSeparateTakesOnSeparateLanesAndOneUndoStep()
    {
        var (song, track) = SongWithTrack();

        // 8 s recorded from 1 s while looping [2, 4): take 1 = 1..4 (the pre-roll stays part of it), then laps 2..4, 2..4, 2..3.
        var status = MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 8, loop: (2, 4));

        Assert.Equal(4, track.AudioClips.Count);
        // Loop laps are separate takes on separate lanes (they overlap on purpose); the older laps are muted, the newest plays.
        Assert.All(track.AudioClips, clip => Assert.Null(clip.TakeGroupId));
        Assert.Equal(new[] { 0, 1, 2, 3 }, track.AudioClips.OrderBy(clip => clip.Lane).Select(clip => clip.Lane).ToArray());
        Assert.Equal(new[] { true, true, true, false }, track.AudioClips.OrderBy(clip => clip.Lane).Select(clip => clip.Muted).ToArray());
        Assert.Same(track.AudioClips.OrderBy(clip => clip.Lane).Last(), Assert.Single(track.AudioClips, clip => ClipLanes.Audible(track, clip)));
        Assert.Equal(RecordedTakes.StatusText(track, 4, 8), status);   // four laps; the pre-roll is part of take 1

        Assert.NotNull(DocumentEdits.Undo(song));               // all takes are one undo step
        Assert.DoesNotContain(song.Project.Tracks, t => t.AudioClips.Count > 0);
    }

    [Fact]
    public void ALoopedMidiRecordingProducesSeparateTakesOnSeparateLanesAndTheNewestPlays()
    {
        var song = new SongDocument(() => null);
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true };
        song.Project.Tracks.Add(track);
        var device = new FakeMidiDevice();
        var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var recording = new TransportRecording(client, new MidiInputHub(device).CreateClient());
        var t0 = Stopwatch.GetTimestamp();

        recording.Toggle(song.Project.Tracks, "unused", startSec: 0, speed: 1, loop: (1, 2));
        Assert.True(recording.IsRecording);

        // The first pass: a note in the first loop. Then a stamp past the loop end wraps to lap 1 (song second 1.x) where a second note is played.
        device.Play(0x90, 60, 100, t0 + Ticks(0.2));
        device.Play(0x80, 60, 0, t0 + Ticks(0.4));
        device.Play(0x90, 64, 100, t0 + Ticks(2.2));
        device.Play(0x80, 64, 0, t0 + Ticks(2.4));

        recording.Toggle(song.Project.Tracks, "unused");
        MainWindowTransportRecord.LandMidiTakes(recording, song);

        Assert.Equal(2, track.AudioClips.Count);
        Assert.All(track.AudioClips, clip => Assert.Null(clip.TakeGroupId));
        Assert.Equal(new[] { 0, 1 }, track.AudioClips.OrderBy(clip => clip.Lane).Select(clip => clip.Lane).ToArray());
        Assert.Equal(new[] { true, false }, track.AudioClips.OrderBy(clip => clip.Lane).Select(clip => clip.Muted).ToArray());

        recording.Dispose();
        client.Dispose();
    }

    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    [AvaloniaFact]
    public void LaneBandsAreDrawnForEmptyLanesToo()
    {
        var (song, track) = SongWithTrack();
        track.AudioClips.Add(Clip(0, 2, lane: 0));
        track.AudioClips.Add(Clip(3, 2, lane: 2));   // lane 1 is empty
        var view = new TimelineView(song.Project);
        var recorder = new TrackRowSelectionTests.RectRecorder();

        TimelineAudioClips.DrawRow(recorder, key => AvaloniaPalette.Get(key, false), view, track, song.Project.Tracks.IndexOf(track),
            view.LaneBandTopOf(song.Project.Tracks.IndexOf(track)), AvaloniaPalette.Get("InstrumentAccent", false));

        var bands = recorder.Rects.Where(r => Math.Abs(r.Height - ClipLanes.AudioLaneHeight) < 0.01).ToList();
        // Each lane band draws a board fill and its track tint at the same box: three laid-out lanes, the empty middle one included.
        Assert.Equal(3, bands.Select(band => Math.Round(band.Y, 3)).Distinct().Count());
        Assert.All(bands, band => Assert.True(band.Width > 0));
    }

    [AvaloniaFact]
    public void AClipEditSyncsTheEngineDownTheNormalPath()
    {
        var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), null) { Engine = client };
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-lane-" + Guid.NewGuid().ToString("N") + ".wav");
        System.IO.File.WriteAllBytes(file, new byte[44]);
        var clip = new AudioClip { File = file, Name = "take", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2 };
        track.AudioClips.Add(clip);
        var savedFileSystem = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = new FixedFileSystem();
        MediaAccess.Evaluate(file, null);   // classify the path first: the no-wait sync path skips a path it is still resolving
        try
        {
            var view = new ArrangementView(song.Project, song);
            // The same post-edit refresh the window attaches to every arrangement (MainWindowClipRefresh.Register), on a bare arrangement.
            view.Timeline.ClipEdited = document => MainWindowClipRefresh.AfterEdit(view, document, null, sessions);
            var sent = new List<ClipSpec>();
            var commands = new List<EngineCommand>();
            client.ClipsSentForTest = (_, clips) => sent.AddRange(clips);
            client.SentForTest = command => commands.Add(command);

            var target = new MainWindowClipKeys.Target(song, view.Timeline, new TimelineClipSelection(track, clip));
            MainWindowClipKeys.Edit(target, () => { clip.StartSec = 1; return true; });

            Assert.Contains(EngineCommand.SetClips, commands);
            Assert.Contains(sent, spec => Math.Abs(spec.StartSec - 1) < 0.001);
        }
        finally
        {
            MediaPathPolicy.FileSystem = savedFileSystem;
            client.Dispose();
            System.IO.File.Delete(file);
        }
    }

    /// <summary>A file system that reports every path as an ordinary fixed-drive file (the tests run without the app's Windows path resolver).</summary>
    private sealed class FixedFileSystem : IMediaFileSystem
    {
        public System.IO.DriveType DriveTypeOf(string root) => System.IO.DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => fullPath;
    }

    /// <summary>A MIDI input device the test plays by hand; it opens when a client holds it and passes each message on as the driver would.</summary>
    private sealed class FakeMidiDevice : IMidiInputSource
    {
        private bool _open;

        public event Action<int, int, int, long>? Message;
        public bool IsOpen => _open;

        public int Open() { _open = true; return 1; }
        public void Close() => _open = false;
        public void Play(int status, int pitch, int velocity, long stamp) => Message?.Invoke(status, pitch, velocity, stamp);
        public void Dispose() => Close();
    }
}
