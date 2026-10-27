using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the file-drop landing preview plan (the track or a new track, the lane, the measured or estimated span, the block label).
// Does not own: the drag events and the drawing (MainWindowAudioDrop, TimelineAudioClips).
// Tests: this file.
public class AudioDropPreviewTests
{
    [Fact]
    public void MeasuredFilesSpanTheirLengthsOnTheTrackLane()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        track.AudioClips.Add(new AudioClip { Name = "Old", StartSec = 0, SourceLengthSec = 1, Lane = 0 });

        var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.On(track), 10,
            new List<(string File, double LengthSec)> { (@"C:\a\one.wav", 2.5), (@"C:\a\two.wav", 1.5) });

        Assert.NotNull(preview);
        Assert.Same(track, preview.Track);
        Assert.Equal(1, preview.Lane);
        Assert.Equal(10, preview.StartSec);
        Assert.Equal(14, preview.EndSec);
        Assert.False(preview.Estimated);
        Assert.False(preview.NewTrack);
        Assert.Equal("2 files", preview.Label);
        Assert.Equal("4.0 s", AudioDropPreviewPlanner.Detail(preview));
    }

    [Fact]
    public void UnmeasuredFilesEstimateOneBarAndStayEstimated()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var barLen = Presenters.AudioClipTimelineRules.SecondsAtBar(song, 1, 0) - Presenters.AudioClipTimelineRules.SecondsAtBar(song, 0, 0);

        var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.On(track), 0,
            new List<(string File, double LengthSec)> { (@"C:\a\one.wav", 0) });

        Assert.NotNull(preview);
        Assert.True(preview.Estimated);
        Assert.Equal("one", preview.Label);
        Assert.Equal(barLen, preview.EndSec - preview.StartSec, 6);
        Assert.Equal("length on drop", AudioDropPreviewPlanner.Detail(preview));
    }

    [Fact]
    public void StripDropPreviewsANewTrackAtLaneZero()
    {
        var song = DemoSongFactory.Create();

        var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.NewTrack, 3,
            new List<(string File, double LengthSec)> { (@"C:\a\one.wav", 2) });

        Assert.NotNull(preview);
        Assert.Null(preview.Track);
        Assert.True(preview.NewTrack);
        Assert.Equal(0, preview.Lane);
        Assert.Equal(3, preview.StartSec);
        Assert.Equal(5, preview.EndSec);
    }

    [Fact]
    public void NoTargetPreviewsNothing()
    {
        var song = DemoSongFactory.Create();

        Assert.Null(AudioDropPreviewPlanner.Plan(song, AudioDropTarget.None, 0,
            new List<(string File, double LengthSec)> { (@"C:\a\one.wav", 2) }));
    }

    [Fact]
    public void LengthTextReadsLikeTheWpfGhost()
    {
        Assert.Equal("4.0 s", AudioDropPreviewPlanner.LengthText(4));
        Assert.Equal("1:05.0", AudioDropPreviewPlanner.LengthText(65));
    }

    [AvaloniaFact]
    public void PreviewDrawsATrackColouredBlockAtThePlannedSpan()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var view = new TimelineView(song);
        var preview = AudioDropPreviewPlanner.Plan(song, AudioDropTarget.On(track), 0,
            new List<(string File, double LengthSec)> { (@"C:\a\one.wav", 2) });
        Assert.NotNull(preview);
        view.FileDropPreview = preview;
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        var fallback = new UiColor(255, 1, 2, 3);
        var expectedFill = view.TrackColourOf(track, fallback).WithOpacity(0.38);

        TimelineAudioClips.DrawFileDropPreview(draw, _ => fallback, view, 800);

        var block = Assert.Single(draw.Rects, r => r.Fill == expectedFill);
        Assert.Equal(view.XOfSeconds(preview.StartSec), block.X, 6);
        Assert.Equal(view.XOfSeconds(preview.EndSec) - view.XOfSeconds(preview.StartSec), block.Width, 2);
        Assert.Contains(draw.Texts, t => t.Text == "one");
    }
}
