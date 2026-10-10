using System.Windows;
using System.Windows.Media;

namespace TabForge.KeyboardMode;

// Owns: the geometry of the keyboard falling-notes view as pure maths: the key window (the notes' range in whole octaves, at least three, widened by octaves inside the 88 until the keys keep real piano
//   proportions within the height cap), the x and width of every key (white keys evenly spaced, black keys 58 % wide and 63 % long, off the seams as on a piano), the strip's height (a white key's width
//   times 6.4, at most a share of the view), each key's own outline (a white key minus the black keys over it), how many pixels a millisecond is (the falling area over the look-ahead, capped in speed)
//   and where a moment ahead of the song time sits. A note's bar uses its key's x and width, so a bar always lands on its key.
// Does not own: the notes, the colours (KeyboardColours) or drawing (KeyboardChromeDrawer, KeyboardPageDrawer, KeyboardKeyPainter).
// Tests: TestKeyboardModeKeyView.
public sealed class KeyboardModeLayout
{
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
    private readonly Geometry?[] _shapes = new Geometry?[128];

    private KeyboardModeLayout(int lowest, int highest, double width, double height, double lookAheadMs)
    {
        Lowest = lowest; Highest = highest; Width = width; Height = height;
        Whites = WhiteCount(lowest, highest);
        WhiteWidth = (width - 2 * SideMargin) / Math.Max(1, Whites);
        Left = (width - WhiteWidth * Whites) / 2;
        StripHeight = Math.Clamp(WhiteWidth * WhiteLengthRatio, Math.Min(MinStripHeight, StripCap(height)), StripCap(height));
        StripTop = height - StripHeight;
        BlackHeight = StripHeight * BlackLengthShare;
        PxPerMs = Math.Min(StripTop / Math.Max(500, lookAheadMs), MaxPxPerSecond / 1000);
        var white = 0;
        for (var m = lowest; m <= highest; m++)
        {
            if (IsWhite(m)) { _x[m] = Left + white * WhiteWidth; _w[m] = WhiteWidth; white++; }
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

    public static bool IsWhite(int midi) => !KeyboardNoteSource.IsBlackKey(midi);

    private static double StripCap(double height) => Math.Max(24, height * MaxStripShare);

    private static int WhiteCount(int lowest, int highest)
    {
        var n = 0;
        for (var m = lowest; m <= highest; m++) if (IsWhite(m)) n++;
        return n;
    }

    /// <summary>The key window to draw for a source's range: widened by whole octaves (alternately down and up, inside the 88 keys) to at least <see cref="MinKeys"/> keys.</summary>
    public static (int Lowest, int Highest) Window(int lowest, int highest) => Widen(lowest, highest, (lo, hi) => hi - lo + 1 >= MinKeys);

    /// <summary>The window for a view: <see cref="Window"/>, then more octaves until the strip at real proportions fits the height cap (or the 88 keys show).</summary>
    public static (int Lowest, int Highest) WindowFor(int lowest, int highest, double width, double height)
    {
        var (lo, hi) = Window(lowest, highest);
        var cap = StripCap(height);
        return Widen(lo, hi, (l, h) => (width - 2 * SideMargin) / Math.Max(1, WhiteCount(l, h)) * WhiteLengthRatio <= cap);
    }

    private static (int, int) Widen(int lowest, int highest, Func<int, int, bool> enough)
    {
        lowest = Math.Clamp(lowest, KeyboardNoteSource.LowestKey, KeyboardNoteSource.HighestKey);
        highest = Math.Clamp(highest, lowest, KeyboardNoteSource.HighestKey);
        var down = true;
        while (!enough(lowest, highest))
        {
            var canDown = lowest > KeyboardNoteSource.LowestKey;
            var canUp = highest < KeyboardNoteSource.HighestKey;
            if (!canDown && !canUp) break;
            if (canDown && (down || !canUp)) lowest = Math.Max(KeyboardNoteSource.LowestKey, lowest - 12);
            else highest = Math.Min(KeyboardNoteSource.HighestKey, highest + 12);
            down = !down;
        }
        return (lowest, highest);
    }

    public static KeyboardModeLayout Compute(int lowest, int highest, double width, double height, double lookAheadMs)
    {
        width = Math.Max(200, width); height = Math.Max(MinHeight, height);
        var (lo, hi) = WindowFor(lowest, highest, width, height);
        return new KeyboardModeLayout(lo, hi, width, height, lookAheadMs);
    }

    public bool Has(int midi) => midi >= Lowest && midi <= Highest;
    public double KeyX(int midi) => _x[Math.Clamp(midi, Lowest, Highest)];
    public double KeyWidth(int midi) => _w[Math.Clamp(midi, Lowest, Highest)];
    public double KeyCentre(int midi) => KeyX(midi) + KeyWidth(midi) / 2;

    /// <summary>The key's rectangle on the strip (a black key is shorter than a white one).</summary>
    public Rect KeyRect(int midi) => new(KeyX(midi), StripTop, KeyWidth(midi), IsWhite(midi) ? StripHeight : BlackHeight);

    /// <summary>The key's own outline: a black key's rectangle, or a white key's rectangle minus the black keys that lie over it (frozen, built once per layout).</summary>
    public Geometry KeyShape(int midi)
    {
        midi = Math.Clamp(midi, Lowest, Highest);
        if (_shapes[midi] is { } done) return done;
        var r = KeyRect(midi);
        Geometry shape;
        if (!IsWhite(midi)) shape = new RectangleGeometry(r);
        else
        {
            // The notches: the black neighbour's edge inside this key, or the key's own edge when there is none.
            var leftIn = midi - 1 >= Lowest && !IsWhite(midi - 1) ? Math.Max(r.Left, KeyX(midi - 1) + KeyWidth(midi - 1)) : r.Left;
            var rightIn = midi + 1 <= Highest && !IsWhite(midi + 1) ? Math.Min(r.Right, KeyX(midi + 1)) : r.Right;
            var notch = r.Top + BlackHeight;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(leftIn, r.Top), true, true);
                c.LineTo(new Point(rightIn, r.Top), false, false);
                if (rightIn < r.Right) { c.LineTo(new Point(rightIn, notch), false, false); c.LineTo(new Point(r.Right, notch), false, false); }
                c.LineTo(new Point(r.Right, r.Bottom), false, false);
                c.LineTo(new Point(r.Left, r.Bottom), false, false);
                if (leftIn > r.Left) { c.LineTo(new Point(r.Left, notch), false, false); c.LineTo(new Point(leftIn, notch), false, false); }
            }
            shape = g;
        }
        shape.Freeze();
        return _shapes[midi] = shape;
    }

    /// <summary>The y of a moment <paramref name="msAhead"/> after the song time (0 is the strip's top, larger is higher up).</summary>
    public double YAhead(double msAhead) => StripTop - msAhead * PxPerMs;
}
