using System;

namespace TabForge.Avalonia.Platform;

// Owns: the track list width rules: the list's minimum (its number and name stay readable), its maximum (a share of the pane width),
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
