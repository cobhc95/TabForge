using System;
using TabForge.Models;

namespace TabForge.Views.Score;

// Owns: where the section name above a bar is drawn, for hit-testing a click on it, and the page top of a bar for scrolling to it.
// Does not own: drawing the heading (ScoreRenderer.Bars.cs), the jump itself (the app's section navigator).
// Tests: TabForge.Avalonia.Tests.SectionLinkTests.
internal static class ScoreSectionLinks
{
    /// <summary>Heading text width per character (10 pt bold), and the padding around it.</summary>
    private const double CharacterWidth = 6.5;
    private const double HeadingPadding = 8;

    /// <summary>Heading band above the staff: from this far above its top down to this far above it.</summary>
    private const double BandTop = 40;
    private const double BandBottom = 6;

    /// <summary>The section whose name is drawn at page point (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public static MarkerModel? HeadingAt(ScoreGeometryProbe probe, double x, double y)
    {
        if (!probe.Appearance.ShowSectionHeadings || probe.Project is null || probe.Track is not { } track) return null;
        var layout = probe.Layout.GetLayout(track);
        foreach (var marker in probe.Project.Markers)
        {
            if (marker.MeasureIndex < 0 || marker.MeasureIndex >= track.Measures.Count || string.IsNullOrWhiteSpace(marker.Title)) continue;
            if (!ReferenceEquals(probe.Layout.MarkerForMeasure(marker.MeasureIndex), marker)) continue;
            var position = layout.Measure(marker.MeasureIndex);
            var staffTop = probe.StaffTop(position.SystemIndex);
            var width = Math.Min(position.Width, marker.Title.Length * CharacterWidth + HeadingPadding);
            if (x >= position.X && x <= position.X + width && y >= staffTop - BandTop && y <= staffTop - BandBottom) return marker;
        }
        return null;
    }

    /// <summary>The page y where scrolling to <paramref name="bar"/> puts the top edge: its system's heading band.</summary>
    public static double BarTop(ScoreGeometryProbe probe, int bar)
    {
        if (probe.Track is not { } track || bar < 0 || bar >= track.Measures.Count) return 0;
        return Math.Max(0, probe.StaffTop(probe.Layout.GetLayout(track).Measure(bar).SystemIndex) - BandTop);
    }
}
