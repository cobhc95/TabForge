using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>Independent compositor-translated destination caret for section dragging.</summary>
internal sealed class SectionInsertionIndicator : FrameworkElement
{
    private readonly TranslateTransform _translation = new();
    private Color _color;
    private bool _hasColor;

    public SectionInsertionIndicator()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        RenderTransform = _translation;
    }

    public void SetPreview(TrackTimeline.SectionDragPreview? preview)
    {
        if (preview is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        _translation.X = preview.InsertionX;
        if (!_hasColor || _color != preview.Snapshot.Color)
        {
            _hasColor = true;
            _color = preview.Snapshot.Color;
            InvalidateVisual();
        }
        Visibility = Visibility.Visible;
    }

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("SectionInsertionIndicator"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "SectionInsertionIndicator", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!_hasColor) return;
        var y = ArrangementPanel.RulerHeight - 1;
        var center = y + 2 + (ArrangementPanel.SectionHeight - 4) / 2;
        dc.DrawLine(Draw.Pen(_color, 8, 0.20), new Point(0, y), new Point(0, ActualHeight - 1));
        dc.DrawLine(Draw.Pen(Colors.White, 2.5), new Point(0, y), new Point(0, ActualHeight - 1));
        dc.DrawEllipse(Draw.Solid(_color), Draw.Pen(Colors.White, 1), new Point(0, center), 5, 5);
    }
}
