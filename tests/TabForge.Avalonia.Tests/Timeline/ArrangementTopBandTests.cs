using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the arrangement's top band at the default track list width: the transport sits in the list column above the header labels with no gap
/// between them, every transport button is inside the transport, and the header rows stay level with the timeline's rows.
/// Does not own: the transport's wrap and button sizes (TransportWrapRulesTests, TransportBarViewTests), the row scroll (TrackListScrollTests).
/// Tests: TabForge.Avalonia.Tests.ArrangementTopBandTests.
/// </summary>
public class ArrangementTopBandTests
{
    // The transport's own margins (top 1, bottom 2 DIP) are the only space between it and the labels; a larger gap would be dead space.
    private const double MaxGapBelowTransport = 3;

    [AvaloniaFact]
    public void ThereIsNoDeadSpaceBetweenTheTransportAndTheHeaderLabels()
    {
        var (window, view) = Open();
        var transport = view.Transport;
        var transportBottom = transport.TranslatePoint(new Point(0, transport.Bounds.Height), window)!.Value.Y;
        var labelsTop = view.Header.TranslatePoint(new Point(0, TimelineView.RulerHeight), window)!.Value.Y;
        var headerTop = view.Header.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        var rulerTop = view.Timeline.TranslatePoint(new Point(0, 0), window)!.Value.Y;

        Assert.InRange(labelsTop - transportBottom, 0, MaxGapBelowTransport);
        Assert.Equal(rulerTop, headerTop, 3);
        Assert.True(transport.Bounds.Width <= view.Header.ColumnWidth + 0.5, "the transport stays in the track list column");
        window.Close();
    }

    [AvaloniaFact]
    public void EveryTransportButtonIsVisibleAtTheDefaultListWidth()
    {
        var (window, view) = Open();
        var transport = view.Transport;
        var buttons = ((WrapPanel)transport.Child!).Children.OfType<StackPanel>().SelectMany(group => group.Children)
            .OfType<TransportBarView.TransportButton>().ToArray();

        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            var origin = button.TranslatePoint(new Point(0, 0), transport)!.Value;
            Assert.True(origin.X >= -0.5 && origin.Y >= -0.5, $"{button.Item.Name} starts inside the transport");
            Assert.True(origin.X + button.Bounds.Width <= transport.Bounds.Width + 0.5, $"{button.Item.Name} is not clipped on the right");
            Assert.True(origin.Y + button.Bounds.Height <= transport.Bounds.Height + 0.5, $"{button.Item.Name} is not clipped at the bottom");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void HeaderRowsStayLevelWithTheTimelineRowsBelowTheTopBand()
    {
        var (window, view) = Open();
        var timeline = view.Timeline;
        double ScreenY(Visual control, double y) => control.TranslatePoint(new Point(0, y), window)!.Value.Y;

        Assert.True(timeline.RowCount > 0);
        for (var row = 0; row < timeline.RowCount; row++)
            Assert.Equal(ScreenY(timeline, timeline.RowTop(row)), ScreenY(view.Header, timeline.RowTop(row)), 3);
        Assert.True(ScreenY(view.Header, timeline.RowTop(0)) >= ScreenY(view.Transport, view.Transport.Bounds.Height) - 0.5, "the rows start below the transport");
        window.Close();
    }

    // The demo song in a window, at the default track list width.
    private static (Window Window, ArrangementView View) Open()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 1300, Height = 500, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }
}
