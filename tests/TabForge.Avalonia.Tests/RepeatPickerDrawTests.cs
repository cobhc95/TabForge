using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the repeat picker drawing (S2): the picker form by region width, the single full-height shown slice of an
//   N>=4 fold, the hollow passes (union, and the selected clip alone), the popover flag rendering, and the N<=3
//   stacked look staying exactly as it is.
// Does not own: the fold (RepeatViewTests), the pass mapping (RepeatPasses) or any picker interaction (S3).
// Tests: this class.
public sealed class RepeatPickerDrawTests
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

    private static Func<string, UiColor> Ink(bool light = false) => key => AvaloniaPalette.Get(key, light);

    [Fact]
    public void PickerFormsFollowTheOnScreenRegionWidth()
    {
        // The task's worked examples: 4 bars at 45 px = 180 px -> chip, at 2x zoom (360 px) -> pills, 1 bar at 30 px -> dot.
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Pills, TimelineRepeatPicker.FormFor(360, 7));
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Pills, TimelineRepeatPicker.FormFor(190, 7));
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Chip, TimelineRepeatPicker.FormFor(189, 7));
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Chip, TimelineRepeatPicker.FormFor(180, 7));
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Chip, TimelineRepeatPicker.FormFor(48, 7));
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Dot, TimelineRepeatPicker.FormFor(47, 7));
        Assert.Equal(TimelineRepeatPicker.RepeatPickerForm.Dot, TimelineRepeatPicker.FormFor(30, 7));
        Assert.Equal("Pass 1 of 7", TimelineRepeatPicker.CaptionFor(1, 7));
        Assert.Equal("Pass 5 of 7", TimelineRepeatPicker.CaptionFor(5, 7));
        Assert.Equal("1/7", TimelineRepeatPicker.ChipCaptionFor(1, 7));

        var pills = TimelineRepeatPicker.PillRects(100, 200, 7);
        Assert.Equal(7, pills.Count);
        Assert.Equal(100 + TimelineRepeatPicker.PickerEdgeDip, pills[0].X, 6);
        Assert.Equal(200 + TimelineRepeatPicker.PickerTopDip, pills[0].Y, 6);
        Assert.Equal(TimelineRepeatPicker.PillStepDip, pills[1].X - pills[0].X, 6);
        Assert.Equal(TimelineRepeatPicker.PillTallDip, pills[0].Height, 6);
        var chip = TimelineRepeatPicker.ChipRect(100, 60, 200, 20);
        Assert.Equal(100 + TimelineRepeatPicker.PickerEdgeDip, chip.X, 6);
        Assert.Equal(200 + TimelineRepeatPicker.PickerTopDip, chip.Y, 6);
        Assert.Equal(TimelineRepeatPicker.PillTallDip, chip.Height, 6);
        var dot = TimelineRepeatPicker.DotRect(100, 200);
        Assert.Equal(TimelineRepeatPicker.DotDip, dot.Width, 6);
        Assert.Equal(TimelineRepeatPicker.DotDip, dot.Height, 6);
    }

    [AvaloniaFact]
    public void SevenPassClipDrawsOnlyTheShownPassFullHeight()
    {
        var (song, track) = RepeatSevenSong();
        var oneBar = OneBar(song.Project);
        var clip = AudioClipSpanning(0, 16 * oneBar);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var region = view.RepeatRegionsNow().First(r => r.PassCount == 7);
        Assert.Equal((1, 2, 7), (region.StartBar, region.EndBar, region.PassCount));
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);

        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        var parts = slices.Where(slice => slice.IsPart).ToList();
        Assert.Equal(7, parts.Count);
        Assert.All(parts, part => Assert.Equal(7, part.PartCount));
        var onlyRegion = Assert.Single(TimelineRepeatPicker.RegionsOfFold(view, slices));
        Assert.Equal(region, onlyRegion);

        // One geometry: the full-height box is the whole lane inner height; the stacked box is one row of seven.
        var full = TimelineAudioClips.SliceRect(view, parts[0], laneTop, true);
        Assert.Equal(ClipLanes.AudioLaneHeight - 2 * 2, full.Height, 6);
        var stacked = TimelineAudioClips.SliceRect(view, parts[0], laneTop);
        Assert.Equal((ClipLanes.AudioLaneHeight - 2 * 2) / 7, stacked.Height, 6);

        // The default shown pass is 1: the head, the pass-1 slice and the tail draw full height; the picker is a chip.
        var drawn = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(drawn, Ink(), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        // The slice boxes only: the moving loading stripe of a clip whose file is still loading is not a slice (it comes and goes with the file load).
        var sliceLefts = slices.Select(slice => TimelineAudioClips.SliceRect(view, slice, laneTop, true).X).ToList();
        var tall = drawn.Rects.Where(r => r.Fill && Math.Abs(r.Height - (ClipLanes.AudioLaneHeight - 4)) < 0.5 && sliceLefts.Any(x => Math.Abs(x - r.X) < 0.5)).ToList();
        Assert.Equal(3, tall.Count);
        Assert.Contains("1/7", drawn.Texts);
        Assert.DoesNotContain(drawn.Texts, text => text.StartsWith("Pass ", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void ShownPassSwitchMovesTheDrawnSliceButTheHitAreaStaysTheWholeClip()
    {
        var (song, track) = RepeatSevenSong();
        var performed = SongExtent.Timing(song.Project).Performed;
        var (start3, length3) = RepeatPasses.PerformedRangeForPass(performed, 1, 2, 3);
        // Passes 3 and 4: a clip covering ONE pass is an ordinary box now (no picker, RepeatUnfoldTests), so this one covers two.
        var (start4, length4) = RepeatPasses.PerformedRangeForPass(performed, 1, 2, 4);
        var clip = AudioClipSpanning(start3, start4 + length4 - start3);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var region = view.RepeatRegionsNow().First(r => r.PassCount == 7);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);

        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        Assert.Equal(new[] { 2, 3 }, slices.Select(slice => slice.PartIndex).ToArray());
        Assert.All(slices, part => Assert.Equal(7, part.PartCount));
        Assert.True(length3 > 0);

        var left = view.BarX(1);
        var right = view.BarX(3);
        var centre = new global::Avalonia.Point((left + right) / 2, laneTop + ClipLanes.AudioLaneHeight / 2);
        // The slice boxes only: the moving loading stripe of a clip whose file is still loading is not a slice (it comes and goes with the file load).
        var sliceLefts = slices.Select(slice => TimelineAudioClips.SliceRect(view, slice, laneTop, true).X).ToList();
        bool IsSliceBox((double X, double Y, double Width, double Height, bool Fill, bool Stroke) r) => r.Fill && Math.Abs(r.Height - (ClipLanes.AudioLaneHeight - 4)) < 0.5 && sliceLefts.Any(x => Math.Abs(x - r.X) < 0.5);

        // Shown pass 1 is not covered: nothing of the clip draws in the region, but the hit area stays the whole clip.
        var hidden = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(hidden, Ink(), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        Assert.DoesNotContain(hidden.Rects, IsSliceBox);
        Assert.Contains("1/7", hidden.Texts);
        Assert.Same(clip, TimelineAudioClips.ClipAt(view, centre)!.Clip);

        // Shown pass 3 is covered: exactly one full-height slice draws, and the chip names the pass.
        view.RepeatShown.SetPass(region, 3);
        var shown = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(shown, Ink(), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        Assert.Single(shown.Rects, IsSliceBox);
        Assert.Contains("3/7", shown.Texts);
        Assert.Same(clip, TimelineAudioClips.ClipAt(view, centre)!.Clip);

        // The outline of the selected clip still spans the whole fold.
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var outlined = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(outlined, Ink(), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        Assert.Equal(ClipLanes.AudioLaneHeight - 4, box.Height, 6);
        Assert.Single(outlined.Rects, r => !r.Fill && r.Stroke && Math.Abs(r.X - box.X) < 1 && Math.Abs(r.Width - box.Width) < 1);
    }

    [AvaloniaFact]
    public void HollowPassesFollowCoverageAndTheSelectedClip()
    {
        var (song, track) = RepeatSevenSong();
        var performed = SongExtent.Timing(song.Project).Performed;
        var (start1, _) = RepeatPasses.PerformedRangeForPass(performed, 1, 2, 1);
        var (start3, length3) = RepeatPasses.PerformedRangeForPass(performed, 1, 2, 3);
        var early = AudioClipSpanning(start1, start3 + length3 - start1);   // passes 1-3
        var all = AudioClipSpanning(0, 16 * OneBar(song.Project));          // every pass
        track.AudioClips.Add(early);
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var region = view.RepeatRegionsNow().First(r => r.PassCount == 7);

        // Per-track union: passes 4-7 are hollow.
        Assert.Equal(new[] { 4, 5, 6, 7 }, TimelineRepeatPicker.HollowPasses(view, track, region).OrderBy(p => p));
        // The selected clip alone counts when it is on that track, even with a covering clip beside it.
        track.AudioClips.Add(all);
        view.SelectedClip = new TimelineClipSelection(track, early);
        Assert.Equal(new[] { 4, 5, 6, 7 }, TimelineRepeatPicker.HollowPasses(view, track, region).OrderBy(p => p));
        // No selection: the union covers every pass.
        view.SelectedClip = null;
        Assert.Empty(TimelineRepeatPicker.HollowPasses(view, track, region));
        // A selection on another track leaves this track's union alone.
        var other = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        other.AudioClips.Add(AudioClipSpanning(0, OneBar(song.Project)));
        song.Project.Tracks.Add(other);
        view.SelectedClip = new TimelineClipSelection(other, other.AudioClips[0]);
        Assert.Empty(TimelineRepeatPicker.HollowPasses(view, track, region));
    }

    [AvaloniaFact]
    public void PillsFormDrawsEveryPassAndThePassLabel()
    {
        var (song, track) = RepeatSevenSong();
        var oneBar = OneBar(song.Project);
        track.AudioClips.Add(AudioClipSpanning(0, 16 * oneBar));
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false }, ZoomFactor = 4 };
        var region = view.RepeatRegionsNow().First(r => r.PassCount == 7);
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);

        var drawn = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(drawn, Ink(), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        Assert.Contains("Pass 1 of 7", drawn.Texts);
        foreach (var pass in Enumerable.Range(1, 7)) Assert.Contains(pass.ToString(), drawn.Texts);
    }

    [AvaloniaFact]
    public void PopoverRendersAboveTheLaneOnlyWhenOpen()
    {
        var (song, track) = RepeatSevenSong();
        var oneBar = OneBar(song.Project);
        track.AudioClips.Add(AudioClipSpanning(0, 16 * oneBar));
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var region = view.RepeatRegionsNow().First(r => r.PassCount == 7);
        var laneTop = view.LaneBandTopOf(song.Project.Tracks.IndexOf(track));

        var closed = new RepeatTakeLandingTests.TextRecorder();
        TimelineRepeatPicker.DrawPopovers(closed, view, Ink());
        Assert.Empty(closed.Rects);
        Assert.Empty(closed.Texts);

        view.PickerPopoverRegion = region;
        var open = new RepeatTakeLandingTests.TextRecorder();
        TimelineRepeatPicker.DrawPopovers(open, view, Ink());
        foreach (var pass in Enumerable.Range(1, 7)) Assert.Contains(pass.ToString(), open.Texts);
        Assert.Contains(open.Rects, rect => rect.Y < laneTop);
    }

    [AvaloniaFact]
    public void TwoPassRepeatsKeepTheirStackedLook()
    {
        var (song, track) = RepeatTwoSong();
        var oneBar = OneBar(song.Project);
        var clip = AudioClipSpanning(0.5 * oneBar, 5 * oneBar);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);
        var region = view.RepeatRegionsNow().First(r => r.PassCount == 2);

        // The stacked slices, labels 1 and 2, and one outline: exactly as before.
        var drawn = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(drawn, Ink(), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        Assert.Contains("1", drawn.Texts);
        Assert.Contains("2", drawn.Texts);
        Assert.DoesNotContain(drawn.Texts, text => text.StartsWith("Pass ", StringComparison.Ordinal));
        Assert.DoesNotContain(drawn.Texts, text => text.Contains('/'));

        // No picker form draws for a two-pass region.
        var picker = new RepeatTakeLandingTests.TextRecorder();
        TimelineRepeatPicker.DrawPicker(picker, view, track, region, laneTop, Ink());
        Assert.Empty(picker.Rects);
        Assert.Empty(picker.Texts);
    }
}
