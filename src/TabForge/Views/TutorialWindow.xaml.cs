using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>One row of the contents list: a chapter, or a heading inside the open chapter.</summary>
internal sealed record TutorialNavItem(string Text, TutorialChapter Chapter, string? HeadingId, int Level)
{
    public Thickness Indent => new(Level == 0 ? 6 : 6 + 12 * Level, Level == 0 ? 3 : 1, 4, Level == 0 ? 3 : 1);
    public FontWeight Weight => Level == 0 ? FontWeights.SemiBold : FontWeights.Normal;
    public double FontSize => Level == 0 ? 13.5 : 12.5;
    public string Automation => Level == 0 ? $"{Chapter.Label}: {Chapter.Title}" : $"Section: {Text}";
}

/// <summary>One search hit in the results list.</summary>
internal sealed record TutorialResultItem(TutorialSearchResult Result)
{
    public string Where => Result.Chapter.Title == Result.HeadingText || Result.HeadingId is null
        ? Result.Chapter.Title : $"{Result.Chapter.Title}  ›  {Result.HeadingText}";
    public string Automation => $"{Where}. {Result.Snippet}";
}

/// <summary>Fills a TextBlock with a search snippet, the matched words in bold on a tinted background (so a match is not shown by colour alone).</summary>
internal static class TutorialSnippet
{
    public static readonly DependencyProperty ResultProperty = DependencyProperty.RegisterAttached(
        "Result", typeof(TutorialSearchResult), typeof(TutorialSnippet), new PropertyMetadata(null, OnResultChanged));

    public static TutorialSearchResult? GetResult(DependencyObject d) => (TutorialSearchResult?)d.GetValue(ResultProperty);
    public static void SetResult(DependencyObject d, TutorialSearchResult? value) => d.SetValue(ResultProperty, value);

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        if (e.NewValue is not TutorialSearchResult result) return;
        var text = result.Snippet;
        var at = 0;
        foreach (var (start, length) in result.Highlights.OrderBy(h => h.Start))
        {
            if (start < at || start + length > text.Length) continue;
            if (start > at) block.Inlines.Add(new Run(text[at..start]));
            var hit = new Run(text.Substring(start, length)) { FontWeight = FontWeights.Bold };
            hit.SetResourceReference(TextElement.ForegroundProperty, "TextStrongBrush");
            hit.SetResourceReference(TextElement.BackgroundProperty, "AccentSoftBrush");
            block.Inlines.Add(hit);
            at = start + length;
        }
        if (at < text.Length) block.Inlines.Add(new Run(text[at..]));
    }
}

/// <summary>
/// Help > Tutorial: the in-app Beginner's Guide. Contents and full-text search on the left, the chapter on the right, history
/// navigation, and Export PDF. Its content is the Markdown in the <c>tutorial</c> folder beside the program.
/// </summary>
public partial class TutorialWindow : Window
{
    private static TutorialWindow? _open;

    private TutorialLibrary _library;
    private TutorialRenderer _renderer;
    private readonly TutorialLibrary _basic;
    private readonly TutorialLibrary _detailed;
    private TutorialGuide _guide = TutorialGuide.Basic;
    private bool _suppressGuide = true;
    private readonly AppSettingsStore? _store;
    private readonly List<(string Chapter, string? Heading)> _history = new();
    private int _historyIndex = -1;
    private TutorialChapter? _current;
    private bool _suppressNav;
    private CancellationTokenSource? _export;
    private string _readerDocumentChapter = "";

    /// <summary>Opens the guide, or brings the open one to the front.</summary>
    public static void ShowOrActivate(Window? owner, TutorialGuide? guide = null)
    {
        if (_open is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            if (guide is { } wanted) existing.SwitchGuide(wanted);
            existing.Activate();
            return;
        }
        var window = new TutorialWindow(TutorialLibrary.Load(TutorialLibrary.GuideFolder(TutorialGuide.Basic)),
            TutorialLibrary.Load(TutorialLibrary.GuideFolder(TutorialGuide.Detailed)), AppSettingsStore.Shared, guide);
        if (owner is { IsLoaded: true }) window.Owner = owner;
        _open = window;
        window.Closed += (_, _) => _open = null;
        window.Show();
    }

    internal TutorialWindow(TutorialLibrary library, AppSettingsStore? store)
        : this(library.Guide == TutorialGuide.Detailed ? TutorialLibrary.Empty() : library,
               library.Guide == TutorialGuide.Detailed ? library : null, store, library.Guide == TutorialGuide.Detailed ? TutorialGuide.Detailed : null) { }

    /// <summary>The window with both guides; <paramref name="initial"/> null = the guide shown last (a missing Detailed Guide falls back to the Basic one).</summary>
    internal TutorialWindow(TutorialLibrary basic, TutorialLibrary? detailed, AppSettingsStore? store, TutorialGuide? initial)
    {
        InitializeComponent();
        _basic = basic;
        _detailed = detailed ?? TutorialLibrary.Empty();
        _library = basic;
        _store = store;
        _renderer = new TutorialRenderer(basic, OnLink);
        ApplySavedSize();
        var hasDetailed = _detailed.Chapters.Count > 0;
        DetailedGuideRadio.IsEnabled = hasDetailed;
        if (!hasDetailed)
        {
            DetailedGuideRadio.ToolTip = "The Detailed Guide is not installed";
            System.Windows.Controls.ToolTipService.SetShowOnDisabled(DetailedGuideRadio, true);
        }
        var wanted = initial ?? (string.Equals(store?.Settings.General.TutorialLastGuide, "detailed", StringComparison.OrdinalIgnoreCase) ? TutorialGuide.Detailed : TutorialGuide.Basic);
        _guide = wanted == TutorialGuide.Detailed && hasDetailed ? TutorialGuide.Detailed : TutorialGuide.Basic;
        _library = _guide == TutorialGuide.Detailed ? _detailed : _basic;
        _renderer = new TutorialRenderer(_library, OnLink);
        ShowLibrary();
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseUp += Window_PreviewMouseUp;
        Closing += Window_Closing;
    }

    // ---- the two guides ----

    internal TutorialGuide Guide => _guide;
    internal bool DetailedGuideAvailable => DetailedGuideRadio.IsEnabled;
    internal TutorialLibrary Library => _library;

    /// <summary>Shows the current library: sets the title and the switch, resets history and search, opens the last chapter.</summary>
    private void ShowLibrary()
    {
        var name = _guide == TutorialGuide.Detailed ? "Detailed Guide" : "Basic Guide";
        Title = "TabForge " + name;
        System.Windows.Automation.AutomationProperties.SetName(this, Title);
        _suppressGuide = true;
        BasicGuideRadio.IsChecked = _guide == TutorialGuide.Basic;
        DetailedGuideRadio.IsChecked = _guide == TutorialGuide.Detailed;
        _suppressGuide = false;
        _history.Clear();
        _historyIndex = -1;
        _current = null;
        _readerDocumentChapter = "";
        SearchBox.Clear();
        ContentsList.ItemsSource = null;
        var any = _library.Chapters.Count > 0;
        EmptyMessage.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Reader.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.IsEnabled = any;
        ExportButton.IsEnabled = any && _export is null;
        if (any)
        {
            var last = _store?.Settings.General.TutorialLastChapter;
            NavigateTo(_library.Find(last) ?? _library.Chapters[0], null, addHistory: true);
        }
        else Reader.Document = new FlowDocument();
        UpdateNavButtons();
    }

    /// <summary>Switches to a guide (the Detailed Guide only when it is installed) and remembers the choice.</summary>
    internal void SwitchGuide(TutorialGuide guide)
    {
        if (guide == TutorialGuide.Detailed && !DetailedGuideAvailable) guide = TutorialGuide.Basic;
        if (guide == _guide) { ShowSwitchState(); return; }
        _guide = guide;
        _library = guide == TutorialGuide.Detailed ? _detailed : _basic;
        _renderer = new TutorialRenderer(_library, OnLink);
        if (_store is not null) { _store.Settings.General.TutorialLastGuide = guide == TutorialGuide.Detailed ? "detailed" : "basic"; _store.MarkChanged(this); }
        ShowLibrary();
    }

    private void ShowSwitchState()
    {
        _suppressGuide = true;
        BasicGuideRadio.IsChecked = _guide == TutorialGuide.Basic;
        DetailedGuideRadio.IsChecked = _guide == TutorialGuide.Detailed;
        _suppressGuide = false;
    }

    private void Guide_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressGuide) return;
        SwitchGuide(ReferenceEquals(sender, DetailedGuideRadio) ? TutorialGuide.Detailed : TutorialGuide.Basic);
    }

    // ---- size and settings ----

    private void ApplySavedSize()
    {
        if (_store?.Settings.General is not { } g) return;
        if (g.TutorialWindowWidth > 0 && g.TutorialWindowHeight > 0)
        {
            Width = Math.Min(Math.Max(MinWidth, g.TutorialWindowWidth), SystemParameters.WorkArea.Width);
            Height = Math.Min(Math.Max(MinHeight, g.TutorialWindowHeight), SystemParameters.WorkArea.Height);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _export?.Cancel();
        if (_store is null) return;
        var g = _store.Settings.General;
        if (WindowState == WindowState.Normal) { g.TutorialWindowWidth = Math.Round(ActualWidth); g.TutorialWindowHeight = Math.Round(ActualHeight); }
        if (_current is not null) g.TutorialLastChapter = _current.Id;
        _store.MarkChanged(this);
    }

    // ---- navigation ----

    internal TutorialChapter? CurrentChapter => _current;
    internal int HistoryCount => _history.Count;

    internal void NavigateTo(TutorialChapter chapter, string? headingId, bool addHistory)
    {
        if (!ReferenceEquals(chapter, _current) || _readerDocumentChapter != chapter.Id)
        {
            _current = chapter;
            _readerDocumentChapter = chapter.Id;
            Reader.Document = _renderer.Render(chapter);
            Reader.SetResourceReference(BackgroundProperty, TutorialRenderer.ReadingSurfaceKey);
            ApplyReadingColumn();
            if (_store is not null) { _store.Settings.General.TutorialLastChapter = chapter.Id; _store.MarkChanged(this); }
            BuildContents(chapter);
        }
        if (addHistory)
        {
            if (_historyIndex >= 0 && _history[_historyIndex] == (chapter.Id, headingId)) { }
            else
            {
                if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add((chapter.Id, headingId));
                _historyIndex = _history.Count - 1;
            }
        }
        SelectContentsItem(chapter, headingId);
        ScrollToHeading(headingId);
        UpdateNavButtons();
    }

    private void BuildContents(TutorialChapter open)
    {
        var items = new List<TutorialNavItem>();
        foreach (var chapter in _library.Chapters)
        {
            items.Add(new TutorialNavItem(chapter.DisplayTitle, chapter, null, 0));
            if (!ReferenceEquals(chapter, open)) continue;
            foreach (var h in chapter.SubHeadings) items.Add(new TutorialNavItem(h.Text, chapter, h.Id, h.Level - 1));
        }
        _suppressNav = true;
        ContentsList.ItemsSource = items;
        _suppressNav = false;
    }

    private void SelectContentsItem(TutorialChapter chapter, string? headingId)
    {
        if (ContentsList.ItemsSource is not List<TutorialNavItem> items) return;
        var item = items.FirstOrDefault(i => ReferenceEquals(i.Chapter, chapter) && string.Equals(i.HeadingId, headingId, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => ReferenceEquals(i.Chapter, chapter) && i.Level == 0);
        _suppressNav = true;
        ContentsList.SelectedItem = item;
        _suppressNav = false;
        if (item is not null) ContentsList.ScrollIntoView(item);
    }

    private void ScrollToHeading(string? headingId)
    {
        if (headingId is not null && _renderer.Anchors.TryGetValue(headingId, out var block))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => block.BringIntoView()));
        else
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FindScrollViewer()?.ScrollToTop()));
    }

    private ScrollViewer? FindScrollViewer() => Reader.Template?.FindName("PART_ContentHost", Reader) as ScrollViewer;

    internal void OnLink(string href)
    {
        if (_library.TryResolveLink(href, _current, out var chapter, out var heading)) { NavigateTo(chapter, heading, addHistory: true); return; }
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Services.Trace.Error(Services.Trace.Ui, "tutorial: open link: " + ex.Message); }
        }
    }

    internal void GoHistory(int delta)
    {
        var target = _historyIndex + delta;
        if (target < 0 || target >= _history.Count) return;
        _historyIndex = target;
        var (id, heading) = _history[target];
        if (_library.Find(id) is { } chapter) NavigateTo(chapter, heading, addHistory: false);
    }

    private void UpdateNavButtons()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        var index = _current is null ? -1 : _library.Chapters.ToList().IndexOf(_current);
        PrevButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < _library.Chapters.Count - 1;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GoHistory(-1);
    private void Forward_Click(object sender, RoutedEventArgs e) => GoHistory(1);
    private void Prev_Click(object sender, RoutedEventArgs e) => StepChapter(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => StepChapter(1);

    private void StepChapter(int delta)
    {
        if (_current is null) return;
        var index = _library.Chapters.ToList().IndexOf(_current) + delta;
        if (index >= 0 && index < _library.Chapters.Count) NavigateTo(_library.Chapters[index], null, addHistory: true);
    }

    private void Contents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNav || ContentsList.SelectedItem is not TutorialNavItem item) return;
        NavigateTo(item.Chapter, item.HeadingId, addHistory: true);
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not TutorialResultItem item) return;
        NavigateTo(item.Result.Chapter, item.Result.HeadingId, addHistory: true);
    }

    // ---- search ----

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (query.Length == 0)
        {
            ResultsList.ItemsSource = null;
            ResultsList.Visibility = Visibility.Collapsed;
            NoResults.Visibility = Visibility.Collapsed;
            ContentsList.Visibility = Visibility.Visible;
            return;
        }
        var results = _library.Search(query).Select(r => new TutorialResultItem(r)).ToList();
        ResultsList.ItemsSource = results;
        ContentsList.Visibility = Visibility.Collapsed;
        ResultsList.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = results.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        NoResults.Text = $"Nothing matches “{query}”.";
    }

    internal void SetSearch(string text) => SearchBox.Text = text;
    internal int ResultCount => (ResultsList.ItemsSource as List<TutorialResultItem>)?.Count ?? 0;

    private void ClearSearch_Click(object sender, RoutedEventArgs e) { ClearSearch(); SearchBox.Focus(); }

    private void ClearSearch()
    {
        SearchBox.Clear();
    }

    // ---- keyboard and mouse ----

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (ctrl && key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (key == Key.Escape && SearchBox.Text.Length > 0)
        {
            ClearSearch();
            (_current is null ? (UIElement)SearchBox : ContentsList).Focus();
            e.Handled = true;
        }
        else if (alt && key == Key.Left) { GoHistory(-1); e.Handled = true; }
        else if (alt && key == Key.Right) { GoHistory(1); e.Handled = true; }
        else if (key == Key.Enter && ReferenceEquals(e.OriginalSource, SearchBox) && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (key == Key.Down && ReferenceEquals(e.OriginalSource, SearchBox) && ResultsList.Visibility == Visibility.Visible)
        {
            ResultsList.Focus();
            if (ResultsList.SelectedIndex < 0 && ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1) { GoHistory(-1); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { GoHistory(1); e.Handled = true; }
    }

    // ---- reading column ----

    private void Reader_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyReadingColumn();

    /// <summary>Centres the text in a column of at most 820 units and caps every picture to that column.</summary>
    private void ApplyReadingColumn()
    {
        if (Reader.Document is not { } doc) return;
        var available = Math.Max(200, Reader.ActualWidth - 20);   // the scroll bar
        var pad = Math.Max(24, (available - 820) / 2);
        doc.PagePadding = new Thickness(pad, 24, pad, 40);
        var column = Math.Max(120, available - 2 * pad - 2);
        foreach (var image in _renderer.Images) image.MaxWidth = column;
    }

    // ---- export ----

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_export is not null || _library.Chapters.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = _guide == TutorialGuide.Detailed ? "Export the Detailed Guide as a PDF" : "Export the Basic Guide as a PDF",
            Filter = "PDF document (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = TutorialPdfOptions.DefaultFileName(_guide),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        _ = RunExportAsync(dialog.FileName);
    }

    private async Task RunExportAsync(string path)
    {
        var cts = new CancellationTokenSource();
        _export = cts;
        ExportButton.IsEnabled = false;
        StatusBar.Visibility = Visibility.Visible;
        CancelExportButton.Visibility = Visibility.Visible;
        DismissStatusButton.Visibility = Visibility.Collapsed;
        ExportProgress.Visibility = Visibility.Visible;
        ExportProgress.IsIndeterminate = false;
        ExportProgress.Maximum = _library.Chapters.Count + 1;
        ExportProgress.Value = 0;
        SetStatus("Preparing the PDF…");
        var progress = new Progress<(int Done, int Total, string Text)>(p =>
        {
            ExportProgress.Value = p.Done;
            if (p.Text == "Laying out pages") ExportProgress.IsIndeterminate = true;
            SetStatus(p.Text == "Done" ? "Finishing…" : p.Text == "Laying out pages" ? "Laying out pages…" : $"Writing chapter {p.Done + 1} of {p.Total}: {p.Text}");
        });
        var options = TutorialPdfOptions.For(_library, $"Version {AppInfo.DisplayVersion}");
        try
        {
            var library = _library;
            var pages = await Task.Run(() => TutorialPdfExporter.Export(library, path, options, (done, total, text) => ((IProgress<(int, int, string)>)progress).Report((done, total, text)), cts.Token), cts.Token);
            ShowExportResult(path, pages);
        }
        catch (OperationCanceledException) { SetStatus("PDF export cancelled."); DismissStatusButton.Visibility = Visibility.Visible; } // Not logged: PDF export cancelled: expected
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Services.Trace.Error(Services.Trace.Ui, "PDF export: write: " + ex.Message);
            SetStatus("The PDF could not be written: " + ex.Message);
            DismissStatusButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _export = null;
            cts.Dispose();
            ExportButton.IsEnabled = true;
            CancelExportButton.Visibility = Visibility.Collapsed;
            ExportProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowExportResult(string path, int pages)
    {
        StatusText.Inlines.Clear();
        StatusText.Inlines.Add(new Run($"Saved {Path.GetFileName(path)} ({pages} pages).  "));
        var open = new Hyperlink(new Run("Open it")) { ToolTip = "Open the PDF in your PDF viewer" };
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Services.Trace.Error(Services.Trace.Ui, "tutorial: open PDF: " + ex.Message); } };
        var folder = new Hyperlink(new Run("Show in folder")) { ToolTip = "Show the file in Explorer" };
        folder.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Services.Trace.Error(Services.Trace.Ui, "tutorial: show in folder: " + ex.Message); } };
        foreach (var link in new[] { open, folder }) link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        StatusText.Inlines.Add(open);
        StatusText.Inlines.Add(new Run("   "));
        StatusText.Inlines.Add(folder);
        DismissStatusButton.Visibility = Visibility.Visible;
    }

    private void SetStatus(string text) { StatusText.Inlines.Clear(); StatusText.Text = text; }
    private void CancelExport_Click(object sender, RoutedEventArgs e) { _export?.Cancel(); SetStatus("Cancelling…"); }
    private void DismissStatus_Click(object sender, RoutedEventArgs e) => StatusBar.Visibility = Visibility.Collapsed;

    // ---- off-screen picture (documentation, tests) ----

    /// <summary>Lays the window out without showing it and writes a PNG of it.</summary>
    internal static void RenderPng(TutorialLibrary library, string? chapterId, string? search, double width, double height, string path, double scale = 1)
    {
        var window = new TutorialWindow(library, null);
        if (library.Find(chapterId) is { } chapter) window.NavigateTo(chapter, null, addHistory: true);
        if (!string.IsNullOrEmpty(search)) window.SetSearch(search);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        FilePathPolicy.WriteAtomically(path, encoder.Save);
    }
}
