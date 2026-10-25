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
        var knob = new Point(view.BarX(end) - 3, TimelineView.RulerHeight - 3);
        Assert.NotNull(view.SectionKnobAt(knob.X, knob.Y));
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
        w.Close();
    }
}
