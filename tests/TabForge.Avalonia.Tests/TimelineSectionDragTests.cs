using System;
using System.IO;
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
/// Owns: the section drag animation: the other sections slide to their new slots over several frames and settle, the drop reorders,
/// and with the animation setting off every position jumps at once. A render of the lifted column goes to %TEMP%\tf-avalonia-tests\TimelineSectionDragTests.
/// Tests: SectionDragAnimator, SectionDragVisual, TimelineInputController.
/// </summary>
public class TimelineSectionDragTests
{
    /// <summary>The clock the slide reads: the test moves it, so the frame times are exact and no real time reaches the animation.</summary>
    private sealed class ManualClock
    {
        public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(1);
    }

    private static (Window Window, TimelineView View, TimelineInputController Input, ScorePreviewEditor.PreviewDocument Doc) Open(bool animate, ManualClock clock)
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, doc, sectionClock: () => clock.Now, sectionTimer: new AvaloniaPlaybackSessionTests.FakeTimer());
        input.SectionAnimator.Enabled = animate;
        var window = new Window { Width = 1300, Height = 400, Content = view };
        window.Show();
        return (window, view, input, doc);
    }

    private static Point DragOverNeighbour(Window w, TimelineView v)
    {
        var slots = v.SectionSlots();
        var from = new Point(slots[0].Left + 4, slots[0].Top + 4);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(from + new Vector(2, 0));
        var to = new Point(slots[1].Left + slots[1].Width - 2, from.Y);
        w.MouseMove(to);
        return to;
    }

    [AvaloniaFact]
    public void NeighbourSlidesOverSeveralFramesAndSettles()
    {
        var clock = new ManualClock();
        var (w, v, input, _) = Open(animate: true, clock);
        var start = v.SectionSlots()[1].Left;
        DragOverNeighbour(w, v);
        var shown = v.SectionPreviewX!;
        Assert.Equal(start, shown[1], 3);
        input.SectionAnimator.Advance(clock.Now);
        var first = shown[1];
        Assert.True(first < start && first > input.SectionAnimator.Targets[1]);
        var frames = 0;
        while (input.SectionAnimator.Advance(clock.Now += TimeSpan.FromMilliseconds(16)) && frames++ < 200) { }
        Assert.Equal(input.SectionAnimator.Targets[1], shown[1], 3);
        Assert.True(frames > 1 && frames < 200);
        var dir = DialogTestKit.RenderDirectory(nameof(TimelineSectionDragTests));
        using (var frame = w.CaptureRenderedFrame()) frame!.Save(Path.Combine(dir, "section-lifted.png"));
        w.Close();
    }

    [AvaloniaFact]
    public void AnimationOffMovesTheNeighbourAtOnce()
    {
        var (w, v, input, _) = Open(animate: false, new ManualClock());
        DragOverNeighbour(w, v);
        Assert.Equal(input.SectionAnimator.Targets[1], v.SectionPreviewX![1], 3);
        w.Close();
    }

    [AvaloniaFact]
    public void DetachMidSlideStopsAndUnhooksTheFrameTimer()
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var timer = new AvaloniaPlaybackSessionTests.FakeTimer();
        var input = new TimelineInputController(view, doc, sectionClock: () => TimeSpan.FromSeconds(1), sectionTimer: timer);
        var window = new Window { Width = 1300, Height = 400, Content = view };
        window.Show();
        DragOverNeighbour(window, view);
        Assert.True(timer.IsEnabled);
        var shown = (double[])input.SectionAnimator.Shown.Clone();
        input.Unwatch();   // what the arrangement does when it leaves the tree
        Assert.False(timer.IsEnabled);
        timer.Fire();
        Assert.Equal(shown, input.SectionAnimator.Shown);
        window.Close();
    }

    [AvaloniaFact]
    public void DropReordersAndTheColumnSettlesIntoItsSlot()
    {
        var clock = new ManualClock();
        var (w, v, input, doc) = Open(animate: true, clock);
        var title = SectionLayout.Sorted(doc.Project)[0].Title;
        var to = DragOverNeighbour(w, v);
        w.MouseUp(to, MouseButton.Left);
        Assert.Equal(title, SectionLayout.Sorted(doc.Project)[1].Title);
        Assert.True(input.SectionAnimator.Settling);
        var frames = 0;
        while (input.SectionAnimator.Advance(clock.Now += TimeSpan.FromMilliseconds(16)) && frames++ < 200) { }
        Assert.Equal(-1, v.DragSection);
        w.Close();
    }

    [AvaloniaFact]
    public void DropRaisesSectionMovedWithTheBarMapping()
    {
        var clock = new ManualClock();
        var (w, v, input, doc) = Open(animate: false, clock);
        TabForge.Documents.IEditableDocument? movedDocument = null;
        int[]? mapping = null;
        input.SectionMoved += (document, map) => { movedDocument = document; mapping = map; };
        var to = DragOverNeighbour(w, v);

        w.MouseUp(to, MouseButton.Left);

        Assert.NotNull(mapping);
        Assert.Same(doc, movedDocument);
        w.Close();
    }
}
