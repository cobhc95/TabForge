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

// Owns: dragging an audio or MIDI clip sideways in its own lane across a repeat with three endings (bars 5-6, then [1.] 7, [2.] 8, [3.] 9, 1-based): the clip
//   moves by the exact performed-time delta of the grabbed pass, stays one continuous piece (length, source, contiguous slices) and one undo step restores it.
// Does not own: the fold itself (RepeatViewTests) or the drag gestures on plain songs (TimelineAudioClipTests).
// Tests: this class.
public sealed class ClipDragRepeatTests
{
    // 0-based: bars 4,5 repeat; bar 6 is [1.], bar 7 is [2.], bar 8 is [3.]; bars 9.. play once. Order: 0 1 2 3 | 4 5 6 | 4 5 7 | 4 5 8 | 9 ...
    private sealed record Setup(SongDocument Song, Window Window, TimelineView View, TrackModel Track, AudioClip Clip, double BarSec);

    private static Setup Open(Func<double, AudioClip> clipOf, bool snap)
    {
        var project = TemplateFactory.Blank();
        foreach (var t in project.Tracks) t.Measures = t.Measures.Take(16).ToList();
        var m = project.Tracks[0].Measures;
        m[4].RepeatStart = true;
        (m[6].AlternateEndingMask, m[6].RepeatEnd) = (1, true);
        (m[7].AlternateEndingMask, m[7].RepeatEnd) = (2, true);
        m[8].AlternateEndingMask = 4;
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(track);
        var barSec = AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        var clip = clipOf(barSec);
        track.AudioClips.Add(clip);
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        var view = new TimelineView(project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Snap = new SnapSettings { Enabled = snap } };
        _ = new TimelineInputController(view, song);
        var window = new Window { Width = 1800, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return new Setup(song, window, view, track, clip, barSec);
    }

    private static AudioClip Audio(double start, double length) =>
        new() { File = "take.wav", Name = "take", StartSec = start, SourceLengthSec = length, FileLengthSec = length };

    private static double PlayStart(TimelineView view, int bar, int play) =>
        SongExtent.Timing(view.Song).Performed.Where(b => b.Bar == bar).ElementAt(play).StartMs / 1000.0;

    private static Point Centre(TimelineView view, int bar, double y) => new((view.BarX(bar) + view.BarX(bar + 1)) / 2, y);

    private static void Drag(Setup s, Point from, Point to)
    {
        Point Win(Point p) => s.View.TranslatePoint(p, s.Window)!.Value;
        s.Window.MouseDown(Win(from), MouseButton.Left);
        for (var i = 1; i <= 10; i++) s.Window.MouseMove(Win(from + (to - from) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(Win(to), MouseButton.Left);
    }

    private static void AssertContinuous(TimelineView view, AudioClip clip, double length)
    {
        Assert.Equal(length, clip.EndSec - clip.StartSec, 6);
        var slices = TimelineAudioClips.FoldOf(view, clip);
        if (slices is not null)
        {
            // The drawn slices cover the performed span of the clip end to end: no gap, no overlap, no stretch.
            Assert.Equal(clip.StartSec, slices.Min(x => x.StartSec), 6);
            Assert.Equal(clip.EndSec, slices.Max(x => x.EndSec), 6);
            var ordered = slices.OrderBy(x => x.StartSec).ToArray();
            for (var i = 1; i < ordered.Length; i++) Assert.Equal(ordered[i - 1].EndSec, ordered[i].StartSec, 6);
        }
        // Playback maps the source onto the timeline in one piece: the same offset and length as before the move.
        var piece = Assert.Single(ClipLoop.Pieces(clip));
        Assert.Equal(clip.StartSec, piece.StartSec, 9);
        Assert.Equal(clip.OffsetSec, piece.OffsetSec, 9);
        Assert.Equal(length, piece.SourceLengthSec, 6);
    }

    private static double MidY(Setup s)
    {
        var box = TimelineAudioClips.ClipBounds(s.View, s.Track, s.Clip);
        return (box.Top + box.Bottom) / 2;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrabbingTheTailAndDraggingIntoTheRepeatMovesByTheExactPerformedDelta(bool midi)
    {
        var s = Open(bar => midi
            ? new AudioClip { File = "", Name = "m", StartSec = 6 * bar, SourceLengthSec = 14 * bar, FileLengthSec = 14 * bar, Notes = new() }
            : Audio(6 * bar, 14 * bar), snap: false);
        var length = s.Clip.EndSec - s.Clip.StartSec;
        var startBefore = s.Clip.StartSec;
        var y = MidY(s);
        var grabbed = PlayStart(s.View, 11, 0) + s.BarSec / 2;   // bar 12, played once, after the repeat
        Drag(s, Centre(s.View, 11, y), Centre(s.View, 5, y));   // the pointer lands in a repeated bar
        // The grabbed time follows the pointer along the grabbed pass: six bars left of the grab, whatever the repeat does in between.
        var expected = -6 * s.BarSec;
        Assert.Equal(startBefore + expected, s.Clip.StartSec, 6);
        Assert.True(grabbed > 0);
        AssertContinuous(s.View, s.Clip, length);
        var id = s.Clip.Id;
        Assert.NotNull(DocumentEdits.Undo(s.Song));
        var restored = s.Song.Project.Tracks.Last().AudioClips.Single(c => c.Id == id);
        Assert.Equal(startBefore, restored.StartSec, 9);
        Assert.Equal(length, restored.SourceLengthSec, 9);
        s.Window.Close();
        s.Song.MarkClean();
    }

    [AvaloniaFact]
    public void DraggingInsideOnePassOfTheRepeatMovesByTheSameBarsWhateverThePass()
    {
        var s = Open(bar => Audio(1 * bar, 12 * bar), snap: false);
        var length = s.Clip.EndSec - s.Clip.StartSec;
        var startBefore = s.Clip.StartSec;
        var slices = TimelineAudioClips.FoldOf(s.View, s.Clip)!;
        Assert.Contains(slices, x => x.IsPart);
        // Grab bar 6 on the second pass row and drag one bar left, to bar 5 on the same row: exactly one bar earlier in performed time.
        var laneTop = s.View.LaneBandTopOf(s.Song.Project.Tracks.IndexOf(s.Track));
        var row = TimelineAudioClips.SliceRect(s.View, slices.First(x => x.IsPart && x.PartIndex == 1), laneTop);
        var y = row.Y + row.Height / 2;
        Drag(s, Centre(s.View, 5, y), Centre(s.View, 4, y));
        Assert.Equal(startBefore - s.BarSec, s.Clip.StartSec, 6);
        AssertContinuous(s.View, s.Clip, length);
        Assert.NotNull(DocumentEdits.Undo(s.Song));
        s.Window.Close();
        s.Song.MarkClean();
    }

    // The written position of x in bars (bar + fraction), so a pointer distance is a bar count whatever the bar widths.
    private static double BarsAt(TimelineView view, double x)
    {
        var bar = Math.Clamp(view.BarAtX(x), 0, view.BarCount - 1);
        return bar + (x - view.BarX(bar)) / (view.BarX(bar + 1) - view.BarX(bar));
    }

    [AvaloniaTheory]
    [InlineData(3, false)]   // the bar before the repeat start
    [InlineData(4, false)]   // the repeat start
    [InlineData(5, true)]    // the repeat end side of the shared bars
    [InlineData(6, false)]   // volta 1.
    [InlineData(6, true)]
    [InlineData(7, false)]   // volta 2.
    [InlineData(7, true)]
    [InlineData(8, false)]   // volta 3.
    public void FineDragsAcrossRepeatSignsAndVoltasMoveByThePointerDistanceAndNeverJump(int bar, bool rightEdge)
    {
        var s = Open(barSec => Audio(1 * barSec, 12 * barSec), snap: false);
        var startBefore = s.Clip.StartSec;
        var length = s.Clip.EndSec - startBefore;
        var slices = TimelineAudioClips.FoldOf(s.View, s.Clip)!;
        var laneTop = s.View.LaneBandTopOf(s.Song.Project.Tracks.IndexOf(s.Track));
        var x = rightEdge ? s.View.BarX(bar + 1) - 2 : s.View.BarX(bar) + 2;
        var half = (s.View.BarX(bar + 1) - s.View.BarX(bar)) / 2;
        var pressed = 0;
        foreach (var pass in new[] { 0, 1, 2 })   // the pass rows of the folded clip (a volta bar sits in the row of its pass)
        {
            var hits = slices.Where(c => c.IsPart && c.PartIndex == pass &&
                TimelineAudioClips.SliceRect(s.View, c, laneTop).Left <= x && x < TimelineAudioClips.SliceRect(s.View, c, laneTop).Right).ToArray();
            var inFold = slices.Any(c => c.IsPart && TimelineAudioClips.SliceRect(s.View, c, laneTop).Left <= x && x < TimelineAudioClips.SliceRect(s.View, c, laneTop).Right);
            if (hits.Length == 0 && (pass == 1 || inFold)) continue;
            var y = MidY(s);   // outside the folded bars the clip has its one main row
            if (hits.Length > 0)
            {
                var rect = TimelineAudioClips.SliceRect(s.View, hits[0], laneTop);
                y = rect.Y + rect.Height / 2;
            }
            foreach (var dx in new[] { 1, -1, 3, -3, 5, -5, 12, -12, half, -half })
            {
                s.Clip.StartSec = startBefore;
                Drag(s, new Point(x, y), new Point(x + dx, y));
                // Below the drag threshold nothing moves; past it the clip moves by the pointer's distance in bars, never to another pass or to 0.
                var expected = Math.Abs(dx) < 4 ? 0 : (BarsAt(s.View, x + dx) - BarsAt(s.View, x)) * s.BarSec;
                Assert.True(Math.Abs(s.Clip.StartSec - startBefore - expected) < 0.01,
                    $"bar {bar} pass {pass + 1} dx {dx}: moved {s.Clip.StartSec - startBefore}, expected {expected}");
                Assert.True(s.Clip.StartSec > 0);
                AssertContinuous(s.View, s.Clip, length);
            }
            pressed++;
        }
        Assert.True(pressed > 0, "the press must land on a pass row");
        s.Window.Close();
        s.Song.MarkClean();
    }

    [AvaloniaFact]
    public void SnappingAClipThatSitsInALaterPassKeepsThatPass()
    {
        // A two-bar clip on the third pass of the repeat (bars 5-6, 1-based), the grid on: one bar to the right keeps it in pass 3, never back in pass 1.
        var s = Open(bar => Audio(0, 2 * bar), snap: true);
        var passThree = PlayStart(s.View, 4, 2);
        s.Clip.StartSec = passThree;
        Drag(s, Centre(s.View, 4, MidY(s)), Centre(s.View, 5, MidY(s)));
        Assert.Equal(passThree + s.BarSec, s.Clip.StartSec, 6);
        AssertContinuous(s.View, s.Clip, 2 * s.BarSec);
        s.Window.Close();
        s.Song.MarkClean();
    }
}
