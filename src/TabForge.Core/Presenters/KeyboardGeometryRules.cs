using TabForge.Platform;

namespace TabForge.Presenters;

// Owns: the keyboard view's key range per size, the octave shift that keeps sounding notes in view, which keys are white, and the
//   white and black key rectangles with the click-to-key hit test. Plain ints and doubles over UiRect and UiPoint.
// Does not own: key colours and drawing (KeyboardRenderer, the Avalonia InstrumentPanelView) or the pane's height rules (KeyboardPaneSizing).
// Tests: no dedicated test class.
public static class KeyboardGeometryRules
{
    /// <summary>Share of a white key's width taken by a black key, and its height share of the keyboard.</summary>
    public const double BlackKeyWidthShare = 0.6;
    public const double BlackKeyHeightShare = 0.6;

    /// <summary>Lowest and highest MIDI note of a standard keyboard size; smaller sizes are shifted by octaves to follow the notes.</summary>
    public static (int Lowest, int Highest) RangeFor(int keys) => keys switch
    {
        76 => (28, 103), 61 => (36, 96), 49 => (36, 84), 37 => (48, 84), 25 => (48, 72), _ => (21, 108)
    };

    /// <summary>The key range of a size, moved by octaves (within A0..C8) so the lowest and highest sounding notes fall inside it; the full 88 keys never move.</summary>
    public static (int Lowest, int Highest) RangeShownFor(int keys, int lowestNote, int highestNote, bool hasNotes)
    {
        var (lowest, highest) = RangeFor(keys);
        if (!hasNotes || highest - lowest >= 87) return (lowest, highest);
        while (lowestNote < lowest && lowest - 12 >= 21) { lowest -= 12; highest -= 12; }
        while (highestNote > highest && highest + 12 <= 108) { lowest += 12; highest += 12; }
        return (lowest, highest);
    }

    public static bool IsWhite(int midi) => (midi % 12) is 0 or 2 or 4 or 5 or 7 or 9 or 11;

    public static int WhiteKeysIn(int lowest, int highest)
    {
        var count = 0;
        for (var midi = lowest; midi <= highest; midi++) if (IsWhite(midi)) count++;
        return count;
    }

    /// <summary>The rectangle of the white key that is the whiteIndex-th white key of the range.</summary>
    public static UiRect WhiteKeyRect(UiRect bounds, double keyWidth, int whiteIndex)
        => new(bounds.X + whiteIndex * keyWidth, bounds.Y, keyWidth, bounds.Height);

    /// <summary>The rectangle of a black key that sits after whiteIndex white keys (centred on the seam).</summary>
    public static UiRect BlackKeyRect(UiRect bounds, double keyWidth, int whiteIndex)
        => new(bounds.X + whiteIndex * keyWidth - keyWidth * (BlackKeyWidthShare / 2), bounds.Y, keyWidth * BlackKeyWidthShare, bounds.Height * BlackKeyHeightShare);

    /// <summary>The MIDI note under a point (black keys sit on top), or null outside the keys.</summary>
    public static int? KeyAt(UiRect bounds, int lowest, int highest, UiPoint point)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || point.X < bounds.X || point.X >= bounds.Right || point.Y < bounds.Y || point.Y >= bounds.Bottom) return null;
        var keyWidth = bounds.Width / Math.Max(1, WhiteKeysIn(lowest, highest));
        var whiteIndex = 0;
        int? white = null;
        for (var midi = lowest; midi <= highest; midi++)
        {
            if (IsWhite(midi))
            {
                var rect = WhiteKeyRect(bounds, keyWidth, whiteIndex++);
                if (point.X >= rect.X && point.X < rect.Right) white = midi;
                continue;
            }
            var black = BlackKeyRect(bounds, keyWidth, whiteIndex);
            if (point.X >= black.X && point.X < black.Right && point.Y < black.Bottom) return midi;
        }
        return white;
    }
}
