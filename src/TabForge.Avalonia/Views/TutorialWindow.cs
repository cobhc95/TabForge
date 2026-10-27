using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Help > Tutorial (Tools > Tutorial… in the preview): the Basic and Detailed guides as a reader with a contents list, Back / Forward /
/// Previous / Next navigation, and search. Export PDF reports on the status line that it needs the full app. Links in the text are shown
/// as their words; following them is not ported.
/// Owns: the window's controls (contents, navigation buttons, search with its results, the reader, the empty-library message), the reader's
///     rendering of the Markdown blocks, and the open chapter and history. Does not own: the rules (TutorialPresenter, TutorialHistory),
///     the content (TutorialLibrary and its chapters), the PDF export, or the saved guide choice (the caller passes the guide).
/// Over 300 lines: the contents, navigation, search and reader share one open chapter, so they stay in one class.
/// Tests: TabForge.Avalonia.Tests.RenderVideoExportTutorialTests.
/// </summary>
public sealed class TutorialWindow : Window
{
    private readonly TutorialLibrary _basic;
    private readonly TutorialLibrary _detailed;
    private readonly TutorialHistory _history = new();
    private readonly Dictionary<string, Control> _headingControls = new();
    private readonly List<TutorialContentsRow> _contentRows = new();
    private readonly List<TutorialSearchResult> _resultItems = new();
    private readonly RadioButton _basicChoice = new() { Name = "Basic", Content = "Basic guide", GroupName = "guide" };
    private readonly RadioButton _detailedChoice = new() { Name = "Detailed", Content = "Detailed guide", GroupName = "guide" };
    private readonly Button _back = new() { Name = "Back", Content = "◀ Back" };
    private readonly Button _forward = new() { Name = "Forward", Content = "Forward ▶" };
    private readonly Button _previous = new() { Name = "Previous", Content = "‹ Previous chapter" };
    private readonly Button _next = new() { Name = "Next", Content = "Next chapter ›" };
    private readonly Button _exportPdf = new() { Name = "ExportPdf", Content = "Export PDF…" };
    private readonly Button _clearSearch = new() { Name = "ClearSearch", Content = "✕" };
    private readonly TextBox _search = new() { Name = "Search", Watermark = "Search the guide (Ctrl+F)", MinWidth = 220 };
    private readonly ListBox _contents = new() { Name = "Contents", MinHeight = 200 };
    private readonly ListBox _results = new() { Name = "Results", MinHeight = 200, IsVisible = false };
    private readonly TextBlock _noResults = ToolWindowParts.Small("Nothing matches that search.");
    private readonly StackPanel _reader = new() { Margin = new Thickness(12, 0, 12, 12) };
    private readonly ScrollViewer _readerScroll;
    private readonly StackPanel _emptyMessage;
    private readonly Grid _main;
    private readonly TextBlock _status = ToolWindowParts.Status("StatusText");
    private TutorialGuide _guide;
    private TutorialLibrary _library;
    private TutorialChapter? _current;
    private bool _updatingLists;

    /// <param name="basic">The Basic Guide's chapters (empty when they are not installed).</param>
    /// <param name="detailed">The Detailed Guide's chapters (empty when they are not installed).</param>
    /// <param name="requested">The guide to open on; null opens the Basic Guide. Detailed falls back to Basic when it has no chapters.</param>
    public TutorialWindow(TutorialLibrary basic, TutorialLibrary detailed, TutorialGuide? requested)
    {
        _basic = basic; _detailed = detailed;
        _guide = TutorialPresenter.OpeningGuide(requested, savedValue: null, TutorialPresenter.DetailedAvailable(detailed));
        _library = LibraryFor(_guide);
        Title = TutorialPresenter.GuideTitle(_guide);
        Width = 960; Height = 660; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        _readerScroll = new ScrollViewer { Content = _reader, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _emptyMessage = BuildEmptyMessage();
        _main = BuildMain();
        var top = BuildToolbar();
        var root = new DockPanel { Margin = DialogMetrics.ContentInset };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var footer = new Border { Child = _status, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        Control body = _library.Chapters.Count > 0 ? _main : _emptyMessage;
        root.Children.Add(body);
        Content = root;

        WireEvents();
        _noResults.IsVisible = false;
        // Perf: the two lists are virtualized (ItemsSource was already bound; the recycling template over a
        // VirtualizingStackPanel keeps only the visible rows built).
        foreach (var list in new[] { _contents, _results })
        {
            list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
            list.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text ?? "" }, supportsRecycling: true);
        }
        ApplyLibraryState();
        if (_library.Chapters.Count > 0) Open(_library.Chapters[0], headingId: null);
    }

    private TutorialLibrary LibraryFor(TutorialGuide guide) => guide == TutorialGuide.Detailed ? _detailed : _basic;

    private Control BuildToolbar()
    {
        _basicChoice.IsChecked = _guide == TutorialGuide.Basic;
        _detailedChoice.IsChecked = _guide == TutorialGuide.Detailed;
        _detailedChoice.IsEnabled = TutorialPresenter.DetailedAvailable(_detailed);
        ToolTip.SetTip(_basicChoice, "The short guide for getting started");
        ToolTip.SetTip(_detailedChoice, "The full reference to every feature");
        ToolTip.SetTip(_back, "Back");
        ToolTip.SetTip(_forward, "Forward");
        ToolTip.SetTip(_previous, "Previous chapter");
        ToolTip.SetTip(_next, "Next chapter");
        ToolTip.SetTip(_search, "Search the whole guide (Ctrl+F). Esc clears the search.");
        ToolTip.SetTip(_clearSearch, "Clear the search (Esc)");
        ToolTip.SetTip(_exportPdf, "Save the whole guide as a PDF");
        var guideRow = ToolWindowParts.Row(_basicChoice, _detailedChoice);
        var navRow = ToolWindowParts.Row(_back, _forward, _previous, _next);
        var searchRow = ToolWindowParts.Row(_search, _clearSearch, _exportPdf);
        var column = new StackPanel { Spacing = 4 };
        column.Children.Add(guideRow); column.Children.Add(navRow); column.Children.Add(searchRow);
        return column;
    }

    private Grid BuildMain()
    {
        var left = new StackPanel { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(_contents);
        left.Children.Add(_results);
        left.Children.Add(_noResults);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_readerScroll, 1);
        grid.Children.Add(left);
        grid.Children.Add(_readerScroll);
        return grid;
    }

    private static StackPanel BuildEmptyMessage()
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 8, Name = "EmptyMessage" };
        panel.Children.Add(new TextBlock { Text = "The guide isn't here yet", FontSize = 17, FontWeight = FontWeight.SemiBold,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") });
        panel.Children.Add(ToolWindowParts.Small("TabForge could not find the Beginner's Guide chapters next to the program. Reinstalling or updating TabForge puts them back; the rest of the app is not affected."));
        return panel;
    }

    private void WireEvents()
    {
        _basicChoice.IsCheckedChanged += (_, _) => SwitchGuide(TutorialGuide.Basic);
        _detailedChoice.IsCheckedChanged += (_, _) => SwitchGuide(TutorialGuide.Detailed);
        _back.Click += (_, _) => Step(-1);
        _forward.Click += (_, _) => Step(1);
        _previous.Click += (_, _) => OpenNeighbour(-1);
        _next.Click += (_, _) => OpenNeighbour(1);
        _search.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) SearchChanged(); };
        _clearSearch.Click += (_, _) => _search.Text = "";
        _exportPdf.Click += (_, _) => _status.Text = ToolWindowParts.NeedsFullApp("PDF export");
        ToolWindowParts.NotAvailable(_exportPdf);
        _contents.SelectionChanged += (_, _) => ContentsPicked();
        _results.SelectionChanged += (_, _) => ResultPicked();
    }

    private void SwitchGuide(TutorialGuide requested)
    {
        if (_updatingLists) return;
        var guide = TutorialPresenter.SwitchedGuide(requested, TutorialPresenter.DetailedAvailable(_detailed));
        if (guide == _guide) return;
        _guide = guide;
        _library = LibraryFor(guide);
        Title = TutorialPresenter.GuideTitle(guide);
        _history.Clear();
        _search.Text = "";
        ApplyLibraryState();
        if (_library.Chapters.Count > 0) Open(_library.Chapters[0], headingId: null);
    }

    private void ApplyLibraryState()
    {
        var state = TutorialPresenter.LibraryState(_library.Chapters.Count, exporting: false);
        _search.IsEnabled = state.SearchEnabled;
        _exportPdf.IsEnabled = state.ExportEnabled;
        _emptyMessage.IsVisible = state.EmptyMessageVisible;
        _main.IsVisible = state.ReaderVisible;
    }

    /// <summary>Shows a chapter (and scrolls to a heading in it) and records the visit in the history.</summary>
    private void Open(TutorialChapter chapter, string? headingId)
    {
        _history.Visit(chapter.Id, headingId);
        Show(chapter, headingId);
    }

    /// <summary>Shows a chapter without changing the history, which Back and Forward walk.</summary>
    private void Show(TutorialChapter chapter, string? headingId)
    {
        _current = chapter;
        _reader.Children.Clear();
        _headingControls.Clear();
        foreach (var block in chapter.Document.Blocks) _reader.Children.Add(RenderBlock(block));
        RebuildContents();
        UpdateNavigation();
        if (headingId is not null && _headingControls.TryGetValue(headingId, out var heading)) heading.BringIntoView();
        else _readerScroll.Offset = new Vector(0, 0);
    }

    private void Step(int delta)
    {
        var entry = _history.Step(delta);
        if (entry is not { } step) return;
        var chapter = _library.Find(step.ChapterId);
        if (chapter is not null) Show(chapter, step.HeadingId);
    }

    private void OpenNeighbour(int delta)
    {
        var index = _current is null ? -1 : _library.Chapters.ToList().IndexOf(_current);
        var target = index + delta;
        if (target >= 0 && target < _library.Chapters.Count) Open(_library.Chapters[target], headingId: null);
    }

    private void UpdateNavigation()
    {
        var nav = TutorialPresenter.NavButtons(_library.Chapters, _current, _history.Index, _history.Count);
        _back.IsEnabled = nav.Back;
        _forward.IsEnabled = nav.Forward;
        _previous.IsEnabled = nav.Previous;
        _next.IsEnabled = nav.Next;
    }

    private void RebuildContents()
    {
        _updatingLists = true;
        _contentRows.Clear();
        _contentRows.AddRange(TutorialPresenter.ContentsRows(_library.Chapters, _current));
        _contents.ItemsSource = _contentRows.Select(row => new string(' ', 4 * row.Level) + row.Text).ToList();
        _contents.SelectedIndex = -1;
        _updatingLists = false;
    }

    private void ContentsPicked()
    {
        if (_updatingLists || _contents.SelectedIndex < 0) return;
        var row = _contentRows[_contents.SelectedIndex];
        Open(row.Chapter, row.HeadingId);
    }

    private void SearchChanged()
    {
        var text = _search.Text ?? "";
        var query = TutorialPresenter.SearchQuery(text);
        _resultItems.Clear();
        if (query.Length > 0) _resultItems.AddRange(_library.Search(query));
        var panels = TutorialPresenter.SearchPanels(text, _resultItems.Count);
        _clearSearch.IsVisible = panels.ClearVisible;
        _contents.IsVisible = panels.ContentsVisible;
        _results.IsVisible = panels.ResultsVisible;
        _noResults.IsVisible = panels.NoResultsVisible;
        if (panels.NoResultsText is not null) _noResults.Text = panels.NoResultsText;
        _updatingLists = true;
        _results.ItemsSource = _resultItems.Select(r => r.Chapter.DisplayTitle + ": " + r.HeadingText).ToList();
        _updatingLists = false;
    }

    private void ResultPicked()
    {
        if (_updatingLists || _results.SelectedIndex < 0) return;
        var result = _resultItems[_results.SelectedIndex];
        Open(result.Chapter, result.HeadingId);
    }

    // ---- reader: each Markdown block as a control ----

    private Control RenderBlock(TutorialBlock block) => block switch
    {
        TutorialHeading heading => RenderHeading(heading),
        TutorialParagraph paragraph => Text(SpanText(paragraph.Spans)),
        TutorialListItem item => new TextBlock
        {
            Text = (item.Ordered ? item.Number + ". " : "• ") + SpanText(item.Spans), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16 * (item.Depth + 1), 2, 0, 2),
        },
        TutorialImage image => Text("[Picture: " + image.Alt + (image.Caption is null ? "]" : "] " + image.Caption)),
        TutorialCallout callout => RenderCallout(callout),
        TutorialTable table => RenderTable(table),
        TutorialCode code => new TextBlock { Text = code.Text, FontFamily = TabForge.Avalonia.Platform.AvaloniaFonts.Family("Consolas, monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) },
        TutorialRule => new Separator { Margin = new Thickness(0, 8, 0, 8) },
        _ => throw new NotSupportedException("The reader has no control for " + block.GetType().Name + "."),
    };

    private Control RenderHeading(TutorialHeading heading)
    {
        var size = heading.Level switch { 1 => 20d, 2 => 17d, _ => 15d };
        var text = new TextBlock
        {
            Text = SpanText(heading.Spans), FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 4), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
        };
        if (!string.IsNullOrEmpty(heading.Id)) _headingControls[heading.Id] = text;
        return text;
    }

    private Control RenderCallout(TutorialCallout callout)
    {
        var inner = new StackPanel { Spacing = 2 };
        foreach (var block in callout.Blocks) inner.Children.Add(RenderBlock(block));
        return new Border
        {
            Child = inner, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 4, 0, 4), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"),
        };
    }

    private static Control RenderTable(TutorialTable table)
    {
        var rows = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        rows.Children.Add(new TextBlock { Text = string.Join("  |  ", table.Header.Select(SpanText)), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var row in table.Rows) rows.Children.Add(Text(string.Join("  |  ", row.Select(SpanText))));
        return rows;
    }

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") };

    /// <summary>The words of a run of spans, with their Markdown marks removed.</summary>
    private static string SpanText(IEnumerable<TutorialSpan> spans) => string.Concat(spans.Select(span => span.Text));
}
