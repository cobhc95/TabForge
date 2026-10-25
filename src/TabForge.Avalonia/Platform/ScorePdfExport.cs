using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: writing the score as a PDF (File > Export PDF). A private score view takes the song and the Score settings, draws the chosen notation
//   track on light paper in fixed pages, and each A4 page is rasterised on an off-screen RenderTargetBitmap and embedded by ScorePdfWriter.
//   The song is not changed: the private view writes no notation, zoom or cursor back to it.
// Does not own: the file dialog and the menu row (MainWindowScorePdf), the pagination (Core ScorePdfPagination), the PDF bytes (Core ScorePdfWriter),
//   the score drawing (Core ScoreGeometryProbe).
// Tests: TabForge.Avalonia.Tests.ScorePdfExportTests.
// Threads: UI thread only (the pages are drawn with Avalonia).
public static class ScorePdfExport
{
    /// <summary>Writes the song's score to <paramref name="path"/> and returns its page count. <paramref name="trackIndex"/> is the track the score shows; an audio track (or a bad index) prints the first notation track.</summary>
    public static int Write(SongDocument document, AppSettings settings, int trackIndex, string path)
    {
        var view = new ScorePreviewView();
        _ = new ScoreSettingsBinding(view, settings);
        view.Editor.ShowDocument(document);
        var probe = view.Probe;
        probe.Light = true;                         // light paper, as the printed page is
        probe.Appearance.CenterSystems = false;     // fixed pages, not the continuous reflow
        probe.HorizontalScroll = false;
        probe.Cursor = null;                        // no cursor on the page
        probe.ShowTrack(PrintTrackIndex(document.Project, trackIndex));
        var (headerHeight, systemHeight, systems) = probe.PrintRows();
        var pageWidth = probe.ContentSize().Width;
        var pages = ScorePdfPagination.Paginate(pageWidth, headerHeight, systemHeight, systems);
        var pointsPerDip = ScorePdfPagination.PointsPerDip(pageWidth);
        var pageCount = 0;
        // Staged beside the destination and moved over it only when complete: a failed draw or write leaves an existing PDF untouched.
        FilePathPolicy.WriteAtomically(path, stream =>
        {
            var writer = new ScorePdfWriter(stream, document.Project.Title);
            foreach (var page in pages)
            {
                var widthPixels = (int)Math.Ceiling(pageWidth * ScorePdfPagination.PixelsPerDip);
                var heightPixels = (int)Math.Ceiling(page.Height * ScorePdfPagination.PixelsPerDip);
                var rgb = Rasterize(probe, pageWidth, page, widthPixels, heightPixels);
                writer.AddPage(widthPixels, heightPixels, pageWidth * pointsPerDip, page.Height * pointsPerDip, rgb);
            }
            writer.Finish();
            pageCount = writer.PageCount;
        });
        return pageCount;
    }

    /// <summary>The track to print: <paramref name="trackIndex"/> when it is a notation track, else the song's first notation track.</summary>
    private static int PrintTrackIndex(SongProject project, int trackIndex)
    {
        if (trackIndex >= 0 && trackIndex < project.Tracks.Count && !project.Tracks[trackIndex].IsAudio) return trackIndex;
        var first = project.FirstNotationTrack ?? throw new InvalidOperationException("A PDF score needs a track with notation.");
        return project.Tracks.IndexOf(first);
    }

    /// <summary>Draws one page of the score (its rows from <see cref="ScorePdfPage.Top"/>) and reads the pixels back as RGB over white.</summary>
    private static byte[] Rasterize(ScoreGeometryProbe probe, double pageWidth, ScorePdfPage page, int widthPixels, int heightPixels)
    {
        using var canvas = new RenderTargetBitmap(new PixelSize(widthPixels, heightPixels), new Vector(ScorePdfPagination.DipsPerInch, ScorePdfPagination.DipsPerInch));
        using (var context = canvas.CreateDrawingContext())
        {
            var draw = new AvaloniaDrawContext().Use(context);
            draw.PushScale(ScorePdfPagination.PixelsPerDip, ScorePdfPagination.PixelsPerDip);
            draw.PushClip(0, 0, pageWidth, page.Height);
            draw.PushTranslate(0, -page.Top);
            probe.DrawPage(draw, int.MaxValue, withPlayhead: false, visibleRows: (page.Top, page.Top + page.Height));
            draw.Pop();
            draw.Pop();
            draw.Pop();
        }
        using var readback = new WriteableBitmap(canvas.PixelSize, canvas.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = readback.Lock();
        canvas.CopyPixels(locked, AlphaFormat.Premul);
        return BgraToRgb(locked, widthPixels, heightPixels);
    }

    /// <summary>The pixels as RGB rows, top to bottom. The pixels are premultiplied, so each channel plus the uncovered share is its value over white.</summary>
    private static byte[] BgraToRgb(ILockedFramebuffer locked, int width, int height)
    {
        var row = new byte[width * 4];
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(locked.Address + y * locked.RowBytes, row, 0, row.Length);
            for (var x = 0; x < width; x++)
            {
                var uncovered = 255 - row[x * 4 + 3];
                var target = (y * width + x) * 3;
                rgb[target] = (byte)(row[x * 4 + 2] + uncovered);
                rgb[target + 1] = (byte)(row[x * 4 + 1] + uncovered);
                rgb[target + 2] = (byte)(row[x * 4] + uncovered);
            }
        }
        return rgb;
    }
}
