using System;

namespace TabForge.Presenters;

// Owns: the keyboard pane's height rule: the natural key height from the white key width (a white key is at most 24 DIPs wide and 5.5 times as
//   tall, kept within 60-120 DIPs), the pane height limits that follow (the keys' inset above and below, and the height a pane's width alone allows),
//   and the white key count of each keyboard size.
// Does not own: the keys' drawing and hit tests (InstrumentPanelView, KeyboardGeometryRules), the pane's layout and its dock clamp (InstrumentPanelStrip,
//   DockWorkspaceView). The WPF KeyboardPaneSizing keeps the same numbers for the WPF panel; this file is the Core copy the Avalonia panel reads.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public static class KeyboardPaneSizeRules
{
    /// <summary>The widest a white key is drawn, in DIPs.</summary>
    public const double MaxWhiteKeyWidth = 24;

    /// <summary>A white key's height over its width (a real piano's shape).</summary>
    public const double KeyHeightRatio = 5.5;

    /// <summary>The shortest and tallest natural key height, in DIPs.</summary>
    public const double MinKeyHeight = 60;
    public const double MaxKeyHeight = 120;

    /// <summary>The gap between the keys and the pane's top and bottom edges, in DIPs.</summary>
    public const double KeyInset = 4;

    /// <summary>The shortest pane: the shortest keys plus the insets.</summary>
    public const double MinPaneHeight = MinKeyHeight + 2 * KeyInset;

    /// <summary>The number of white keys in the range of a keyboard size (the range moves by octaves, which keeps the count).</summary>
    public static int WhiteKeysFor(int keys)
    {
        var (lowest, highest) = KeyboardGeometryRules.RangeFor(keys);
        return KeyboardGeometryRules.WhiteKeysIn(lowest, highest);
    }

    /// <summary>The pane height at which the keys stop growing in a pane of <paramref name="paneWidth"/> DIPs: the natural key height plus the insets.
    /// A width of +infinity means no width limit, the tallest keys.</summary>
    public static double MaxPaneHeight(double paneWidth, int whiteKeys) => NaturalKeyHeight(paneWidth, whiteKeys) + 2 * KeyInset;

    /// <summary>The natural key height: the white key width times <see cref="KeyHeightRatio"/>, kept within <see cref="MinKeyHeight"/> to <see cref="MaxKeyHeight"/>.</summary>
    private static double NaturalKeyHeight(double paneWidth, int whiteKeys) =>
        Math.Clamp(WhiteKeyWidth(paneWidth, whiteKeys) * KeyHeightRatio, MinKeyHeight, MaxKeyHeight);

    /// <summary>The width of one white key in a pane of <paramref name="paneWidth"/> DIPs, at most <see cref="MaxWhiteKeyWidth"/>.</summary>
    private static double WhiteKeyWidth(double paneWidth, int whiteKeys) => Math.Min(MaxWhiteKeyWidth, paneWidth / Math.Max(1, whiteKeys));
}
