using System;
using TabForge.Avalonia;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the numeric rule of the transport bar's fit: whether the wrapped groups sit in one row (their wrapped height is no taller than the
//     tallest group), and the snug widths (the bar and the track list column) that fit every item on one row at the density factor: the bar's
//     width is its content (every full-size button, the divider, the master knob, the zoom column and the master meter, with its spacing) with
//     its border and padding, and the list width adds the bar's outer margins. The bar never shrinks its buttons or hides an item; when narrow the
//     groups wrap to more rows.
// Does not own: the layout itself (TransportBarView's WrapPanel and its groups), the button sizes (UiMetrics.TransportButtonSize and the
//     ChromeMetrics parts above), the track list minimum (TrackListWidthRules.MinListWidth).
// Tests: TabForge.Avalonia.Tests.TransportWrapRulesTests.
public static class TransportWrapRules
{
    /// <summary>True when a wrapped height is at most the tallest group's height, so every group shares the one row.</summary>
    public static bool FitsOneRow(double wrappedHeight, double tallestGroupHeight) => wrappedHeight <= tallestGroupHeight + 0.5;

    // The full-size buttons that take the density-scaled side margin: the playback and toggle groups (Items), the live input button and the
    // tuning fork, snap magnet and mixer buttons. The zoom pair is one unscaled column and the divider is counted on its own below.
    private static double NormalButtonCount => TransportBarView.Items.Count + 4;

    // The layout rounds each side margin to a whole DIP when it measures, so a scaled margin counts rounded here (0.85 reads as 1, 2.55 as 3,
    // 3.4 as 3 and 5.1 as 5). Halves round up; none of the densities below lands a margin on one.
    private static double WholeMargin(double margin, double factor) => Math.Round(margin * factor, MidpointRounding.AwayFromZero);

    /// <summary>The transport row's content width at the density factor: every full-size button, the divider, the master knob (sized to the
    /// buttons), the zoom column and the master meter, each with its spacing as the layout measures it. The zoom column's own margin stays unscaled, as the bar lays it.</summary>
    public static double SnugContentWidth(double factor) =>
        NormalButtonCount * (UiMetrics.TransportButtonSize + (WholeMargin(ChromeMetrics.TransportSideMargin, factor) + WholeMargin(ChromeMetrics.TransportSideMargin, factor)))
            + ChromeMetrics.TransportDividerSpan + (WholeMargin(ChromeMetrics.TransportDividerSideMargin, factor) + WholeMargin(ChromeMetrics.TransportDividerSideMargin, factor))
            + UiMetrics.TransportButtonSize + WholeMargin(ChromeMetrics.TransportKnobLeftMargin, factor) + WholeMargin(ChromeMetrics.TransportKnobRightMargin, factor)
            + ChromeMetrics.TransportZoomSpan + (ChromeMetrics.TransportZoomSideMargin + ChromeMetrics.TransportZoomSideMargin)
            + UiMetrics.TransportButtonSize + (WholeMargin(ChromeMetrics.TransportSideMargin, factor) + WholeMargin(ChromeMetrics.TransportSideMargin, factor));

    /// <summary>The transport bar's outer width at the density factor: the content with the bar's border and side padding, rounded up to a whole
    /// DIP, so the master meter ends at the right padding instead of wrapping to a second row on a sub-pixel fraction.</summary>
    public static double SnugBarWidth(double factor) =>
        Math.Ceiling(SnugContentWidth(factor) + (ChromeMetrics.TransportBarBorder + ChromeMetrics.TransportBarBorder)
            + (ChromeMetrics.TransportBarPadding + ChromeMetrics.TransportBarPadding) * factor);

    /// <summary>The track list width that holds the snug bar at the density factor: the bar with its outer left and right margins.</summary>
    public static double SnugListWidth(double factor) =>
        SnugBarWidth(factor) + ChromeMetrics.TransportBarOuterLeft + ChromeMetrics.TransportBarOuterRight;

    /// <summary>The track list width that holds the snug bar at the appearance density (Comfortable is the reference, Compact the app default).</summary>
    public static double SnugListWidth(AppearanceSettings appearance) => SnugListWidth(UiDensity.Of(appearance).Pad);
}
