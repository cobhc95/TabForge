// Owns: the scaled root of a window: a LayoutTransformControl whose content always fills the window at a uniform UI scale.
// Does not own: which scale applies (ThemeSettingsBinding) or the window's sizes (WindowUiScale).
// Tests: UiScaleRootTests.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TabForge.Avalonia;

/// <summary>
/// Avalonia's LayoutTransformControl gives a scaled child the largest rectangle its transform fits, which for a scale below 1 in a
/// window larger than its default is narrower than the window, so the content sat centred with empty bands on each side.
/// At a uniform scale this root makes the child at least the window's size divided by the scale (less the child's margin);
/// with an unbounded size (a window sized to its content) the child keeps its own minimums.
/// </summary>
public sealed class UiScaleRoot : LayoutTransformControl
{
    private Control? _sized;
    private double _ownMinWidth, _ownMinHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is { } child)
        {
            if (!ReferenceEquals(child, _sized)) { _sized = child; _ownMinWidth = child.MinWidth; _ownMinHeight = child.MinHeight; }
            var scale = LayoutTransform is ScaleTransform s && s.ScaleX > 0 && Math.Abs(s.ScaleX - s.ScaleY) < 1e-9 ? s.ScaleX : 1.0;
            var margin = child.Margin;
            // Only content meant to stretch is filled: a fixed size or a non-stretch alignment keeps its own size.
            var fillWidth = double.IsNaN(child.Width) && child.HorizontalAlignment == HorizontalAlignment.Stretch;
            var fillHeight = double.IsNaN(child.Height) && child.VerticalAlignment == VerticalAlignment.Stretch;
            child.MinWidth = fillWidth ? Fill(availableSize.Width, scale, margin.Left + margin.Right, _ownMinWidth) : _ownMinWidth;
            child.MinHeight = fillHeight ? Fill(availableSize.Height, scale, margin.Top + margin.Bottom, _ownMinHeight) : _ownMinHeight;
        }
        return base.MeasureOverride(availableSize);
    }

    // At scale 1 there is no transform and Avalonia already fills the window: the child keeps its own minimums.
    private static double Fill(double available, double scale, double margin, double own) => scale == 1.0 ? own :
        double.IsFinite(available) ? Math.Max(own, Math.Max(0, available / scale - margin)) : own;
}
