using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the timeline's selected-row cue (the optional shade and the playhead arrow). Tests: TimelineView.SelectedRow, TimelinePlayheadMarks.</summary>
public class TimelineSelectedRowTests
{
    [AvaloniaFact]
    public void ArrowSitsInTheSelectedRowBand()
    {
        var view = new TimelineView(DemoSongFactory.Create()) { Width = 1300, Height = 400 };
        view.SelectedRow = 1;
        var top = view.ArrowLaneTop();
        var centre = TimelinePlayheadMarks.ArrowCentreY(top, TimelineView.DefaultRowHeight);
        Assert.Equal(view.RowTop(1), top);
        Assert.InRange(centre, view.RowTop(1), view.RowTop(1) + TimelineView.DefaultRowHeight);
    }

    [Fact]
    public void ThePlayheadLabelSitsUnderTheBarNumbersAndAboveTheRulerEdge()
    {
        Assert.Equal(TimelinePlayheadMarks.BarNumberTop + TimelinePlayheadMarks.BarNumberSize + TimelinePlayheadMarks.PlayheadLabelGap, TimelinePlayheadMarks.PlayheadLabelTop);
        Assert.True(TimelinePlayheadMarks.PlayheadLabelTop + TimelinePlayheadMarks.BarNumberSize <= TimelineView.RulerHeight);
    }

    [AvaloniaFact]
    public void ChangingSelectedRowKeepsValueAndNoSelectionHidesArrow()
    {
        var view = new TimelineView(DemoSongFactory.Create());
        view.SelectedRow = 0;
        view.SelectedRow = 0;
        Assert.Equal(0, view.SelectedRow);
        view.SelectedRow = -1;
        Assert.Equal(-1, view.ArrowLaneTop());
    }

    [AvaloniaFact]
    public void TheSelectedTrackShadeIsDrawnInTheTrackListOnlyAndTheLanesStayUntouched()
    {
        Assert.False(new TabForge.Services.TimelineSettings().ShadeSelectedTrack);
        var view = new TimelineView(DemoSongFactory.Create()) { Width = 600, Height = 300 };
        Assert.False(view.ShadeSelectedTrack);
        var window = new global::Avalonia.Controls.Window { Width = 600, Height = 300, Content = view };
        window.Show();
        view.SelectedRow = 1;
        var unshaded = Render(view);
        view.ShadeSelectedTrack = true;
        var shaded = Render(view);
        Assert.Equal(unshaded, shaded);   // the shade lives in TrackHeaderColumn (HoverShade.RowOverlay); the lanes are not shaded
        view.ShadeSelectedTrack = false;
        Assert.Equal(unshaded, Render(view));
        window.Close();
    }

    private static byte[] Render(TimelineView view)
    {
        view.UpdateLayout();
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize(600, 300), new global::Avalonia.Vector(96, 96));
        bitmap.Render(view);
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
