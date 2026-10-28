using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Views;

/// <summary>
/// A small line drawing of a bend or tremolo-bar curve type, made from the type's own points (eased for a bend, straight for a tremolo bar), in the
/// theme's text colour, or its disabled icon colour while the row is disabled.
/// Owns: the mapping of a curve's points into the icon square and its drawing. Does not own: the type table (EffectCurveTypes) or the list it sits in (CurveSidePanel).
/// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// </summary>
internal sealed class CurveShapeIcon : Control
{
    private const double Inset = 2, Line = 1.5;
    private const int Samples = 8;

    private readonly AvaloniaDrawContext _draw = new();
    private readonly IReadOnlyList<BendPointModel> _points;
    private readonly bool _eased;
    private readonly double _low, _high;
    private ThemeColourCache? _theme;
    private ThemeColourCache Colours => _theme ??= new(this);

    /// <param name="low">The value at the bottom of the icon.</param>
    /// <param name="high">The value at the top of the icon (the same range for every icon of a list, so shapes compare).</param>
    public CurveShapeIcon(IReadOnlyList<BendPointModel> points, bool eased, double low, double high)
    {
        _points = points;
        _eased = eased;
        _low = low;
        _high = Math.Max(high, low + 1);
        Width = DialogMetrics.CurveIconSize;
        Height = DialogMetrics.CurveIconSize;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) InvalidateVisual();
    }

    private Point At(double offset, double value, double size) => new(
        Inset + offset / EffectCurve.Span * (size - 2 * Inset),
        size - Inset - (value - _low) / (_high - _low) * (size - 2 * Inset));

    public override void Render(DrawingContext context)
    {
        var d = _draw.Use(context);
        var size = DialogMetrics.CurveIconSize;
        var stroke = new UiStroke(Colours.Get(IsEffectivelyEnabled ? "TextBrush" : "IconDisabledBrush"), Line, RoundCaps: true, RoundJoins: true);
        for (var i = 1; i < _points.Count; i++)
        {
            var from = _points[i - 1];
            var to = _points[i];
            var samples = _eased && from.Value != to.Value ? Samples : 1;
            var last = At(from.Offset, from.Value, size);
            for (var k = 1; k <= samples; k++)
            {
                var t = (double)k / samples;
                var next = At(from.Offset + (to.Offset - from.Offset) * t, EffectCurveShape.Between(from.Value, to.Value, t), size);
                d.Line(stroke, last.X, last.Y, next.X, next.Y);
                last = next;
            }
        }
    }
}
