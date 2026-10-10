using System.Windows.Media;

namespace TabForge.KeyboardMode;

// Owns: the colours of the keyboard falling-notes view in the dark and the light theme, as frozen brushes and pens: one colour per hand (a blue and an amber that differ in brightness too, so the
//   pair reads for colour-blind viewers), the keys, held-key colours that are also told apart by a mark (a dot for a right key, a cross for an extra one) and the grade flash text colours.
//   Each note carries its hand (KeyboardHands); lit keys fill their own outline with a gradient of their state's colour, hit and missed bars get a green or red edge.
// Does not own: drawing, or which theme is on (KeyboardModePalette.Keyboard picks by the pane's theme).
// Tests: TestKeyboardModeKeyView (contrast of text colours on their ground).
public sealed class KeyboardColours
{
    private KeyboardColours(bool dark, Color left, Color leftEdge, Color right, Color rightEdge, Color white, Color black, Color seam, Color keyLabel, Color okKey, Color extraKey, Color mark,
        Color perfect, Color good, Color slip, Color miss, Color guide, Color panel, Color wait)
    {
        Dark = dark;
        Left = Brush(left); Right = Brush(right);
        LeftEdge = Pen(leftEdge, 1.2); RightEdge = Pen(rightEdge, 1.2);
        LeftColour = left; RightColour = right;
        LeftText = Brush(KeyboardModePalette.TextOn(left)); RightText = Brush(KeyboardModePalette.TextOn(right));
        White = Brush(white); Black = Brush(black); Seam = Pen(seam, 1);
        KeyLabelColour = keyLabel; WhiteColour = white;
        KeyLabel = Brush(keyLabel);
        OkKey = Brush(okKey); ExtraKey = Brush(extraKey); Mark = Brush(mark); MarkPen = Pen(mark, 2);
        PerfectColour = perfect; GoodColour = good; SlipColour = slip; MissColour = miss;
        Perfect = Brush(perfect); Good = Brush(good); Slip = Brush(slip); Miss = Brush(miss);
        Octave = Pen(Color.FromArgb(0x70, guide.R, guide.G, guide.B), 1.2); Guide = Pen(Color.FromArgb(0x28, guide.R, guide.G, guide.B), 1);
        Backdrop = Brush(Color.FromArgb(0xE6, panel.R, panel.G, panel.B)); BackdropEdge = Pen(Color.FromArgb(0x80, guide.R, guide.G, guide.B), 1);
        Cap = Pen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 2);
        WaitColour = wait; WaitPen = Pen(wait, 3);
        LitLeft = Lit(left); LitRight = Lit(right); LitOk = Lit(okKey); LitExtra = Lit(extraKey);
        OkColour = okKey; ExtraColour = extraKey;
        HitEdge = Pen(okKey, 2.5); MissEdge = Pen(extraKey, 2.5);
        Glow = Pen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 1);
    }

    /// <summary>A lit key: its colour, brighter where the note lands (the key's top) and a little deeper at its front.</summary>
    private static LinearGradientBrush Lit(Color c)
    {
        var b = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.55), 0));
        b.GradientStops.Add(new GradientStop(c, 0.45));
        b.GradientStops.Add(new GradientStop(Mix(c, Colors.Black, 0.18), 1));
        b.Freeze();
        return b;
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public bool Dark { get; }
    public SolidColorBrush Left { get; }
    public SolidColorBrush Right { get; }
    public Pen LeftEdge { get; }
    public Pen RightEdge { get; }
    /// <summary>The note name written on a bar: white or near-black, whichever reads better on the hand's colour.</summary>
    public SolidColorBrush LeftText { get; }
    public SolidColorBrush RightText { get; }
    public Color LeftColour { get; }
    public Color RightColour { get; }
    /// <summary>A key lit while its note is due (its hand's colour), held right (green) or held extra (red): a vertical gradient filled within the key's outline.</summary>
    public Brush LitLeft { get; }
    public Brush LitRight { get; }
    public Brush LitOk { get; }
    public Brush LitExtra { get; }
    public Color OkColour { get; }
    public Color ExtraColour { get; }
    /// <summary>The edge of a falling bar that was hit (green) or missed (red).</summary>
    public Pen HitEdge { get; }
    public Pen MissEdge { get; }
    /// <summary>The inner glow line of a lit key (its width is set per layout by the painter).</summary>
    public Pen Glow { get; }
    public SolidColorBrush White { get; }
    public Color WhiteColour { get; }
    public SolidColorBrush Black { get; }
    public Pen Seam { get; }
    /// <summary>The note name written on a C (text on a white key).</summary>
    public SolidColorBrush KeyLabel { get; }
    public Color KeyLabelColour { get; }
    /// <summary>A held key that hit a note, and one that matched nothing.</summary>
    public SolidColorBrush OkKey { get; }
    public SolidColorBrush ExtraKey { get; }
    /// <summary>The dot or cross drawn on a held key (so its state does not rest on colour alone).</summary>
    public SolidColorBrush Mark { get; }
    public Pen MarkPen { get; }
    public SolidColorBrush Perfect { get; }
    public SolidColorBrush Good { get; }
    public SolidColorBrush Slip { get; }
    public SolidColorBrush Miss { get; }
    public Color PerfectColour { get; }
    public Color GoodColour { get; }
    public Color SlipColour { get; }
    public Color MissColour { get; }
    /// <summary>The octave lines (at each C) and the fainter white-key lines of the falling area.</summary>
    public Pen Octave { get; }
    public Pen Guide { get; }
    /// <summary>The ground of the grade flash, the score panel and the waiting cue.</summary>
    public SolidColorBrush Backdrop { get; }
    public Pen BackdropEdge { get; }
    /// <summary>The bright line at a note's start.</summary>
    public Pen Cap { get; }
    /// <summary>The outline of a key the song waits for.</summary>
    public Pen WaitPen { get; }
    public Color WaitColour { get; }

    public static KeyboardColours For(bool dark) => dark ? DarkSet : LightSet;

    public SolidColorBrush HandBrush(bool leftHand) => leftHand ? Left : Right;
    public SolidColorBrush HandText(bool leftHand) => leftHand ? LeftText : RightText;
    public Pen HandEdge(bool leftHand) => leftHand ? LeftEdge : RightEdge;
    public Brush LitHand(bool leftHand) => leftHand ? LitLeft : LitRight;

    /// <summary>The flash text colour of a grade.</summary>
    public SolidColorBrush TextOf(KeyGradeKind kind) => kind switch { KeyGradeKind.Perfect => Perfect, KeyGradeKind.Good => Good, KeyGradeKind.Miss => Miss, _ => Slip };
    public Color ColourOf(KeyGradeKind kind) => kind switch { KeyGradeKind.Perfect => PerfectColour, KeyGradeKind.Good => GoodColour, KeyGradeKind.Miss => MissColour, _ => SlipColour };

    private static readonly KeyboardColours DarkSet = new(true,
        left: Rgb(0x3B, 0x9E, 0xF0), leftEdge: Rgb(0x1E, 0x5F, 0xA8), right: Rgb(0xFB, 0xBF, 0x24), rightEdge: Rgb(0xB4, 0x7A, 0x06),
        white: Rgb(0xF1, 0xF5, 0xF9), black: Rgb(0x0B, 0x12, 0x20), seam: Rgb(0x64, 0x74, 0x8B), keyLabel: Rgb(0x33, 0x41, 0x55),
        okKey: Rgb(0x4A, 0xDE, 0x80), extraKey: Rgb(0xF8, 0x71, 0x71), mark: Rgb(0x0B, 0x12, 0x20),
        perfect: Rgb(0x4A, 0xDE, 0x80), good: Rgb(0x7D, 0xD3, 0xFC), slip: Rgb(0xFC, 0xD3, 0x4D), miss: Rgb(0xFC, 0xA5, 0xA5),
        guide: Rgb(0xB8, 0xC4, 0xDC), panel: Rgb(0x0E, 0x15, 0x26), wait: Rgb(0xFF, 0x8A, 0x1F));

    private static readonly KeyboardColours LightSet = new(false,
        left: Rgb(0x1E, 0x40, 0xAF), leftEdge: Rgb(0x17, 0x25, 0x54), right: Rgb(0xB4, 0x53, 0x09), rightEdge: Rgb(0x7C, 0x2D, 0x12),
        white: Rgb(0xFF, 0xFF, 0xFF), black: Rgb(0x1E, 0x29, 0x3B), seam: Rgb(0x94, 0xA3, 0xB8), keyLabel: Rgb(0x33, 0x41, 0x55),
        okKey: Rgb(0x22, 0xA5, 0x5E), extraKey: Rgb(0xDC, 0x26, 0x26), mark: Rgb(0xFF, 0xFF, 0xFF),
        perfect: Rgb(0x16, 0x65, 0x34), good: Rgb(0x1D, 0x4E, 0xD8), slip: Rgb(0x85, 0x4D, 0x0E), miss: Rgb(0xB9, 0x1C, 0x1C),
        guide: Rgb(0x2B, 0x36, 0x50), panel: Rgb(0xEE, 0xF2, 0xF9), wait: Rgb(0xD9, 0x68, 0x0A));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen Pen(Color c, double thickness) { var p = new Pen(Brush(c), thickness); p.Freeze(); return p; }
}
