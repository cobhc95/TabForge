using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the timeline's playhead as two thin layers over the body: the moving part (white line, amber flag, lane arrow, with a blurred black
//     shadow on the layer), drawn once around its own origin and placed with a translate transform snapped to device pixels, and the bar number,
//     drawn once per played bar. A frame only changes the transform, so the clips, ruler and rows are never redrawn for the playhead.
// Does not own: the position (TimelineView.PlayheadX), the clock, the cue shapes (TimelinePlayheadMarks).
// Tests: TabForge.Avalonia.Tests.PlayheadClockTests.
// Threads: UI only. Redraw runs when the played bar, the selected lane, the size or the theme changes; Place runs every frame and allocates nothing.
internal sealed class TimelinePlayheadLayer : Control
{
    public const double LineWidth = 3;
    /// <summary>The shadow round the line and arrow: a blur of this radius with no offset, at this opacity (the playhead drop shadow).</summary>
    public const double ShadowBlurRadius = 4, ShadowOpacity = 0.72;

    private readonly TimelineView _view;
    private readonly AvaloniaDrawContext _draw = new();
    private readonly TranslateTransform _move = new();
    private readonly BarNumberLayer _number;
    private int _drawnBar = int.MinValue;
    private int _drawnPass = 1;

    public TimelinePlayheadLayer(TimelineView view)
    {
        _view = view;
        _number = new BarNumberLayer(view);
        IsHitTestVisible = false;
        RenderTransform = _move;
        Effect = new DropShadowEffect
        {
            Color = ShadowColour(),
            BlurRadius = ShadowBlurRadius,
            OffsetX = 0,
            OffsetY = 0,
            Opacity = ShadowOpacity,
        };
        ActualThemeVariantChanged += (_, _) => { ((DropShadowEffect)Effect!).Color = ShadowColour(); Redraw(); };
    }

    // The playhead's drop shadow in the theme this layer is shown in (the dark one until the layer is attached).
    private Color ShadowColour() => AvaloniaPalette.ColorOf(AvaloniaPalette.Get("PlayheadShadow", ActualThemeVariant == ThemeVariant.Light));

    /// <summary>The bar-number layer; the timeline adds it to its visual children beside this layer.</summary>
    public Control NumberLayer => _number;

    /// <summary>How many times the layer drew itself; a frame inside one bar adds none.</summary>
    internal int RedrawCount { get; private set; }

    /// <summary>The x the layer is placed at (DIPs, sub-pixel), or NaN while the playhead is hidden.</summary>
    internal double PlacedX { get; private set; } = double.NaN;

    /// <summary>Moves the layer to the playhead; redraws only when the played bar changed.</summary>
    public void Place()
    {
        var x = _view.PlayheadX();
        PlacedX = x;
        IsVisible = !double.IsNaN(x);
        _number.IsVisible = IsVisible;
        if (!IsVisible) { _drawnBar = int.MinValue; _drawnPass = 1; return; }
        // The line is LineWidth wide and centred on x, so its left edge lands on a whole device pixel.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _move.X = LayoutHelper.RoundLayoutValue(x - LineWidth / 2, scale) + LineWidth / 2;
        if (_view.PlayheadBar != _drawnBar || _view.PlayheadPass != _drawnPass) Redraw();
    }

    /// <summary>Draws the layers again (the body drew, or the lane, size or theme changed).</summary>
    public void Redraw()
    {
        _drawnBar = _view.PlayheadBar;
        _drawnPass = _view.PlayheadPass;
        if (Effect is DropShadowEffect shadow)
            shadow.Color = AvaloniaPalette.ColorOf(AvaloniaPalette.Get("PlayheadShadow", ActualThemeVariant == ThemeVariant.Light));
        InvalidateVisual();
        _number.InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (double.IsNaN(PlacedX)) return;
        RedrawCount++;
        var light = ActualThemeVariant == ThemeVariant.Light;
        UiColor Ink(string key) => AvaloniaPalette.Get(key, light);
        var d = _draw.Use(context);
        var height = _view.Bounds.Height;
        if (PlayheadStyles.ShowsLine(_view.PlayheadStyle))
            d.Rect(Ink("TimelinePlayhead"), null, -LineWidth / 2, 0, LineWidth, height);
        if (!_view.Compact) TimelinePlayheadMarks.DrawRulerFlag(d, Ink, 0);
        var laneTop = _view.Compact ? TimelineView.RulerHeight + TimelineView.SectionHeight : _view.ArrowLaneTop();
        if (laneTop >= 0)
            TimelinePlayheadMarks.DrawArrow(d, Ink, -LineWidth / 2, TimelinePlayheadMarks.ArrowCentreY(laneTop, _view.Compact ? _view.MasterLaneHeight : _view.RowHeight));
    }

    /// <summary>The amber number of the played bar in the ruler; it stays at the bar's left edge, so it is drawn once per bar.</summary>
    private sealed class BarNumberLayer : Control
    {
        private readonly TimelineView _view;
        private readonly AvaloniaDrawContext _draw = new();

        public BarNumberLayer(TimelineView view)
        {
            _view = view;
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context)
        {
            if (_view.Compact || double.IsNaN(_view.PlayheadX())) return;
            var light = ActualThemeVariant == ThemeVariant.Light;
            TimelinePlayheadMarks.DrawBarNumber(_draw.Use(context), key => AvaloniaPalette.Get(key, light), _view.BarX(_view.PlayheadBar), _view.PlayheadBar, _view.PlayheadPass, _view.RepeatRegionsNow(), TimelineView.Font);
        }
    }
}
