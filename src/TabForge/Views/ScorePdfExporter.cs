using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>
/// Exports the engraved score as a PDF with no printer and no extra package: the score is drawn on a
/// private light-paper <see cref="TabEditorControl"/>, cut into A4 pages on system boundaries, rasterised
/// (JPEG, ~200 dpi) and embedded in a minimal hand-written PDF 1.4 file (one image per page).
/// </summary>
internal static class ScorePdfExporter
{
    private const double PageW = 595, PageH = 842, Margin = 28; // A4 in points

    /// <summary>Splits a score of <paramref name="systems"/> systems into pages; returns (top, height) in score DIPs.</summary>
    internal static List<(double Top, double Height)> Paginate(double pageWidth, double headerHeight, double systemHeight, int systems)
    {
        var scale = (PageW - 2 * Margin) / pageWidth;
        var usable = (PageH - 2 * Margin) / scale;
        var perFirst = Math.Max(1, (int)Math.Floor((usable - headerHeight) / systemHeight));
        var perOther = Math.Max(1, (int)Math.Floor(usable / systemHeight));
        var pages = new List<(double Top, double Height)>();
        var s = 0;
        while (true)
        {
            var first = pages.Count == 0;
            var take = Math.Clamp(first ? perFirst : perOther, 0, Math.Max(0, systems - s));
            var top = first ? 0 : headerHeight + s * systemHeight;
            var height = (first ? headerHeight : 0) + take * systemHeight;
            pages.Add((top, Math.Max(height, 1)));
            s += take;
            if (s >= systems) break;
        }
        return pages;
    }

    /// <summary>Writes the PDF; returns the page count.</summary>
    public static int Export(SongProject project, int trackIndex, string path)
    {
        // An audio track has no notation: the first notation track is exported instead, and a song with none is refused.
        Services.AudioTrackExport.RequireNotation(project, "a PDF score");
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count || project.Tracks[trackIndex].IsAudio)
            trackIndex = project.Tracks.IndexOf(project.FirstNotationTrack!);
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = Math.Max(0, trackIndex), DarkPaper = false, HideCursor = true, PlaybackMeasure = -1 };
        editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        editor.Arrange(new Rect(editor.DesiredSize));
        editor.UpdateLayout();
        var (pageWidth, headerHeight, systemHeight, systems) = editor.ExportMetrics();
        var scale = (PageW - 2 * Margin) / pageWidth; // DIPs -> points
        var pages = Paginate(pageWidth, headerHeight, systemHeight, systems);
        var images = new List<(byte[] Jpeg, int W, int H, double PtW, double PtH)>();
        const double dpiScale = 200.0 / 96.0; // pixels per DIP
        foreach (var (top, height) in pages)
        {
            var px = (int)Math.Ceiling(pageWidth * dpiScale);
            var py = (int)Math.Ceiling(height * dpiScale);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var area = new Rect(0, 0, pageWidth, height);
                dc.DrawRectangle(Brushes.White, null, area);
                dc.DrawRectangle(new VisualBrush(editor)
                {
                    Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, top, pageWidth, height),
                    ViewportUnits = BrushMappingMode.Absolute, Viewport = area
                }, null, area);
            }
            var bmp = new RenderTargetBitmap(px, py, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            images.Add((ms.ToArray(), px, py, pageWidth * scale, height * scale));
        }
        WritePdf(path, images, project.Title);
        return images.Count;
    }

    private static void WritePdf(string path, List<(byte[] Jpeg, int W, int H, double PtW, double PtH)> images, string title)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        var offsets = new List<long>();
        void Write(string text) { var b = Encoding.ASCII.GetBytes(text); fs.Write(b, 0, b.Length); }
        void Begin(int id) { while (offsets.Count < id) offsets.Add(0); offsets[id - 1] = fs.Position; Write($"{id} 0 obj\n"); }
        Write("%PDF-1.4\n");
        // Objects: 1 catalog, 2 pages, 3 info, then page/content/image per page (4+3i, 5+3i, 6+3i).
        Begin(1); Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Begin(2);
        Write("<< /Type /Pages /Count " + images.Count.ToString(inv) + " /Kids [" +
              string.Join(" ", Enumerable.Range(0, images.Count).Select(i => $"{4 + 3 * i} 0 R")) + "] >>\nendobj\n");
        Begin(3);
        var safeTitle = new string((title ?? "").Where(c => c >= 32 && c < 127 && c != '(' && c != ')' && c != '\\').ToArray());
        Write($"<< /Title ({safeTitle}) /Producer (TabForge) >>\nendobj\n");
        for (var i = 0; i < images.Count; i++)
        {
            var (jpeg, w, h, ptW, ptH) = images[i];
            var x = (PageW - ptW) / 2;
            var y = PageH - Margin - ptH;
            Begin(4 + 3 * i);
            Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW.ToString(inv)} {PageH.ToString(inv)}] /Resources << /XObject << /Im0 {6 + 3 * i} 0 R >> >> /Contents {5 + 3 * i} 0 R >>\nendobj\n");
            var content = $"q {ptW.ToString("0.##", inv)} 0 0 {ptH.ToString("0.##", inv)} {x.ToString("0.##", inv)} {y.ToString("0.##", inv)} cm /Im0 Do Q";
            Begin(5 + 3 * i);
            Write($"<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
            Begin(6 + 3 * i);
            Write($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            fs.Write(jpeg, 0, jpeg.Length);
            Write("\nendstream\nendobj\n");
        }
        var xref = fs.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write(o.ToString("0000000000", inv) + " 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
