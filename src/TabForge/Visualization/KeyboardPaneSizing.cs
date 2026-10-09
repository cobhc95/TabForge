using System;
using System.Linq;
using System.Windows;

namespace TabForge.Visualization;

/// <summary>
/// Natural key height of the keyboard pane, from the key width: white keys are at most 24 px wide, and their
/// height is about 5.5 times their width (a real piano's shape), kept within 60-120 px.
/// Owns: the key-range table per keyboard size and the height rule. Does not own: drawing (KeyboardRenderer),
/// pane layout or persistence (InstrumentPanel, DockLayoutController).
/// </summary>
public static class KeyboardPaneSizing
{
    public const double MaxWhiteKeyWidth = 24;
    public const double KeyHeightRatio = 5.5;
    public const double MinKeyHeight = 60;
    public const double MaxKeyHeight = 120;

    /// <summary>Lowest and highest MIDI note of a standard keyboard size; smaller sizes are shifted by octaves at draw time.</summary>
    public static (int Lowest, int Highest) RangeFor(int keys) => keys switch
    {
        76 => (28, 103), 61 => (36, 96), 49 => (36, 84), 37 => (48, 84), 25 => (48, 72), _ => (21, 108)
    };

    /// <summary>Number of white keys in the standard range of a keyboard size.</summary>
    public static int WhiteKeysFor(int keys)
    {
        var (lowest, highest) = RangeFor(keys);
        return Enumerable.Range(lowest, highest - lowest + 1).Count(IsWhite);
    }

    /// <summary>Width of one white key in a pane of <paramref name="paneWidth"/> DIPs, capped at 24.</summary>
    public static double WhiteKeyWidth(double paneWidth, int whiteKeys) =>
        Math.Min(MaxWhiteKeyWidth, paneWidth / Math.Max(1, whiteKeys));

    /// <summary>Where the keys are drawn: at most 24 px per white key, centred horizontally in <paramref name="width"/>
    /// (after the legend reserve), and <see cref="KeyHeightIn"/> tall, centred vertically.</summary>
    public static Rect KeysRect(double width, double height, int keys, double reserve)
    {
        var available = Math.Max(120, width - reserve);
        var keyWidth = Math.Min(available, WhiteKeysFor(keys) * MaxWhiteKeyWidth);
        var keyHeight = Math.Min(height, KeyHeightIn(height));
        return new Rect(Math.Max(0, (available - keyWidth) / 2), Math.Max(0, (height - keyHeight) / 2), keyWidth, keyHeight);
    }

    /// <summary>Key height drawn in a pane of the given height: the pane height, kept within 60-120 px.</summary>
    public static double KeyHeightIn(double paneHeight) => Math.Clamp(paneHeight, MinKeyHeight, MaxKeyHeight);

    /// <summary>Natural key height: white key width x 5.5, clamped to 60-120 px.</summary>
    public static double HeightFor(double paneWidth, int whiteKeys) =>
        Math.Clamp(WhiteKeyWidth(paneWidth, whiteKeys) * KeyHeightRatio, MinKeyHeight, MaxKeyHeight);

    private static bool IsWhite(int midi) => (midi % 12) is 0 or 2 or 4 or 5 or 7 or 9 or 11;
}
