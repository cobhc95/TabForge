using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: the arrangement panel's resize preview: hiding the real rows and showing the ResizeShade while the dock splitter is dragged.
// Does not own: drawing the shade (ResizeShade) or when a splitter drag starts and ends (TrackListFitController).
// Tests: TestSplitterDragKeepsScore.
internal sealed class ResizePreviewController
{
    private readonly Grid _g;
    private readonly ScrollViewer _scroll;
    private readonly TrackTimeline _timeline;
    private ResizeShade? _shade;
    private readonly List<UIElement> _hiddenForResize = new();

    public ResizePreviewController(Grid panel, ScrollViewer scroll, TrackTimeline timeline) { _g = panel; _scroll = scroll; _timeline = timeline; }

    /// <summary>
    /// While the dock splitter above the panel is dragged, the real rows are collapsed and a light shade is drawn instead: one
    /// translucent band per track in its colour, scaled with the live height (like the section-drag shadow). A drag step costs
    /// one tiny redraw, no layout; the real rows come back once, at the final size, when the drag ends.
    /// </summary>
    public void Begin()
    {
        var project = _timeline.Project;
        if (_shade is not null || project is null || _g.ActualHeight < 1) return;
        var header = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var listWidth = _g.ColumnDefinitions.Count > 0 ? _g.ColumnDefinitions[0].ActualWidth : ArrangementPanel.ControlsWidth;
        var scroll = _scroll.HorizontalOffset;
        var viewWidth = Math.Max(0, _g.ActualWidth - listWidth);
        // Built once from the song (not from the controls): per track a band with its name, and in the timeline one cell per
        // visible bar, filled where the bar has notes; drawn later with a vertical scale only.
        var drawing = new System.Windows.Media.DrawingGroup();
        var names = new List<(System.Windows.Media.FormattedText Text, double Centre)>();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Draw.Solid(System.Windows.Media.Color.FromRgb(0x22, 0x26, 0x2D), 1), null, new Rect(0, 0, _g.ActualWidth, header));
            var top = header;
            var typeface = new System.Windows.Media.Typeface("Segoe UI");
            for (var t = 0; t < project.Tracks.Count; t++)
            {
                var track = project.Tracks[t];
                var h = ArrangementPanel.RowHeightOf(project, track);
                var colour = Draw.Tame(TrackControlWidgets.ParseColour(track.ColorHex));
                dc.DrawRectangle(Draw.Solid(colour, 0.16), null, new Rect(0, top, _g.ActualWidth, h));
                names.Add((new System.Windows.Media.FormattedText(track.Name ?? "", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, 12, Draw.Solid(System.Windows.Media.Colors.White, 0.55), 1.0), top + h / 2));
                for (var bar = 0; bar < track.Measures.Count; bar++)
                {
                    var x = listWidth + _timeline.XOfBar(bar) - scroll; var x2 = listWidth + _timeline.XOfBar(bar + 1) - scroll;
                    if (x2 < listWidth) continue; if (x > _g.ActualWidth) break;
                    var filled = track.Measures[bar].Cells.Any(c => c.Notes.Count > 0);
                    dc.DrawRoundedRectangle(Draw.Solid(colour, filled ? 0.55 : 0.12), null, new Rect(Math.Max(listWidth, x + 1), top + 2, Math.Max(0, x2 - x - 2), Math.Max(1, h - 4)), 3, 3);
                }
                top += h;
            }
        }
        drawing.Freeze();
        foreach (UIElement child in _g.Children) { if (child.Visibility == Visibility.Visible) { child.Visibility = Visibility.Collapsed; _hiddenForResize.Add(child); } }
        _shade = new ResizeShade(drawing, names, _g.ActualHeight, header) { IsHitTestVisible = false };
        Grid.SetColumnSpan(_shade, Math.Max(1, _g.ColumnDefinitions.Count)); Grid.SetRowSpan(_shade, Math.Max(1, _g.RowDefinitions.Count));
        _g.Children.Add(_shade);
    }

    /// <summary>Ends the resize preview: the real rows come back (laid out once at the final size).</summary>
    public void End()
    {
        if (_shade is null) return;
        _g.Children.Remove(_shade);
        _shade = null;
        foreach (var child in _hiddenForResize) child.Visibility = Visibility.Visible;
        _hiddenForResize.Clear();
    }
}
