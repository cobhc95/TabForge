using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

/// <summary>The areas of a score that carry their own text style.</summary>
internal enum ScoreTextArea { General, Header, BarInfo, Fret, Technique, Chord, Lyrics }

/// <summary>
/// Score text: the shared font settings, the per-area styles, the bounded caches of typefaces and laid-out text, and the frozen brush
/// and draw helpers every score drawing uses. State is process-wide, so every editor and the staff engraver draw with the same style.
/// </summary>
internal static class ScoreText
{
    private static string _fontFamily = "Segoe UI";
    private static double _textSize = 13.5;
    private static bool _textBold;
    private static bool _textItalic;

    /// <summary>The configured score text size in points.</summary>
    internal static double TextSize => _textSize;

    /// <summary>The ink of the page being drawn: a coloured area style leaves highlight colours (playing, selected) alone.</summary>
    internal static Color NormalInk = Colors.White;

    // Frozen brushes are shared, so a repaint reuses them instead of allocating a brush per glyph.
    // The cache is bounded by the number of distinct colours in the palette (a few dozen).
    internal static SolidColorBrush Brush(Color colour) => (SolidColorBrush)RenderDraw.Solid(colour);

    internal static void ConfigureStyle(string? fontFamily, double size, bool bold, bool italic)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        try { _ = new FontFamily(family); }
        catch (ArgumentException) { family = "Segoe UI"; } // not a usable family name
        var clampedSize = Math.Clamp(size, 8, 24);
        if (_fontFamily == family && Math.Abs(_textSize - clampedSize) < 0.001 &&
            _textBold == bold && _textItalic == italic) return;
        _fontFamily = family;
        _textSize = clampedSize;
        _textBold = bold;
        _textItalic = italic;
        TypefaceCache.Clear();
        TextCache.Clear();
    }

    private const double DynamicFontSize = 13;

    private static readonly Dictionary<(string Name, uint Colour, int Dpi), FormattedText> DynamicTextCache = new();

    /// <summary>Bold italic serif letters, the usual engraving of a dynamic.</summary>
    internal static FormattedText DynamicText(string name, Color colour)
    {
        var dpi = (int)Math.Round(RenderDraw.PixelsPerDip * 1000);
        var key = (name, ColourKey(colour), dpi);
        if (DynamicTextCache.TryGetValue(key, out var cached)) return cached;
        var created = new FormattedText(name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TypefaceFor("Times New Roman", FontWeights.Bold, true), DynamicFontSize, Brush(colour), RenderDraw.PixelsPerDip);
        if (DynamicTextCache.Count >= 256) DynamicTextCache.Clear();
        DynamicTextCache[key] = created;
        return created;
    }

    // Text layout and typeface creation are the most expensive part of a custom drawing pass, and a
    // playback repaint redraws the same fret numbers, technique labels and headers every time. Frozen
    // brushes plus bounded caches keep that work out of the playback loop.
    private static readonly Dictionary<(string Font, int Weight, bool Italic), Typeface> TypefaceCache = new();
    private static readonly Dictionary<(string Text, int Size, uint Colour, int Weight, string Font, bool Italic, int Area, int Dpi), FormattedText> TextCache = new();
    private const int TextCacheLimit = 4096;

    internal static uint ColourKey(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    /// <summary>Shared cached text layout (also used by the staff engraver).</summary>
    internal static FormattedText CachedText(string text, double size, Brush brush, FontWeight? weight, string font) =>
        MakeText(text, size, brush, weight, font);

    internal static Typeface TypefaceFor(string font, FontWeight weight) => TypefaceFor(font, weight, _textItalic);

    internal static Typeface TypefaceFor(string font, FontWeight weight, bool italic)
    {
        var key = (font, weight.ToOpenTypeWeight(), italic);
        if (TypefaceCache.TryGetValue(key, out var cached)) return cached;
        var created = new Typeface(new FontFamily(font), italic ? FontStyles.Italic : FontStyles.Normal, weight, FontStretches.Normal);
        TypefaceCache[key] = created;
        return created;
    }

    internal static FormattedText MakeText(string text, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
        => MakeTextIn(ScoreTextArea.General, text, size, brush, weight, font);

    internal static FormattedText MakeTextIn(ScoreTextArea area, string text, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var style = AreaStyles[(int)area];
        var bold = style.Bold ?? _textBold;
        var italic = style.Italic ?? _textItalic;
        var effectiveWeight = bold ? FontWeights.Bold : style.Bold == false ? FontWeights.Normal : weight ?? FontWeights.Normal;
        var effectiveSize = size * (_textSize / 12.0) * style.Scale;
        var effectiveFont = style.Font ?? _fontFamily;
        // A colour override replaces the normal text colour; highlight colours (playing / selected notes) are kept.
        if (style.Colour is { } overrideColour && brush is SolidColorBrush normal &&
            (area != ScoreTextArea.Fret || normal.Color == NormalInk))
            brush = Brush(overrideColour);
        var colour = brush is SolidColorBrush solid ? ColourKey(solid.Color) : 0u;
        var pixelsPerDip = TabForge.Visualization.Draw.PixelsPerDip;   // this editor's display DPI (the scope is opened in OnRender)
        var key = (text, (int)Math.Round(effectiveSize * 4), colour, effectiveWeight.ToOpenTypeWeight(), effectiveFont, italic, (int)area, (int)Math.Round(pixelsPerDip * 1000));
        if (TextCache.TryGetValue(key, out var cached)) return cached;
        var created = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TypefaceFor(effectiveFont, effectiveWeight, italic), effectiveSize, brush, pixelsPerDip);
        if (style.Outline is { } outline) TabForge.Visualization.Draw.SetOutline(created, outline);
        if (TextCache.Count >= TextCacheLimit) TextCache.Clear();
        TextCache[key] = created;
        return created;
    }

    internal static void DrawIn(ScoreTextArea area, DrawingContext dc, string text, double x, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
        => TabForge.Visualization.Draw.DrawText(dc, MakeTextIn(area, text, size, brush, weight, font), new Point(x, y));

    internal static void DrawCenteredIn(ScoreTextArea area, DrawingContext dc, string text, double cx, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var ft = MakeTextIn(area, text, size, brush, weight, font);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, y));
    }

    // ---- per-area text styles (Score > Appearance > Text & fonts) ----
    private sealed record AreaStyle(string? Font, double Scale, bool? Bold, bool? Italic, Color? Colour, Pen? Outline);
    private static readonly AreaStyle[] AreaStyles = Enumerable.Repeat(new AreaStyle(null, 1, null, null, null, null), 7).ToArray();

    internal static void ConfigureAreas(IReadOnlyDictionary<string, TabForge.Services.ScoreTextAreaStyle>? styles)
    {
        for (var i = 0; i < AreaStyles.Length; i++)
        {
            var name = ((ScoreTextArea)i).ToString();
            if (styles is null || !styles.TryGetValue(name, out var s) || s is null) { AreaStyles[i] = new AreaStyle(null, 1, null, null, null, null); continue; }
            Color? colour = TabForge.Views.ColourChooser.TryParse(s.Colour, out var c) ? c : null;
            Pen? outline = null;
            if (s.OutlineThickness > 0 && TabForge.Views.ColourChooser.TryParse(s.OutlineColour, out var oc))
            {
                outline = new Pen(new SolidColorBrush(oc), s.OutlineThickness * 2) { LineJoin = PenLineJoin.Round };
                outline.Freeze();
            }
            AreaStyles[i] = new AreaStyle(string.IsNullOrWhiteSpace(s.Font) ? null : s.Font,
                Math.Clamp(s.SizePercent <= 0 ? 1 : s.SizePercent / 100.0, 0.4, 3), s.Bold, s.Italic, colour, outline);
        }
        TextCache.Clear();
        TypefaceCache.Clear();
    }
    internal static void Draw(DrawingContext dc, string text, double x, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
        => TabForge.Visualization.Draw.DrawText(dc, MakeText(text, size, brush, weight, font), new Point(x, y));

    internal static void DrawCentered(DrawingContext dc, string text, double cx, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var ft = MakeText(text, size, brush, weight, font);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, y));
    }}
