using System.Windows;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

// TabEditorControl: the clef drawn at the start of each system, and the smaller one where a bar changes clef.
public sealed partial class TabEditorControl
{
    internal enum ClefShape { Treble, Bass, Alto, Tenor, Percussion }

    /// <summary>The clef shape of a bar's clef text ("G8" = the guitar default, drawn as a plain treble clef; "G8vb" adds a small 8 under it).</summary>
    internal static (ClefShape Shape, bool EightBelow) ClefShapeOf(string? clef)
    {
        var value = (clef ?? "").Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        var below = value.Contains("8vb", StringComparison.Ordinal);
        if (value.Contains("perc", StringComparison.Ordinal) || value.Contains("neutral", StringComparison.Ordinal) || value.Contains("drum", StringComparison.Ordinal)) return (ClefShape.Percussion, false);
        if (value.Contains("bass", StringComparison.Ordinal) || value.StartsWith('f')) return (ClefShape.Bass, below);
        if (value.Contains("tenor", StringComparison.Ordinal) || value == "c4") return (ClefShape.Tenor, below);
        if (value.Contains("alto", StringComparison.Ordinal) || value.StartsWith('c')) return (ClefShape.Alto, below);
        return (ClefShape.Treble, below);
    }

    /// <summary>Whether a bar's clef differs from the previous bar's (the bar then starts with a smaller clef).</summary>
    internal static bool ClefChanges(TrackModel track, int measureIndex) =>
        measureIndex > 0 && measureIndex < track.Measures.Count &&
        !string.Equals(track.Measures[measureIndex].Clef, track.Measures[measureIndex - 1].Clef, StringComparison.OrdinalIgnoreCase) &&   // the usual case, without allocating (runs per bar per paint)
        ClefShapeOf(track.Measures[measureIndex].Clef) != ClefShapeOf(track.Measures[measureIndex - 1].Clef);

    internal const double ClefChangeWidth = 17;

    /// <summary>Draws a clef with its left edge at <paramref name="x"/>; <paramref name="size"/> 22 is the system clef, about 15 the mid-system change.</summary>
    private void DrawClef(DrawingContext dc, string? clef, double x, double staffTop, double size, Color ink)
    {
        var (shape, eightBelow) = ClefShapeOf(clef);
        var scale = size / 22.0;
        var brush = Brush(ink);
        // Offsets of the glyph box from the staff top at size 22, so the clef's curl / dots / centre sit on the right line.
        // Anchor: the staff line the clef marks (G, F, C or middle line, counted from the top); a smaller clef keeps that line, not the staff top.
        var (glyph, dy, anchor) = shape switch
        {
            ClefShape.Bass => ("\U0001D122", -8.0, 1.0),
            ClefShape.Alto => ("\U0001D121", -3.0, 2.0),
            ClefShape.Tenor => ("\U0001D121", -12.0, 1.0),
            ClefShape.Percussion => ("\U0001D125", 3.0, 2.0),
            _ => ("\U0001D11E", -6.0, 3.0)
        };
        var line = anchor * 9.0;   // at size 22 the offsets above were set against the 9 px staff gap
        Draw(dc, glyph, x, staffTop + line - (line - dy) * scale, size, brush);
        if (eightBelow && shape is ClefShape.Treble or ClefShape.Bass)
            DrawCentered(dc, "8", x + 7 * scale, staffTop + 4 * StaffGap + 8, 8 * scale + 1, brush);
    }
}
