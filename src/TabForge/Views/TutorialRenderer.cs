using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Turns a parsed tutorial chapter into a <see cref="FlowDocument"/> for the reader. Colours come from the app's DynamicResource
/// brushes, so a theme change restyles it; the only fixed colours are the accent edges of the callouts, which are always paired
/// with a label and an icon so meaning never rests on colour alone.
/// </summary>
internal sealed class TutorialRenderer
{
    private static readonly Dictionary<string, (Color Edge, string Glyph, string Label)> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tip"] = (Color.FromRgb(0x2E, 0x9E, 0x4F), "✓", "Tip"),
        ["Note"] = (Color.FromRgb(0x2F, 0x7D, 0xE0), "i", "Note"),
        ["Warning"] = (Color.FromRgb(0xD0, 0x8A, 0x00), "!", "Warning"),
        ["Shortcut"] = (Color.FromRgb(0x7C, 0x5C, 0xD6), "K", "Shortcut"),
        ["Quote"] = (Color.FromRgb(0x8A, 0x94, 0xA3), "", ""),
    };

    private readonly string _imageRoot;
    private readonly string? _brandFolder;
    private readonly Action<string> _linkClicked;
    private readonly TutorialLibrary _library;
    private readonly Dictionary<string, BitmapSource?> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pictures of the document last built, so the window can cap their width to the reading column.</summary>
    public List<Image> Images { get; } = new();
    /// <summary>Heading id to its paragraph in the document last built.</summary>
    public Dictionary<string, Block> Anchors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TutorialRenderer(TutorialLibrary library, Action<string> linkClicked)
    {
        _library = library;
        _imageRoot = library.Folder;
        _brandFolder = library.Folder.Length > 0 ? Path.Combine(library.Folder, "assets", "brand") : null;
        _linkClicked = linkClicked;
    }

    /// <summary>The reading surface: the window colour in the dark theme, the near-white score paper in the light theme (grey text pages tire the eyes).</summary>
    internal static string ReadingSurfaceKey => TabForge.Visualization.VisualTheme.IsLight ? "PaperLightBrush" : "WindowBrush";

    public FlowDocument Render(TutorialChapter chapter, double baseFontSize = 15)
    {
        Images.Clear();
        Anchors.Clear();
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = baseFontSize,
            LineHeight = baseFontSize * 1.55,
            PagePadding = new Thickness(32, 24, 32, 40),
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
            IsOptimalParagraphEnabled = false,
            IsHyphenationEnabled = false,
        };
        doc.SetResourceReference(FlowDocument.BackgroundProperty, ReadingSurfaceKey);
        doc.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

        var kicker = new Paragraph(new Run(chapter.Label.ToUpperInvariant())) { FontSize = baseFontSize * 0.8, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
        kicker.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
        doc.Blocks.Add(kicker);
        var blocks = chapter.Document.Blocks.ToList();
        var titleIndex = blocks.FindIndex(b => b is TutorialHeading { Level: 1 });
        // the first level-1 heading is the chapter title; without one the front-matter title stands in
        var title = titleIndex >= 0 ? (TutorialHeading)blocks[titleIndex] : new TutorialHeading(1, new[] { new TutorialSpan(chapter.Title) }, "title");
        doc.Blocks.Add(HeadingParagraph(title, baseFontSize * 2.1, 0, 6));
        if (chapter.Summary.Length > 0)
        {
            var summary = new Paragraph(new Run(chapter.Summary)) { FontSize = baseFontSize * 1.1, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 14) };
            summary.SetResourceReference(TextElement.ForegroundProperty, "SecondaryTextBrush");
            doc.Blocks.Add(summary);
        }
        if (titleIndex >= 0) blocks.RemoveAt(titleIndex);
        AddBlocks(doc.Blocks, blocks, chapter, baseFontSize);
        AddPrevNext(doc.Blocks, chapter, baseFontSize);
        return doc;
    }

    private void AddBlocks(BlockCollection target, IReadOnlyList<TutorialBlock> blocks, TutorialChapter chapter, double size)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case TutorialHeading h:
                    target.Add(HeadingParagraph(h, h.Level == 1 ? size * 1.7 : h.Level == 2 ? size * 1.45 : size * 1.18, h.Level == 2 ? 22 : 16, 6));
                    break;
                case TutorialParagraph p:
                    target.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }.WithInlines(Inlines(p.Spans, chapter)));
                    break;
                case TutorialListItem:
                {
                    var j = i;
                    var firstOrdered = ((TutorialListItem)blocks[i]).Ordered;
                    // a switch between bullets and numbers at the top level starts a new list
                    while (j < blocks.Count && blocks[j] is TutorialListItem li && !(li.Depth == 0 && li.Ordered != firstOrdered)) j++;
                    target.Add(BuildList(blocks.Skip(i).Take(j - i).Cast<TutorialListItem>().ToList(), chapter));
                    i = j - 1;
                    break;
                }
                case TutorialImage img:
                    AddImage(target, img);
                    break;
                case TutorialCallout c:
                    target.Add(BuildCallout(c, chapter, size));
                    break;
                case TutorialTable t:
                    target.Add(BuildTable(t, chapter));
                    break;
                case TutorialCode code:
                {
                    var section = new Section { Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1) };
                    section.SetResourceReference(Block.BorderBrushProperty, "BorderBrush");
                    section.SetResourceReference(Block.BackgroundProperty, "Panel2Brush");
                    var p = new Paragraph(new Run(code.Text)) { FontFamily = new FontFamily("Consolas"), FontSize = size * 0.9, Margin = new Thickness(0), LineHeight = size * 1.3 };
                    section.Blocks.Add(p);
                    target.Add(section);
                    break;
                }
                case TutorialRule:
                {
                    var rule = new Paragraph { Margin = new Thickness(0, 8, 0, 14), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2 };
                    rule.SetResourceReference(Block.BorderBrushProperty, "BorderBrush");
                    target.Add(rule);
                    break;
                }
            }
        }
    }

    private Paragraph HeadingParagraph(TutorialHeading h, double size, double before, double after)
    {
        var p = new Paragraph { FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, before, 0, after), LineHeight = size * 1.25, KeepWithNext = true };
        p.SetResourceReference(TextElement.ForegroundProperty, "TextStrongBrush");
        foreach (var inline in Inlines(h.Spans, null)) p.Inlines.Add(inline);
        if (h.Level == 2)
        {
            p.BorderThickness = new Thickness(0, 0, 0, 1);
            p.Padding = new Thickness(0, 0, 0, 3);
            p.SetResourceReference(Block.BorderBrushProperty, "BorderBrush");
        }
        p.Tag = h.Id;
        AutomationProperties.SetName(p, h.Text);
        Anchors[h.Id] = p;
        return p;
    }

    private IEnumerable<Inline> Inlines(IReadOnlyList<TutorialSpan> spans, TutorialChapter? chapter)
    {
        foreach (var span in spans)
        {
            var run = new Run(span.Text);
            var spanHref = span.Href ?? (span.Bold ? _library.ResolveChapterReference(span.Text)?.FileName : null);
            if (span.Bold) run.FontWeight = FontWeights.Bold;
            if (span.Italic) run.FontStyle = FontStyles.Italic;
            if (span.Code)
            {
                run.FontFamily = new FontFamily("Consolas");
                run.FontSize = Math.Max(11, run.FontSize);
                run.SetResourceReference(TextElement.BackgroundProperty, "Panel3Brush");
            }
            if (spanHref is { } href)
            {
                var link = new Hyperlink(run) { Focusable = true };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                link.ToolTip = href;
                link.Click += (_, _) => _linkClicked(href);
                yield return link;
            }
            else yield return run;
        }
    }

    private Block BuildList(IReadOnlyList<TutorialListItem> items, TutorialChapter chapter)
    {
        List MakeList(TutorialListItem first) => new()
        {
            MarkerStyle = first.Ordered ? TextMarkerStyle.Decimal : first.Depth == 0 ? TextMarkerStyle.Disc : TextMarkerStyle.Circle,
            StartIndex = first.Ordered ? Math.Max(1, first.Number) : 1,
            Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(26, 0, 0, 0), MarkerOffset = 8,
        };
        var root = MakeList(items[0]);
        var stack = new List<(int Depth, List List, ListItem? Last)> { (0, root, null) };
        foreach (var item in items)
        {
            var depth = Math.Min(item.Depth, stack[^1].Depth + 1);
            while (stack.Count > 1 && stack[^1].Depth > depth) stack.RemoveAt(stack.Count - 1);
            if (stack[^1].Depth < depth && stack[^1].Last is { } parent)
            {
                var child = MakeList(item);
                child.Margin = new Thickness(0, 2, 0, 2);
                parent.Blocks.Add(child);
                stack.Add((depth, child, null));
            }
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 3) }.WithInlines(Inlines(item.Spans, chapter));
            var li = new ListItem(paragraph);
            stack[^1].List.ListItems.Add(li);
            stack[^1] = (stack[^1].Depth, stack[^1].List, li);
        }
        return root;
    }

    private void AddImage(BlockCollection target, TutorialImage img)
    {
        var bitmap = LoadImage(img.Source);
        if (bitmap is null)
        {
            var missing = new Paragraph(new Run($"[Picture: {(img.Alt.Length > 0 ? img.Alt : img.Source)}]")) { FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 10) };
            missing.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
            target.Add(missing);
            return;
        }
        var image = new Image
        {
            Source = bitmap,
            Width = bitmap.PixelWidth * 96.0 / (bitmap.DpiX > 0 ? bitmap.DpiX : 96.0),   // natural size in device-independent units
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4),
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(image, img.Alt.Length > 0 ? img.Alt : (img.Title ?? "Picture"));
        Images.Add(image);
        var frame = new Border { Child = image, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        target.Add(new BlockUIContainer(frame) { Margin = new Thickness(0, 4, 0, 4) });
        var caption = img.Caption;
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var c = new Paragraph(new Run(caption)) { FontSize = 12.5, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 12) };
            c.SetResourceReference(TextElement.ForegroundProperty, "SecondaryTextBrush");
            target.Add(c);
        }
    }

    private BitmapSource? LoadImage(string source)
    {
        if (_imageCache.TryGetValue(source, out var cached)) return cached;
        BitmapSource? result = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(source) && !source.Contains("://", StringComparison.Ordinal) && _imageRoot.Length > 0)
            {
                var root = Path.GetFullPath(_imageRoot);
                var full = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(source).Replace('/', Path.DirectorySeparatorChar)));
                // a chapter may only show pictures that live inside the tutorial folder
                if (full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                    result = LoadBitmap(full);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Services.Trace.Error(Services.Trace.Ui, "tutorial: load image: " + ex.Message); }
        _imageCache[source] = result;
        return result;
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // the file is released at once
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FileFormatException or UriFormatException) { return null; } // Not logged: render path: no logging per frame
    }

    private Block BuildCallout(TutorialCallout callout, TutorialChapter chapter, double size)
    {
        var kind = Kinds.TryGetValue(callout.Kind, out var k) ? k : Kinds["Quote"];
        var section = new Section
        {
            Padding = new Thickness(14, 8, 14, 4),
            Margin = new Thickness(0, 4, 0, 14),
            BorderThickness = new Thickness(4, 0, 0, 0),
            BorderBrush = Frozen(new SolidColorBrush(kind.Edge)),
        };
        section.SetResourceReference(Block.BackgroundProperty, "Panel2Brush");
        if (kind.Label.Length > 0)
        {
            var label = new Paragraph { Margin = new Thickness(0, 0, 0, 3), KeepWithNext = true, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextElement.ForegroundProperty, "TextStrongBrush");
            label.Inlines.Add(new InlineUIContainer(CalloutIcon(callout.Kind, kind.Edge, kind.Glyph)) { BaselineAlignment = BaselineAlignment.Center });
            label.Inlines.Add(new Run("  " + kind.Label));
            section.Blocks.Add(label);
        }
        AddBlocks(section.Blocks, callout.Blocks, chapter, size);
        // the last paragraph's bottom margin would leave a gap inside the box
        if (section.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 4);
        return section;
    }

    /// <summary>The brand icon (callouts\tip.png and so on in the tutorial's assets\brand folder) or, without one, a drawn badge.</summary>
    private FrameworkElement CalloutIcon(string kind, Color edge, string glyph)
    {
        if (_brandFolder is not null)
            foreach (var name in new[] { kind.ToLowerInvariant() + "@2x.png", kind.ToLowerInvariant() + ".png" })
            {
                var path = Path.Combine(_brandFolder, "callouts", name);
                if (File.Exists(path) && LoadBitmap(path) is { } bmp)
                {
                    var image = new Image { Source = bmp, Width = 20, Height = 20, Stretch = Stretch.Uniform };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    AutomationProperties.SetName(image, kind);
                    return image;
                }
            }
        var badge = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Frozen(new SolidColorBrush(edge)) };
        badge.Child = new TextBlock { Text = glyph, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(badge, kind);
        return badge;
    }

    private Block BuildTable(TutorialTable data, TutorialChapter chapter)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 14), BorderThickness = new Thickness(0.5) };
        table.SetResourceReference(Block.BorderBrushProperty, "BorderBrush");
        var columns = Math.Max(1, data.Header.Count);
        for (var c = 0; c < columns; c++)
        {
            var longest = TutorialMarkdown.PlainText(data.Header[c]).Length;
            foreach (var r in data.Rows) if (c < r.Count) longest = Math.Max(longest, TutorialMarkdown.PlainText(r[c]).Length);
            table.Columns.Add(new TableColumn { Width = new GridLength(Math.Clamp(longest, 12, 48), GridUnitType.Star) });
        }
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        TableRow MakeRow(IReadOnlyList<IReadOnlyList<TutorialSpan>> cells, bool header)
        {
            var row = new TableRow();
            for (var c = 0; c < columns; c++)
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                if (c < cells.Count) foreach (var inline in Inlines(cells[c], chapter)) para.Inlines.Add(inline);
                var cell = new TableCell(para) { BorderThickness = new Thickness(0.5), Padding = new Thickness(8, 4, 8, 4) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderBrush");
                if (header)
                {
                    cell.FontWeight = FontWeights.SemiBold;
                    cell.SetResourceReference(TableCell.BackgroundProperty, "Panel3Brush");
                }
                row.Cells.Add(cell);
            }
            return row;
        }
        group.Rows.Add(MakeRow(data.Header, header: true));
        foreach (var r in data.Rows) group.Rows.Add(MakeRow(r, header: false));
        return table;
    }

    private void AddPrevNext(BlockCollection target, TutorialChapter chapter, double size)
    {
        var index = _library.Chapters.ToList().IndexOf(chapter);
        if (index < 0) return;
        var rule = new Paragraph { Margin = new Thickness(0, 22, 0, 8), BorderThickness = new Thickness(0, 1, 0, 0), FontSize = 2 };
        rule.SetResourceReference(Block.BorderBrushProperty, "BorderBrush");
        target.Add(rule);
        var nav = new Paragraph { FontSize = size, Margin = new Thickness(0) };
        if (index > 0)
        {
            var prev = _library.Chapters[index - 1];
            var link = new Hyperlink(new Run("‹ " + prev.DisplayTitle));
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            link.Click += (_, _) => _linkClicked(prev.FileName);
            nav.Inlines.Add(new Run("Previous: "));
            nav.Inlines.Add(link);
        }
        if (index > 0 && index < _library.Chapters.Count - 1) nav.Inlines.Add(new Run("      "));
        if (index < _library.Chapters.Count - 1)
        {
            var next = _library.Chapters[index + 1];
            var link = new Hyperlink(new Run(next.DisplayTitle + " ›"));
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            link.Click += (_, _) => _linkClicked(next.FileName);
            nav.Inlines.Add(new Run("Next: "));
            nav.Inlines.Add(link);
        }
        target.Add(nav);
    }

    private static Brush Frozen(SolidColorBrush brush) { brush.Freeze(); return brush; }
}

internal static class TutorialRendererExtensions
{
    public static Paragraph WithInlines(this Paragraph paragraph, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines) paragraph.Inlines.Add(inline);
        return paragraph;
    }
}
