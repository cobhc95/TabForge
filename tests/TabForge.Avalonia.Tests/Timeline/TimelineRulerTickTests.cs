using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: ruler bar ticks stop at the ruler bottom and the track-lane bar grid starts below the section lane.
public class TimelineRulerTickTests
{
    [AvaloniaFact]
    public void RulerTicksStopAtRulerBottomAndNoVerticalLineCrossesSectionLane()
    {
        var view = new TimelineView(DemoSongFactory.Create()) { Width = 1200, Height = 400 };
        view.Measure(new global::Avalonia.Size(1200, 400));
        view.Arrange(new global::Avalonia.Rect(0, 0, 1200, 400));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.DrawBody(draw, false);

        var ruler = TimelineView.RulerHeight;
        var gridTop = ruler + TimelineView.SectionHeight;
        var vertical = draw.LineOps.Where(l => l.X1 == l.X2 && l.Y1 != l.Y2).ToList();
        Assert.Contains(vertical, l => l.Y1 == ruler - 6 && l.Y2 <= ruler);
        Assert.Contains(vertical, l => l.Y1 >= gridTop);
        Assert.DoesNotContain(vertical, l => l.Y1 < gridTop && l.Y2 > ruler);
    }
}
