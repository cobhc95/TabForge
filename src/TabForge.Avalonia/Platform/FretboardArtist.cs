using System;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>What the artist draws: the surface, the theme colours, the board's layout, the chosen look, the instrument family and the panel's caches.</summary>
internal readonly record struct FretboardScene(IDrawContext D, Func<string, UiColor> Ink, FretboardGeometryRules.Layout Layout, FretboardStyles.Look Look, bool Bass,
    FretboardAssets Assets, FretboardInlayPlan Inlays, string InlayColourKey = "InstrumentWhite", double InlayBrightness = 0);

// Owns: the layered, lit drawing of the fretboard itself: the wood (tiled, never stretched) with edge shading and a soft sheen, the binding,
//   the shaped inlays cut from their material, the bevelled fret wires, the nut and the strings with their cast shadows, all to the
//   real proportions of FretboardRealRules. Every colour is a palette token. A steady frame allocates nothing: shapes are planned once per
//   layout and look (FretboardInlayPlan) and textures are remembered per look (FretboardAssets).
// Does not own: the layout (FretboardGeometryRules), the look names (FretboardStyles), the textures' decoding (FretboardAssets), the notes,
//   markers, capo and legend (InstrumentPanelView.*).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, TabForge.Avalonia.Tests.FretboardStyleTests.
internal static class FretboardArtist
{
    /// <summary>How many straight pieces the bowed part of a vibrating string is drawn in.</summary>
    private const int BowSegments = 16;

    /// <summary>The wood: the string span widened by the margin the outer strings sit in from the edges, as on a real neck.</summary>
    internal static UiRect Wood(in FretboardGeometryRules.Layout layout, bool bass)
    {
        var margin = layout.StringGap * FretboardRealRules.EdgeMarginRatio(bass);
        return new UiRect(layout.Board.X, layout.Board.Y - margin, layout.Board.Width, layout.Board.Height + 2 * margin);
    }

    /// <summary>The binding strip's height in DIPs: none without binding, else a little over a tenth of the string gap.</summary>
    internal static double BindingHeight(in FretboardScene s) =>
        s.Look.Binding == FretboardStyles.None ? 0 : Math.Clamp(s.Layout.StringGap * 0.14, 2.5, 6.5);

    /// <summary>The wood and its binding together: what the nut covers and the fret wires run across.</summary>
    internal static UiRect Frame(in FretboardScene s)
    {
        var wood = Wood(s.Layout, s.Bass);
        var binding = BindingHeight(s);
        return new UiRect(wood.X, wood.Y - binding, wood.Width, wood.Height + 2 * binding);
    }

    /// <summary>Everything under the strings and inlays: shadow, wood, lighting and binding.</summary>
    internal static void DrawBackdrop(in FretboardScene s)
    {
        var d = s.D;
        var wood = Wood(s.Layout, s.Bass);
        var boardShadow = s.Ink("FretboardBoardShadow");
        for (var i = 1; i <= 4; i++) d.Rect(boardShadow, null, wood.X + i * 0.7, wood.Y + i * 1.5, wood.Width, wood.Height, 2);
        d.Rect(s.Ink("InstrumentWood"), null, wood.X, wood.Y, wood.Width, wood.Height, 2);
        DrawTiled(d, s.Assets.Wood(s.Look.Wood), wood);
        Lighting(d, s.Ink, wood);
        if (s.Look.Binding != FretboardStyles.None)
        {
            var image = s.Assets.Binding(s.Look.Binding);
            var height = BindingHeight(s);
            BindingStrip(s, image, new UiRect(wood.X, wood.Y - height, wood.Width, height), top: true);
            BindingStrip(s, image, new UiRect(wood.X, wood.Bottom, wood.Width, height), top: false);
        }
        d.Rect(null, new UiStroke(s.Ink("FretboardInlayRim"), 0.8), wood.X, wood.Y, wood.Width, wood.Height, 2);
    }

    /// <summary>The wood texture at its own aspect: one centred crop when a tile covers the board, else a row of tiles.</summary>
    private static void DrawTiled(IDrawContext d, IUiImage? image, UiRect box)
    {
        if (FretboardAssets.SizeOf(image) is not { } size || AvaloniaDrawContext.ClipsAreSkipped || box.Width <= 0 || box.Height <= 0) return;
        var tiles = FretboardWoodTiling.TileCount(box.Width, box.Height, size.Width, size.Height);
        if (tiles <= 0) return;
        d.PushClip(box.X, box.Y, box.Width, box.Height);
        for (var i = 0; i < tiles; i++)
        {
            var tile = FretboardWoodTiling.TileRect(i, box.X, box.Y, box.Width, box.Height, size.Width, size.Height);
            d.Image(image!, tile.X, tile.Y, tile.Width, tile.Height);
        }
        d.Pop();
    }

    /// <summary>Soft light on the wood: the edges fall into shade and a gentle sheen lies along the middle (stacked translucent bands).</summary>
    private static void Lighting(IDrawContext d, Func<string, UiColor> ink, UiRect wood)
    {
        var shade = ink("FretboardWoodShade");
        var reach = Math.Min(wood.Height * 0.3, 14);
        const int Bands = 7;
        for (var i = 0; i < Bands; i++)
        {
            var h = reach * (Bands - i) / Bands;
            var alpha = 0.16 * (Bands - i) / Bands;
            d.Rect(shade.WithOpacity(alpha), null, wood.X, wood.Y, wood.Width, h);
            d.Rect(shade.WithOpacity(alpha), null, wood.X, wood.Bottom - h, wood.Width, h);
        }
        var sheen = ink("FretboardWoodSheen");
        for (var i = 0; i < 5; i++)
        {
            var h = wood.Height * (0.62 - i * 0.11);
            d.Rect(sheen, null, wood.X, wood.Y + (wood.Height - h) / 2, wood.Width, h);
        }
    }

    /// <summary>One binding strip: a slice of the binding material running with its grain.</summary>
    private static void BindingStrip(in FretboardScene s, IUiImage? image, UiRect strip, bool top)
    {
        s.D.Rect(s.Ink("FretboardNutBody"), null, strip.X, strip.Y, strip.Width, strip.Height, 1);
        if (FretboardAssets.SizeOf(image) is { } size && !AvaloniaDrawContext.ClipsAreSkipped)
        {
            // Cover the strip's width at the material's own aspect and show the middle slice of it.
            var drawHeight = strip.Width * size.Height / size.Width;
            s.D.PushClip(strip.X, strip.Y, strip.Width, strip.Height);
            s.D.Image(image!, strip.X, strip.Y + (strip.Height - drawHeight) / 2, strip.Width, drawHeight);
            s.D.Pop();
        }
        // Bevel: light catches the upper part of the strip, the lower part falls into shade (the outer edge on the bottom strip, the seam with the wood on the top one).
        var light = s.Ink("FretboardBindingLight");
        var shade = s.Ink("FretboardBindingShade");
        var band = Math.Max(0.8, strip.Height * 0.38);
        s.D.Rect(light, null, strip.X, strip.Y, strip.Width, band, 1);
        s.D.Rect(shade, null, strip.X, strip.Bottom - band, strip.Width, band, 1);
        var seamY = top ? strip.Bottom - 0.6 : strip.Y;
        s.D.Rect(s.Ink("FretboardNutShadow"), null, strip.X, seamY, strip.Width, 0.6);
        s.D.Rect(null, new UiStroke(s.Ink("FretboardInlayRim"), 0.7), strip.X, strip.Y, strip.Width, strip.Height, 1);
    }

    /// <summary>The position inlays at their frets, cut from the chosen material and set into the wood.</summary>
    internal static void DrawInlays(in FretboardScene s)
    {
        var d = s.D;
        var material = s.Assets.Inlay(s.Look.InlayMaterial);
        var clipped = d is AvaloniaDrawContext && !AvaloniaDrawContext.ClipsAreSkipped && material is not null;
        foreach (var inlay in s.Inlays.For(s))
        {
            d.PushTranslate(0.8, 1.2);
            d.Path(s.Ink("FretboardInlayHole"), null, inlay.Path);
            d.Pop();
            if (clipped)
            {
                // The material fills the shape's box at its own square aspect, clipped to the shape: never stretched.
                ((AvaloniaDrawContext)d).PushClip(inlay.Path);
                d.Image(material!, inlay.X - inlay.Side / 2, inlay.Y - inlay.Side / 2, inlay.Side, inlay.Side);
                d.Pop();
            }
            else d.Path(s.Ink("FretboardInlayFallback"), null, inlay.Path);
            // These are decorative position inlays, NOT the now/next note dots. Tint the material without hiding its grain.
            // The old settings reached Look but were lost when textured inlays replaced the flat circles.
            d.Path(s.Ink(s.InlayColourKey).WithOpacity(0.45), null, inlay.Path);
            if (s.InlayBrightness > 0)
                d.Path(s.Ink("InstrumentWhite").WithOpacity(s.InlayBrightness * 0.65), null, inlay.Path);
            d.Path(null, new UiStroke(s.Ink("FretboardInlayRim"), 0.8), inlay.Path);
            if (inlay.Round) d.Ellipse(s.Ink("FretboardInlayGlint"), null, inlay.X - inlay.Side * 0.12, inlay.Y - inlay.Side * 0.14, inlay.Side * 0.12, inlay.Side * 0.09);
            else
            {
                d.PushTranslate(-0.35, -0.45);
                d.Path(null, new UiStroke(s.Ink("FretboardInlayGlint"), 0.5), inlay.Path);
                d.Pop();
            }
        }
    }

    /// <summary>Dims/brightens the complete physical neck after strings and capo, before any note or scale overlay.
    /// A veil also reaches bitmap textures; adjusting palette colours alone cannot darken those.</summary>
    internal static void DrawBrightness(in FretboardScene s, int percent)
    {
        var alpha = FretboardStyles.BrightnessVeil(percent);
        if (alpha <= 0) return;
        var frame = Frame(s);
        var nut = FretboardRealRules.NutWidthPx(s.Layout.FretWidth, FretboardSizeRules.FretCellMm(s.Bass));
        var tint = s.Ink(percent < 100 ? "FretboardWoodShade" : "InstrumentWhite") with { A = 255 };
        var colour = tint.WithOpacity(alpha);
        s.D.Rect(colour, null, frame.X - nut, frame.Y, frame.Width + nut, frame.Height, 2);
    }

    /// <summary>The nut (an ivory block at the board's left end) and the bevelled fret wires between the frets.</summary>
    internal static void DrawWires(in FretboardScene s)
    {
        var d = s.D;
        var l = s.Layout;
        var wood = Frame(s);
        var cellMm = FretboardSizeRules.FretCellMm(s.Bass);
        var wire = FretboardRealRules.WireWidthPx(l.FretWidth, cellMm);
        for (var fret = l.FirstFret; fret <= l.LastFret + 1; fret++)
        {
            var x = FretboardGeometryRules.WireX(l, fret - 1);
            if (fret == l.FirstFret && l.FirstFret == 1) { Nut(s, wood, x); continue; }
            d.Line(new UiStroke(s.Ink("FretboardStringCastSoft"), wire * 2.1), x + wire * 1.05, wood.Y, x + wire * 1.05, wood.Bottom);
            d.Line(new UiStroke(s.Ink("FretboardFretShadow"), wire * 0.8), x + wire * 0.75, wood.Y, x + wire * 0.75, wood.Bottom);
            d.Line(new UiStroke(s.Ink("FretboardWireBody"), wire), x, wood.Y, x, wood.Bottom);
            d.Line(new UiStroke(s.Ink("FretboardWireHighlight"), Math.Max(0.5, wire * 0.22)), x - wire * 0.22, wood.Y, x - wire * 0.22, wood.Bottom);
            d.Line(new UiStroke(s.Ink("FretboardWireShade"), Math.Max(0.7, wire * 0.25)), x + wire * 0.34, wood.Y, x + wire * 0.34, wood.Bottom);
        }
    }

    private static void Nut(in FretboardScene s, UiRect wood, double x)
    {
        var d = s.D;
        var width = FretboardRealRules.NutWidthPx(s.Layout.FretWidth, FretboardSizeRules.FretCellMm(s.Bass));
        var left = x - width;
        d.Rect(s.Ink("FretboardNutCast"), null, x, wood.Y, 3.2, wood.Height);
        d.Rect(s.Ink("FretboardNutBody"), null, left, wood.Y, width, wood.Height, 1.2);
        d.Rect(s.Ink("FretboardNutLight"), null, left, wood.Y, width * 0.42, wood.Height, 1.2);
        d.Rect(s.Ink("FretboardNutShade"), null, left + width * 0.78, wood.Y, width * 0.22, wood.Height);
        d.Rect(null, new UiStroke(s.Ink("FretboardNutShadow"), 0.8), left, wood.Y, width, wood.Height, 1.2);
        // A groove across the nut at every string: a dark notch a little wider than the string, with a lit lower lip, so the string sits down in it.
        var slot = s.Ink("FretboardNutSlot");
        var lip = s.Ink("FretboardNutSlotEdge");
        for (var i = 0; i < s.Layout.Strings; i++)
        {
            var y = FretboardGeometryRules.StringY(s.Layout, i);
            var groove = FretboardRealRules.StringWidthPx(i, s.Layout.Strings, s.Layout.StringGap, s.Bass) + 1.2;
            d.Rect(slot, null, left, y - groove / 2, width, groove, groove / 2);
            d.Line(new UiStroke(lip, 0.7), left + 0.8, y + groove / 2 + 0.4, left + width - 0.8, y + groove / 2 + 0.4);
        }
    }

    private static string StringToken(string look) => look switch
    {
        "Nickel" => "FretboardStringNickel",
        "Gold" => "FretboardStringGold",
        "Black" => "FretboardStringBlack",
        _ => "FretboardStringSteel",   // Bronze too: a bronze set's plain strings are bare steel.
    };

    /// <summary>The strings, thickest at the bottom, with a cast shadow on the wood. The thick ones are the wound texture; without it, and on the plain strings,
    /// a drawn body with a winding and a glint along the top stand in. <paramref name="wobble"/> gives each string's sideways offset in DIPs (zero when it is still).</summary>
    internal static void DrawStrings(in FretboardScene s, Func<int, (double Offset, int Fret)> wobble)
    {
        var d = s.D;
        var l = s.Layout;
        var wood = Wood(l, s.Bass);
        var body = s.Ink(StringToken(s.Look.StringLook));
        var castSoft = s.Ink("FretboardStringCastSoft");
        var castHard = s.Ink("FretboardStringCastHard");
        var winding = s.Ink("FretboardStringWinding");
        var glint = s.Ink("FretboardStringGlint");
        var texture = AvaloniaDrawContext.ClipsAreSkipped ? null : s.Assets.WoundString(s.Look.StringLook);
        var start = l.Board.X - FretboardRealRules.NutWidthPx(l.FretWidth, FretboardSizeRules.FretCellMm(s.Bass)) + 0.5;
        for (var i = 0; i < l.Strings; i++)
        {
            var y = FretboardGeometryRules.StringY(l, i);
            var vibration = wobble(i);
            var fret = vibration.Fret + l.CapoOf(i);
            var anchor = fret > 0 ? FretboardGeometryRules.FretX(l, fret) : (l.LeftHanded ? wood.Right : start);
            var w = FretboardRealRules.StringWidthPx(i, l.Strings, l.StringGap, s.Bass);
            var drop = w * 0.65 + 0.9;
            StringLine(d, new UiStroke(castSoft, w * 2.3, RoundCaps: true), start + 1, wood.Right, y + drop, anchor, vibration.Offset, l.LeftHanded);
            StringLine(d, new UiStroke(castHard, w * 0.75, RoundCaps: true), start + 0.6, wood.Right, y + drop * 0.8, anchor, vibration.Offset, l.LeftHanded);
            var wound = FretboardRealRules.IsWound(i, l.Strings, s.Bass);
            if (wound && texture is not null)
            {
                WoundString(d, texture, start, wood.Right, y, w, anchor, vibration.Offset, l.LeftHanded);
                continue;
            }
            StringLine(d, new UiStroke(body, w), start, wood.Right, y, anchor, vibration.Offset, l.LeftHanded);
            if (wound) StringLine(d, new UiStroke(winding, w * 0.85, Dash: w * 0.38, Gap: w * 0.42), start, wood.Right, y + w * 0.1, anchor, vibration.Offset, l.LeftHanded);
            StringLine(d, new UiStroke(glint, Math.Max(0.45, w * 0.22)), start, wood.Right, y - w * 0.26, anchor, vibration.Offset, l.LeftHanded);
        }
    }

    /// <summary>A wound string, the texture alone so it reads the same still or moving: the nut side stays put and the bridge side bows in slices, as <see cref="StringLine"/> does.</summary>
    private static void WoundString(IDrawContext d, IUiImage texture, double left, double right, double y, double width, double anchor, double offset, bool leftHanded)
    {
        if (offset == 0)
        {
            WoundTexture(d, texture, left, left, right, y, width);
            return;
        }
        anchor = Math.Clamp(anchor, left, right);
        var end = leftHanded ? left : right;
        WoundTexture(d, texture, left, leftHanded ? anchor : left, leftHanded ? right : anchor, y, width);
        for (var n = 0; n < BowSegments; n++)
        {
            var a = anchor + (end - anchor) * n / BowSegments;
            var b = anchor + (end - anchor) * (n + 1) / BowSegments;
            // Each slice runs a little under the next, so no gap shows where they meet.
            WoundTexture(d, texture, left, Math.Min(a, b), Math.Max(a, b) + 0.5, y + offset * Math.Sin(Math.PI * (n + 0.5) / BowSegments), width);
        }
    }

    /// <summary>The wound texture between two points of a string, repeated from the string's left end at a coil size that suits its width.</summary>
    private static void WoundTexture(IDrawContext d, IUiImage texture, double origin, double from, double to, double y, double width)
    {
        var (imageWidth, _) = FretboardAssets.SizeOf(texture)!.Value;
        var tile = imageWidth / FretboardRealRules.WoundTexturePixelsPerCoil * FretboardRealRules.WindingPitchPx(width);
        // Whole-DIP edges: a band that starts between pixels blurs across two of them.
        var top = Math.Round(y - width / 2);
        var height = Math.Max(1, Math.Round(y + width / 2) - top);
        d.PushClip(from, top, to - from, height);
        for (var x = origin + Math.Floor((from - origin) / tile) * tile; x < to; x += tile) d.Image(texture, x, top, tile, height);
        d.Pop();
    }

    /// <summary>The nut side stays still; the bridge side bows smoothly from the played fret to the board edge.</summary>
    private static void StringLine(IDrawContext d, UiStroke stroke, double left, double right, double y, double anchor, double offset, bool leftHanded)
    {
        if (offset == 0) { d.Line(stroke, left, y, right, y); return; }
        anchor = Math.Clamp(anchor, left, right);
        var end = leftHanded ? left : right;
        d.Line(stroke, leftHanded ? right : left, y, anchor, y);
        BowedLine(d, stroke, anchor, end, y, offset);
    }

    private static void BowedLine(IDrawContext d, UiStroke stroke, double anchor, double end, double y, double offset)
    {
        var x0 = anchor;
        var y0 = y;
        for (var n = 1; n <= BowSegments; n++)
        {
            var t = (double)n / BowSegments;
            var x = anchor + (end - anchor) * t;
            var nextY = y + offset * Math.Sin(Math.PI * t);
            d.Line(stroke, x0, y0, x, nextY);
            x0 = x;
            y0 = nextY;
        }
    }
}
