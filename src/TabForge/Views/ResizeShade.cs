using System.Windows;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: drawing the arrangement panel's resize shade (a one-time drawing stretched to the live height).
// Does not own: when the shade shows (ArrangementPanel.BeginResizePreview / EndResizePreview).
// Tests: TestSplitterDragKeepsScore.
/// <summary>The drag shade: the panel drawn once from the song, stretched vertically (rows only) to the live height.</summary>
internal sealed class ResizeShade : FrameworkElement
{
    private readonly System.Windows.Media.Drawing _drawing;
    private readonly IReadOnlyList<(System.Windows.Media.FormattedText Text, double Centre)> _names;
    private readonly double _startHeight, _header;
    public ResizeShade(System.Windows.Media.Drawing drawing, IReadOnlyList<(System.Windows.Media.FormattedText, double)> names, double startHeight, double header)
    { _drawing = drawing; _names = names; _startHeight = startHeight; _header = header; SizeChanged += (_, _) => InvalidateVisual(); }
    protected override void OnRender(System.Windows.Media.DrawingContext dc)
    {
        dc.DrawRectangle(Draw.Solid(System.Windows.Media.Color.FromRgb(0x14, 0x17, 0x1C), 1), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var scale = Math.Max(0.1, (ActualHeight - _header) / Math.Max(1, _startHeight - _header));
        dc.PushClip(new System.Windows.Media.RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.PushTransform(new System.Windows.Media.ScaleTransform(1, scale, 0, _header));
        dc.DrawDrawing(_drawing);
        dc.Pop();
        // Names stay at their real size: only their position follows the stretched rows.
        foreach (var (text, centre) in _names) dc.DrawText(text, new Point(56, _header + (centre - _header) * scale - text.Height / 2));
        dc.Pop();
    }
}
