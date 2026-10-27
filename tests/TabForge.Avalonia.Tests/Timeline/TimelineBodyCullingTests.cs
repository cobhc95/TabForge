using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline body's culling. A track row draws only the bars its scroll viewer shows, and a row wholly outside the track band is not drawn.
/// Does not own: the bar geometry (TimelineGeometryRules), the drawing of one cell (TimelineView.DrawBarCell).
/// Tests: TimelineView.DrawBody (DrawnBarCount against the bars that overlap the viewport, DrawnRowCount).
/// </summary>
public class TimelineBodyCullingTests
{
    [AvaloniaFact]
    public void BodyDrawsOnlyTheBarsTheViewportShows()
    {
        var timeline = new TimelineView(DemoSongFactory.Create(), null);
        var viewer = new ScrollViewer
        {
            Content = timeline,
            Width = 240,
            Height = 400,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var window = new Window { Width = 260, Height = 420, Content = viewer };
        window.Show();
        window.UpdateLayout();
        viewer.Offset = new Vector(timeline.BarsWidth * 0.4, 0);
        window.UpdateLayout();

        var left = viewer.Offset.X;
        var right = left + viewer.Viewport.Width;
        var visibleBars = Enumerable.Range(0, timeline.BarCount)
            .Count(bar => timeline.BarX(bar + 1) - 1 >= left && timeline.BarX(bar) <= right);

        timeline.DrawBody(new NullDrawContext(), false);

        Assert.True(timeline.DrawnRowCount > 0, "The track rows were not drawn.");
        Assert.True(visibleBars < timeline.BarCount, "The viewport should not show every bar, or the check proves nothing.");
        Assert.Equal(visibleBars * timeline.DrawnRowCount, timeline.DrawnBarCount);
        window.Close();
    }
}
