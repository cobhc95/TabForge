using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the real recording path at command level (MainWindowTransportRecord.Toggle/LandTake, the engine's Recorded event and the
//   live-take overlay): loop laps land as separate takes on separate lanes (even one lap), a linear take stays one clip, a mid-take disarm
//   cuts the capture and keeps its lane, arm/disarm are their own undo steps, and the take length follows the session speed.
// Does not own: the pure fold geometry (LiveTakeFoldTests), the model round-trip (LoopLanesStage1Tests) or the engine itself.
// Tests: this class covers spec examples (a)-(g) and the live-overlay row clip.
public sealed class RecordingRealPathTests
{
    private static SongDocument NewSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        return song;
    }

    private static TrackModel AddAudio(SongDocument song)
    {
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return track;
    }

    private static TimelineView View(SongDocument song) => new(song.Project) { Snap = new SnapSettings { Enabled = false } };

    private static global::Avalonia.Point CentreOf(TimelineView view, TrackModel track, AudioClip clip)
    {
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        return new global::Avalonia.Point((box.X + box.Right) / 2, (box.Y + box.Bottom) / 2);
    }

    [AvaloniaFact]
    public void A_AThreeLapLoopRecordingLandsThreeSeparateTakesOnThreeLanes()
    {
        // (a) Loop (2,4), recorded from 1 for three laps: take 1 = 1..4 (the pre-roll stays part of it), laps 2-3 = 2..4, each on a new lane.
        var song = NewSong();
        var track = AddAudio(song);
        var status = MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 7, loop: (2, 4));

        Assert.Equal(3, track.AudioClips.Count);
        Assert.All(track.AudioClips, clip => Assert.Null(clip.TakeGroupId));
        var ordered = track.AudioClips.OrderBy(clip => clip.Lane).ToList();
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(clip => clip.Lane));
        Assert.Equal(1, ordered[0].StartSec, 6);
        Assert.Equal(4, ordered[0].EndSec, 6);
        Assert.Equal(2, ordered[1].StartSec, 6);
        Assert.Equal(2, ordered[2].StartSec, 6);
        Assert.Equal(4, ordered[1].EndSec, 6);
        Assert.Equal(4, ordered[2].EndSec, 6);
        Assert.Equal(new[] { true, true, false }, ordered.Select(clip => clip.Muted));
        Assert.Same(ordered[2], Assert.Single(ordered, clip => ClipLanes.Audible(track, clip)));
        Assert.Equal($"Recorded 3 takes on {track.Name} (the newest plays)", status);

        var view = View(song);
        var row = song.Project.Tracks.IndexOf(track);
        Assert.Equal(3 * ClipLanes.AudioLaneHeight, view.RowHeightOf(row), 6);
        foreach (var take in ordered)
            Assert.Same(take, TimelineAudioClips.ClipAt(view, CentreOf(view, track, take))!.Clip);

        // Example (a): a take is still a normal clip for a time move.
        var before = ordered[2].StartSec;
        var point = CentreOf(view, track, ordered[2]);
        var gestures = new TimelineClipGestureController(view);
        gestures.Press(true, point);
        Assert.True(gestures.Begin());
        gestures.Update(new global::Avalonia.Point(point.X + view.XOfSeconds(1) - view.XOfSeconds(0), point.Y), altHeld: false);   // drag 1 s right of the press
        Assert.True(gestures.Commit(song));
        Assert.Equal(before + 1, ordered[2].StartSec, 6);
        Assert.Equal(1, ordered[0].StartSec, 6);
        Assert.Equal(2, ordered[1].StartSec, 6);
    }

    [AvaloniaFact]
    public void B_AOneLapLoopRecordingLandsOneOrdinaryTakeWithNoGroup()
    {
        // (b) The same loop but stopped inside the first lap: one ordinary take 1..4, no group, no extra lane.
        var song = NewSong();
        var track = AddAudio(song);
        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 3, loop: (2, 4));

        var take = Assert.Single(track.AudioClips);
        Assert.Null(take.TakeGroupId);
        Assert.Equal(1, take.StartSec, 6);
        Assert.Equal(4, take.EndSec, 6);
        Assert.False(take.Muted);
        Assert.Equal(0, take.Lane);

        var view = View(song);
        var row = song.Project.Tracks.IndexOf(track);
        Assert.Equal(ClipLanes.AudioLaneHeight, view.RowHeightOf(row), 6);
        Assert.Same(take, TimelineAudioClips.ClipAt(view, CentreOf(view, track, take))!.Clip);
    }

    [AvaloniaFact]
    public void C_LoopOffLandsOneLinearClipAndItsAreaIsNotPaintedAsTheActiveLoop()
    {
        // (c) No loop: one ordinary clip, selectable; a waiting area uses the inactive ink, never the active loop accent.
        var song = NewSong();
        var track = AddAudio(song);
        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 3);
        var clip = Assert.Single(track.AudioClips);
        Assert.Null(clip.TakeGroupId);

        var view = View(song);
        Assert.Same(clip, TimelineAudioClips.ClipAt(view, CentreOf(view, track, clip))!.Clip);
        Assert.Equal(("LoopEdge", "LoopRegion"), (TimelineLoopMarks.EdgeKey, TimelineLoopMarks.RegionKey));   // the loop has its own tokens, never the selection accent
    }

    [AvaloniaFact]
    public void D_TheLiveOverlayGrowsALanePerLapFoldedOntoTheLoopRange()
    {
        // (d) Before the first wrap: one box from the record start to the playhead (not the loop end).
        var song = NewSong();
        var track = AddAudio(song);
        track.RecordArm = true;
        var view = new TimelineView(song.Project);
        var takes = new LiveTakes { Loop = (2, 4) };
        view.UseLiveTakes(takes);
        takes.LaneCreated = _ => view.Reload(song.Project);
        var row = song.Project.Tracks.IndexOf(track);

        Assert.True(takes.Frame(track, 1, 0.2f, midi: false));
        Assert.True(takes.Frame(track, 3, 0.2f, midi: false));
        var before = new TrackRowSelectionTests.RectRecorder();
        view.LiveTakeLayer.Draw(before, light: false, view.BarsWidth + 100);
        var single = Assert.Single(before.Rects);
        Assert.Equal(view.XOfSeconds(1), single.X, 6);
        Assert.Equal(view.XOfSeconds(3) - view.XOfSeconds(1), single.Width, 6);   // the playhead, not the loop end
        Assert.Equal(view.RowHeight + ArmedRowPainter.SubRowHeight, view.RowHeightOf(row), 6);   // armed: one lane band is shorter than main line + strip, so the row grows by the shortfall

        // After the first wrap: a second live lane appears, the row grows by one lane, and both laps sit on the loop range.
        Assert.True(takes.Frame(track, 5, 0.2f, midi: false));
        Assert.Equal(2 * ClipLanes.AudioLaneHeight, view.RowHeightOf(row), 6);
        var after = new TrackRowSelectionTests.RectRecorder();
        view.LiveTakeLayer.Draw(after, light: false, view.BarsWidth + 100);
        Assert.Equal(2, after.Rects.Count);
        Assert.All(after.Rects, rect => Assert.True(rect.X >= view.XOfSeconds(1) - 0.01 && rect.X + rect.Width <= view.XOfSeconds(4) + 0.01,
            $"live box {rect.X}..{rect.X + rect.Width} left the 1..4 span"));
        var rowTop = view.RowTop(row);
        var rowHeight = view.RowHeightOf(row);
        Assert.All(after.Rects, rect => Assert.True(rect.Y >= rowTop - 0.01 && rect.Y + rect.Height <= rowTop + rowHeight + 0.01,
            $"live box {rect.Y}..{rect.Y + rect.Height} left the row {rowTop}..{rowTop + rowHeight}"));
        Assert.NotEqual(after.Rects[0].Y, after.Rects[1].Y);   // one lane per lap
        Assert.Equal(view.XOfSeconds(2), after.Rects[1].X, 3);   // the partial lap grows from the loop start, not the loop end
        Assert.Equal(view.XOfSeconds(3) - view.XOfSeconds(2), after.Rects[1].Width, 3);
    }

    [AvaloniaFact]
    public void E_DisarmDuringMidiRecordingEndsTheTakeThereAndKeepsItsLaneUntilTheStop()
    {
        // (e) MIDI: notes after the disarm are dropped; the take still lands (one take, ending at the disarm).
        var device = new FakeMidi();
        using var capture = new MidiTakeCapture(device);
        var song = NewSong();
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Keys);
        track.AudioInput = AudioInputs.Midi;
        track.RecordArm = true;
        song.Project.Tracks.Add(track);

        var start = Stopwatch.GetTimestamp();
        Assert.Equal(1, capture.Start(new[] { track }, 0, start, 1, 0));
        device.Play(0x90, 60, 100, start + Ticks(1));
        device.Play(0x80, 60, 0, start + Ticks(1.5));
        Assert.True(capture.Disarm(track, start + Ticks(2)));
        device.Play(0x90, 64, 100, start + Ticks(3));   // dropped
        var finished = capture.Stop(start + Ticks(4));

        var (owner, clips) = Assert.Single(finished);
        Assert.Same(track, owner);
        var clip = Assert.Single(clips);
        Assert.Equal(2, clip.EndSec, 3);
        Assert.Equal(60, Assert.Single(clip.Notes!).Pitch);

        // The live box and its lane survive the disarm (frozen), until the global stop lands the take.
        var view = new TimelineView(song.Project);
        var takes = new LiveTakes();
        view.UseLiveTakes(takes);
        takes.Frame(track, 0, 0f, midi: true);
        takes.Freeze(track);
        track.RecordArm = false;
        view.Reload(song.Project);
        var row = song.Project.Tracks.IndexOf(track);
        Assert.Equal(1, view.LaidOutLaneCountOf(track));
        Assert.True(view.RowHeightOf(row) > 0);
    }

    [AvaloniaFact]
    public void F_ArmAndDisarmAreTheirOwnUndoStepsAroundTheTake()
    {
        // (f) arm (step), record (step); undo -> take gone, still armed; undo -> disarmed; redo twice -> armed + take.
        var song = NewSong();
        var track = AddAudio(song);
        song.TrackIndex = song.Project.Tracks.IndexOf(track);

        MainWindowTrackArmKeys.ToggleArm(song);
        Assert.True(track.RecordArm);
        Assert.Equal(1, song.Undo.UndoCount);
        Assert.Equal("Arm track", song.Undo.Latest!.Value.Label);

        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 2);
        Assert.Single(track.AudioClips);
        Assert.Equal(2, song.Undo.UndoCount);

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Empty(song.Project.Tracks[^1].AudioClips);
        Assert.True(song.Project.Tracks[^1].RecordArm);

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.False(song.Project.Tracks[^1].RecordArm);

        Assert.NotNull(DocumentEdits.Redo(song));
        Assert.True(song.Project.Tracks[^1].RecordArm);
        Assert.NotNull(DocumentEdits.Redo(song));
        Assert.Single(song.Project.Tracks[^1].AudioClips);
    }

    [AvaloniaFact]
    public void G_TheLandedLengthFollowsTheSessionSpeed()
    {
        // (g) The engine's file length is wall time: at speed 2 a 1 s file covers 2 s of song.
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var song = NewSong();
        var track = AddAudio(song);
        track.RecordArm = true;
        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(playback, new AvaloniaUiThread(Dispatcher.UIThread), () => song.Project);
        session.SetSpeed(2);
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var hooks = new RecordingPresenter.EngineHooks(client, tabs, _ => { });
        try
        {
            hooks.BeginLive(session, song.Project.Tracks);
            hooks.BeginLanding(song.Undo.Snapshot(song.Project, "Record takes"));
            client.RaiseRecordedForTest(track, "take.wav", 10, 1);
            Dispatcher.UIThread.RunJobs();

            var clip = Assert.Single(song.Project.Tracks[^1].AudioClips);
            Assert.Equal(10, clip.StartSec, 6);
            Assert.Equal(12, clip.EndSec, 6);
        }
        finally
        {
            hooks.Dispose();
            tabs.Dispose();
        }
    }

    private static long Ticks(double seconds) => (long)(Stopwatch.Frequency * seconds);

    private sealed class FakeMidi : IMidiInputClient
    {
        public string? Device { get; set; }
        public bool IsOpen { get; private set; }
        public event Action<int, int, int, long>? Message;
        public int Open() { IsOpen = true; return 1; }
        public void Close() => IsOpen = false;
        public void Dispose() => Close();
        public void Play(int status, int data1, int data2, long stamp) => Message?.Invoke(status, data1, data2, stamp);
    }
}
