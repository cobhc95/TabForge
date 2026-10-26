// Owns: the score's playing marker (green line with its top dot) as its own layer over the page. It is drawn around its own x origin and placed
//   with a translate transform, so a frame inside a bar only changes the transform; the page and the layer's own drawing are not repainted.
// Does not own: the line's geometry (ScoreGeometryProbe.DrawPlayhead), the clock, the page drawing and the lit notes (ScorePreviewView, ScoreRenderer).
// Tests: TabForge.Avalonia.Tests.PlayheadClockTests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.Platform;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

/// <summary>The overlay's job: draws the moving line above the retained page. Thread: UI.</summary>
internal sealed class ScorePlayheadOverlay : Control
{
    private readonly ScoreGeometryProbe _probe;
    private readonly System.Func<double> _zoom;
    private readonly AvaloniaDrawContext _draw = new();
    private readonly TranslateTransform _move = new();
    private (double Top, double Bottom, double Zoom, double Thickness, bool Light, double X, UiColor Colour) _drawn = (double.NaN, 0, 0, 0, false, 0, default);

    public ScorePlayheadOverlay(ScoreGeometryProbe probe, System.Func<double> zoom)
    {
        _probe = probe;
        _zoom = zoom;
        IsHitTestVisible = false;
        RenderTransform = _move;
    }

    /// <summary>How many times the layer drew itself; a frame that only moves the line adds none.</summary>
    internal int RedrawCount { get; private set; }

    /// <summary>The x of the playing line in view coordinates (DIPs, sub-pixel), or NaN while nothing plays.</summary>
    internal double LineX { get; private set; } = double.NaN;

    /// <summary>The playing line's top end, where its round dot sits (view coordinates), or null while nothing plays.</summary>
    internal Point? DotCentre => _probe.Play.PlayheadLine(_probe) is var (x, top, _) ? new Point(x * _zoom(), top * _zoom()) : null;

    /// <summary>Moves the line to the playhead; redraws only when its height, zoom, thickness or theme changed.</summary>
    public void Place()
    {
        if (_probe.Play.PlayheadLine(_probe) is not var (x, top, bottom))
        {
            LineX = double.NaN;
            IsVisible = false;
            return;
        }
        var zoom = _zoom();
        var light = _probe.Light;
        var colour = _probe.Appearance.PlaybackColor;
        var thickness = _probe.Appearance.PlayheadThickness;
        IsVisible = true;
        LineX = x * zoom;
        _move.X = LineX - _drawn.X * zoom;
        if (_drawn.Top == top && _drawn.Bottom == bottom && _drawn.Zoom == zoom && _drawn.Thickness == thickness && _drawn.Light == light && _drawn.Colour == colour) return;
        // The layer is drawn at the x of this moment and moved from there, so a redraw also resets the transform.
        _drawn = (top, bottom, zoom, thickness, light, x, colour);
        _move.X = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        RedrawCount++;
        if (_probe.Play.PlayheadLine(_probe) is not var (nowX, _, _)) return;
        using var zoomed = context.PushTransform(Matrix.CreateScale(_zoom(), _zoom()));
        // The probe draws at the playhead of this instant; the transform is relative to the x recorded when the layer was last placed, so the
        // line is drawn there, however long the render took to arrive.
        using var anchored = context.PushTransform(Matrix.CreateTranslation(_drawn.X - nowX, 0));
        _probe.DrawPlayhead(_draw.Use(context));
    }
}
