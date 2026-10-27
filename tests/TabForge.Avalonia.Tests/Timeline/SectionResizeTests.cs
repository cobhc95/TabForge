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
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: dragging a section's edge knob on the ruler: the clamped resize rule (right edge, neighbour clamp, one-bar minimum), the live preview, and the single undo step.
/// Tests: SectionLayout.ResizeClamped, SectionKnobs, TimelineInputController (section resize).
/// </summary>
public class SectionResizeTests
{
    private static (TabForge.Models.SongProject Song, TabForge.Models.MarkerModel A, TabForge.Models.MarkerModel B) Gapped()
    {
        var song = DemoSongFactory.Create();
        var sorted = SectionLayout.Sorted(song);
        Assert.True(sorted.Count >= 2);
        SectionLayout.Freeze(song);
        return (song, sorted[0], sorted[1]);
    }

    [Fact]
    public void RightEdgeShrinksLeavingAGapAndMovesNothingElse()
    {
        var (song, a, b) = Gapped();
        var bStart = b.MeasureIndex;
        var start = a.MeasureIndex;
        Assert.True(SectionLayout.ResizeClamped(song, a, rightEdge: true, start + 1));
        SectionLayout.TryGetBounds(song, a, out var s, out var e);
        Assert.Equal((start, start + 1), (s, e));
        Assert.Equal(bStart, b.MeasureIndex);
    }

    [Fact]
    public void RightEdgeClampsAtTheNeighbourAndNeverBelowOneBar()
    {
        var (song, a, b) = Gapped();
        Assert.True(SectionLayout.ResizeClamped(song, a, true, a.MeasureIndex + 1));
        Assert.False(SectionLayout.ResizeClamped(song, a, true, a.MeasureIndex - 5));   // min 1 bar: already there
        Assert.True(SectionLayout.ResizeClamped(song, a, true, 10_000));
        SectionLayout.TryGetBounds(song, a, out _, out var end);
        Assert.Equal(b.MeasureIndex, end);   // never over the neighbour
    }

    [Fact]
    public void LeftEdgeClampsAtThePreviousSectionAndKeepsTheEnd()
    {
        var (song, a, b) = Gapped();
        SectionLayout.TryGetBounds(song, b, out _, out var endBefore);
        Assert.False(SectionLayout.ResizeClamped(song, b, false, 0));   // already against the previous end
        Assert.True(SectionLayout.ResizeClamped(song, b, false, endBefore - 1));
        SectionLayout.TryGetBounds(song, b, out var s, out var e);
        Assert.Equal((endBefore - 1, endBefore), (s, e));
        Assert.True(SectionLayout.ResizeClamped(song, b, false, -3));
        SectionLayout.TryGetBounds(song, a, out _, out var aEnd);
        SectionLayout.TryGetBounds(song, b, out var bs, out _);
        Assert.Equal(aEnd, bs);
    }

    [AvaloniaFact]
    public void DraggingTheRightKnobResizesInOneUndoStepWithAPreview()
    {
        var song = DemoSongFactory.Create();
        SectionLayout.Freeze(song);
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, doc, sectionTimer: new AvaloniaPlaybackSessionTests.FakeTimer());
        var w = new Window { Width = 1300, Height = 400, Content = view };
        w.Show();
        var first = SectionLayout.Sorted(song)[0];
        SectionLayout.TryGetBounds(song, first, out var start, out var end);
        Assert.True(end - start >= 2, "demo first section needs two bars");
        var knob = new Point(view.BarX(end) - 3, TimelineView.RulerHeight + 10);
        Assert.NotNull(view.SectionEdgeAt(knob.X, knob.Y));
        w.MouseMove(knob);
        Assert.NotNull(view.Cursor);
        w.MouseDown(knob, MouseButton.Left);
        w.MouseMove(knob + new Vector(-(view.BarX(end) - view.BarX(end - 1)) - 1, 0));
        Assert.Equal((0, start, end - 1), view.SectionResizePreview);
        Assert.Equal(0, doc.Undo.UndoCount);   // only a preview until the release
        w.MouseUp(new Point(view.BarX(end - 1) - 1, knob.Y), MouseButton.Left);
        Assert.Null(view.SectionResizePreview);
        SectionLayout.TryGetBounds(song, SectionLayout.Sorted(song)[0], out var s2, out var e2);
        Assert.Equal((start, end - 1), (s2, e2));
        Assert.Equal(1, doc.Undo.UndoCount);
        w.Close();
    }

    [AvaloniaFact]
    public void ResizeCursorIsOnlyOverAKnob()
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, doc, sectionTimer: new AvaloniaPlaybackSessionTests.FakeTimer());
        var w = new Window { Width = 1300, Height = 400, Content = view };
        w.Show();
        var slot = view.SectionSlots()[0];
        w.MouseMove(new Point(slot.Left + slot.Width / 2, slot.Top + 6));
        Assert.Null(view.Cursor);
        Assert.Null(view.SectionEdgeAt(slot.Left + slot.Width / 2, slot.Top + 6));
        w.Close();
    }

    // The old bug: a neighbour with an implied length stretched into the freed bars. No Freeze here on purpose.
    private static (TabForge.Models.SongProject Song, TabForge.Models.MarkerModel Prev, TabForge.Models.MarkerModel V) Unfrozen()
    {
        var song = DemoSongFactory.Create();
        var sorted = SectionLayout.Sorted(song);
        var i = sorted.FindIndex(m => SectionLayout.TryGetBounds(song, m, out var s, out var e) && e - s >= 2 && sorted.IndexOf(m) > 0);
        Assert.True(i > 0);
        return (song, sorted[i - 1], sorted[i]);
    }

    [Fact]
    public void LeftEdgeRightLeavesAGapAndNeverStretchesThePreviousSection()
    {
        var (song, prev, v) = Unfrozen();
        SectionLayout.TryGetBounds(song, prev, out var ps, out var pe);
        Assert.True(SectionLayout.ResizeClamped(song, v, false, v.MeasureIndex + 1));
        SectionLayout.TryGetBounds(song, prev, out var ps2, out var pe2);
        Assert.Equal((ps, pe), (ps2, pe2));
        SectionLayout.TryGetBounds(song, v, out var vs, out _);
        Assert.True(vs > pe2);   // gap left
    }

    [Fact]
    public void RightEdgeLeftNeverMovesTheFollowingSection()
    {
        var song = DemoSongFactory.Create();
        var sorted = SectionLayout.Sorted(song);
        var a = sorted.First(m => SectionLayout.TryGetBounds(song, m, out var s, out var e) && e - s >= 2 && sorted.IndexOf(m) < sorted.Count - 1);
        var next = sorted[sorted.IndexOf(a) + 1];
        SectionLayout.TryGetBounds(song, next, out var ns, out var ne);
        Assert.True(SectionLayout.ResizeClamped(song, a, true, a.MeasureIndex + 1));
        SectionLayout.TryGetBounds(song, next, out var ns2, out var ne2);
        Assert.Equal((ns, ne), (ns2, ne2));
    }

    [Fact]
    public void ResizeNeverCrossesNeighbourOrBelowOneBar()
    {
        var (song, prev, v) = Unfrozen();
        SectionLayout.TryGetBounds(song, prev, out _, out var pe);
        SectionLayout.ResizeClamped(song, v, false, -50);
        Assert.Equal(pe, v.MeasureIndex);
        SectionLayout.TryGetBounds(song, v, out var vs, out var ve);
        SectionLayout.ResizeClamped(song, v, false, 10_000);
        SectionLayout.TryGetBounds(song, v, out var s2, out var e2);
        Assert.Equal(ve, e2);
        Assert.Equal(1, e2 - s2);
    }
}
