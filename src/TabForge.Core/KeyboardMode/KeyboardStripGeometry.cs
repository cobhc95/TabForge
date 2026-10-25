using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.KeyboardMode;

// Owns: the numbers of the keyboard falling-notes strip as pure maths: the key window (the notes' range in whole octaves, at least three, widened by octaves inside the 88 until the keys keep
//   real piano proportions within the height cap), the x and width of every key (white keys evenly spaced, black keys 58 % wide and 63 % long, off the seams as on a piano), the strip's height,
//   how many pixels a millisecond is (the falling area over the look-ahead, capped in speed) and where a moment ahead of the song time sits.
// Does not own: the key outlines and rectangles as toolkit shapes (the WPF KeyboardModeLayout), the notes, the colours or drawing.
// Tests: TestKeyboardModeKeyView.
public sealed class KeyboardStripGeometry
{
    public const int LowestKey = 21, HighestKey = 108;
    public const double SideMargin = 6;
    /// <summary>A white key's length over its width (23.5 mm by 150 mm).</summary>
    public const double WhiteLengthRatio = 6.4;
    /// <summary>A black key's width and length as shares of a white key's (13.7 mm by 95 mm).</summary>
    public const double BlackWidthShare = 0.58, BlackLengthShare = 0.63;
    /// <summary>The strip's most share of the view height; the falling area is the rest.</summary>
    public const double MaxStripShare = 0.30;
    public const double MinStripHeight = 72;
    /// <summary>The smallest view height; the falling area is what is left above the strip.</summary>
    public const double MinHeight = 160;
    /// <summary>The fewest keys the strip shows (three octaves), so a one-octave piece does not get giant keys.</summary>
    public const int MinKeys = 36;
    /// <summary>The fastest the notes fall (DIPs per second): a shorter look-ahead on a tall view is lengthened so a scroll never smears.</summary>
    public const double MaxPxPerSecond = 640;
    /// <summary>Black key centre off the seam to its right, as a share of a white key's width, by pitch class (C# and D# apart, F# and A# apart, G# centred).</summary>
    private static readonly double[] BlackShift = { 0, -0.098, 0, 0.098, 0, 0, -0.146, 0, 0, 0, 0.146, 0 };

    private readonly double[] _x = new double[128];
    private readonly double[] _w = new double[128];

    private KeyboardStripGeometry(int lowest, int highest, double width, double height, double lookAheadMs)
    {
        Lowest = lowest; Highest = highest; Width = width; Height = height;
        Whites = KeyboardGeometryRules.WhiteKeysIn(lowest, highest);
        WhiteWidth = (width - 2 * SideMargin) / Math.Max(1, Whites);
        Left = (width - WhiteWidth * Whites) / 2;
        StripHeight = Math.Clamp(WhiteWidth * WhiteLengthRatio, Math.Min(MinStripHeight, StripCap(height)), StripCap(height));
        StripTop = height - StripHeight;
        BlackHeight = StripHeight * BlackLengthShare;
        PxPerMs = Math.Min(StripTop / Math.Max(500, lookAheadMs), MaxPxPerSecond / 1000);
        var white = 0;
        for (var m = lowest; m <= highest; m++)
        {
            if (KeyboardGeometryRules.IsWhite(m)) { _x[m] = Left + white * WhiteWidth; _w[m] = WhiteWidth; white++; }
            else
            {
                var bw = WhiteWidth * BlackWidthShare;
                _x[m] = Left + white * WhiteWidth + BlackShift[m % 12] * WhiteWidth - bw / 2;
                _w[m] = bw;
            }
        }
    }

    public int Lowest { get; }
    public int Highest { get; }
    public double Width { get; }
    public double Height { get; }
    public double WhiteWidth { get; }
    public int Whites { get; }
    /// <summary>The x just right of the last white key.</summary>
    public double Right => Left + WhiteWidth * Whites;
    /// <summary>The x of the first key (the keys are centred).</summary>
    public double Left { get; }
    /// <summary>The y of the strip's top edge: where a note's start meets its key at the song time.</summary>
    public double StripTop { get; }
    public double StripHeight { get; }
    public double BlackHeight { get; }
    /// <summary>Pixels per millisecond of song time: the falling area spans the look-ahead (longer when the speed cap holds).</summary>
    public double PxPerMs { get; }
    /// <summary>How many milliseconds of song lie above the strip.</summary>
    public double AheadMs => StripTop / PxPerMs;

    /// <summary>The tallest strip a view of this height may have.</summary>
    public static double StripCap(double height) => Math.Max(24, height * MaxStripShare);

    /// <summary>The key window to draw for a source's range: widened by whole octaves (alternately down and up, inside the 88 keys) to at least <see cref="MinKeys"/> keys.</summary>
    public static (int Lowest, int Highest) Window(int lowest, int highest) => Widen(lowest, highest, (lo, hi) => hi - lo + 1 >= MinKeys);

    /// <summary>The window for a view: <see cref="Window"/>, then more octaves until the strip at real proportions fits the height cap (or the 88 keys show).</summary>
    public static (int Lowest, int Highest) WindowFor(int lowest, int highest, double width, double height)
    {
        var (lo, hi) = Window(lowest, highest);
        var cap = StripCap(height);
        return Widen(lo, hi, (l, h) => (width - 2 * SideMargin) / Math.Max(1, KeyboardGeometryRules.WhiteKeysIn(l, h)) * WhiteLengthRatio <= cap);
    }

    private static (int, int) Widen(int lowest, int highest, Func<int, int, bool> enough)
    {
        lowest = Math.Clamp(lowest, LowestKey, HighestKey);
        highest = Math.Clamp(highest, lowest, HighestKey);
        var down = true;
        while (!enough(lowest, highest))
        {
            var canDown = lowest > LowestKey;
            var canUp = highest < HighestKey;
            if (!canDown && !canUp) break;
            if (canDown && (down || !canUp)) lowest = Math.Max(LowestKey, lowest - 12);
            else highest = Math.Min(HighestKey, highest + 12);
            down = !down;
        }
        return (lowest, highest);
    }

    public static KeyboardStripGeometry Compute(int lowest, int highest, double width, double height, double lookAheadMs)
    {
        width = Math.Max(200, width); height = Math.Max(MinHeight, height);
        var (lo, hi) = WindowFor(lowest, highest, width, height);
        return new KeyboardStripGeometry(lo, hi, width, height, lookAheadMs);
    }

    public bool Has(int midi) => midi >= Lowest && midi <= Highest;
    public double KeyX(int midi) => _x[Math.Clamp(midi, Lowest, Highest)];
    public double KeyWidth(int midi) => _w[Math.Clamp(midi, Lowest, Highest)];
    public double KeyCentre(int midi) => KeyX(midi) + KeyWidth(midi) / 2;

    /// <summary>The y of a moment <paramref name="msAhead"/> after the song time (0 is the strip's top, larger is higher up).</summary>
    public double YAhead(double msAhead) => StripTop - msAhead * PxPerMs;

    /// <summary>The key's own outline as polygon points (from the top left, clockwise): a black key's rectangle, or a white key's rectangle minus the black keys over it. Returns the count (at most 8); <paramref name="points"/> needs 8 slots.</summary>
    public int KeyOutline(int midi, Span<UiPoint> points)
    {
        midi = Math.Clamp(midi, Lowest, Highest);
        double l = KeyX(midi), r = l + KeyWidth(midi), t = StripTop;
        if (!KeyboardGeometryRules.IsWhite(midi))
        {
            points[0] = new(l, t); points[1] = new(r, t); points[2] = new(r, t + BlackHeight); points[3] = new(l, t + BlackHeight);
            return 4;
        }
        var b = t + StripHeight;
        var leftIn = midi - 1 >= Lowest && !KeyboardGeometryRules.IsWhite(midi - 1) ? Math.Max(l, KeyX(midi - 1) + KeyWidth(midi - 1)) : l;
        var rightIn = midi + 1 <= Highest && !KeyboardGeometryRules.IsWhite(midi + 1) ? Math.Min(r, KeyX(midi + 1)) : r;
        var notch = t + BlackHeight;
        var n = 0;
        points[n++] = new(leftIn, t); points[n++] = new(rightIn, t);
        if (rightIn < r) { points[n++] = new(rightIn, notch); points[n++] = new(r, notch); }
        points[n++] = new(r, b); points[n++] = new(l, b);
        if (leftIn > l) { points[n++] = new(l, notch); points[n++] = new(leftIn, notch); }
        return n;
    }
}
