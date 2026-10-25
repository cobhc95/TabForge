using System;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the live recording take of the arrangement overlay: the bounded peak envelope and growing span, the drawn red box (starting at the
/// take's first frame, growing monotonically, clipped to the take's lane), the lane recomputed over the growing span (a collision moves the
/// take and asks for the row refresh), and the stop rules (audio kept until its WAV lands, MIDI dropped at once). A recording frame
/// invalidates the overlay only, never the timeline body.
/// Does not own: the take feed from the engine/MIDI input (MainWindowTransportRecord), the timeline body and playhead (TimelineView), the
/// lane rules (ClipLanes) or the MIDI take capture (MidiTakeCaptureTests).
/// Tests: this class.
/// </summary>
public class RecordingLiveTakeTests
{
    private static (SongProject Song, TrackModel Track, TimelineView View, LiveTakes Takes) Setup()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        track.RecordArm = true;   // the live take opens the track's first real lane band (D2: it keeps that lane until the stop)
        song.Tracks.Add(track);
        var view = new TimelineView(song);
        var takes = new LiveTakes();
        view.UseLiveTakes(takes);
        // The real window reloads the rows when the live take opens its lane (MainWindowTransportRecord.LiveLaneCreated).
        takes.LaneCreated = _ => view.Reload(song);
        return (song, track, view, takes);
    }

    private static global::Avalonia.Rect DrawnBox(TimelineView view)
    {
        var recorder = new TrackRowSelectionTests.RectRecorder();
        view.LiveTakeLayer.Draw(recorder, light: false, view.BarsWidth + 100);
        var box = Assert.Single(recorder.Rects);
        return new global::Avalonia.Rect(box.X, box.Y, box.Width, box.Height);
    }

    [AvaloniaFact]
    public void ThreeFramesGrowTheDrawnBoxMonotonicallyFromTheTakeStart()
    {
        var (_, track, view, takes) = Setup();

        Assert.True(takes.Frame(track, 2.0, 0.2f, midi: false));
        var take = Assert.Single(takes.All);
        Assert.Equal(2.0, take.StartSec, 6);
        Assert.Equal(2.0, take.EndSec, 6);
        var first = DrawnBox(view);
        Assert.Equal(view.XOfSeconds(2.0), first.X, 6);
        Assert.DoesNotContain(first.X, new[] { 0.0, view.XOfSeconds(0.0) });   // the box starts at the take, not at the song start
        Assert.Equal(ClipLanes.AudioLaneHeight - 6, first.Height, 6);   // the live take now opens a real lane band

        Assert.True(takes.Frame(track, 3.0, 0.4f, midi: false));
        var second = DrawnBox(view);
        Assert.True(takes.Frame(track, 4.0, 0.8f, midi: false));
        var third = DrawnBox(view);

        Assert.Equal(4.0, take.EndSec, 6);
        Assert.Equal(view.XOfSeconds(4.0) - view.XOfSeconds(2.0), third.Width, 6);
        Assert.True(third.Width > second.Width && second.Width > first.Width, $"{first.Width} < {second.Width} < {third.Width}");
        Assert.Equal(3, take.Peaks.Count);   // one bucket per frame until the envelope fills
    }

    [AvaloniaFact]
    public void TheEnvelopeStaysBoundedWhileTheSpanKeepsGrowing()
    {
        var (_, track, _, takes) = Setup();
        for (var frame = 0; frame < LivePeaks.Capacity * 3 + 10; frame++) takes.Frame(track, frame / 60.0, 0.5f, midi: false);
        var take = Assert.Single(takes.All);
        Assert.True(take.Peaks.Count <= LivePeaks.Capacity);
        Assert.True(take.Peaks.FramesPerBucket > 1);
        Assert.Equal((LivePeaks.Capacity * 3 + 9) / 60.0, take.EndSec, 6);
    }

    [AvaloniaFact]
    public void ARecordingFrameInvalidatesTheOverlayOnlyNotTheTimelineBody()
    {
        var (_, track, view, takes) = Setup();
        var recorder = new TrackRowSelectionTests.RectRecorder();
        view.DrawBody(recorder, light: false);
        Assert.Equal(1, view.BodyRenderCount);
        takes.Frame(track, 1.0, 0.5f, midi: false);
        view.RefreshLiveTakes();
        Assert.Equal(1, view.BodyRenderCount);
    }

    [AvaloniaFact]
    public void ACollidingTakeMovesToTheNextFreeLaneAndAsksForTheRowsToRefresh()
    {
        var (_, track, _, takes) = Setup();
        track.AudioClips.Add(new AudioClip { File = "old.wav", Name = "old", StartSec = 0, SourceLengthSec = 20, FileLengthSec = 20 });
        var lanesCreated = 0;
        takes.LaneCreated = _ => lanesCreated++;

        takes.Frame(track, 1.0, 0.5f, midi: false);

        var take = Assert.Single(takes.All);
        Assert.Equal(1, take.Lane);                 // lane 0 is taken by the clip over the take's whole span
        Assert.Single(track.Lanes);                 // the song is untouched while recording: the overlay alone grows the lane
        Assert.Equal(1, lanesCreated);              // and the rows were asked to refresh once

        takes.Frame(track, 2.0, 0.5f, midi: false);
        Assert.Equal(1, lanesCreated);              // still the same lane, so no repeated layout work
    }

    [AvaloniaFact]
    public void StopKeepsTheAudioTakeUntilItsWavArrivesAndDropsTheMidiTakeAtOnce()
    {
        var (song, audioTrack, _, takes) = Setup();
        var midiTrack = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(midiTrack);
        takes.Frame(audioTrack, 1.0, 0.5f, midi: false);
        takes.Frame(midiTrack, 1.0, 0f, midi: true);
        takes.Note(midiTrack, 0x90, 60, 100, 1.1);
        takes.Note(midiTrack, 0x80, 60, 0, 1.6);
        var midiTake = takes.Of(midiTrack)!;
        var note = Assert.Single(midiTake.Notes);
        Assert.Equal(60, note.Pitch);
        Assert.Equal(0.5, note.LengthSec, 6);

        Assert.True(takes.Stop());

        var audioTake = Assert.Single(takes.All);
        Assert.Same(audioTrack, audioTake.Track);
        Assert.False(audioTake.Midi);
        Assert.Null(takes.Of(midiTrack));

        Assert.True(takes.Remove(audioTrack));
        Assert.Empty(takes.All);
    }

    [AvaloniaFact]
    public void AMidiTakeDrawsItsNotesAndHeldOnesInsideTheBox()
    {
        var (_, track, view, takes) = Setup();
        takes.Frame(track, 1.0, 0f, midi: true);
        takes.Note(track, 0x90, 64, 100, 1.2);
        takes.Frame(track, 2.0, 0f, midi: true);
        var recorder = new TrackRowSelectionTests.RectRecorder();
        view.LiveTakeLayer.Draw(recorder, light: false, view.BarsWidth + 100);
        // The box itself plus the note (the held note grows to the take's end, so it draws as a second small rect inside the box).
        Assert.True(recorder.Rects.Count >= 2);
        var box = recorder.Rects[0];
        Assert.True(recorder.Rects.Exists(r => r.Height < box.Height && r.X >= box.X && r.X < box.X + box.Width));
    }

    [AvaloniaFact]
    public void ATakeBeyondTheLaidOutLanesDrawsClampedToTheLastLane()
    {
        var (_, track, view, takes) = Setup();
        track.AudioClips.Add(new AudioClip { File = "a.wav", Name = "a", StartSec = 0, SourceLengthSec = 20, FileLengthSec = 20, Lane = 0 });
        track.AudioClips.Add(new AudioClip { File = "b.wav", Name = "b", StartSec = 0, SourceLengthSec = 20, FileLengthSec = 20, Lane = 1 });
        takes.Frame(track, 1.0, 0.5f, midi: false);
        var take = Assert.Single(takes.All);
        take.Lane = 7;   // a stale lane past the laid-out bands: T1 grows the row for the new live lane
        var box = DrawnBox(view);
        var row = view.Song.Tracks.IndexOf(track);
        var laidOut = view.LaidOutLaneCountOf(track);   // the grown row, not the stale take.LaneCount
        Assert.True(laidOut >= take.Lane + 1);   // the row grew to include the live lane
        Assert.Equal(view.LaneBandTopOf(row, laidOut - 1) + 3, box.Y, 6);
        Assert.Equal(ClipLanes.AudioLaneHeight - 6, box.Height, 6);
        var rowTop = view.RowTop(row);
        Assert.True(box.Y >= rowTop);   // inside its own (grown) row
        Assert.True(box.Y + box.Height <= rowTop + view.RowHeightOf(row) + 1e-6);
        if (row + 1 < view.RowCount)
            Assert.True(box.Y + box.Height <= view.RowTop(row + 1) + 1e-6);   // never draws into another track's row
    }

    [AvaloniaFact]
    public void AMidiTakeBegunByANoteDrawsNothingUntilItsFirstFrame()
    {
        var (_, track, view, takes) = Setup();
        Assert.True(takes.Note(track, 0x90, 60, 100, 1.1));
        var recorder = new TrackRowSelectionTests.RectRecorder();
        view.LiveTakeLayer.Draw(recorder, light: false, view.BarsWidth + 100);
        Assert.Empty(recorder.Rects);   // no lane band yet: the take has no frame and no lane count
    }
}
