using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Model;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the fold decision (fold a clip only when it covers 2+ passes of a repeat, RepeatFold.FoldedSlices): a clip covering one pass is one
//   ordinary box (tagged with its pass from pass 2), two or more passes fold as before, judged per region; a click inside the one-pass box selects the clip with no slice hit.
// Does not own: the fold itself (RepeatViewTests) or the picker (RepeatPickerDrawTests).
// Tests: this class.
public sealed class RepeatUnfoldTests
{
    /// <summary>Performed bar order, one second per bar (bar index i plays from i seconds).</summary>
    private static PerformedBar[] Order(params int[] bars) =>
        bars.Select((bar, index) => new PerformedBar(bar, index * 1000.0, (index + 1) * 1000.0)).ToArray();

    /// <summary>Bars 1-12, repeat 5-8 twice: pass 1 is 4-8 s, pass 2 is 8-12 s.</summary>
    private static PerformedBar[] SpecSong() => Order(1, 2, 3, 4, 5, 6, 7, 8, 5, 6, 7, 8, 9, 10, 11, 12);

    [Fact]
    public void E1_PassOneOnlyIsOneNormalBoxWithNoTag()
    {
        var only = Assert.Single(RepeatFold.FoldedSlices(4, 8, SpecSong()));
        Assert.False(only.IsPart);
        Assert.Equal(0, only.TagPass);
        Assert.Equal(4, only.StartSec, 6);
        Assert.Equal(8, only.EndSec, 6);
    }

    [Fact]
    public void E2_TwoPassesStayFoldedAsTwoSlices()
    {
        var slices = RepeatFold.FoldedSlices(4, 12, SpecSong());
        Assert.Equal(2, slices.Count);
        Assert.All(slices, slice => Assert.Equal(2, slice.PartCount));
        Assert.Equal(new[] { 0, 1 }, slices.Select(slice => slice.PartIndex).ToArray());
    }

    [Fact]
    public void E3_PassTwoOnlyIsOneNormalBoxTaggedPass2()
    {
        var only = Assert.Single(RepeatFold.FoldedSlices(9, 11, SpecSong()));
        Assert.False(only.IsPart);
        Assert.Equal(2, only.TagPass);
    }

    [Fact]
    public void E4_BarThreeIntoBarSixOfPassOneIsOneNormalBox()
    {
        var only = Assert.Single(RepeatFold.FoldedSlices(2, 5.5, SpecSong()));
        Assert.False(only.IsPart);
        Assert.Equal(0, only.TagPass);
        Assert.Equal(2, only.StartSec, 6);
        Assert.Equal(5.5, only.EndSec, 6);
    }

    [Fact]
    public void E5_ClipEndingExactlyAtTheStartOfPassTwoCountsAsPassOneOnly()
    {
        var only = Assert.Single(RepeatFold.FoldedSlices(2, 8, SpecSong()));
        Assert.False(only.IsPart);
        // A sliver of under a millisecond into pass 2 does not count as covering it either.
        var sliver = Assert.Single(RepeatFold.FoldedSlices(2, 8.0004, SpecSong()));
        Assert.False(sliver.IsPart);
    }

    [Fact]
    public void E6_ManyPassRegionCoveringOnePassIsOneNormalBoxTaggedWithItsPass()
    {
        // Bars 2-3 played four times: pass k is [1 + 2(k-1), 3 + 2(k-1)) s.
        var performed = Order(1, 2, 3, 2, 3, 2, 3, 2, 3, 4);
        var only = Assert.Single(RepeatFold.FoldedSlices(5, 7, performed));
        Assert.False(only.IsPart);
        Assert.Equal(3, only.TagPass);
        var two = RepeatFold.FoldedSlices(3, 7, performed);
        Assert.Equal(2, two.Count);
        Assert.All(two, slice => Assert.Equal(4, slice.PartCount));
    }

    [Fact]
    public void E7_PassTwoOnlyRunningIntoTheTailIsTaggedPassTwoThenAnOrdinaryTail()
    {
        // Pass 2 (8-12 s, written 4-8 s) is the clip's only play of bars 5-8; the tail is bars 9-10 at 12-14 s, not merged into the tagged box.
        var slices = RepeatFold.FoldedSlices(8, 14, SpecSong());
        Assert.Equal(2, slices.Count);
        Assert.All(slices, slice => Assert.False(slice.IsPart));
        Assert.Equal(2, slices[0].TagPass);
        Assert.Equal(4, slices[0].WrittenStartSec, 6);
        Assert.Equal(8, slices[0].WrittenEndSec, 6);
        Assert.Equal(0, slices[1].TagPass);
        Assert.Equal(slices[0].EndSec, slices[1].StartSec, 6);
        Assert.Equal(14, slices[1].EndSec, 6);
        // Pass 1 with an ordinary lead-in (E4 shape) still merges into one untagged box.
        var lead = Assert.Single(RepeatFold.FoldedSlices(2, 5.5, SpecSong()));
        Assert.Equal(0, lead.TagPass);
    }

    [Fact]
    public void F5_ClipFoldsInTheRegionItCoversTwiceAndNotInTheOneItCoversOnce()
    {
        // Region A = bars 2-3 x2 (1-5 s), region B = bars 5-6 x2 (6-8 s then 8-10 s): the clip covers A twice, B once.
        var slices = RepeatFold.FoldedSlices(1, 8, Order(1, 2, 3, 2, 3, 4, 5, 6, 5, 6, 7));
        Assert.Equal(3, slices.Count);
        Assert.True(slices[0].IsPart);
        Assert.True(slices[1].IsPart);
        Assert.False(slices[2].IsPart);   // bar 4 and pass 1 of B merged into one box
        Assert.Equal(5, slices[2].StartSec, 6);
        Assert.Equal(8, slices[2].EndSec, 6);
    }

    [Fact]
    public void LiveBoxIsOneNormalBandInPassOneAndFoldsOnceItEntersPassTwo()
    {
        var inPassOne = LiveTakeFold.RepeatBands(4, 7, SpecSong());
        Assert.Single(inPassOne);
        Assert.Equal(0, inPassOne[0].Pass);
        var inPassTwo = LiveTakeFold.RepeatBands(4, 9, SpecSong());
        Assert.Equal(2, inPassTwo.Count);
        Assert.Equal(new[] { 1, 2 }, inPassTwo.Select(band => band.Pass).ToArray());
    }

    [AvaloniaFact]
    public void E1_ClickInsideThePassOneBoxSelectsTheClipWithNoSliceHit()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var oneBar = AudioClipTimelineRules.SecondsAtBar(song.Project, 1, 0);
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = oneBar, SourceLengthSec = 2 * oneBar, FileLengthSec = 2 * oneBar };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, song);
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        var laneTop = view.LaneBandTopOf(song.Project.Tracks.IndexOf(track));

        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        var box = TimelineAudioClips.SliceRect(view, Assert.Single(slices), laneTop);
        Assert.Equal(ClipLanes.AudioLaneHeight - 4, box.Height, 6);   // one full-height box, not a half-lane slice
        var at = view.TranslatePoint(new Point((box.X + box.Right) / 2, (box.Y + box.Bottom) / 2), window) ?? default;
        var positions = new List<RepeatPosition>();
        input.PositionSeekRequested += positions.Add;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.Close();
        song.MarkClean();

        Assert.Same(clip, view.SelectedClip?.Clip);
        Assert.DoesNotContain(slices, slice => slice.IsPart);
        Assert.All(positions, position => Assert.Equal(1, position.Pass));
    }
}
