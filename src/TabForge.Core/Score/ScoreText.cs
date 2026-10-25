using System.Linq;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Views.Score;

/// <summary>
/// Score text: the shared font settings, the per-area styles, and the lay-out and draw helpers every score drawing uses, through
/// <see cref="IDrawContext.Text"/>. State is process-wide, so every editor and the staff engraver draw with the same style.
/// Owns: the font settings and area styles and how they turn a request into a text look. Does not own: shaping or its cache (the drawing
/// adapter's shared text cache, keyed by the whole look), colours (the callers').
/// Tests: no dedicated test class.
/// </summary>
internal static class ScoreText
{
    // Process-wide text style, kept as ONE immutable snapshot swapped whole (a layout thread never sees a half-applied change). Key: none;
    // every score uses this one family, size, bold and italic. Ceiling: one snapshot, the size clamped to 8 to 24 points and 7 fixed area styles.
    // Key: none; the default look of an area without a style; never written.
    private static readonly AreaStyle Plain = new(null, 1, null, null, null, null);
    private sealed record Look(string FontFamily, double TextSize, bool Bold, bool Italic, AreaStyle[] Areas, UiColor Ink);
    // The starting ink is a placeholder: ScoreGeometryProbe sets the theme's ink before anything is drawn (same value the field had before the snapshot).
    private static readonly UiColor PlaceholderInk = new(255, 255, 255, 255);
    private static volatile Look _look = new("Segoe UI", 13.5, false, false, Enumerable.Repeat(Plain, 7).ToArray(), PlaceholderInk);

    /// <summary>The one place the look changes: <paramref name="change"/> builds the new snapshot from the current one and it is swapped in whole; two concurrent changes both land (the loser rebuilds on the winner's snapshot).</summary>
    private static void Configure(Func<Look, Look> change)
    {
        while (true)
        {
            var current = _look;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _look, change(current), current), current)) return;
        }
    }

    /// <summary>The configured score text size in points.</summary>
    internal static double TextSize => _look.TextSize;

    /// <summary>The configured score text family (Segoe UI when none is set), and its bold and italic flags.</summary>
    internal static string FontFamily => _look.FontFamily;
    internal static bool TextBold => _look.Bold;
    internal static bool TextItalic => _look.Italic;

    /// <summary>The ink of the page being drawn: a coloured area style leaves highlight colours (playing, selected) alone.</summary>
    // Key: none; ceiling: one colour, held in the look snapshot; set by the editor and the geometry probe before a draw pass.
    internal static UiColor NormalInk
    {
        get => _look.Ink;
        set => Configure(look => look with { Ink = value });
    }

    /// <summary>Applies the score text settings; a family that is not installed falls back to the default score font.</summary>
    internal static void ConfigureStyle(TabForge.Services.AppearanceSettings appearance, ITextMeasure fonts)
    {
        var family = appearance.ScoreFontFamily;
        ConfigureStyle(family is not null && fonts.FontExists(family.Trim()) ? family : null,
            appearance.ScoreTextSize, appearance.ScoreTextBold, appearance.ScoreTextItalic);
    }

    /// <param name="fontFamily">A family the caller has checked exists; blank means Segoe UI.</param>
    internal static void ConfigureStyle(string? fontFamily, double size, bool bold, bool italic)
    {
        Configure(look => look with { FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim(), TextSize = Math.Clamp(size, 8, 24), Bold = bold, Italic = italic });
    }

    private const double DynamicFontSize = 13;

    /// <summary>Bold italic serif letters, the usual engraving of a dynamic.</summary>
    internal static UiText DynamicText(IDrawContext d, string name, UiColor colour) =>
        d.Text(name, "Times New Roman", DynamicFontSize, colour, d.PixelsPerDip, weight: 700, italic: true, idealLayout: true);

    /// <summary>Beat text ink: mixed toward the faint ink so it reads as secondary text.</summary>
    internal static UiColor BeatTextColor(UiColor ink, UiColor faint) => new(255,
        (byte)((ink.R + faint.R * 2) / 3), (byte)((ink.G + faint.G * 2) / 3), (byte)((ink.B + faint.B * 2) / 3));

    // The ink layout measures with: white, so a measured width shares the cache entry of white-ink drawing. Width does not depend on ink.
    // Key: none; ceiling: one fixed colour; thread: any, it is never written.
    private static readonly UiColor MeasureInk = new(255, 255, 255, 255);

    /// <summary>Laid-out width of score text for layout: the same cached layout the white-ink drawing uses.</summary>
    /// <param name="weight">OpenType weight, 400 normal, 700 bold.</param>
    internal static double Width(IDrawContext d, ScoreTextArea area, string text, double size, int weight = 400, string font = "Segoe UI") =>
        MakeTextIn(d, area, text, size, MeasureInk, weight, font).Width;

    /// <summary>Laid-out width of a dynamic's letters, from the same cache the drawing uses.</summary>
    internal static double DynamicWidth(IDrawContext d, string name) => DynamicText(d, name, MeasureInk).Width;

    internal static UiText MakeText(IDrawContext d, string text, double size, UiColor colour, int? weight = null, string font = "Segoe UI")
        => MakeTextIn(d, ScoreTextArea.General, text, size, colour, weight, font);

    /// <param name="weight">OpenType weight; null is normal.</param>
    internal static UiText MakeTextIn(IDrawContext d, ScoreTextArea area, string text, double size, UiColor colour, int? weight = null, string font = "Segoe UI")
    {
        var look = _look;   // read once: the whole call sees one look
        var style = look.Areas[(int)area];
        var bold = style.Bold ?? look.Bold;
        var italic = style.Italic ?? look.Italic;
        var effectiveWeight = bold ? 700 : style.Bold == false ? 400 : weight ?? 400;
        var effectiveSize = size * (look.TextSize / 12.0) * style.Scale;
        // A colour override replaces the normal text colour; highlight colours (playing / selected notes) are kept.
        if (style.Colour is { } overrideColour && (area != ScoreTextArea.Fret || colour == look.Ink)) colour = overrideColour;
        return d.Text(text, style.Font ?? look.FontFamily, effectiveSize, colour, d.PixelsPerDip, weight: effectiveWeight, italic: italic,
            outline: style.Outline, idealLayout: true);
    }

    internal static void DrawIn(ScoreTextArea area, IDrawContext d, string text, double x, double y, double size, UiColor colour, int? weight = null, string font = "Segoe UI")
        => d.DrawText(MakeTextIn(d, area, text, size, colour, weight, font), x, y);

    internal static void DrawCenteredIn(ScoreTextArea area, IDrawContext d, string text, double cx, double y, double size, UiColor colour, int? weight = null, string font = "Segoe UI")
    {
        var t = MakeTextIn(d, area, text, size, colour, weight, font);
        d.DrawText(t, cx - t.Width / 2, y);
    }

    internal static void Draw(IDrawContext d, string text, double x, double y, double size, UiColor colour, int? weight = null, string font = "Segoe UI")
        => DrawIn(ScoreTextArea.General, d, text, x, y, size, colour, weight, font);

    internal static void DrawCentered(IDrawContext d, string text, double cx, double y, double size, UiColor colour, int? weight = null, string font = "Segoe UI")
        => DrawCenteredIn(ScoreTextArea.General, d, text, cx, y, size, colour, weight, font);

    /// <summary>Draws laid-out text with its top left at (<paramref name="x"/>, <paramref name="y"/>), outline included.</summary>
    internal static void DrawFormatted(IDrawContext d, in UiText text, double x, double y) => d.DrawText(text, x, y);

    // ---- per-area text styles (Score > Appearance > Text & fonts) ----
    private sealed record AreaStyle(string? Font, double Scale, bool? Bold, bool? Italic, UiColor? Colour, UiStroke? Outline);

    internal static void ConfigureAreas(IReadOnlyDictionary<string, TabForge.Services.ScoreTextAreaStyle>? styles)
    {
        var areas = new AreaStyle[7];   // built whole, then swapped in with the rest of the look
        for (var i = 0; i < areas.Length; i++)
        {
            var name = ((ScoreTextArea)i).ToString();
            if (styles is null || !styles.TryGetValue(name, out var s) || s is null) { areas[i] = Plain; continue; }
            UiColor? colour = ColourHex.TryParse(s.Colour, out var c) ? c.ToUi() : null;
            UiStroke? outline = s.OutlineThickness > 0 && ColourHex.TryParse(s.OutlineColour, out var oc)
                ? new UiStroke(oc.ToUi(), s.OutlineThickness * 2, RoundJoins: true) : null;
            areas[i] = new AreaStyle(string.IsNullOrWhiteSpace(s.Font) ? null : s.Font,
                Math.Clamp(s.SizePercent <= 0 ? 1 : s.SizePercent / 100.0, 0.4, 3), s.Bold, s.Italic, colour, outline);
        }
        Configure(look => look with { Areas = areas });
    }

}

/// <summary>The score layout's text widths, read from <see cref="ScoreText"/> through a drawing context used only to lay text out.</summary>
internal sealed class ScoreTextWidths(IDrawContext measure) : IScoreTextWidths
{
    public double Width(ScoreTextArea area, string text, double size, int weight = 400, string font = "Segoe UI") => ScoreText.Width(measure, area, text, size, weight, font);
    public double DynamicWidth(string name) => ScoreText.DynamicWidth(measure, name);
}
