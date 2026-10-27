using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Sections pane scrolling its list when the sections do not fit the pane's height, and showing no scroll bar when they do.
/// Does not own: the row looks (SectionColourPresenterTests).
/// Tests: TabForge.Avalonia.Tests.SectionsPaneScrollTests.
/// </summary>
public class SectionsPaneScrollTests
{
    private static (Window Window, SectionsPanelView Pane) Open(double height, int sections)
    {
        var markers = Enumerable.Range(0, sections).Select(i => new MarkerModel { MeasureIndex = i * 4, Title = "Section " + (i + 1), LengthBars = 4 });
        var pane = new SectionsPanelView(markers);
        var window = new Window { Width = 260, Height = height, Content = pane };
        window.Show();
        window.UpdateLayout();
        return (window, pane);
    }

    private static bool BarShows(SectionsPanelView pane) =>
        pane.GetVisualDescendants().OfType<ScrollBar>().Any(bar => bar.Orientation == global::Avalonia.Layout.Orientation.Vertical && bar.IsVisible);

    [AvaloniaFact]
    public void SmallPaneShowsAScrollBarAndScrollsToTheLastSection()
    {
        var (window, pane) = Open(200, 12);
        var scroll = pane.GetVisualDescendants().OfType<ScrollViewer>().Single();

        Assert.True(BarShows(pane));
        scroll.ScrollToEnd();
        window.UpdateLayout();
        Assert.True(scroll.Offset.Y > 0);
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 3);
    }

    [AvaloniaFact]
    public void LargePaneShowsNoScrollBar()
    {
        var (_, pane) = Open(900, 3);

        Assert.False(BarShows(pane));
    }
}
