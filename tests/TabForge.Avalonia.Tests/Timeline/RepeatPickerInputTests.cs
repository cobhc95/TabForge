using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the repeat picker interaction (S3): pill click sets the shown pass without seeking/selecting, chip click wraps,
//   Alt+wheel steps (clamped) while plain wheel keeps zooming, a press below the pills keeps the clip path, N=2 has no
//   picker, and the dot popover opens on hover and closes on leave/Esc.
// Does not own: the picker drawing (RepeatPickerDrawTests) or any follow-playback.
// Tests: this class.
public sealed class RepeatPickerInputTests
{
    private static (SongDocument Song, TrackModel Track) RepeatSevenSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        song.Project.Tracks[0].Measures[2].RepeatCount = 7;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    private static (SongDocument Song, TrackModel Track) RepeatTwoSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    private static double OneBar(SongProject project) => AudioClipTimelineRules.SecondsAtBar(project, 1, 0);

    private static AudioClip AudioClipSpanning(double startSec, double lengthSec) => new()
    {
        File = "take.wav", Name = "take", StartSec = startSec, SourceLengthSec = lengthSec, FileLengthSec = lengthSec,
    };

    private static (Window Window, TimelineView View, TimelineInputController Input) OpenTimeline(SongDocument song, double zoom = 1.0)
    {
        var view = new TimelineView(song.Project)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (Math.Abs(zoom - 1.0) > 1e-9) view.ZoomFactor = zoom;
        var input = new TimelineInputController(view, song);
        var window = new Window { Width = 1300, Height = 500, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return (window, view, input);
    }

    private static (Window Window, ArrangementView View) OpenArrangement(SongDocument song)
    {
        var view = new ArrangementView(song.Project, song)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Width = 1200, Height = 500, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    private static Point ToWindow(TimelineView view, Window window, Point point) =>
        view.TranslatePoint(point, window) ?? point;

    [AvaloniaFact]
    public void ClickPill5SetsShown5WithoutSeekOrSelect()
    {
        var (song, track) = RepeatSevenSong();
        track.AudioClips.Add(AudioClipSpanning(0, 16 * OneBar(song.Project)));
        var (window, view, input) = OpenTimeline(song, zoom: 4.0);
        var region = Assert.Single(view.RepeatRegionsNow());
        Assert.Equal(7, region.PassCount);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var regionX = view.BarX(region.StartBar);
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Pills,
            TimelineRepeatPicker.FormFor(view.BarX(region.EndBar + 1) - regionX, region.PassCount));

        var pills = TimelineRepeatPicker.PillRects(regionX, laneTop, region.PassCount);
        var pill5 = pills[4];
        var at = ToWindow(view, window, new Point(pill5.X + pill5.Width / 2, pill5.Y + pill5.Height / 2));
        var playheadBefore = view.PlayheadBar;
        var seeked = -1;
        input.BarSeekRequested += bar => seeked = bar;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.Equal(5, view.RepeatShown.ShownPass(region));
        Assert.Equal(playheadBefore, view.PlayheadBar);
        Assert.Null(view.SelectedClip);
        Assert.Equal(-1, seeked);
        Assert.Equal(-1, input.SelectedBar);
        window.Close();
        song.MarkClean();
    }

    [AvaloniaFact]
    public void ClickChipOn7Of7WrapsTo1()
    {
        var (song, track) = RepeatSevenSong();
        track.AudioClips.Add(AudioClipSpanning(0, 16 * OneBar(song.Project)));
        var (window, view, input) = OpenTimeline(song);
        var region = Assert.Single(view.RepeatRegionsNow());
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var regionX = view.BarX(region.StartBar);
        var regionSpan = view.BarX(region.EndBar + 1) - regionX;
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Chip,
            TimelineRepeatPicker.FormFor(regionSpan, region.PassCount));
        view.RepeatShown.SetPass(region, 7);

        var chip = TimelineRepeatPicker.ChipHitRect(regionX, regionSpan, laneTop, 7, region.PassCount);
        var at = ToWindow(view, window, new Point(chip.X + chip.Width / 2, chip.Y + chip.Height / 2));
        var seeked = -1;
        input.BarSeekRequested += bar => seeked = bar;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.Equal(1, view.RepeatShown.ShownPass(region));
        Assert.Equal(-1, seeked);
        Assert.Null(view.SelectedClip);
        Assert.NotNull(view.PickerPopoverRegion);
        window.Close();
        song.MarkClean();
    }

    [AvaloniaFact]
    public void AltWheelUpTwiceFrom3Reaches5()
    {
        var (song, track) = RepeatSevenSong();
        track.AudioClips.Add(AudioClipSpanning(0, 16 * OneBar(song.Project)));
        var (window, arrangement) = OpenArrangement(song);
        var timeline = arrangement.Timeline;
        var region = Assert.Single(timeline.RepeatRegionsNow());
        timeline.RepeatShown.SetPass(region, 3);
        var row = song.Project.Tracks.IndexOf(track);
        var at = timeline.TranslatePoint(
            new Point(timeline.BarX(1) + 5, timeline.LaneBandTopOf(row) + 10), window)!;

        window.MouseWheel(at.Value, new Vector(0, 1), RawInputModifiers.Alt);
        window.MouseWheel(at.Value, new Vector(0, 1), RawInputModifiers.Alt);

        Assert.Equal(5, timeline.RepeatShown.ShownPass(region));
        window.Close();
        song.MarkClean();
    }

    [AvaloniaFact]
    public void AltWheelUpAt7StaysAt7()
    {
        var (song, track) = RepeatSevenSong();
        track.AudioClips.Add(AudioClipSpanning(0, 16 * OneBar(song.Project)));
        var (window, arrangement) = OpenArrangement(song);
        var timeline = arrangement.Timeline;
        var region = Assert.Single(timeline.RepeatRegionsNow());
        timeline.RepeatShown.SetPass(region, 7);
        var row = song.Project.Tracks.IndexOf(track);
        var at = timeline.TranslatePoint(
            new Point(timeline.BarX(1) + 5, timeline.LaneBandTopOf(row) + 10), window)!;

        window.MouseWheel(at.Value, new Vector(0, 1), RawInputModifiers.Alt);

        Assert.Equal(7, timeline.RepeatShown.ShownPass(region));
        window.Close();
        song.MarkClean();
    }

    [AvaloniaFact]
    public void PlainWheelOverRegionKeepsZoomBehaviour()
    {
        var (song, track) = RepeatSevenSong();
        track.AudioClips.Add(AudioClipSpanning(0, 16 * OneBar(song.Project)));
        var (window, arrangement) = OpenArrangement(song);
        var timeline = arrangement.Timeline;
        var region = Assert.Single(timeline.RepeatRegionsNow());
        timeline.RepeatShown.SetPass(region, 3);
        var before = timeline.ZoomFactor;
        var row = song.Project.Tracks.IndexOf(track);
        var at = timeline.TranslatePoint(
            new Point(timeline.BarX(1) + 5, timeline.LaneBandTopOf(row) + 10), window)!;

        window.MouseWheel(at.Value, new Vector(0, 1));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.Equal(3, timeline.RepeatShown.ShownPass(region));
        Assert.True(timeline.ZoomFactor > before);
        window.Close();
        song.MarkClean();
    }
    private static int BarUnder(TimelineView view, double x)
    {
        var bar = 0;
        for (var b = 0; b < view.BarCount; b++) if (view.BarX(b) <= x) bar = b;
        return bar;
    }

    [AvaloniaFact]
    public void PressOnClipAudioBelowThePillsKeepsClipSelectAndSeeksTheClipStart()
    {
        var (song, track) = RepeatSevenSong();
        var clip = AudioClipSpanning(0, 16 * OneBar(song.Project));
        track.AudioClips.Add(clip);
        var (window, view, input) = OpenTimeline(song, zoom: 4.0);
        var region = Assert.Single(view.RepeatRegionsNow());
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var middleX = (view.BarX(region.StartBar) + view.BarX(region.EndBar + 1)) / 2;
        var at = ToWindow(view, window, new Point(middleX, laneTop + 30));
        var seeked = -1;
        input.BarSeekRequested += bar => seeked = bar;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.NotNull(view.SelectedClip);
        Assert.Same(clip, view.SelectedClip!.Clip);
        Assert.Equal(BarUnder(view, middleX), seeked);   // a plain clip click moves the playhead to the bar under the pointer
        Assert.Equal(1, view.RepeatShown.ShownPass(region));
        Assert.Null(view.PickerPopoverRegion);
        window.Close();
        song.MarkClean();
    }

    [AvaloniaFact]
    public void TwoPassRegionHasNoPickerAndKeepsExistingBehaviour()
    {
        var (song, track) = RepeatTwoSong();
        var clip = AudioClipSpanning(0.5 * OneBar(song.Project), 5 * OneBar(song.Project));
        track.AudioClips.Add(clip);
        var (window, view, input) = OpenTimeline(song);
        var region = Assert.Single(view.RepeatRegionsNow());
        Assert.Equal(2, region.PassCount);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var regionX = view.BarX(region.StartBar);
        var at = ToWindow(view, window, new Point(regionX + 10, laneTop + 5));
        var seeked = -1;
        input.BarSeekRequested += bar => seeked = bar;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.NotNull(view.SelectedClip);
        Assert.Same(clip, view.SelectedClip!.Clip);
        Assert.Equal(BarUnder(view, regionX + 10), seeked);   // a plain clip click moves the playhead to the bar under the pointer
        Assert.Null(view.PickerPopoverRegion);
        window.Close();
        song.MarkClean();
    }

    [AvaloniaFact]
    public void DotHoverOpensPopoverLeaveAndEscCloseIt()
    {
        var (song, track) = RepeatSevenSong();
        track.AudioClips.Add(AudioClipSpanning(0, 16 * OneBar(song.Project)));
        var (window, view, _) = OpenTimeline(song, zoom: 0.5);
        var region = Assert.Single(view.RepeatRegionsNow());
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var regionX = view.BarX(region.StartBar);
        var regionSpan = view.BarX(region.EndBar + 1) - regionX;
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Dot,
            TimelineRepeatPicker.FormFor(regionSpan, region.PassCount));

        var dot = TimelineRepeatPicker.DotRect(regionX, laneTop);
        var overDot = ToWindow(view, window, new Point(dot.X + dot.Width / 2, dot.Y + dot.Height / 2));
        window.MouseMove(overDot);
        Assert.NotNull(view.PickerPopoverRegion);
        Assert.Equal(region, view.PickerPopoverRegion);

        window.MouseMove(new Point(10, 5));
        Assert.Null(view.PickerPopoverRegion);

        window.MouseMove(overDot);
        Assert.NotNull(view.PickerPopoverRegion);

        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, KeyModifiers = KeyModifiers.None, Source = view });
        Assert.Null(view.PickerPopoverRegion);
        window.Close();
        song.MarkClean();
    }
}
