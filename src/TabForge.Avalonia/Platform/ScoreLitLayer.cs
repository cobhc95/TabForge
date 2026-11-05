// Owns: the layer between the score page and its playhead that shows the playing state: the lit notes and the playing-bar band, redrawn on a note change
//   so the page itself stays retained during playback.
// Does not own: which notes are lit (ScorePlayState), the drawing (ScoreGeometryProbe.DrawLitSystems), when it repaints (ScorePlayheadBinding, ScorePreviewView).
// Tests: TabForge.Avalonia.Tests.ScoreFollowContinuousPlayTests.
// Threads: UI thread only.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

internal sealed class ScoreLitLayer : Control
{
    private readonly ScoreGeometryProbe _probe;
    private readonly AvaloniaDrawContext _draw;
    private readonly Func<double> _zoom;

    public ScoreLitLayer(ScoreGeometryProbe probe, AvaloniaDrawContext draw, Func<double> zoom)
    {
        (_probe, _draw, _zoom) = (probe, draw, zoom);
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        if (!_probe.Play.Active) return;
        var zoom = _zoom();
        using (context.PushTransform(Matrix.CreateScale(zoom, zoom)))
            _probe.DrawLitSystems(_draw.Use(context));
    }
}
