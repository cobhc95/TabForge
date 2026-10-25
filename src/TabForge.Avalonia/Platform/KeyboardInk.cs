using TabForge.KeyboardMode;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The colours of the keyboard falling-notes view for one theme, each read from a palette key; the shades (hand ink, edges of hit and missed bars, the panel backdrop, guide lines) are derived from them.
/// Owns: the key-to-colour table of the keyboard view and the derived shades. Does not own: the palette (Core), drawing, or which theme is on (the view picks).
/// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests (text contrast on its ground).
/// </summary>
public sealed class KeyboardInk
{
    /// <summary>Alphas of the derived shades: the octave line, the white-key guide line, the backdrop ground and the backdrop edge.</summary>
    private const byte OctaveAlpha = 0x70, GuideAlpha = 0x28, BackdropAlpha = 0xE6, BackdropEdgeAlpha = 0x80;

    private static readonly KeyboardInk Dark = new(false), Light = new(true);

    private KeyboardInk(bool light)
    {
        UiColor Get(string key) => AvaloniaPalette.Get(key, light);
        var guide = Get("KeyboardGuide");
        var panel = Get("KeyboardPanel");
        var white = Get("White");
        Panel = panel; Strip = Get("KeyboardStrip"); HitLine = Get("KeyboardHitLine"); Label = Get("KeyboardPanelText");
        Left = Get("KeyboardLeftHand"); LeftEdge = Get("KeyboardLeftHandEdge"); Right = Get("KeyboardRightHand"); RightEdge = Get("KeyboardRightHandEdge");
        var ink = Get("KeyboardTextInk");
        LeftText = KeyboardColourMath.TextOn(Left, white, ink); RightText = KeyboardColourMath.TextOn(Right, white, ink);
        WhiteKey = Get("KeyboardWhiteKey"); BlackKey = Get("KeyboardBlackKey"); Seam = Get("KeyboardKeySeam"); KeyLabel = Get("KeyboardKeyLabel");
        Ok = Get("KeyboardHitKey"); Extra = Get("KeyboardExtraKey"); Mark = Get("KeyboardKeyMark");
        Perfect = Get("KeyboardGradePerfect"); Good = Get("KeyboardGradeGood"); Slip = Get("KeyboardGradeSlip"); Miss = Get("KeyboardGradeMiss");
        Octave = guide with { A = OctaveAlpha }; GuideLine = guide with { A = GuideAlpha };
        Backdrop = panel with { A = BackdropAlpha }; BackdropEdge = guide with { A = BackdropEdgeAlpha };
        Cap = Get("KeyboardNoteCap"); Glow = Get("KeyboardKeyGlow"); Wait = Get("KeyboardWaitCue");
    }

    public static KeyboardInk For(bool light) => light ? Light : Dark;

    public UiColor Panel { get; }
    public UiColor Strip { get; }
    public UiColor HitLine { get; }
    /// <summary>Text on the panel (the hint, the score panel).</summary>
    public UiColor Label { get; }
    public UiColor Left { get; }
    public UiColor LeftEdge { get; }
    public UiColor Right { get; }
    public UiColor RightEdge { get; }
    /// <summary>The note name and finger number written on a bar: light or dark ink, whichever reads better on the hand's colour.</summary>
    public UiColor LeftText { get; }
    public UiColor RightText { get; }
    public UiColor WhiteKey { get; }
    public UiColor BlackKey { get; }
    public UiColor Seam { get; }
    /// <summary>The note name written on a C (text on a white key).</summary>
    public UiColor KeyLabel { get; }
    /// <summary>A key held that hit a note (also the edge and dot of a hit bar), and one that matched nothing (also a missed bar).</summary>
    public UiColor Ok { get; }
    public UiColor Extra { get; }
    /// <summary>The check and cross drawn on a held key and a judged bar.</summary>
    public UiColor Mark { get; }
    public UiColor Perfect { get; }
    public UiColor Good { get; }
    public UiColor Slip { get; }
    public UiColor Miss { get; }
    /// <summary>The octave lines (at each C) and the fainter white-key lines of the falling area.</summary>
    public UiColor Octave { get; }
    public UiColor GuideLine { get; }
    /// <summary>The ground and the edge of the grade flash, the score panel and the hand legend.</summary>
    public UiColor Backdrop { get; }
    public UiColor BackdropEdge { get; }
    /// <summary>The bright line at a note's start, and the inner glow line of a lit key.</summary>
    public UiColor Cap { get; }
    public UiColor Glow { get; }
    /// <summary>The cue colour of a key the song waits for.</summary>
    public UiColor Wait { get; }

    public UiColor HandFill(bool leftHand) => leftHand ? Left : Right;
    public UiColor HandEdge(bool leftHand) => leftHand ? LeftEdge : RightEdge;
    public UiColor HandText(bool leftHand) => leftHand ? LeftText : RightText;

    /// <summary>The flash text colour of a grade.</summary>
    public UiColor GradeColour(KeyGradeKind kind) => kind switch { KeyGradeKind.Perfect => Perfect, KeyGradeKind.Good => Good, KeyGradeKind.Miss => Miss, _ => Slip };
}
