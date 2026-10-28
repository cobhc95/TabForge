using System;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the track list width rules: the list's minimum (its number and name stay readable), its maximum (a share of the pane width),
//     the snug default that fits the transport row (TransportWrapRules at the density factor, never narrower than the columns need),
//     and the name column width that a list width leaves for the name.
// Does not own: the splitter drag (ArrangementView), the saved width (TimelineSettings.TrackListWidth), the column layout (TrackRowCells).
// Tests: TabForge.Avalonia.Tests.TrackListWidthTests.
public static class TrackListWidthRules
{
    /// <summary>The narrowest name column: enough for the track number and a short name.</summary>
    public const double MinNameWidth = 80;

    /// <summary>The widest the list may be, as a share of the pane it sits in.</summary>
    public const double MaxShareOfPane = 0.6;

    /// <summary>The list width that is not the name column: every fixed column and the grid margins.</summary>
    public static double FixedWidth => TrackRowCells.TotalWidth - TrackRowCells.NameWidth;

    /// <summary>The default list width at the density factor: the snug transport fit, never narrower than the columns need.</summary>
    public static double DefaultListWidthFor(double factor) =>
        Math.Max(TransportWrapRules.SnugListWidth(factor), MinListWidth);

    /// <summary>The default list width at the appearance density (Comfortable is the reference, Compact the app default).</summary>
    public static double DefaultListWidthFor(AppearanceSettings appearance) => DefaultListWidthFor(UiDensity.Of(appearance).Pad);

    /// <summary>The default list width: the snug transport fit at Comfortable density.</summary>
    public static double DefaultListWidth => DefaultListWidthFor(1.0);

    /// <summary>The name column width the default list width leaves.</summary>
    public static double DefaultNameWidth => NameWidthFor(DefaultListWidth);

    /// <summary>The name column width the snug default at the density factor leaves.</summary>
    public static double DefaultNameWidthFor(double factor) => NameWidthFor(DefaultListWidthFor(factor));

    // A saved width equal to either old default (532 or the 600 that followed it) reads as "never changed" and takes the snug default.
    private static bool IsOldDefault(double savedWidth) =>
        savedWidth == ChromeMetrics.TrackListOldDefaultWidth || savedWidth == ChromeMetrics.TrackListDefaultWidth;

    /// <summary>The saved list width to apply at Comfortable density: either old default reads as the new default, any other width the user dragged to is kept.</summary>
    public static double ResolveSaved(double savedWidth) => IsOldDefault(savedWidth) ? DefaultListWidth : savedWidth;

    /// <summary>The saved list width to apply at the density factor: either old default reads as the snug default there, any other width the user dragged to is kept.</summary>
    public static double ResolveSaved(double savedWidth, double factor) =>
        IsOldDefault(savedWidth) ? DefaultListWidthFor(factor) : savedWidth;

    /// <summary>The saved list width to apply at the appearance density: either old default reads as the snug default there, any other width the user dragged to is kept.</summary>
    public static double ResolveSaved(double savedWidth, AppearanceSettings appearance) =>
        ResolveSaved(savedWidth, UiDensity.Of(appearance).Pad);

    /// <summary>The narrowest list width.</summary>
    public static double MinListWidth => FixedWidth + MinNameWidth;

    /// <summary>The name column width a list width leaves.</summary>
    public static double NameWidthFor(double listWidth) => listWidth - FixedWidth;

    /// <summary>Clamps a list width to [MinListWidth, MaxShareOfPane of the pane]; a pane of unknown width (0) only sets the minimum.</summary>
    public static double ClampListWidth(double listWidth, double paneWidth)
    {
        var maximum = paneWidth > 0 ? Math.Max(MinListWidth, paneWidth * MaxShareOfPane) : double.PositiveInfinity;
        return Math.Clamp(listWidth, MinListWidth, maximum);
    }
}
