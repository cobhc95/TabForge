using System.Globalization;

namespace TabForge.Models;

// Owns: how a bend or tremolo-bar curve runs between two points (the ease of a bend, which the editor draws and playback follows; a tremolo bar runs straight) and the tone scale texts of the editors.
// Does not own: the point list (EffectCurve), the drawing (CurveEditorView) or the pitch wheel arithmetic (ScoreToMidiCompiler.PitchCurves).
// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// <summary>The shared shape of a curve: eased like a guitar string bend (slow start, quick middle, slow arrival), and the tone scale of the axis.</summary>
public static class EffectCurveShape
{
    /// <summary>The model's values per whole tone: a value of 1 is a quarter tone, 4 a whole tone ("Full").</summary>
    public const double ValuesPerTone = 4;

    /// <summary>The eased progress (0..1) of a segment that is <paramref name="t"/> (0..1) of the way along it.</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The value on the eased segment from <paramref name="from"/> to <paramref name="to"/> at <paramref name="t"/> (0..1) along it.</summary>
    public static double Between(double from, double to, double t) => from + (to - from) * Ease(t);

    /// <summary>
    /// The axis text of a value in tones: 0, a quarter, a half, three quarters, Full, 1 and a quarter ... for a bend; a tremolo bar shows signed numbers (-3, +1 and a half).
    /// </summary>
    public static string ToneLabel(double value, bool bend)
    {
        var tones = value / ValuesPerTone;
        var size = Math.Abs(tones);
        var whole = Math.Floor(size + 1e-9);
        var quarters = Math.Round((size - whole) * 4);
        string text;
        if (Math.Abs(size - whole - quarters / 4) > 1e-6) text = size.ToString("0.##", CultureInfo.InvariantCulture);
        else
        {
            if (quarters >= 4) { whole++; quarters = 0; }
            var part = quarters switch { 1 => "\u00BC", 2 => "\u00BD", 3 => "\u00BE", _ => "" };
            text = whole == 0 && part.Length > 0 ? part : whole.ToString("0", CultureInfo.InvariantCulture) + part;
        }
        if (size == 0) return "0";
        if (bend && size == 1 && tones > 0) return "Full";
        return (tones < 0 ? "-" : bend ? "" : "+") + text;
    }
}
