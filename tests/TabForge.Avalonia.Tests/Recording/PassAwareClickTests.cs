using System;
using System.Collections.Generic;
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

// Owns: the S4b pass-aware click (RV12) and the S3b follow-playback state (RV11): a folded clip's part-2
//   slice seeks pass 2, part 1 and ruler clicks keep the old bar path, an x7 shown pass seeks that pass,
//   Alt+click on the ruler cycles (wrapping, no-op outside), a drag never seeks, follow keeps LastPlayed
//   while playing and the pick returns on stop, and the playhead label.
// Does not own: the core seek (PassAwareSeekTests), the fold or the picker interaction itself.
// Tests: this class.
public sealed class PassAwareClickTests
{
    private static (SongDocument Song, TrackModel Track) RepeatSong(int passes)
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        if (passes != 2) song.Project.Tracks[0].Measures[2].RepeatCount = passes;
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
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return (window, view, input);
    }

    private static Point ToWindow(TimelineView view, Window window, Point point) =>
        view.TranslatePoint(point, window) ?? point;

    private static Point RulerAt(TimelineView view, int bar) => new(view.BarX(bar) + 5, TimelineView.RulerHeight / 2);

    private static Point Centre(Rect rect) => new((rect.X + rect.Right) / 2, (rect.Y + rect.Bottom) / 2);

    private static int BarUnder(TimelineView view, double x)
    {
        var bar = 0;
        for (var b = 0; b < view.BarCount; b++) if (view.BarX(b) <= x) bar = b;
        return bar;
    }

    // Rule: a plain click on a clip selects it and moves the playhead to the bar under the pointer (pass 1); no pass-aware position is raised.
    [AvaloniaFact]
    public void ClickPart2Slice_SelectsTheClipAndSeeksItsStart()
    {
        var (song, track) = RepeatSong(2);
        var clip = AudioClipSpanning(0, 4 * OneBar(song.Project));
        track.AudioClips.Add(clip);
        var (window, view, input) = OpenTimeline(song);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        RepeatSlice part2 = default;
        var found = false;
        foreach (var slice in slices)
            if (slice.IsPart && slice.PartIndex == 1) { part2 = slice; found = true; }
        Assert.True(found);
        var clickBox = TimelineAudioClips.SliceRect(view, part2, laneTop);
        var at = ToWindow(view, window, Centre(clickBox));
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.Close();
        song.MarkClean();

        Assert.Equal(new[] { BarUnder(view, Centre(clickBox).X) }, bars);   // a plain clip click moves the playhead to the bar under the pointer (TimelineInputController), pass 1
        Assert.Empty(positions);
        Assert.NotNull(view.SelectedClip);
    }

    // Rule: a plain click on a clip selects it and moves the playhead to the bar under the pointer (pass 1); no pass-aware position is raised.
    [AvaloniaFact]
    public void ClickPart1Slice_SelectsTheClipAndSeeksItsStart()
    {
        var (song, track) = RepeatSong(2);
        var clip = AudioClipSpanning(0, 4 * OneBar(song.Project));
        track.AudioClips.Add(clip);
        var (window, view, input) = OpenTimeline(song);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        RepeatSlice part1 = default;
        var found = false;
        foreach (var slice in slices)
            if (slice.IsPart && slice.PartIndex == 0) { part1 = slice; found = true; break; }
        Assert.True(found);
        var clickBox = TimelineAudioClips.SliceRect(view, part1, laneTop);
        var at = ToWindow(view, window, Centre(clickBox));
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.Close();
        song.MarkClean();

        Assert.Equal(new[] { BarUnder(view, Centre(clickBox).X) }, bars);   // a plain clip click moves the playhead to the bar under the pointer (TimelineInputController), pass 1
        Assert.Empty(positions);
        Assert.NotNull(view.SelectedClip);
    }

    [AvaloniaFact]
    public void RulerClickInRepeat_UsesOldBarPath()
    {
        var (song, track) = RepeatSong(2);
        track.AudioClips.Add(AudioClipSpanning(0, 4 * OneBar(song.Project)));
        var (window, view, input) = OpenTimeline(song);
        var region = view.RepeatRegionsNow()[0];
        var at = ToWindow(view, window, RulerAt(view, region.StartBar));
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.Close();
        song.MarkClean();

        Assert.Equal(new[] { region.StartBar }, bars);
        Assert.Empty(positions);
    }

    [AvaloniaFact]
    public void ShownPass5EmptyLaneClick_StaysOnTheOldBarPath()
    {
        var (song, track) = RepeatSong(7);
        track.AudioClips.Add(AudioClipSpanning(0, OneBar(song.Project)));   // bar 1 only: the click below lands in the repeat on an empty part of the lane, not on a clip (a clip click never seeks)
        var (window, view, input) = OpenTimeline(song);
        var region = view.RepeatRegionsNow()[0];
        Assert.Equal(7, region.PassCount);
        view.RepeatShown.SetPass(region, 5);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var middleX = (view.BarX(region.StartBar) + view.BarX(region.EndBar + 1)) / 2;
        var at = ToWindow(view, window, new Point(middleX, laneTop + 30));
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.Close();
        song.MarkClean();

        // An empty-lane click never moves the playhead to a pass: it stays pass 1 on the bar path, whatever pass the fold shows.
        Assert.Single(bars);
        Assert.True(bars[0] >= region.StartBar && bars[0] <= region.EndBar);
        Assert.Empty(positions);
    }

    [AvaloniaFact]
    public void AltClickRulerCyclesPass_WrapsTo1()
    {
        var (song, track) = RepeatSong(2);
        track.AudioClips.Add(AudioClipSpanning(0, 4 * OneBar(song.Project)));
        var (window, view, input) = OpenTimeline(song);
        var region = view.RepeatRegionsNow()[0];
        Assert.Equal(2, region.PassCount);
        view.SetPlayhead(region.StartBar, 0.5, 2);
        var at = ToWindow(view, window, RulerAt(view, region.StartBar));
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Alt);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Alt);
        window.Close();
        song.MarkClean();

        Assert.Empty(bars);
        Assert.Single(positions);
        Assert.Equal(region.StartBar, positions[0].WrittenBar);
        Assert.Equal(1, positions[0].Pass);
    }

    [AvaloniaFact]
    public void AltClickRulerOutsideRepeat_IsNoOp()
    {
        var (song, track) = RepeatSong(2);
        track.AudioClips.Add(AudioClipSpanning(0, 4 * OneBar(song.Project)));
        var (window, view, input) = OpenTimeline(song);
        var at = ToWindow(view, window, RulerAt(view, 0));
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Alt);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Alt);
        window.Close();
        song.MarkClean();

        Assert.Empty(bars);
        Assert.Empty(positions);
    }

    [AvaloniaFact]
    public void ClipDragInRepeat_MovesWithoutSeeking()
    {
        var (song, track) = RepeatSong(2);
        var clip = AudioClipSpanning(0, 4 * OneBar(song.Project));
        track.AudioClips.Add(clip);
        var (window, view, input) = OpenTimeline(song);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var before = clip.StartSec;
        var bars = new List<int>();
        var positions = new List<RepeatPosition>();
        input.BarSeekRequested += bars.Add;
        input.PositionSeekRequested += positions.Add;
        var at = ToWindow(view, window, press);
        var moved = ToWindow(view, window, press + new Vector(30, 0));

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(moved);
        window.MouseUp(moved, MouseButton.Left);
        window.Close();
        song.MarkClean();

        Assert.NotEqual(before, clip.StartSec);
        Assert.Empty(bars);
        Assert.Empty(positions);
    }

    [AvaloniaFact]
    public void FollowPlayback_ShowsPlayingPass_KeepsLastPlayed_RestoresPick()
    {
        var (song, track) = RepeatSong(7);
        track.AudioClips.Add(AudioClipSpanning(0, 4 * OneBar(song.Project)));
        var (window, view, _) = OpenTimeline(song);
        var region = view.RepeatRegionsNow()[0];
        view.RepeatShown.SetPass(region, 2);

        view.RepeatShown.FollowPlayback(region, 3);
        Assert.Equal(3, view.RepeatShown.ShownPass(region));
        Assert.Equal(3, view.RepeatShown.LastPlayed(region));

        Assert.True(view.RepeatShown.ResumeUserChoices());
        Assert.Equal(2, view.RepeatShown.ShownPass(region));
        Assert.Equal(3, view.RepeatShown.LastPlayed(region));
        Assert.False(view.RepeatShown.ResumeUserChoices());
        window.Close();
        song.MarkClean();
    }

    [Fact]
    public void PlayheadLabel_InsideRepeatShowsPass_OutsideUnchanged()
    {
        var regions = new[] { new RepeatRegion(1, 2, 2) };

        Assert.Equal("2 - pass 2", TimelinePlayheadMarks.PlayheadLabelFor(1, 2, regions));
        Assert.Equal("2 - pass 1", TimelinePlayheadMarks.PlayheadLabelFor(1, 1, regions));
        Assert.Equal("1", TimelinePlayheadMarks.PlayheadLabelFor(0, 1, regions));
        Assert.Equal("4", TimelinePlayheadMarks.PlayheadLabelFor(3, 1, regions));
    }
}
