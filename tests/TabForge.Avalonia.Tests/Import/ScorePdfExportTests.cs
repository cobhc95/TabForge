using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: File > Export PDF (SC-01): the score of a song is written as a PDF file with at least one page, and the pages split on system boundaries.
// Does not own: the file dialog and the menu row (MainWindowScorePdf), the drawing (ScorePdfExport), the PDF bytes (ScorePdfWriter).
// Tests: ScorePdfExportTests (this file).
public class ScorePdfExportTests
{
    [AvaloniaFact]
    public void ExportPdfWritesAPdfFileWithAtLeastOnePage()
    {
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create(), IsNew = true };
        var path = Path.Combine(Path.GetTempPath(), $"tf-score-pdf-{Guid.NewGuid():N}.pdf");
        try
        {
            var pages = ScorePdfExport.Write(document, new AppSettings(), 0, path);

            var text = Encoding.ASCII.GetString(File.ReadAllBytes(path));
            Assert.True(pages >= 1);
            Assert.StartsWith("%PDF-1.4", text);
            Assert.Contains($"/Type /Pages /Count {pages}", text);
            Assert.EndsWith("%%EOF\n", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AFailedExportLeavesTheOldPdfUntouched()
    {
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create(), IsNew = true };
        var path = Path.Combine(Path.GetTempPath(), $"tf-score-pdf-{Guid.NewGuid():N}.pdf");
        var old = Encoding.ASCII.GetBytes("%PDF-1.4 old\n%%EOF\n");
        File.WriteAllBytes(path, old);
        try
        {
            FilePathPolicy.FaultInjection = stage =>
            {
                if (stage == "staged:" + Path.GetFileName(path)) throw new IOException("simulated failure");
            };
            Assert.Throws<IOException>(() => ScorePdfExport.Write(document, new AppSettings(), 0, path));
            Assert.Equal(old, File.ReadAllBytes(path));
        }
        finally
        {
            FilePathPolicy.FaultInjection = null;
            File.Delete(path);
        }
    }

    [Fact]
    public void PaginationKeepsWholeSystemsAndTheHeaderOnTheFirstPage()
    {
        const double header = 120, systemHeight = 300;
        const int systems = 12;

        var pages = ScorePdfPagination.Paginate(pageWidth: 1280, headerHeight: header, systemHeight: systemHeight, systems: systems);

        Assert.True(pages.Count > 1, "twelve systems do not fit one A4 page");
        var placed = 0;
        for (var index = 0; index < pages.Count; index++)
        {
            if (index == 0) Assert.Equal(0, pages[0].Top, 6);
            else Assert.Equal(header + placed * systemHeight, pages[index].Top, 6);
            var headerOnPage = index == 0 ? header : 0;
            placed += (int)Math.Round((pages[index].Height - headerOnPage) / systemHeight);
        }
        Assert.Equal(systems, placed);
    }
}
