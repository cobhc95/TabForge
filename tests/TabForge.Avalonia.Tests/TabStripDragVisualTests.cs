using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the visuals of a tab drag on the strip: the insertion marker is hidden by a rebuild and by a lost capture, and the dragged tab
/// follows the pointer by its render transform. Tests: TabStripView (drag visuals).
/// </summary>
public sealed class TabStripDragVisualTests
{
    private static readonly IReadOnlyList<TabStripDocument> Three = new[]
    {
        new TabStripDocument(null, "First"), new TabStripDocument(null, "Second"), new TabStripDocument(null, "Third"),
    };

    private static (Window Window, TabStripView Strip) OpenStrip()
    {
        var strip = new TabStripView(new TabSettings());
        var window = new Window { Width = 900, Height = 300, Content = strip };
        window.Show();
        strip.Show(Three, 0, 900);
        window.UpdateLayout();
        return (window, strip);
    }

    [AvaloniaFact]
    public void InsertionMarkerIsHiddenByARebuildOfTheStrip()
    {
        var (_, strip) = OpenStrip();
        strip.ShowDropPreview(1, highlightStrip: true);
        Assert.True(strip.IsInsertionMarkerShown);

        strip.Show(Three, 0, 900);

        Assert.False(strip.IsInsertionMarkerShown);
        Assert.False(strip.IsDropHighlightShown);
    }

    [AvaloniaFact]
    public void InsertionMarkerIsHiddenWhenTheDragsCaptureIsLost()
    {
        var (window, strip) = OpenStrip();
        var tab = strip.Tabs[0];
        IPointer? pointer = null;
        tab.PointerPressed += (_, e) => pointer = e.Pointer;
        var cancelled = 0;
        strip.TabDragCancelled += () => cancelled++;
        var start = tab.TranslatePoint(new Point(20, tab.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 40, start.Y), RawInputModifiers.LeftMouseButton);
        strip.ShowDropPreview(1, highlightStrip: false);
        Assert.True(strip.IsInsertionMarkerShown);

        pointer!.Capture(null);

        Assert.Equal(1, cancelled);
        Assert.False(strip.IsInsertionMarkerShown);
    }

    [AvaloniaFact]
    public void InsertionSlotAndReorderSlideUseTheStripGeometry()
    {
        var (_, strip) = OpenStrip();
        var tabs = strip.Tabs;
        var leftOfSecond = tabs[1].PointToScreen(new Point(1, 0)).X;
        Assert.Equal(1, strip.InsertionSlot(leftOfSecond));

        strip.SlideForReorder(fromIndex: 0, slot: 2, animate: false);

        var transform = Assert.IsType<TranslateTransform>(tabs[1].RenderTransform);
        Assert.Equal(-(tabs[0].Bounds.Width + TabStripView.TabSpacing), transform.X, 3);
    }

    [AvaloniaFact]
    public void DraggedTabFollowsThePointerByTheSameDistance()
    {
        var (window, strip) = OpenStrip();
        var tab = strip.Tabs[0];
        var start = tab.TranslatePoint(new Point(20, tab.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 40, start.Y), RawInputModifiers.LeftMouseButton);

        var transform = Assert.IsType<TranslateTransform>(tab.RenderTransform);
        Assert.InRange(transform.X, 39, 41);
        Assert.Equal(transform.X, strip.DraggedTabOffset, 3);
        window.MouseUp(new Point(start.X + 40, start.Y), MouseButton.Left);
    }
}
