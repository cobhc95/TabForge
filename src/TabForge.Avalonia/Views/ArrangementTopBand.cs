using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the arrangement's top band: the transport bar's place above the track list's labels (its width at the list column, or the narrow column of
//   the compact content), the top margin that moves the columns below it, the pane height left for the rows under the band and the Add track strip,
//   the Add track strip's height, and the natural height the pane is capped at.
// Does not own: the transport's controls and wiring (TransportBarView), the rows and their geometry (TimelineView, TrackHeaderColumn), the compact
//   switch (ArrangementView.SetCompact sets Compact), the pane's cap itself (ArrangementView.MaxHeight).
// Tests: TabForge.Avalonia.Tests.ArrangementNaturalHeightTests, TabForge.Avalonia.Tests.AddTrackStripTests, TabForge.Avalonia.Tests.TrackListWidthTests.
// Threads: UI thread only.
public sealed class ArrangementTopBand
{
    /// <summary>The width the transport wraps in while the compact content shows: the live input and transport buttons in one row at their normal size, so it wraps to more rows.</summary>
    public const double CompactTransportWidth = 6 * (TransportBarView.TransportButtonSize + CompactButtonSlack) + 2 * CompactTransportChrome;

    /// <summary>The room a transport button takes beyond its size (its outline and rounding), in DIP.</summary>
    private const double CompactButtonSlack = 2;

    /// <summary>The transport bar's border and padding on one side, in DIP.</summary>
    private const double CompactTransportChrome = 5;

    private readonly TransportBarView _transport;
    private readonly TrackHeaderColumn _header;
    private readonly TimelineView _timeline;
    private readonly AddTrackBar _addTrackBar;
    private readonly Grid _columns;

    /// <param name="columns">The grid of the track list and the timeline; its top margin is the band's height above the labels.</param>
    public ArrangementTopBand(TransportBarView transport, TrackHeaderColumn header, TimelineView timeline, AddTrackBar addTrackBar, Grid columns)
    {
        _transport = transport;
        _header = header;
        _timeline = timeline;
        _addTrackBar = addTrackBar;
        _columns = columns;
    }

    /// <summary>True while the compact content shows (compact mode and the Band view): the transport is a narrow column at the left and the band has no top margin.</summary>
    public bool Compact { get; set; }

    /// <summary>The height the "Add track" strip takes under the track rows (0 when hidden).</summary>
    public double AddTrackBarHeight => _addTrackBar.IsVisible ? AddTrackLane.Height : 0;

    /// <summary>
    /// The height the transport bar takes above the header's labels: the part of the transport that is taller than the ruler band. The
    /// header's labels sit under it at the section strip's height, so the labels stay level with the timeline's section strip.
    /// </summary>
    public double TopBandHeight => Compact ? 0 : Math.Max(0, TransportContentHeight - TimelineView.RulerHeight);

    /// <summary>The transport's own height: Avalonia's DesiredSize includes the Margin, and the top margin is the pad this band adds, so the margin is taken out.</summary>
    private double TransportContentHeight => _transport.DesiredSize.Height - _transport.Margin.Top - _transport.Margin.Bottom;

    /// <summary>The pane's height with every row at base height: the transport bar stacked above the timeline's base content.</summary>
    public double NaturalHeight => Compact
        ? Math.Max(_transport.DesiredSize.Height, _timeline.BaseContentHeight)
        : TopBandHeight + _timeline.BaseContentHeight + AddTrackBarHeight;

    // Every margin write of the band goes through here, so the layout token guard sees one assignment.
    private static void SetMargin(Control control, Thickness margin)
    {
        if (control.Margin != margin) control.Margin = margin;
    }

    /// <summary>
    /// Lays the band out for a pane of <paramref name="paneHeight"/>: the transport is measured at the list column's width and wraps there, the columns
    /// move down by the band, and the rows get the pane height that is left. Writes a property only when its value changes.
    /// </summary>
    public void Layout(double paneHeight)
    {
        // The transport keeps its natural height: a MinHeight would stretch its wrapped row past the button groups. TransportBarView's BarMargin has no top or bottom part, so the pad below supplies the space that closes the gap to the labels.
        if (_transport.MinHeight != 0) _transport.MinHeight = 0;
        if (Compact)
        {
            // Compact content: the transport wraps in its own narrow column at the left and the timeline starts right after it.
            SetMargin(_transport, _transport.Margin.WithTop(0));
            if (_transport.Width != CompactTransportWidth) _transport.Width = CompactTransportWidth;
            _transport.Measure(new Size(CompactTransportWidth, double.PositiveInfinity));
            var left = _transport.Margin.Left + CompactTransportWidth + _transport.Margin.Right;
            SetMargin(_columns, default(Thickness).WithLeft(left));
            _timeline.PaneHeight = paneHeight;
            return;
        }
        var width = Math.Max(0, _header.ColumnWidth - _transport.Margin.Left - _transport.Margin.Right);
        if (_transport.Width != width) _transport.Width = width;
        _transport.Measure(new Size(width, double.PositiveInfinity));
        // A transport shorter than the ruler band is padded from above, so its bottom meets the labels: the pad is the top margin, not a stretch.
        var padTop = Math.Max(0, TimelineView.RulerHeight - TransportContentHeight);
        SetMargin(_transport, _transport.Margin.WithTop(padTop));
        _transport.Measure(new Size(width, double.PositiveInfinity));
        var top = TopBandHeight;
        // The compact content left its transport column's offset on the columns; the full content starts at the left again. Reset the whole margin,
        // not just the top: the top is 0 whenever the transport fits the ruler band, so a top-only change check would keep the compact offset for good.
        SetMargin(_columns, default(Thickness).WithTop(top));
        if (_addTrackBar.ListWidth != _header.ColumnWidth) _addTrackBar.ListWidth = _header.ColumnWidth;
        _timeline.PaneHeight = Math.Max(0, paneHeight - top - AddTrackBarHeight);
    }
}
