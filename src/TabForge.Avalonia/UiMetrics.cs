// Owns: the one popup and density-size entry point: bar and row heights and pads by density, and the popup placement
//   clamp to the owner screen working area.
// Does not own: the density factors (UiDensity), the working area (WindowMetrics), or any view layout (each window keeps its own).
// Tests: UiMetricsTests.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>
/// The one popup and density-size entry point. Bar, row and pad take their existing base and scale it by density, so at
/// Comfortable they return the base exactly (Compact shrinks and Spacious enlarges it). The popup clamp keeps a
/// popup inside the owner working area for the supported placements; other placements are left alone. Nothing calls it yet.
/// </summary>
public static class UiMetrics
{
    /// <summary>The square of a transport bar button at full width (DIPs; it shrinks to the transport minimum when the bar is narrow).</summary>
    public const double TransportButtonSize = 34;
    /// <summary>The square of a preset row action button (DIPs).</summary>
    public const double PresetRowButtonSize = 30;
    /// <summary>The square of a Tools pane tool button (DIPs).</summary>
    public const double ToolButtonSize = 36;

    /// <summary>The quick actions group's (Open / Save) frame: the tinted group's corner, padding and gap, an action button's size.</summary>
    public static void StyleQuickGroup(Border group)
    {
        group.CornerRadius = new CornerRadius(6);
        group.BorderThickness = new Thickness(1);
        group.Padding = new Thickness(2, 0);
        group.Margin = new Thickness(0, 0, 6, 0);
    }

    /// <summary>The size of a quick action button (a menu row icon button).</summary>
    public static void StyleQuickCell(TemplatedControl button)
    {
        button.Height = 27;
        button.MinWidth = 30;
        button.Padding = new Thickness(4, 0);
    }

    /// <summary>The bar height for the base at the appearance density.</summary>
    public static double Bar(double baseDips, AppearanceSettings appearance) =>
        baseDips * UiDensity.Of(appearance).Bar;

    /// <summary>The row height for the base at the appearance density.</summary>
    public static double Row(double baseDips, AppearanceSettings appearance) =>
        baseDips * UiDensity.Of(appearance).Row;

    /// <summary>The uniform pad for the base at the appearance density.</summary>
    public static Thickness Pad(double baseDips, AppearanceSettings appearance) =>
        new Thickness(baseDips * UiDensity.Of(appearance).Pad);

    /// <summary>Sets the outer margin of <paramref name="control"/> (device-independent pixels) and returns it, so a view can place a part without a literal thickness.</summary>
    public static T WithMargin<T>(this T control, double left, double top, double right, double bottom) where T : Layoutable
    {
        control.Margin = new Thickness(left, top, right, bottom);
        return control;
    }

    /// <summary>Sets the padding of <paramref name="control"/> (device-independent pixels, <paramref name="x"/> left and right, <paramref name="y"/> top and bottom) and returns it.</summary>
    public static T WithPadding<T>(this T control, double x, double y) where T : global::Avalonia.Controls.Primitives.TemplatedControl
    {
        control.Padding = new Thickness(x, y, x, y);
        return control;
    }

    /// <summary>Sets the padding of a border (<paramref name="x"/> left and right, <paramref name="y"/> top and bottom) and returns it.</summary>
    public static Border WithPadding(this Border border, double x, double y)
    {
        border.Padding = new Thickness(x, y, x, y);
        return border;
    }

    /// <summary>A thickness of the four sides each multiplied by <paramref name="factor"/> (the density factor).</summary>
    public static Thickness Sides(double left, double top, double right, double bottom, double factor) =>
        new Thickness(left * factor, top * factor, right * factor, bottom * factor);

    /// <summary>The asymmetric pad at the appearance density: the height scales, the width stays as set.</summary>
    public static Thickness Pad(double x, double y, AppearanceSettings appearance)
    {
        var scaled = y * UiDensity.Of(appearance).Pad;
        return new Thickness(x, scaled, x, scaled);
    }

    /// <summary>
    /// The top-left clamped inside the working area, in the same unit for all three. A popup larger than the area sits at the origin.
    /// </summary>
    public static PixelPoint ClampPopup(PixelPoint desired, Size popupSize, Size workingArea)
    {
        double x = desired.X;
        double y = desired.Y;
        if (popupSize.Width >= workingArea.Width)
            x = 0;
        else
            x = Math.Clamp(x, 0, workingArea.Width - popupSize.Width);
        if (popupSize.Height >= workingArea.Height)
            y = 0;
        else
            y = Math.Clamp(y, 0, workingArea.Height - popupSize.Height);
        return new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }
}
