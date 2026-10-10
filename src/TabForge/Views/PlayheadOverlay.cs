using System.Windows;
using System.Windows.Media;
using TabForge.Views.Rendering;

namespace TabForge.Views;

/// <summary>
/// A lightweight overlay that draws only the playback caret. It sits above the score in the same
/// coordinate space, so the caret can move every UI tick without repainting the (expensive) score
/// page, and it can never lag the audio by a repaint cadence.
/// </summary>
public sealed class PlayheadOverlay : FrameworkElement
{
    private (double X, double Top, double Bottom)? _geometry;
    private (double X, double EndX, double Top, double Bottom)[] _durationGeometries = Array.Empty<(double, double, double, double)>();
    private Color _color = Color.FromRgb(0x3F, 0xB9, 0x50);
    private Color _durationColor = Color.FromRgb(0x3F, 0xB9, 0x50);
    private double _durationOpacity = 0.1;
    private double _thickness = 1.7;
    private bool _durationEnabled = true;
    public Color CurrentColor => _color;

    public PlayheadOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public void SetGeometry((double X, double Top, double Bottom)? geometry)
    {
        if (_geometry is { } a && geometry is { } b &&
            Math.Abs(a.X - b.X) < 0.2 && Math.Abs(a.Top - b.Top) < 0.2 && Math.Abs(a.Bottom - b.Bottom) < 0.2)
            return;
        _geometry = geometry;
        InvalidateVisual();
    }

    public void SetColor(Color color)
    {
        if (_color == color) return;
        _color = color;
        InvalidateVisual();
    }

    public void SetDurationGeometry((double X, double EndX, double Top, double Bottom)? geometry)
    {
        SetDurationGeometries(geometry is { } value ? new[] { value } : Array.Empty<(double, double, double, double)>());
    }

    public void SetDurationGeometries(IReadOnlyList<(double X, double EndX, double Top, double Bottom)> geometries)
    {
        if (_durationGeometries.Length == geometries.Count)
        {
            var unchanged = true;
            for (var i = 0; i < _durationGeometries.Length; i++)
            {
                var a = _durationGeometries[i];
                var b = geometries[i];
                if (Math.Abs(a.X - b.X) >= 0.2 || Math.Abs(a.EndX - b.EndX) >= 0.2 ||
                    Math.Abs(a.Top - b.Top) >= 0.2 || Math.Abs(a.Bottom - b.Bottom) >= 0.2)
                {
                    unchanged = false;
                    break;
                }
            }
            if (unchanged) return;
        }

        _durationGeometries = new (double X, double EndX, double Top, double Bottom)[geometries.Count];
        for (var i = 0; i < geometries.Count; i++) _durationGeometries[i] = geometries[i];
        InvalidateVisual();
    }

    public void SetDurationStyle(Color color, double opacity, bool enabled = true)
    {
        opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;
        if (_durationColor == color && Math.Abs(_durationOpacity - opacity) < 0.001 && _durationEnabled == enabled) return;
        _durationColor = color;
        _durationOpacity = opacity;
        _durationEnabled = enabled;
        InvalidateVisual();
    }

    public void SetThickness(double thickness)
    {
        _thickness = double.IsFinite(thickness) ? Math.Clamp(thickness, 0.5, 5) : 1.7;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("PlayheadOverlay"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "PlayheadOverlay", dc, ActualWidth, ActualHeight)) { } // Not logged: render path: runs per frame.
    }

    private void RenderCore(DrawingContext dc)
    {
        if (_durationEnabled && _durationOpacity > 0)
        {
            var alpha = PlaybackGlowIntensity.ScaleAlpha(_durationColor.A, _durationOpacity);
            if (alpha > 0)
            {
                var brush = new SolidColorBrush(Color.FromArgb(alpha, _durationColor.R, _durationColor.G, _durationColor.B));
                foreach (var glow in _durationGeometries)
                    if (glow.EndX > glow.X)
                        dc.DrawRectangle(brush, null, SnapToPixels ? SnappedBox(glow, VisualTreeHelper.GetDpi(this).PixelsPerDip) : new Rect(glow.X, glow.Top, glow.EndX - glow.X, glow.Bottom - glow.Top));
            }
        }
        if (_geometry is not { } g) return;
        var (x, width) = SnapToPixels ? SnappedLine(g.X, _thickness, VisualTreeHelper.GetDpi(this).PixelsPerDip) : (g.X, _thickness);
        var pen = new Pen(new SolidColorBrush(_color), width);
        dc.DrawLine(pen, new Point(x, g.Top), new Point(x, g.Bottom));
        dc.DrawEllipse(new SolidColorBrush(_color), null, new Point(x, g.Top - 3), 3.6, 3.6);
    }

    /// <summary>
    /// True: the line and the duration glow sit on whole device pixels, so the moving caret keeps hard edges instead of two soft columns.
    /// Off by default: a picture drawn for a file (the video frame) keeps its fixed pixels.
    /// </summary>
    public bool SnapToPixels
    {
        get => _snapToPixels;
        set { if (_snapToPixels == value) return; _snapToPixels = value; InvalidateVisual(); }
    }
    private bool _snapToPixels;

    /// <summary>The line's centre and width in DIPs, covering a whole number of device pixels (at least one).</summary>
    internal static (double X, double Width) SnappedLine(double x, double thickness, double pixelsPerDip)
    {
        var pixels = Math.Max(1, Math.Round(thickness * pixelsPerDip));
        var centre = x * pixelsPerDip;
        // An odd width is centred on a pixel's middle, an even one on a pixel edge.
        centre = pixels % 2 == 1 ? Math.Floor(centre) + 0.5 : Math.Round(centre);
        return (centre / pixelsPerDip, pixels / pixelsPerDip);
    }

    private static Rect SnappedBox((double X, double EndX, double Top, double Bottom) box, double pixelsPerDip)
    {
        double x = PixelSnap.Snap(box.X, pixelsPerDip), right = PixelSnap.Snap(box.EndX, pixelsPerDip);
        double top = PixelSnap.Snap(box.Top, pixelsPerDip), bottom = PixelSnap.Snap(box.Bottom, pixelsPerDip);
        return new Rect(x, top, Math.Max(0, right - x), Math.Max(0, bottom - top));
    }
}
