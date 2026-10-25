using Avalonia.Headless.XUnit;
using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the placement of audio clips on the arrangement timeline: a song time in seconds lands in the bar that holds it, at the same fraction of
/// that bar's width as the playhead uses.
/// Tests: AudioClipTimelineRules, TimelineView.XOfSeconds, TimelineView.SecondsOfX.
/// </summary>
public class TimelineAudioClipTests
{
    [Fact]
    public void AMutedDarkClipInLightModeKeepsReadableInk()
    {
        var lane = new UiColor(255, 255, 255, 255);
        var dark = new UiColor(255, 20, 20, 60);
        var ink = TimelineAudioClips.ClipInk(lane, dark, 0.35);
        Assert.True(ThemeColours.Contrast(ink, ThemeColours.Blend(lane, dark, 0.35)) >= 4.5);
    }

    [AvaloniaFact]
    public void SecondsLandInTheBarTheyFallIn()
    {
        var song = DemoSongFactory.Create();
        var view = new TimelineView(song);
        var halfWayThroughSecondBar = MusicTime.BarMs(song, 0) / 1000 + MusicTime.BarMs(song, 1) / 2000;

        var (bar, fraction) = AudioClipTimelineRules.BarAtSeconds(song, halfWayThroughSecondBar);
        Assert.Equal(1, bar);
        Assert.Equal(0.5, fraction, 6);
        Assert.Equal(view.BarX(1) + (view.BarX(2) - view.BarX(1)) / 2, view.XOfSeconds(halfWayThroughSecondBar), 6);
    }

    [AvaloniaFact]
    public void PointerXGivesBackTheSecondsItWasDrawnAt()
    {
        var song = DemoSongFactory.Create();
        var view = new TimelineView(song);
        var halfWayThroughSecondBar = MusicTime.BarMs(song, 0) / 1000 + MusicTime.BarMs(song, 1) / 2000;

        Assert.Equal(halfWayThroughSecondBar, view.SecondsOfX(view.XOfSeconds(halfWayThroughSecondBar)), 6);
    }

    [AvaloniaFact]
    public void ABarAfterARepeatSitsWhereThePlaybackReachesIt()
    {
        // The demo song repeats its first eight bars (count 2): bar 8 (zero based) is reached after two passes, and a time in the second pass of bar 0 is drawn over bar 0.
        var song = DemoSongFactory.Create();
        var onePass = Enumerable.Range(0, 8).Sum(bar => MusicTime.BarMs(song, bar)) / 1000;

        Assert.True(AudioClipTimelineRules.SecondsAtBar(song, 8, 0) > onePass + 1e-6);
        var (bar, fraction) = AudioClipTimelineRules.BarAtSeconds(song, onePass + MusicTime.BarMs(song, 0) / 2000);
        Assert.Equal(0, bar);
        Assert.Equal(0.5, fraction, 6);
    }

    [AvaloniaFact]
    public void AClipWhoseFileIsNotReadYetBuildsNoWaveformOutline()
    {
        var view = new TimelineView(DemoSongFactory.Create());
        var clip = new AudioClip { File = "waveform-test-missing.wav", Name = "Take", SourceLengthSec = 2 };
        // While the file has no peaks, Draw shows its status and builds no outline.
        view.Waveforms.Draw(new NullDrawContext(), clip, new global::Avalonia.Rect(0, 0, 100, 30), default, 1);
        Assert.Equal(0, view.Waveforms.OutlinesBuilt);
    }

    [AvaloniaFact]
    public void AClipMovedNearThePlayheadSnapsItsStartToIt()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = new AudioClip { Name = "Take", StartSec = 0.5, SourceLengthSec = 1 };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song) { Snap = new SnapSettings { ToGrid = false, ToItems = false } };
        view.SetPlayhead(2, 0);
        var press = new global::Avalonia.Point((view.XOfSeconds(0.5) + view.XOfSeconds(1.5)) / 2, view.RowTop(song.Tracks.Count - 1) + 4);
        var gestures = new TimelineClipGestureController(view);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        // The pointer moves until the clip's start sits 3 px left of the playhead bar, inside the snap distance.
        var movedBy = view.BarX(2) - 3 - view.XOfSeconds(0.5);
        gestures.Update(new global::Avalonia.Point(press.X + movedBy, press.Y), altHeld: false);
        Assert.Equal(view.SecondsOfX(view.BarX(2)), clip.StartSec, 6);
    }

    [AvaloniaFact]
    public void DraggingTheFadeInHandleSetsTheFadeIn()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = new AudioClip { Name = "Take", StartSec = 0.5, SourceLengthSec = 1 };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song) { Snap = new SnapSettings { Enabled = false } };
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new global::Avalonia.Point(box.X + 1, box.Y + 1);
        var gestures = new TimelineClipGestureController(view);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        gestures.Update(new global::Avalonia.Point(view.XOfSeconds(0.8), press.Y), altHeld: false);
        Assert.Equal(0.3, clip.FadeInSec, 2);
        Assert.Equal(0.5, clip.StartSec, 6);
    }

    [AvaloniaFact]
    public void ClipFillMatchesTheWpfFillAndNarrowClipsShowNoName()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = new AudioClip { Name = "Take", StartSec = 0, SourceLengthSec = 8, FileLengthSec = 8 };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song);
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();

        TimelineAudioClips.DrawRow(draw, key => AvaloniaPalette.Get(key, false), view, track, song.Tracks.Count - 1,
            view.LaneBandTopOf(song.Tracks.Count - 1), AvaloniaPalette.Get("InstrumentAccent", false));

        // The WPF fill (0.34): the same-colour waveform stays readable over it.
        var expectedAlpha = (byte)Math.Round(255 * 0.34);
        Assert.Contains(draw.Rects, r => r.Fill is { } fill && fill.A == expectedAlpha);
        Assert.Contains(draw.Texts, t => t.Text == "Take");

        var narrow = new InstrumentFretNumberTests.RecordingDrawContext();
        clip.SourceLengthSec = 0.0001; clip.FileLengthSec = 0.0001;
        TimelineAudioClips.DrawRow(narrow, key => AvaloniaPalette.Get(key, false), view, track, song.Tracks.Count - 1,
            view.LaneBandTopOf(song.Tracks.Count - 1), AvaloniaPalette.Get("InstrumentAccent", false));
        Assert.DoesNotContain(narrow.Texts, t => t.Text == "Take");
    }

    [AvaloniaFact]
    public void MidiClipDrawsItsNotesUnderTheName()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = new AudioClip
        {
            Name = "Keys", StartSec = 0, SourceLengthSec = 8, FileLengthSec = 8,
            Notes = new List<TabForge.Models.ClipNote> { new(0, 1, 60, 100), new(1, 0.5, 72, 90) },
        };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song);
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();

        TimelineAudioClips.DrawRow(draw, key => AvaloniaPalette.Get(key, false), view, track, song.Tracks.Count - 1,
            view.LaneBandTopOf(song.Tracks.Count - 1), AvaloniaPalette.Get("InstrumentAccent", false));

        Assert.Contains(draw.Texts, t => t.Text == "Keys");
        Assert.True(draw.Rects.Any(r => Math.Abs(r.Height - (ClipLanes.AudioLaneHeight - 8) / 13.0) < 0.01), "the MIDI notes draw as rows of the pitch range");
    }
}
