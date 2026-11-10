using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Features.Help;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Help;

// Owns: the Help search results pane: the ranked result rows (chapter and section breadcrumb, a title line and a snippet with the matched words in the accent
//   colour), the "Preparing search..." notice while the index builds, and the no-results page that offers the popular chapters instead of an empty page.
// Does not own: running the search (Core HelpSearchIndex), the box and its debounce (HelpWindow), opening the chapter (the window's callback).
// Tests: TabForge.Avalonia.Tests.HelpWindowTests.
internal sealed class HelpResults : ScrollViewer
{
    private readonly Action<int, int?> _open;
    private readonly StackPanel _list = DialogMetrics.Spaced(new StackPanel { MaxWidth = ChromeMetrics.HelpReadingWidth, HorizontalAlignment = HorizontalAlignment.Left }, ChromeMetrics.HelpPagePadding);

    // A ScrollViewer by look: the theme keys its template to that type.
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    internal HelpResults(Action<int, int?> open)
    {
        _open = open;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = _list;
    }

    /// <summary>The result rows on show (0 for a notice page).</summary>
    internal int RowCount { get; private set; }

    /// <summary>The text of the notice or heading line at the top, for tests and screen readers.</summary>
    internal string? Notice { get; private set; }

    internal void ShowPreparing()
    {
        Reset("Preparing search…", "The search index is built once, in the background. It is ready in a moment.");
    }

    internal void Show(string query, IReadOnlyList<HelpHit> hits)
    {
        if (hits.Count == 0) { ShowNone(query); return; }
        Reset(hits.Count == 1 ? "1 result" : hits.Count + " results", null);
        foreach (var hit in hits) _list.Children.Add(Row(hit));
        RowCount = hits.Count;
        Offset = default;
    }

    private void ShowNone(string query)
    {
        Reset("No results for “" + query.Trim() + "”", "Try fewer or simpler words, such as sound, save or slow. Everyday phrases work too, like no sound or wrong string. Put words in quotes to find an exact phrase.");
        var heading = HelpRichText.Line("Popular chapters", "Accent", ChromeMetrics.HelpSectionSize, FontWeight.SemiBold);
        DialogMetrics.Spaced(heading, 0, ChromeMetrics.HelpSectionGap, 0, ChromeMetrics.HelpBlockGap);
        _list.Children.Add(heading);
        foreach (var index in HelpChapterCatalog.Suggested)
        {
            var link = HelpRichText.Link(HelpChapterCatalog.All[index].Title, ChromeMetrics.HelpBodySize, () => _open(index, null));
            DialogMetrics.Spaced(link, 0, 0, 0, ChromeMetrics.HelpBlockGap);
            _list.Children.Add(link);
        }
    }

    private void Reset(string notice, string? hint)
    {
        _list.Children.Clear();
        RowCount = 0;
        Notice = notice;
        _list.Children.Add(HelpRichText.Line(notice, "TextStrong", ChromeMetrics.HelpSectionSize, FontWeight.SemiBold));
        if (hint is not null)
        {
            var line = HelpRichText.Line(hint, "TextMuted");
            DialogMetrics.Spaced(line, 0, ChromeMetrics.HelpBlockGap, 0, 0);
            _list.Children.Add(line);
        }
        Offset = default;
    }

    private Control Row(HelpHit hit)
    {
        var stack = DialogMetrics.Spaced(new StackPanel { Spacing = ChromeMetrics.HelpRowGap }, ChromeMetrics.HelpCardPadding);
        var crumb = hit.SectionHeading is null || hit.Kind == HelpEntryKind.Chapter ? hit.ChapterTitle : hit.ChapterTitle + "  ›  " + hit.SectionHeading;
        stack.Children.Add(HelpRichText.Line(crumb, "TextMuted", ChromeMetrics.TextBody));
        stack.Children.Add(HelpRichText.Marked(hit.Title, hit.TitleMarks, "TextStrong", ChromeMetrics.HelpBodySize, FontWeight.SemiBold));
        if (hit.Snippet.Length > 0) stack.Children.Add(HelpRichText.Marked(hit.Snippet, hit.SnippetMarks, "TextPrimary", ChromeMetrics.HelpBodySize));
        var row = DialogMetrics.Spaced(new Border { Child = stack, Background = AvaloniaPalette.TransparentBrush, CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge) }, 0, 0, 0, ChromeMetrics.HelpRowGap);
        row.PointerEntered += (_, _) => HelpRichText.Bind(row, Border.BackgroundProperty, "HoverBrush");
        row.PointerExited += (_, _) => row.Background = AvaloniaPalette.TransparentBrush;
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            _open(hit.Chapter, hit.Section >= 0 ? hit.Section : null);
        };
        return row;
    }
}
