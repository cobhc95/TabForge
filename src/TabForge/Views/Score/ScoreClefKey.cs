using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

internal enum ClefShape { Treble, Bass, Alto, Tenor, Percussion }

/// <summary>Clef shapes, clef changes and key-signature glyph counts.</summary>
internal static class ScoreClefKey
{
    internal const double ClefChangeWidth = 17;

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

    /// <summary>Naturals that cancel the previous signature, then the new accidentals (the reference engraving).</summary>
    internal static (int Naturals, int Accidentals) KeySignatureGlyphs(int previous, int current)
    {
        previous = Math.Clamp(previous, -7, 7);
        current = Math.Clamp(current, -7, 7);
        var naturals = 0;
        if (previous != 0)
            naturals = Math.Sign(previous) != Math.Sign(current) ? Math.Abs(previous)
                : Math.Max(0, Math.Abs(previous) - Math.Abs(current));
        return (naturals, Math.Abs(current));
    }
}
