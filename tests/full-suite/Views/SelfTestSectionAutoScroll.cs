using System.Windows;
using System.Windows.Controls;
using TabForge.Views;

namespace TabForge;

/// <summary>A section dragged to the edge of the timeline view scrolls it that way; away from the edges it does not.</summary>
public static partial class SelfTest
{
    private static void TestSectionAutoScroll()
    {
        var scroll = new ScrollViewer { Width = 400, Height = 100, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = new Border { Width = 5000, Height = 50 } };
        scroll.Measure(new Size(400, 100)); scroll.Arrange(new Rect(0, 0, 400, 100)); scroll.UpdateLayout();
        scroll.ScrollToHorizontalOffset(1000); scroll.UpdateLayout();
        var follow = new SectionAutoScrollController(scroll, new TrackTimeline());
        follow.Track(1000 + 200);
        follow.Advance();
        Check("section auto-scroll: a pointer mid-view does not scroll", Math.Abs(scroll.HorizontalOffset - 1000) < 0.5, $"{scroll.HorizontalOffset}");
        follow.Track(1000 + 395);
        follow.Advance(); scroll.UpdateLayout();
        Check("section auto-scroll: a pointer at the right edge scrolls right", scroll.HorizontalOffset > 1010, $"{scroll.HorizontalOffset}");
        var after = scroll.HorizontalOffset;
        follow.Track(after + 5);
        follow.Advance(); scroll.UpdateLayout();
        Check("section auto-scroll: a pointer at the left edge scrolls left", scroll.HorizontalOffset < after - 10, $"{after} -> {scroll.HorizontalOffset}");
        follow.Track(null);
        follow.Track(null);
    }
}
