using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: building the arrangement's controls and the structure/layout that joins them: the timeline, its header column, the track-row stack (through
//   ArrangementTrackRows: row source, armed sub-row layer and row scroll bar), the input controller, the name editor, the sideways viewer, the top
//   band and the splitter, in the columns/frame/body tree and child order the view uses.
// Does not own: the view's behaviour wiring (event subscriptions, attach/detach, playback, wheel and splitter drag callbacks, the transport's
//   command host) — that stays in ArrangementView — nor the drawing or the edits of those controls.
// Tests: TabForge.Avalonia.Tests.ArrangementViewTests, TabForge.Avalonia.Tests.ArrangementTopBandTests.
internal static class ArrangementComposition
{
    /// <summary>The controls one composition built, in the order the view's readonly fields expect them.</summary>
    internal sealed record Parts(
        TimelineView Timeline,
        TrackHeaderColumn Header,
        ArrangementTrackRows TrackRows,
        TimelineInputController Input,
        TrackNameEditor Names,
        ScrollViewer Sideways,
        ArrangementTopBand TopBand,
        DockSplitter ListSplitter,
        Control Content);

    /// <summary>Builds the controls and lays them out; the transport and the Add track bar are the view's own existing controls, placed into the
    /// tree here exactly as the constructor did.</summary>
    public static Parts Build(ArrangementView view, SongProject song, IEditableDocument document, ContextMenuHost? contextMenus, SharedMeterTick? meterTick = null)
    {
        var transport = view.Transport;
        var addTrackBar = view.AddTrackBar;
        var timeline = new TimelineView(song, contextMenus, document.Selection) { Snap = transport.Snap };
        if (document is SongDocument shown) timeline.Media = shown.Media;   // the clips read their files in the document media scope
        var header = new TrackHeaderColumn(timeline, song);
        var input = new TimelineInputController(timeline, document, header);
        var names = new TrackNameEditor(timeline, header);
        // The viewer's own bar is hidden (not disabled, so the offset still moves); the footer bar in the status bar drives it instead.
        var sideways = new ScrollViewer { Content = timeline, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Columns: the track list, the 1 px divider (the splitter's line; its wider grab area overlaps the columns either side), the timeline,
        // then the track rows' scroll bar at the timeline's right edge (collapsed when the rows fit).
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions($"Auto,{ArrangementView.DividerWidth},*,Auto") };
        var topBand = new ArrangementTopBand(transport, header, timeline, addTrackBar, columns);
        var trackRows = new ArrangementTrackRows(timeline, header, document, () => topBand.NaturalHeight, value => { if (view.MaxHeight != value) view.MaxHeight = value; }, meterTick);
        Grid.SetColumn(sideways, 2);
        var splitter = new DockSplitter(horizontalSplit: true);
        Grid.SetColumn(splitter, 1);
        var grabOverlap = (DockSplitter.BarThickness - ArrangementView.DividerWidth) / 2;
        splitter.Margin = new Thickness(-grabOverlap, 0, -grabOverlap, 0);
        Grid.SetColumn(trackRows.Bar, 3);
        // The header and the timeline both start at the top of the row, so a row N of the header lines up with lane N whatever the pane height.
        header.VerticalAlignment = VerticalAlignment.Top;
        timeline.VerticalAlignment = VerticalAlignment.Top;
        // Left-aligned in the viewer, so a song shorter than the pane starts next to the track list instead of floating in the middle.
        timeline.HorizontalAlignment = HorizontalAlignment.Left;
        sideways.VerticalAlignment = VerticalAlignment.Top;
        columns.Children.Add(header);
        columns.Children.Add(trackRows.Armed);   // over the header, so the armed sub-rows' controls take their own presses
        columns.Children.Add(trackRows.Bar);
        columns.Children.Add(sideways);
        columns.Children.Add(splitter);   // after the timeline, so its grab area over the timeline's first pixels takes the pointer
        // The transport bar sits in the top slot of the track list column, beside the ruler and section strip (the controls header). It is as
        // wide as the list and wraps inside it; the columns start below the part of it that rises above the header's labels (ArrangementTopBand).
        transport.VerticalAlignment = VerticalAlignment.Top;
        transport.HorizontalAlignment = HorizontalAlignment.Left;
        columns.Children.Add(names.Box);
        var frame = new Grid();
        frame.Children.Add(columns);
        frame.Children.Add(transport);   // last, so the transport is drawn and hit over the list column's top band
        // The Add track strip is the row under both the track list and the timeline, full width, so the rows end exactly at its top.
        Grid.SetRow(addTrackBar, 1);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        body.Children.Add(frame);
        body.Children.Add(addTrackBar);
        // The track rows scroll inside the arrangement (see TimelineView.RowScrollOffset), so the outer viewer never scrolls vertically.
        var content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return new Parts(timeline, header, trackRows, input, names, sideways, topBand, splitter, content);
    }
}
