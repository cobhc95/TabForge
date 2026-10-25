using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace TabForge.Views.Score;

// Owns: the bytes of a score PDF 1.4 file: the catalogue, the info dictionary, and per page a page object, its content stream and an RGB image
//   (Flate-compressed) drawn across the printable width at the top of the page; the page tree and the cross-reference table close the file.
// Does not own: rendering the page images (Avalonia ScorePdfExport), the page geometry (ScorePdfPagination), the stream (the caller opens and disposes it).
// Tests: TabForge.Avalonia.Tests.ScorePdfExportTests.

/// <summary>Writes a score as a PDF: call <see cref="AddPage"/> for each page in order, then <see cref="Finish"/>.</summary>
public sealed class ScorePdfWriter
{
    // Object numbers: 1 catalogue, 2 page tree, 3 info; page i uses 4+3i (page), 5+3i (content) and 6+3i (image).
    private const int CatalogueObject = 1, PageTreeObject = 2, InfoObject = 3, FirstPageObject = 4, ObjectsPerPage = 3;

    private readonly Stream _stream;
    private readonly Dictionary<int, long> _offsets = new();
    private int _pageCount;
    private bool _finished;

    /// <summary>Starts the file on <paramref name="stream"/> and writes the catalogue and the info dictionary (the title is kept to printable ASCII).</summary>
    public ScorePdfWriter(Stream stream, string title)
    {
        _stream = stream;
        Write("%PDF-1.4\n");
        Begin(CatalogueObject);
        Write($"<< /Type /Catalog /Pages {PageTreeObject} 0 R >>\nendobj\n");
        Begin(InfoObject);
        Write($"<< /Title ({SafeTitle(title)}) /Producer (TabForge) >>\nendobj\n");
    }

    /// <summary>The pages written so far.</summary>
    public int PageCount => _pageCount;

    /// <summary>
    /// Writes one page: an RGB image of <paramref name="widthPixels"/> by <paramref name="heightPixels"/> pixels (3 bytes a pixel, rows top to bottom),
    /// drawn <paramref name="widthPoints"/> by <paramref name="heightPoints"/> points at the top of the A4 page, centred across it.
    /// </summary>
    public void AddPage(int widthPixels, int heightPixels, double widthPoints, double heightPoints, byte[] rgb)
    {
        if (_finished) throw new InvalidOperationException("The PDF is already finished.");
        if (rgb.Length != widthPixels * heightPixels * 3) throw new ArgumentException("The page image is not width x height x 3 bytes.", nameof(rgb));
        var page = FirstPageObject + ObjectsPerPage * _pageCount;
        var content = page + 1;
        var image = page + 2;
        var x = (ScorePdfPagination.PageWidthPoints - widthPoints) / 2;
        var y = ScorePdfPagination.PageHeightPoints - ScorePdfPagination.MarginPoints - heightPoints;
        var drawing = $"q {Number(widthPoints)} 0 0 {Number(heightPoints)} {Number(x)} {Number(y)} cm /Im0 Do Q";
        var compressed = Compress(rgb);

        Begin(page);
        Write($"<< /Type /Page /Parent {PageTreeObject} 0 R /MediaBox [0 0 {Number(ScorePdfPagination.PageWidthPoints)} {Number(ScorePdfPagination.PageHeightPoints)}] " +
              $"/Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>\nendobj\n");
        Begin(content);
        Write($"<< /Length {drawing.Length} >>\nstream\n{drawing}\nendstream\nendobj\n");
        Begin(image);
        Write($"<< /Type /XObject /Subtype /Image /Width {widthPixels} /Height {heightPixels} /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
              $"/Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        _stream.Write(compressed, 0, compressed.Length);
        Write("\nendstream\nendobj\n");
        _pageCount++;
    }

    /// <summary>Writes the page tree and the cross-reference table, and ends the file. The stream is left open for the caller.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        Begin(PageTreeObject);
        var kids = string.Join(" ", Enumerable.Range(0, _pageCount).Select(i => $"{FirstPageObject + ObjectsPerPage * i} 0 R"));
        Write($"<< /Type /Pages /Count {_pageCount} /Kids [{kids}] >>\nendobj\n");
        var size = _offsets.Keys.Max() + 1;
        var xref = _stream.Position;
        Write($"xref\n0 {size}\n0000000000 65535 f \n");
        for (var id = 1; id < size; id++) Write(_offsets[id].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {size} /Root {CatalogueObject} 0 R /Info {InfoObject} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private void Begin(int id)
    {
        _offsets[id] = _stream.Position;
        Write($"{id} 0 obj\n");
    }

    private void Write(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The page image compressed as a zlib stream (the PDF FlateDecode filter).</summary>
    private static byte[] Compress(byte[] rgb)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(rgb, 0, rgb.Length);
        return buffer.ToArray();
    }

    /// <summary>The title as PDF string text: printable ASCII only, without the characters a PDF string must escape.</summary>
    private static string SafeTitle(string? title) =>
        new((title ?? "").Where(c => c >= 32 && c < 127 && c != '(' && c != ')' && c != '\\').ToArray());
}
