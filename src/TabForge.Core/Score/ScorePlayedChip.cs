using TabForge.Platform;

namespace TabForge.Views.Score;

// Owns: the glow around the fret number of the note sounding now (a pill, plus a ring for a note just struck).
// Does not own: choosing which notes sound (PlaybackOverlay) or where they sit (ScoreRenderer, BandLane).
// Tests: no dedicated test class.
internal static class ScorePlayedChip
{
    /// <summary>The least height a clipped glow keeps, so a rounding slip never hides it.</summary>
    private const double MinGlowHeight = 1;

    /// <summary>Draws the glow behind a fret number whose chip is <paramref name="chipWidth"/> by <paramref name="chipHeight"/>, centred on (<paramref name="centerX"/>, <paramref name="y"/>).
    /// A chord stacks one pill per string; <paramref name="clipTop"/> and <paramref name="clipBottom"/> trim a pill to its string's band (the
    /// midpoint to a sounding neighbour) so stacked pills tile instead of overprinting, at the same width. Null clips draw the whole pill.</summary>
    public static void Draw(IDrawContext d, UiColor playColor, bool struck, double centerX, double y, double chipWidth, double chipHeight, double? clipTop = null, double? clipBottom = null, bool light = false)
    {
        // One soft tinted pill with a single 1px outline (a just-struck note is tinted a little stronger); no second ring, so the fret number stays readable.
        var glow = playColor with { A = HaloAlpha(light, struck) };
        var (x, top, width, height) = Clip(centerX - chipWidth / 2 - 2, y - chipHeight / 2 - 2, chipWidth + 4, chipHeight + 4, clipTop, clipBottom);
        d.Rect(glow, new UiStroke(playColor, 1.0), x, top, width, height, 4);
    }

    /// <summary>The tint strength of a lit note's pill or halo: about a quarter of the colour on the light paper, a little less on the dark.</summary>
    public static byte HaloAlpha(bool light, bool struck) => light ? (struck ? (byte)90 : (byte)64) : (struck ? (byte)90 : (byte)46);

    /// <summary>True when <paramref name="paper"/> is a light paper (its brightness is over half).</summary>
    public static bool IsLight(UiColor paper) => paper.R + paper.G + paper.B > 384;

    /// <summary>Intersects a glow box with the string band; a sliver stays when the rounding slips past it.</summary>
    private static (double X, double Top, double Width, double Height) Clip(double x, double top, double width, double height, double? clipTop, double? clipBottom)
    {
        if (clipTop is { } noHigher) top = Math.Max(top, noHigher);
        var bottom = top + height;
        if (clipBottom is { } noLower) bottom = Math.Min(bottom, noLower);
        if (bottom - top < MinGlowHeight) { var middle = (top + bottom) / 2; top = middle - MinGlowHeight / 2; bottom = middle + MinGlowHeight / 2; }
        return (x, top, width, bottom - top);
    }
}
