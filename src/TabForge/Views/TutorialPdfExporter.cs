using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the PDF export needs besides the chapters.</summary>
public sealed class TutorialPdfOptions
{
    public string Title { get; init; } = "TabForge Beginner's Guide";
    public string Subtitle { get; init; } = "Learn to read, write and play tablature";
    /// <summary>Shown on the cover and in the footer, for example "Version 0.5".</summary>
    public string VersionText { get; init; } = "";
    /// <summary>Brand artwork (cover.*, icon-tip.*, icon-note.*, icon-warning.*, chapter banners); missing files are simply skipped.</summary>
    public string? BrandFolder { get; init; }
    /// <summary>Use the brand artwork chapters\chapter-NN.png as the banner of chapter NN (its title is part of the picture, so the art must match the chapter). Off: the banner is drawn from the chapter's own title.</summary>
    public bool UseChapterArt { get; init; }
    /// <summary>Folder the chapters' relative image paths are resolved against (the tutorial folder).</summary>
    public string? ImageRoot { get; init; }

    /// <summary>The default PDF file name of a guide.</summary>
    public static string DefaultFileName(TutorialGuide guide) => guide == TutorialGuide.Detailed ? "TabForge Detailed Guide.pdf" : "TabForge Basic Guide.pdf";

    /// <summary>Options for exporting a library: the cover title follows the guide; the brand artwork is found in the guide's folder or, for the Detailed Guide, its parent.</summary>
    public static TutorialPdfOptions For(TutorialLibrary library, string versionText)
    {
        var detailed = library.Guide == TutorialGuide.Detailed;
        var brand = Path.Combine(library.Folder, "assets", "brand");
        if (detailed && !Directory.Exists(brand) && Path.GetDirectoryName(library.Folder.TrimEnd('\\', '/')) is { Length: > 0 } parent)
            brand = Path.Combine(parent, "assets", "brand");
        return new TutorialPdfOptions
        {
            Title = detailed ? "TabForge Detailed Guide" : "TabForge Basic Guide",
            Subtitle = detailed ? "The complete reference to every feature" : "Learn to read, write and play tablature",
            VersionText = versionText,
            BrandFolder = brand,
            ImageRoot = library.Folder,
        };
    }
}

/// <summary>
/// Writes the whole tutorial as an A4 PDF with PDFsharp + MigraDoc (MIT; the WPF build, so no GDI+ and no browser engine): cover,
/// contents with page numbers, one banner per chapter, a running header and footer with page numbers, callout boxes, images at their
/// full pixel resolution and real, selectable text in an embedded Segoe UI subset. Runs on any thread and never touches the UI.
/// </summary>
public static class TutorialPdfExporter
{
    private const string Body = "Segoe UI";
    private const string Mono = "Consolas";
    private static readonly Color Ink = new(0xFF, 0x22, 0x2B, 0x38);
    private static readonly Color Brand = new(0xFF, 0x1F, 0x3A, 0x5F);
    private static readonly Color BrandAccent = new(0xFF, 0x4C, 0x9A, 0xFF);
    private static readonly Color LinkColour = new(0xFF, 0x1F, 0x6F, 0xD6);
    private static readonly Color Grid = new(0xFF, 0xC9, 0xD1, 0xDC);
    private static readonly Color Soft = new(0xFF, 0xF2, 0xF5, 0xF9);
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" };

    private static readonly Dictionary<string, (Color Edge, Color Fill, string Label)> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tip"] = (new Color(0xFF, 0x2E, 0x9E, 0x4F), new Color(0xFF, 0xEA, 0xF6, 0xEE), "TIP"),
        ["Note"] = (new Color(0xFF, 0x2F, 0x7D, 0xE0), new Color(0xFF, 0xE8, 0xF1, 0xFC), "NOTE"),
        ["Warning"] = (new Color(0xFF, 0xD0, 0x8A, 0x00), new Color(0xFF, 0xFD, 0xF3, 0xDC), "WARNING"),
        ["Shortcut"] = (new Color(0xFF, 0x7C, 0x5C, 0xD6), new Color(0xFF, 0xF1, 0xEE, 0xFB), "SHORTCUT"),
        ["Quote"] = (new Color(0xFF, 0x8A, 0x94, 0xA3), new Color(0xFF, 0xF2, 0xF5, 0xF9), ""),
    };

    /// <summary>
    /// Writes <paramref name="outputPath"/> and returns its page count. <paramref name="progress"/> gets (chapters done, chapter count, text);
    /// cancelling stops between chapters and deletes nothing that already existed.
    /// </summary>
    public static int Export(TutorialLibrary library, string outputPath, TutorialPdfOptions options, Action<int, int, string>? progress = null, CancellationToken cancel = default)
    {
        if (library.Chapters.Count == 0) throw new InvalidOperationException("There is no tutorial content to export.");
        var doc = new Builder(library, options, progress, cancel).Build();
        cancel.ThrowIfCancellationRequested();
        progress?.Invoke(library.Chapters.Count, library.Chapters.Count, "Laying out pages");
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        var pages = renderer.PdfDocument.PageCount;
        cancel.ThrowIfCancellationRequested();
        // Write beside the target first so a failure never leaves a half-written PDF where an old one was.
        var temp = outputPath + ".part";
        try
        {
            renderer.PdfDocument.Save(temp);
            File.Move(temp, outputPath, overwrite: true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } } // Not logged: temp file cleanup.
        progress?.Invoke(library.Chapters.Count, library.Chapters.Count, "Done");
        return pages;
    }

    private static string Bookmark(string prefix, string text) => prefix + "_" + Regex.Replace(text, @"[^A-Za-z0-9]", "_");

    private sealed class Builder
    {
        private readonly TutorialLibrary _library;
        private readonly TutorialPdfOptions _options;
        private readonly Action<int, int, string>? _progress;
        private readonly CancellationToken _cancel;
        private readonly Document _doc = new();
        private double _contentWidth;   // points

        public Builder(TutorialLibrary library, TutorialPdfOptions options, Action<int, int, string>? progress, CancellationToken cancel)
        { _library = library; _options = options; _progress = progress; _cancel = cancel; }

        public Document Build()
        {
            _doc.Info.Title = _options.Title;
            _doc.Info.Subject = _options.Subtitle;
            _doc.Info.Author = "TabForge";
            _doc.Info.Keywords = "TabForge, tutorial, tablature";
            DefineStyles();
            AddCover();
            AddContents();
            var n = 0;
            foreach (var chapter in _library.Chapters)
            {
                _cancel.ThrowIfCancellationRequested();
                _progress?.Invoke(n, _library.Chapters.Count, chapter.Title);
                AddChapter(chapter);
                n++;
            }
            return _doc;
        }

        private void DefineStyles()
        {
            var normal = _doc.Styles[StyleNames.Normal]!;
            normal.Font.Name = Body; normal.Font.Size = 10.5; normal.Font.Color = Ink;
            normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
            normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
            normal.ParagraphFormat.LineSpacing = 1.25;
            normal.ParagraphFormat.Alignment = ParagraphAlignment.Left;
            normal.ParagraphFormat.WidowControl = true;

            var h1 = _doc.Styles[StyleNames.Heading1]!;
            h1.Font.Name = Body; h1.Font.Size = 22; h1.Font.Bold = true; h1.Font.Color = Brand;
            h1.ParagraphFormat.SpaceBefore = 0; h1.ParagraphFormat.SpaceAfter = 10; h1.ParagraphFormat.KeepWithNext = true;
            var h2 = _doc.Styles[StyleNames.Heading2]!;
            h2.Font.Name = Body; h2.Font.Size = 15; h2.Font.Bold = true; h2.Font.Color = Brand;
            h2.ParagraphFormat.SpaceBefore = 16; h2.ParagraphFormat.SpaceAfter = 6; h2.ParagraphFormat.KeepWithNext = true;
            h2.ParagraphFormat.Borders.Bottom.Color = BrandAccent; h2.ParagraphFormat.Borders.Bottom.Width = 0.75;
            h2.ParagraphFormat.Borders.DistanceFromBottom = 3;
            var h3 = _doc.Styles[StyleNames.Heading3]!;
            h3.Font.Name = Body; h3.Font.Size = 12; h3.Font.Bold = true; h3.Font.Color = Ink;
            h3.ParagraphFormat.SpaceBefore = 12; h3.ParagraphFormat.SpaceAfter = 4; h3.ParagraphFormat.KeepWithNext = true;
            h3.ParagraphFormat.Borders.Bottom.Visible = false;   // heading 3 is based on heading 2; no rule under it
            var header = _doc.Styles[StyleNames.Header]!;
            header.Font.Size = 8.5; header.Font.Color = new Color(0xFF, 0x6B, 0x75, 0x85);
            var footer = _doc.Styles[StyleNames.Footer]!;
            footer.Font.Size = 8.5; footer.Font.Color = new Color(0xFF, 0x6B, 0x75, 0x85);
        }

        private Section NewSection(bool margins = true)
        {
            var section = _doc.AddSection();
            var ps = section.PageSetup;
            ps.PageFormat = PageFormat.A4;
            ps.Orientation = Orientation.Portrait;
            ps.LeftMargin = margins ? Unit.FromCentimeter(2.2) : 0;
            ps.RightMargin = margins ? Unit.FromCentimeter(2.2) : 0;
            ps.TopMargin = margins ? Unit.FromCentimeter(2.6) : 0;
            ps.BottomMargin = margins ? Unit.FromCentimeter(2.4) : 0;
            ps.HeaderDistance = Unit.FromCentimeter(1.2);
            ps.FooterDistance = Unit.FromCentimeter(1.1);
            if (margins) _contentWidth = Unit.FromCentimeter(21).Point - 2 * Unit.FromCentimeter(2.2).Point;   // A4 minus the side margins, in points
            return section;
        }

        private void AddRunning(Section section, string headerText)
        {
            section.PageSetup.DifferentFirstPageHeaderFooter = true;   // the first page of a chapter has the banner instead of a header
            var header = section.Headers.Primary.AddParagraph(headerText);
            header.Format.Alignment = ParagraphAlignment.Right;
            header.Format.Borders.Bottom.Color = Grid; header.Format.Borders.Bottom.Width = 0.5;
            header.Format.Borders.DistanceFromBottom = 3;
            foreach (var footer in new[] { section.Footers.Primary, section.Footers.FirstPage })
            {
                var p = footer.AddParagraph();
                p.Format.TabStops.AddTabStop(Unit.FromPoint(_contentWidth), TabAlignment.Right);
                p.AddText(_options.Title + (_options.VersionText.Length > 0 ? "  |  " + _options.VersionText : ""));
                p.AddTab();
                p.AddText("Page ");
                p.AddPageField();
            }
        }

        // ---- cover ----

        private void AddCover()
        {
            var section = NewSection(margins: false);
            var cover = FindBrand("cover");
            var pageW = section.PageSetup.PageWidth;
            var pageH = section.PageSetup.PageHeight;
            var background = section.AddTextFrame();
            background.Width = pageW; background.Height = pageH;
            background.RelativeHorizontal = RelativeHorizontal.Page; background.RelativeVertical = RelativeVertical.Page;
            background.Left = ShapePosition.Left; background.Top = ShapePosition.Top;
            background.FillFormat.Color = Brand;
            background.LineFormat.Visible = false;
            var fullPage = false;
            if (cover is not null && TryImageSize(cover, out var cw, out var ch) && ch > 0)
            {
                var image = section.AddImage(cover);
                image.RelativeHorizontal = RelativeHorizontal.Page; image.RelativeVertical = RelativeVertical.Page;
                image.Left = ShapePosition.Left; image.Top = ShapePosition.Top;
                image.LockAspectRatio = true;
                image.Width = pageW;
                // art taller than ~85 % of an A4 page is a complete cover (title baked in); anything else is a header picture
                fullPage = (double)ch / cw * pageW.Point >= pageH.Point * 0.85;
                if (fullPage) image.Height = pageH;
            }
            if (!fullPage)
            {
                var text = section.AddTextFrame();
                text.RelativeHorizontal = RelativeHorizontal.Page; text.RelativeVertical = RelativeVertical.Page;
                text.Left = Unit.FromCentimeter(2.4); text.Top = Unit.FromCentimeter(cover is null ? 9.5 : 16.5);
                text.Width = Unit.FromCentimeter(16.2); text.Height = Unit.FromCentimeter(9);
                text.LineFormat.Visible = false;
                var title = text.AddParagraph(_options.Title);
                title.Format.Font.Size = 34; title.Format.Font.Bold = true; title.Format.Font.Color = Colors.White;
                title.Format.SpaceAfter = 10; title.Format.LineSpacingRule = LineSpacingRule.Single;
                var sub = text.AddParagraph(_options.Subtitle);
                sub.Format.Font.Size = 15; sub.Format.Font.Color = new Color(0xFF, 0xCF, 0xE2, 0xFF);
                sub.Format.SpaceAfter = 24;
                if (_options.VersionText.Length > 0)
                {
                    var version = text.AddParagraph(_options.VersionText);
                    version.Format.Font.Size = 11; version.Format.Font.Color = Colors.White;
                }
            }
            // MigraDoc needs a paragraph in the section for the frames to anchor to.
            var anchor = section.AddParagraph();
            anchor.Format.Font.Size = 1;
        }

        // ---- contents ----

        private void AddContents()
        {
            var section = NewSection();
            AddRunning(section, "Contents");
            var title = section.AddParagraph("Contents", StyleNames.Heading1);
            title.Format.OutlineLevel = OutlineLevel.Level1;
            title.Format.SpaceAfter = 14;
            foreach (var chapter in _library.Chapters)
            {
                _cancel.ThrowIfCancellationRequested();
                var line = section.AddParagraph();
                line.Format.Font.Size = 11.5; line.Format.Font.Bold = true; line.Format.SpaceBefore = 9; line.Format.SpaceAfter = 2;
                line.Format.KeepWithNext = true;
                line.Format.TabStops.AddTabStop(Unit.FromPoint(_contentWidth), TabAlignment.Right, TabLeader.Dots);
                var link = line.AddHyperlink(Bookmark("c", chapter.Id), HyperlinkType.Bookmark);
                link.AddText(chapter.DisplayTitle);
                line.AddTab();
                line.AddPageRefField(Bookmark("c", chapter.Id));
                foreach (var h in chapter.SubHeadings.Where(h => h.Level == 2))
                {
                    var sub = section.AddParagraph();
                    sub.Format.LeftIndent = Unit.FromPoint(18); sub.Format.SpaceAfter = 0; sub.Format.Font.Size = 9.5;
                    sub.Format.TabStops.AddTabStop(Unit.FromPoint(_contentWidth), TabAlignment.Right, TabLeader.Dots);
                    var subLink = sub.AddHyperlink(Bookmark("h", chapter.Id + "_" + h.Id), HyperlinkType.Bookmark);
                    subLink.AddText(h.Text);
                    sub.AddTab();
                    sub.AddPageRefField(Bookmark("h", chapter.Id + "_" + h.Id));
                }
            }
        }

        // ---- chapters ----

        private void AddChapter(TutorialChapter chapter)
        {
            var section = NewSection();
            AddRunning(section, chapter.Title);
            AddBanner(section, chapter);
            var blocks = chapter.Document.Blocks;
            var skippedTitle = false;
            foreach (var group in blocks)
            {
                // the first level-1 heading is the chapter title, which the banner already shows
                if (!skippedTitle && group is TutorialHeading { Level: 1 }) { skippedTitle = true; continue; }
                WriteBlock(section.Elements, group, chapter);
            }
        }

        private void AddBanner(Section section, TutorialChapter chapter)
        {
            var art = _options.UseChapterArt && chapter.Number > 0 ? FindBrand($"chapters/chapter-{chapter.Number:00}") : null;
            if (art is not null && TryImageSize(art, out _, out _))
            {
                // the art carries the chapter label and title; an invisible bookmark keeps the contents and links working
                var anchor = section.AddParagraph();
                anchor.Format.Font.Size = 1; anchor.Format.SpaceAfter = 0;
                anchor.AddBookmark(Bookmark("c", chapter.Id));
                var image = section.AddImage(art);
                image.LockAspectRatio = true; image.Width = Unit.FromPoint(_contentWidth);
                var gap0 = section.AddParagraph();
                gap0.Format.SpaceAfter = 8; gap0.Format.Font.Size = 4;
                return;
            }
            var table = section.AddTable();
            table.Borders.Visible = false;
            table.Rows.LeftIndent = 0;
            table.AddColumn(Unit.FromPoint(_contentWidth));
            var row = table.AddRow();
            row.Shading.Color = Brand;
            row.TopPadding = 14; row.BottomPadding = 16;
            var cell = row.Cells[0];
            cell.Format.LeftIndent = Unit.FromPoint(18); cell.Format.RightIndent = Unit.FromPoint(18);
            var kicker = cell.AddParagraph(chapter.Label.ToUpperInvariant());
            kicker.Format.Font.Size = 9.5; kicker.Format.Font.Bold = true; kicker.Format.Font.Color = new Color(0xFF, 0x9F, 0xC8, 0xFF);
            kicker.Format.SpaceAfter = 4;
            var title = cell.AddParagraph(chapter.Title);
            title.Style = StyleNames.Heading1;
            title.Format.LeftIndent = Unit.FromPoint(18);
            title.Format.Font.Color = Colors.White; title.Format.Font.Size = 24; title.Format.SpaceAfter = 2;
            title.Format.OutlineLevel = OutlineLevel.Level1;
            title.Format.KeepWithNext = false;
            title.AddBookmark(Bookmark("c", chapter.Id));
            if (chapter.Summary.Length > 0)
            {
                var summary = cell.AddParagraph(chapter.Summary);
                summary.Format.Font.Size = 11; summary.Format.Font.Color = new Color(0xFF, 0xDB, 0xE8, 0xFA);
                summary.Format.SpaceAfter = 0; summary.Format.SpaceBefore = 4;
            }
            var gap = section.AddParagraph();
            gap.Format.SpaceAfter = 6; gap.Format.Font.Size = 4;
        }

        private void WriteBlock(DocumentElements target, TutorialBlock block, TutorialChapter chapter, bool inCallout = false)
        {
            switch (block)
            {
                case TutorialHeading h:
                {
                    var p = target.AddParagraph(string.Empty, h.Level == 1 ? StyleNames.Heading2 : h.Level == 2 ? StyleNames.Heading2 : StyleNames.Heading3);
                    p.Format.OutlineLevel = h.Level <= 2 ? OutlineLevel.Level2 : OutlineLevel.Level3;
                    p.AddBookmark(Bookmark("h", chapter.Id + "_" + h.Id));
                    AddSpans(p, h.Spans, chapter);
                    break;
                }
                case TutorialParagraph para:
                {
                    var p = target.AddParagraph();
                    AddSpans(p, para.Spans, chapter);
                    break;
                }
                case TutorialListItem item:
                {
                    var p = target.AddParagraph();
                    var indent = 16 + item.Depth * 16;
                    p.Format.LeftIndent = Unit.FromPoint(indent);
                    p.Format.FirstLineIndent = Unit.FromPoint(-16);
                    p.Format.SpaceAfter = 3;
                    p.Format.TabStops.AddTabStop(Unit.FromPoint(indent));
                    p.AddText((item.Ordered ? item.Number.ToString(CultureInfo.InvariantCulture) + "." : item.Depth == 0 ? "•" : "–") + "\t");
                    AddSpans(p, item.Spans, chapter);
                    break;
                }
                case TutorialImage img:
                    WriteImage(target, img, chapter);
                    break;
                case TutorialCallout callout:
                    WriteCallout(target, callout, chapter);
                    break;
                case TutorialTable table:
                    WriteTable(target, table, chapter);
                    break;
                case TutorialCode code:
                {
                    var t = target.AddTable();
                    t.Borders.Color = Grid; t.Borders.Width = 0.5;
                    t.Rows.LeftIndent = 0;
                    t.AddColumn(Unit.FromPoint(_contentWidth - (inCallout ? 30 : 0)));
                    var row = t.AddRow();
                    row.Shading.Color = Soft; row.TopPadding = 5; row.BottomPadding = 5;
                    foreach (var line in code.Text.Split('\n'))
                    {
                        var p = row.Cells[0].AddParagraph(line.Length == 0 ? " " : line);
                        p.Format.Font.Name = Mono; p.Format.Font.Size = 9; p.Format.SpaceAfter = 0;
                        p.Format.LineSpacingRule = LineSpacingRule.Single;
                    }
                    var gap = target.AddParagraph(); gap.Format.SpaceAfter = 4; gap.Format.Font.Size = 3;
                    break;
                }
                case TutorialRule:
                {
                    var p = target.AddParagraph();
                    p.Format.Borders.Bottom.Color = Grid; p.Format.Borders.Bottom.Width = 0.75;
                    p.Format.SpaceBefore = 6; p.Format.SpaceAfter = 10; p.Format.Font.Size = 2;
                    break;
                }
            }
        }

        private void AddSpans(Paragraph p, IReadOnlyList<TutorialSpan> spans, TutorialChapter chapter)
        {
            foreach (var span in spans)
            {
                if (span.Text.Length == 0) continue;
                var href = span.Href ?? (span.Bold ? _library.ResolveChapterReference(span.Text)?.FileName : null);
                if (href is not null)
                {
                    Hyperlink link;
                    if (_library.TryResolveLink(href, chapter, out var target, out var headingId))
                        link = p.AddHyperlink(headingId is null ? Bookmark("c", target.Id) : Bookmark("h", target.Id + "_" + headingId), HyperlinkType.Bookmark);
                    else
                        link = p.AddHyperlink(href, HyperlinkType.Url);
                    var ft = link.AddFormattedText(span.Text);
                    Style(ft, span, link: true);
                }
                else
                {
                    var ft = p.AddFormattedText(span.Text);
                    Style(ft, span, link: false);
                }
            }
        }

        private static void Style(FormattedText ft, TutorialSpan span, bool link)
        {
            if (span.Bold) ft.Bold = true;
            if (span.Italic) ft.Italic = true;
            if (span.Code) { ft.Font.Name = Mono; ft.Font.Size = 9.5; ft.Font.Color = new Color(0xFF, 0x8A, 0x2B, 0x5C); }
            if (link) { ft.Font.Color = LinkColour; ft.Font.Underline = Underline.Single; }
        }

        private void WriteImage(DocumentElements target, TutorialImage img, TutorialChapter chapter)
        {
            var path = ResolveImage(img.Source);
            if (path is null || !TryImageSize(path, out var pw, out var ph) || pw <= 0 || ph <= 0)
            {
                // a picture that is missing or not a raster format shows its description instead of leaving a gap
                var note = target.AddParagraph($"[Picture: {(img.Alt.Length > 0 ? img.Alt : img.Source)}]");
                note.Format.Font.Italic = true; note.Format.Font.Color = new Color(0xFF, 0x6B, 0x75, 0x85);
                return;
            }
            // natural size at 96 dpi (the size the app shows), shrunk to the column and to a page-friendly height
            double width = Math.Min(pw * 0.75, _contentWidth);
            var maxHeight = 560.0;
            if (width * ph / pw > maxHeight) width = maxHeight * pw / ph;
            var p = target.AddParagraph();
            p.Format.KeepWithNext = !string.IsNullOrWhiteSpace(img.Caption);
            p.Format.SpaceBefore = 4; p.Format.SpaceAfter = 4;
            p.Format.Alignment = ParagraphAlignment.Center;
            var image = p.AddImage(path);
            image.LockAspectRatio = true; image.Width = Unit.FromPoint(width);
            var caption = img.Caption;
            if (!string.IsNullOrWhiteSpace(caption))
            {
                var c = target.AddParagraph(caption);
                c.Format.Alignment = ParagraphAlignment.Center;
                c.Format.Font.Size = 9; c.Format.Font.Italic = true; c.Format.Font.Color = new Color(0xFF, 0x5A, 0x64, 0x73);
                c.Format.SpaceAfter = 10;
            }
        }

        private void WriteCallout(DocumentElements target, TutorialCallout callout, TutorialChapter chapter)
        {
            var kind = Kinds.TryGetValue(callout.Kind, out var k) ? k : Kinds["Quote"];
            var table = target.AddTable();
            table.Borders.Visible = false;
            table.Rows.LeftIndent = 0;
            table.AddColumn(Unit.FromPoint(_contentWidth));
            var row = table.AddRow();
            row.Shading.Color = kind.Fill;
            row.TopPadding = 7; row.BottomPadding = 5;
            row.KeepWith = 0;
            var cell = row.Cells[0];
            cell.Borders.Left.Color = kind.Edge; cell.Borders.Left.Width = 3.5;
            cell.Format.LeftIndent = Unit.FromPoint(10); cell.Format.RightIndent = Unit.FromPoint(10);
            if (kind.Label.Length > 0)
            {
                var label = cell.AddParagraph();
                label.Format.SpaceAfter = 2; label.Format.KeepWithNext = true;
                var icon = FindBrand("callouts/" + callout.Kind.ToLowerInvariant() + "@2x") ?? FindBrand("callouts/" + callout.Kind.ToLowerInvariant());
                if (icon is not null && TryImageSize(icon, out _, out _))
                {
                    var im = label.AddImage(icon);
                    im.LockAspectRatio = true; im.Height = Unit.FromPoint(13);
                    label.AddText(" ");
                }
                var ft = label.AddFormattedText(kind.Label);
                ft.Bold = true; ft.Font.Size = 8.5; ft.Font.Color = kind.Edge;
            }
            foreach (var inner in callout.Blocks)
                WriteInto(cell, inner, chapter);
            var gap = target.AddParagraph(); gap.Format.SpaceAfter = 6; gap.Format.Font.Size = 3;
        }

        /// <summary>Writes a block into a table cell (cells and sections share the same element API through <see cref="DocumentElements"/>).</summary>
        private void WriteInto(Cell cell, TutorialBlock block, TutorialChapter chapter) => WriteBlock(cell.Elements, block, chapter, inCallout: true);

        private void WriteTable(DocumentElements target, TutorialTable data, TutorialChapter chapter)
        {
            var columns = Math.Max(1, data.Header.Count);
            var weights = new double[columns];
            for (var c = 0; c < columns; c++)
            {
                var longest = TutorialMarkdown.PlainText(data.Header[c]).Length;
                foreach (var r in data.Rows) if (c < r.Count) longest = Math.Max(longest, TutorialMarkdown.PlainText(r[c]).Length);
                weights[c] = Math.Clamp(longest, 10, 48);
            }
            var total = weights.Sum();
            var table = target.AddTable();
            table.Borders.Color = Grid; table.Borders.Width = 0.5;
            table.Rows.LeftIndent = 0;
            for (var c = 0; c < columns; c++) table.AddColumn(Unit.FromPoint(_contentWidth * weights[c] / total));
            var head = table.AddRow();
            head.HeadingFormat = true; head.Shading.Color = Brand; head.TopPadding = 3; head.BottomPadding = 3;
            for (var c = 0; c < columns; c++)
            {
                var p = head.Cells[c].AddParagraph();
                p.Format.Font.Size = 9.5; p.Format.Font.Bold = true; p.Format.Font.Color = Colors.White; p.Format.SpaceAfter = 0;
                AddSpans(p, data.Header[c], chapter);
            }
            var odd = false;
            foreach (var r in data.Rows)
            {
                var row = table.AddRow();
                row.TopPadding = 3; row.BottomPadding = 3;
                if (odd) row.Shading.Color = Soft;
                odd = !odd;
                for (var c = 0; c < columns; c++)
                {
                    var p = row.Cells[c].AddParagraph();
                    p.Format.Font.Size = 9.5; p.Format.SpaceAfter = 0;
                    if (c < r.Count) AddSpans(p, r[c], chapter);
                }
            }
            var gap = target.AddParagraph(); gap.Format.SpaceAfter = 6; gap.Format.Font.Size = 3;
        }

        // ---- files ----

        private string? FindBrand(string baseName)
        {
            if (string.IsNullOrEmpty(_options.BrandFolder) || !Directory.Exists(_options.BrandFolder)) return null;
            try
            {
                foreach (var ext in ImageExtensions)
                {
                    var path = Path.Combine(_options.BrandFolder, baseName + ext);
                    if (File.Exists(path)) return path;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { } // Not logged: optional file probe in a loop.
            return null;
        }

        private string? ResolveImage(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Contains("://", StringComparison.Ordinal) || string.IsNullOrEmpty(_options.ImageRoot)) return null;
            try
            {
                var root = Path.GetFullPath(_options.ImageRoot);
                var full = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(source).Replace('/', Path.DirectorySeparatorChar)));
                // a chapter may only reference pictures inside the tutorial folder
                if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(full) && ImageExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase) ? full : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; } // Not logged: font probe: null means none
        }

        /// <summary>The pixel size of a raster picture, read without decoding it.</summary>
        private static bool TryImageSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var stream = File.OpenRead(path);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                width = frame.PixelWidth; height = frame.PixelHeight;
                return width > 0 && height > 0;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FileFormatException or UnauthorizedAccessException) { return false; } // Not logged: probe: false means none
        }
    }
}
