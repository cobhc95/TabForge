using System;
using System.Collections.Generic;

namespace TabForge.Views.Score;

// Owns: the score PDF's page geometry (A4 in points with its margin, and the raster density of the page images) and the split of a score into
//   pages on system boundaries (Paginate): the first page takes the header block, and every page keeps whole systems.
// Does not own: drawing the pages (Avalonia ScorePdfExport), the PDF bytes (ScorePdfWriter), the score's layout (ScoreGeometryProbe).
// Tests: TabForge.Avalonia.Tests.ScorePdfExportTests.

/// <summary>One PDF page's share of the score: its top edge and height, in score DIPs.</summary>
public readonly record struct ScorePdfPage(double Top, double Height);

public static class ScorePdfPagination
{
    /// <summary>The A4 page in points (PDF user units) and the margin kept on every side.</summary>
    public const double PageWidthPoints = 595, PageHeightPoints = 842, MarginPoints = 28;

    /// <summary>Device-independent pixels per inch, and the dots per inch the page images are rendered at.</summary>
    public const double DipsPerInch = 96, RasterDotsPerInch = 200;

    /// <summary>Raster pixels per score DIP of a page image.</summary>
    public const double PixelsPerDip = RasterDotsPerInch / DipsPerInch;

    /// <summary>Points per score DIP for a page <paramref name="pageWidth"/> DIPs wide, so the page fills the printable width.</summary>
    public static double PointsPerDip(double pageWidth) => (PageWidthPoints - 2 * MarginPoints) / pageWidth;

    /// <summary>
    /// Splits a score of <paramref name="systems"/> systems into pages. The first page holds the header block and as many systems as fit below it;
    /// the others hold whole systems only. Each page is returned as its top edge and height in score DIPs.
    /// </summary>
    public static List<ScorePdfPage> Paginate(double pageWidth, double headerHeight, double systemHeight, int systems)
    {
        var scale = PointsPerDip(pageWidth);
        var usable = (PageHeightPoints - 2 * MarginPoints) / scale;
        var systemsOnFirstPage = Math.Max(1, (int)Math.Floor((usable - headerHeight) / systemHeight));
        var systemsOnLaterPages = Math.Max(1, (int)Math.Floor(usable / systemHeight));
        var pages = new List<ScorePdfPage>();
        var placed = 0;
        while (true)
        {
            var first = pages.Count == 0;
            var take = Math.Clamp(first ? systemsOnFirstPage : systemsOnLaterPages, 0, Math.Max(0, systems - placed));
            var top = first ? 0 : headerHeight + placed * systemHeight;
            var height = (first ? headerHeight : 0) + take * systemHeight;
            pages.Add(new ScorePdfPage(top, Math.Max(height, 1)));
            placed += take;
            if (placed >= systems) break;
        }
        return pages;
    }
}
