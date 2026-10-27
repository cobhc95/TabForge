using System;
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
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a long audio clip dragged and dropped across the active loop region (and a repeat after it) stays one continuous piece: one clip, the same length and source,
//   drawn slices that touch end to end with no hole over the loop, the same slices the drag preview showed at that position.
// Does not own: the fold rules (RepeatViewTests) or the drag math (ClipDragRepeatTests).
// Tests: this class.
public sealed class ClipDropLoopRegionTests
{
    private sealed record Setup(SongDocument Song, Window Window, TimelineView View, TrackModel Track, AudioClip Clip, double BarSec);

    // 0-based: loop over bars 39-46 ("Chorus 1"); a repeat at bars 49-50 with endings [1.] 51 and [2.] 52 (plays 49 50 51 | 49 50 52).
    private static Setup Open(bool repeat, bool loop, int at = 49)
    {
        var project = TemplateFactory.Blank();
        foreach (var t in project.Tracks)
            while (t.Measures.Count < 70) t.Measures.Add(new MeasureModel { Number = t.Measures.Count + 1 });
        var m = project.Tracks[0].Measures;
        if (repeat)
        {
            m[at].RepeatStart = true;
            (m[at + 2].AlternateEndingMask, m[at + 2].RepeatEnd) = (1, true);
            m[at + 3].AlternateEndingMask = 2;
        }
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(track);
        var barSec = AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = 30 * barSec, SourceLengthSec = 24 * barSec, FileLengthSec = 24 * barSec };
        track.AudioClips.Add(clip);
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        if (loop) song.Selection.SetLoop(39, 46);
        var view = new TimelineView(project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Snap = new SnapSettings { Enabled = false } };
        _ = new TimelineInputController(view, song);
        var window = new Window { Width = 3000, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return new Setup(song, window, view, track, clip, barSec);
    }

    private static void Drag(Setup s, Point from, Point to)
    {
        Point Win(Point p) => s.View.TranslatePoint(p, s.Window)!.Value;
        s.Window.MouseDown(Win(from), MouseButton.Left);
        for (var i = 1; i <= 10; i++) s.Window.MouseMove(Win(from + (to - from) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(Win(to), MouseButton.Left);
    }

    private static void AssertOnePiece(Setup s, double length)
    {
        Assert.Same(s.Clip, Assert.Single(s.Track.AudioClips));
        Assert.Equal(length, s.Clip.EndSec - s.Clip.StartSec, 6);
        var piece = Assert.Single(ClipLoop.Pieces(s.Clip));
        Assert.Equal(s.Clip.StartSec, piece.StartSec, 9);
        Assert.Equal(length, piece.SourceLengthSec, 6);
        var laneTop = s.View.LaneBandTopOf(s.Song.Project.Tracks.IndexOf(s.Track));
        var slices = TimelineAudioClips.FoldOf(s.View, s.Clip);
        if (slices is null) return;
        var ordered = slices.OrderBy(x => x.StartSec).ToArray();
        Assert.Equal(s.Clip.StartSec, ordered[0].StartSec, 6);
        Assert.Equal(s.Clip.EndSec, ordered[^1].EndSec, 6);
        for (var i = 1; i < ordered.Length; i++) Assert.Equal(ordered[i - 1].EndSec, ordered[i].StartSec, 6);
        // The drawn boxes leave no hole over the loop bars (the loop is no repeat: it never splits a clip).
        var loopLeft = s.View.BarX(39);
        var loopRight = s.View.BarX(47);
        var boxes = slices.Select(x => TimelineAudioClips.SliceRect(s.View, x, laneTop)).ToArray();
        for (var x = loopLeft + 1; x < loopRight; x += 7) Assert.Contains(boxes, b => b.Left <= x && x <= b.Right);
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, -2)]
    public void ADroppedLongClipAcrossTheLoopRegionIsOneContinuousClip(bool repeat, int bars) => DropAcrossTheLoop(repeat, bars, 49);

    [AvaloniaFact]
    public void ADroppedLongClipAcrossARepeatInsideTheLoopRegionIsOneContinuousClip() => DropAcrossTheLoop(true, 2, 41);

    private static void DropAcrossTheLoop(bool repeat, int bars, int at)
    {
        var s = Open(repeat, loop: true, at);
        var length = s.Clip.EndSec - s.Clip.StartSec;
        var startBefore = s.Clip.StartSec;
        var box = TimelineAudioClips.ClipBounds(s.View, s.Track, s.Clip);
        var y = (box.Top + box.Bottom) / 2;
        var from = new Point(s.View.BarX(42) + 5, y);
        var to = new Point(s.View.BarX(42 + bars) + 5, y);
        Drag(s, from, to);
        Assert.Equal(startBefore + bars * s.BarSec, s.Clip.StartSec, 3);
        AssertOnePiece(s, length);
        Assert.NotNull(DocumentEdits.Undo(s.Song));
        var restored = s.Song.Project.Tracks.Last().AudioClips.Single();
        Assert.Equal(startBefore, restored.StartSec, 6);
        Assert.Equal(length, restored.SourceLengthSec, 6);
        s.Window.Close();
        s.Song.MarkClean();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDrawnBoxesOfAClipAtAnyPositionCoverItsWrittenSpanWithoutAHole(bool repeat)
    {
        var s = Open(repeat, loop: true);
        var laneTop = s.View.LaneBandTopOf(s.Song.Project.Tracks.IndexOf(s.Track));
        var length = s.Clip.EndSec - s.Clip.StartSec;
        for (var half = 0; half <= 60; half++)
        {
            s.Clip.StartSec = half * s.BarSec / 2;
            var slices = TimelineAudioClips.FoldOf(s.View, s.Clip);
            if (slices is null) continue;
            var boxes = slices.Select(x => TimelineAudioClips.SliceRect(s.View, x, laneTop)).OrderBy(b => b.Left).ToArray();
            var reach = boxes[0].Right;
            foreach (var box in boxes.Skip(1))
            {
                Assert.True(box.Left <= reach + 1.5, $"start {s.Clip.StartSec / s.BarSec} bars: hole between x {reach} and {box.Left}");
                reach = Math.Max(reach, box.Right);
            }
            Assert.Equal(length, s.Clip.EndSec - s.Clip.StartSec, 6);
        }
        s.Window.Close();
        s.Song.MarkClean();
    }
}
