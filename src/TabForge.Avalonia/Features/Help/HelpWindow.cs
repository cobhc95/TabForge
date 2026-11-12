using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Features.Help;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Features.Help;

// Owns: the Help window: the left column (search box with its ready notice, the Troubleshooting quick-link, the chapters grouped Start here / Writing music /
//   Playing and tools / Help, the open chapter highlighted with the accent tint), the right pane (the reading pane or the search results), the debounced search,
//   opening a chapter or a result at its section, the Close button and Esc, and the remembered size.
// Does not own: reading and parsing the chapters (HelpContent), the search engine (Core HelpSearchIndex), the chapter and result drawing (HelpReader, HelpResults),
//   the one-window rule and the F1 command (HelpFeature), saving the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.HelpWindowTests.
internal sealed class HelpWindow : Window
{
    private readonly HelpContent _content;
    private readonly AppSettings? _sizing;
    private readonly Action? _saveSettings;
    private readonly TextBox _search = new() { Name = "SearchBox", Watermark = "Search help" };
    private readonly TextBlock _ready = DialogMetrics.Spaced(new TextBlock { Name = "SearchStatus", FontSize = ChromeMetrics.TextSmall }, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpRowGap, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpBlockGap);
    private readonly HelpReader _reader;
    private readonly HelpResults _results;
    private readonly List<HelpNavRow> _rows = new();
    private HelpSearchIndex? _index;
    private CancellationTokenSource? _debounce;

    internal HelpWindow(HelpContent content, AppSettings? sizing, Action? saveSettings)
    {
        _content = content;
        _sizing = sizing;
        _saveSettings = saveSettings;
        Title = "TabForge Help";
        ShowInTaskbar = false;
        MinWidth = ChromeMetrics.HelpWindowMinWidth;
        MinHeight = ChromeMetrics.HelpWindowMinHeight;
        DialogMetrics.CentreOnOwner(this);
        FontSize = ChromeMetrics.TextDialog;
        HelpRichText.Bind(this, BackgroundProperty, "WindowBackground");
        HelpRichText.Bind(this, ForegroundProperty, "TextPrimary");
        ApplyInitialSize();

        _reader = new HelpReader(content, Open);
        _results = new HelpResults(Open) { IsVisible = false };
        HelpRichText.Bind(_ready, TextBlock.ForegroundProperty, "TextMuted");
        _ready.Text = PreparingText;

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        var nav = BuildNav();
        var divider = new Border { Width = ChromeMetrics.HelpDivider };
        HelpRichText.Bind(divider, Border.BackgroundProperty, "SoftBorder");
        Grid.SetColumn(divider, 1);
        var right = new Grid();
        right.Children.Add(_reader);
        right.Children.Add(_results);
        Grid.SetColumn(right, 2);
        body.Children.Add(nav);
        body.Children.Add(divider);
        body.Children.Add(right);

        var close = DialogMetrics.ActionButton(new Button { Content = "Close", Name = "CloseButton", HorizontalContentAlignment = HorizontalAlignment.Center });
        close.Click += (_, _) => Close();
        var footer = DialogMetrics.Inset(new DockPanel { LastChildFill = false });
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;

        _search.TextChanged += (_, _) => OnSearchChanged();
        Select(HelpChapterCatalog.StartIndex);
        _reader.Show(HelpChapterCatalog.StartIndex, null);
        Closing += (_, _) => RememberSize();
        // The index builds in the background (once for the feature); a finished one is taken at once, as awaiting a completed task continues in place.
        _ = AwaitIndexAsync();
    }

    private const string ReadyText = "Search ready", PreparingText = "Preparing search…";

    /// <summary>The chapter the reading pane holds (it keeps its chapter while the results show).</summary>
    internal int ChapterIndex => _reader.ChapterIndex;

    internal HelpReader Reader => _reader;
    internal HelpResults Results => _results;
    internal TextBox SearchBox => _search;
    internal string StatusText => _ready.Text ?? "";
    internal bool ShowingResults => _results.IsVisible;

    /// <summary>Opens chapter <paramref name="index"/> in the reading pane, at a section when given, and highlights it in the list.</summary>
    internal void Open(int index, int? section)
    {
        if (index < 0 || index >= HelpChapterCatalog.All.Count) return;
        _results.IsVisible = false;
        _reader.IsVisible = true;
        Select(index);
        _reader.Show(index, section);
    }

    private void Select(int index)
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].Selected = _rows[i].Chapter == index;
    }

    // ---- left column ----

    private Control BuildNav()
    {
        var column = new DockPanel { Width = ChromeMetrics.HelpNavWidth };
        HelpRichText.Bind(column, Panel.BackgroundProperty, "PanelBackground");
        DialogMetrics.Spaced(_search, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpNavIndent, 0);
        DockPanel.SetDock(_search, Dock.Top);
        column.Children.Add(_search);
        DockPanel.SetDock(_ready, Dock.Top);
        column.Children.Add(_ready);

        var list = new StackPanel();
        var trouble = HelpRichText.Link("Troubleshooting", ChromeMetrics.HelpBodySize, () => Open(HelpChapterCatalog.TroubleshootingIndex, null), FontWeight.SemiBold);
        trouble.Name = "TroubleshootingLink";
        DialogMetrics.Spaced(trouble, ChromeMetrics.HelpNavIndent, 0, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpBlockGap);
        list.Children.Add(trouble);
        string? group = null;
        for (var i = 0; i < HelpChapterCatalog.All.Count; i++)
        {
            var info = HelpChapterCatalog.All[i];
            if (info.Group != group)
            {
                group = info.Group;
                var heading = HelpRichText.Line(group.ToUpperInvariant(), "TextMuted", ChromeMetrics.TextSmall, FontWeight.SemiBold);
                DialogMetrics.Spaced(heading, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpBlockGap, ChromeMetrics.HelpNavIndent, ChromeMetrics.HelpRowGap);
                list.Children.Add(heading);
            }
            var row = new HelpNavRow(i, info.Title, Open, Step);
            _rows.Add(row);
            list.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = list, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        column.Children.Add(scroll);
        return column;
    }

    // Up / Down in the list opens the neighbouring chapter and keeps the focus on its row.
    private void Step(int from, int by)
    {
        var to = from + by;
        if (to < 0 || to >= HelpChapterCatalog.All.Count) return;
        Open(to, null);
        _rows[to].Focus();
    }

    // ---- search ----

    private void OnSearchChanged()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = null;
        if (string.IsNullOrWhiteSpace(_search.Text)) { _results.IsVisible = false; _reader.IsVisible = true; return; }
        var source = _debounce = new CancellationTokenSource();
        _ = RunAfterDelayAsync(source.Token);
    }

    private async Task RunAfterDelayAsync(CancellationToken token)
    {
        try { await Task.Delay(UiTiming.HelpSearchDebounceMs, token); }
        catch (OperationCanceledException) { return; }
        RunSearch();
    }

    /// <summary>Runs the search for the box's text now and shows the results (or the preparing notice while the index builds).</summary>
    internal void RunSearch()
    {
        var text = _search.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) { _results.IsVisible = false; _reader.IsVisible = true; return; }
        _reader.IsVisible = false;
        _results.IsVisible = true;
        if (_index is null) { _results.ShowPreparing(); return; }
        _results.Show(text, _index.Search(text));
    }

    private async Task AwaitIndexAsync()
    {
        try { _index = await _content.Index(); }
        catch (Exception ex) { Trace.Error(Trace.Ui, "help search index: " + ex.Message); return; }
        _ready.Text = ReadyText;
        if (_results.IsVisible) RunSearch();
    }

    // ---- window ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; _search.Focus(); _search.SelectAll(); }
        else if (e.Key == Key.Enter && _search.IsFocused) { e.Handled = true; _debounce?.Cancel(); RunSearch(); }
    }

    private void ApplyInitialSize()
    {
        var wanted = _sizing is not null && double.IsFinite(_sizing.HelpWindowWidth) && double.IsFinite(_sizing.HelpWindowHeight) && _sizing.HelpWindowWidth > 0 && _sizing.HelpWindowHeight > 0
            ? new Size(_sizing.HelpWindowWidth, _sizing.HelpWindowHeight)
            : new Size(ChromeMetrics.HelpWindowWidth, ChromeMetrics.HelpWindowHeight);
        var fit = WindowUiScale.ClampRememberedSize(wanted, new Size(MinWidth, MinHeight), this);
        Width = fit.Width;
        Height = fit.Height;
    }

    private void RememberSize()
    {
        if (_sizing is null || WindowState != WindowState.Normal || Bounds.Width < MinWidth || Bounds.Height < MinHeight) return;
        if (_sizing.HelpWindowWidth == Bounds.Width && _sizing.HelpWindowHeight == Bounds.Height) return;
        _sizing.HelpWindowWidth = Bounds.Width;
        _sizing.HelpWindowHeight = Bounds.Height;
        _saveSettings?.Invoke();
    }
}
