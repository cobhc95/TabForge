using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Add plug-in window, the plug-in browser: the search box, the sortable Name / Vendor / Format / Role
/// list, the status line, the remember option and the action buttons, in the order with the texts, tooltips and automation ids.
/// Enter or a double-click on a row chooses it; in the search box Down moves to the list and Enter chooses the selected (or first) row; Esc cancels.
/// Owns: the controls, the sort state, the key and double-click routing, and the chosen plug-in.
/// Does not own: the rows, filter, sort order and status text (PluginBrowserListPresenter); scanning, identifying and the folder lists (PluginBrowserSession);
/// the file and folder pickers (IFileDialogs), the settings page (the opener passed in) and approval (the caller, which approves what is chosen).
/// The scan, the identify probes, the remembered list and the folder choices run in a PluginBrowserSession; the buttons here ask the session and the pickers.
/// Tests: TabForge.Avalonia.Tests.PluginBrowserWindowTests.
/// </summary>
public sealed class PluginBrowserWindow : Window
{
    // The list columns; a header's index is the presenter's sort column.
    private const string ColumnWidths = "300,190,70,100";
    private static readonly IReadOnlyList<string> ColumnHeaders = ["Name", "Vendor", "Format", "Role"];
    private static readonly Thickness CellPadding = new(6, 3);

    private readonly PluginBrowserSession _session;
    private readonly TabForge.Platform.IFileDialogs _files;
    private readonly Action<Window> _openSettings;
    private readonly ProgressBar _progress = new() { Height = 4, IsIndeterminate = true, IsVisible = false, Margin = new Thickness(0, 6, 0, 0), Name = "ScanProgress" };
    private readonly TextBox _search = new() { Name = "SearchBox", Margin = new Thickness(0, 0, 0, 6) };
    private readonly ListBox _list = new() { Name = "PluginList" };
    private readonly TextBlock _status = new() { Name = "StatusText", Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CheckBox _remember = new() { Name = "RememberBox", Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel _buttons = new() { Name = "ButtonBar", Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<TextBlock> _sortArrows = new();
    private readonly Button _scanStandard;   // the empty-state button; shown only while the list is empty (the preview has the standard folders off)
    private int _sortColumn;
    private bool _sortAscending = true;
    // Perf: typing filters at once on the first keystroke and once more when it rests (~100 ms).
    private readonly TabForge.Platform.IUiTimer _filterDebounce = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication()
        .CreateTimer(System.TimeSpan.FromMilliseconds(TabForge.Avalonia.Platform.UiTiming.FilterDebounceMs));
    private bool _filterDirty;

    /// <summary>The plug-in of the row chosen with Enter or a double-click, or the file chosen by hand; null when the window closes without one.</summary>
    public VstPluginInfo? Chosen { get; private set; }

    /// <param name="session">The plug-in list, scan and identify work.</param>
    /// <param name="files">The file and folder pickers.</param>
    /// <param name="openSettings">Opens the audio and plug-in settings over the given window.</param>
    public PluginBrowserWindow(PluginBrowserSession session, TabForge.Platform.IFileDialogs files, Action<Window> openSettings)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _files = files;
        _openSettings = openSettings;
        Title = "Add plug-in";
        Width = 760;
        Height = 560;
        MinWidth = 520;
        MinHeight = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var root = new DockPanel { Margin = new Thickness(10) };
        root.Children.Add(BuildSearchBox());
        root.Children.Add(BuildBottom());
        _scanStandard = BuildButtons();
        root.Children.Add(BuildList());
        Content = root;
        WireInput();
        session.Changed += OnSessionChanged;
        session.Status += OnSessionStatus;
        Closed += (_, _) => { session.Changed -= OnSessionChanged; session.Status -= OnSessionStatus; _filterDebounce.Stop(); };
        Filter();
        Opened += (_, _) =>
        {
            _search.Focus();
            _ = session.ScansOnOpen ? Scan(null) : Identify();
        };
    }

    /// <summary>Shows the window over <paramref name="owner"/> and returns the plug-in the user chose, or null.</summary>
    public static async System.Threading.Tasks.Task<VstPluginInfo?> ChooseAsync(Window owner, PluginBrowserSession session, TabForge.Platform.IFileDialogs files, Action<Window> openSettings)
    {
        var window = new PluginBrowserWindow(session, files, openSettings);
        return await new TabForge.Avalonia.Platform.AvaloniaDialogHost().ShowModal<bool>(window, owner) ? window.Chosen : null;
    }

    private void OnSessionChanged() { _progress.IsVisible = _session.Scanning; Filter(); }

    private void OnSessionStatus(string text) => _status.Text = text;

    private async System.Threading.Tasks.Task Scan(System.Collections.Generic.List<string>? roots)
    {
        _progress.IsVisible = true;
        await _session.ScanAsync(roots);
        _progress.IsVisible = false;
        _ = Identify();
    }

    private System.Threading.Tasks.Task Identify() => _session.IdentifyAsync(() => IsVisible);

    private Control BuildSearchBox()
    {
        ToolTip.SetTip(_search, "Type to filter by name or vendor");
        AutomationProperties.SetAutomationId(_search, "Plugins.Search");
        AutomationProperties.SetName(_search, "Search plug-ins");
        DockPanel.SetDock(_search, Dock.Top);
        return _search;
    }

    // The status line, the remember option and the button row, below the list as in the window.
    private Control BuildBottom()
    {
        _status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        _remember.Content = TabForge.Services.PluginSettings.RememberScanLabel;
        _remember.IsChecked = _session.Settings.RememberScan;
        ToolTip.SetTip(_remember, "On: the plug-ins found are saved and this window opens instantly from that list; use Rescan after installing new plug-ins. Off: the folders are scanned each time this window opens.");
        _remember.IsCheckedChanged += (_, _) => { if ((_remember.IsChecked == true) != _session.Settings.RememberScan) _session.SetRemember(_remember.IsChecked == true); };
        var bottom = new StackPanel { Children = { _progress, _status, _remember, _buttons } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        return bottom;
    }

    // The buttons in order; returns the empty-state button. The folder list in its tooltip is VstScannerService.StandardFolderLabels.
    private Button BuildButtons()
    {
        var scanStandard = AddButton("Scan the standard VST folders", "Plugins.ScanStandard",
            "Turns on Settings > Audio & Plug-ins > Also scan the standard VST folders, then scans: " + string.Join(", ", VstScannerService.StandardFolderLabels) + ", and the common folders list (Edit list…).",
            () => { _session.EnableStandardFolders(); _scanStandard.IsVisible = false; _ = Scan(null); });
        AddButton("Add folder…", "Plugins.AddFolder", "Add one or more folders that hold plug-ins; they are kept in Settings > Audio & Plug-ins.", () =>
        {
            var chosen = _files.PickFolders(new TabForge.Platform.FileDialogOptions { Title = "Add plug-in folders", Multiselect = true }, this);
            if (chosen is null) return;
            _session.AddFolders(chosen);
            _ = Scan(null);
        });
        AddButton("Browse for a plug-in file…", "Plugins.BrowseFile", "Add a single plug-in file without adding its folder.", BrowseForFile);
        AddButton("Rescan", "Plugins.Rescan", "Scan the folders again (after installing or removing plug-ins).", () => _ = Scan(null));
        AddButton("Scan common folders", "Plugins.ScanCommon", "Scan the usual VST locations (Steinberg VstPlugins, Common Files VST3, VST2 ...); found plug-ins are merged into the list. Edit the folder list with Edit list.", () =>
        {
            var roots = _session.CommonRoots();
            if (roots.Count == 0) { _status.Text = PluginBrowserSession.NoCommonFoldersText; return; }
            _ = Scan(roots);
        });
        AddButton("Edit list…", "Plugins.EditList", "Edit the list of common folders (also in Settings > Audio & Plug-ins), with Reset to defaults.", EditCommonFolders);
        AddButton("Folder settings…", "Plugins.FolderSettings", "Plug-in folders, remembered scans and plug-in window options (Settings > Audio & Plug-ins).", () => { Close(false); _openSettings(this); });
        AddButton("Cancel", "Plugins.Cancel", "Close without adding", () => Close(false)).IsCancel = true;
        return scanStandard;
    }

    private void BrowseForFile()
    {
        var path = _files.OpenFile(new TabForge.Platform.FileDialogOptions { Title = "Choose a VST plug-in", Filter = "VST plug-ins (*.vst3;*.dll)|*.vst3;*.dll" }, this);
        if (path is null) return;
        if (PluginBrowserSession.FromFile(path, out var problem) is not { } info) { _status.Text = problem ?? ""; return; }
        Chosen = info;
        Close(true);
    }

    private async void EditCommonFolders()
    {
        try
        {
            var lines = await new TabForge.Avalonia.Platform.AvaloniaDialogHost().ShowModal<string[]>(new CommonFoldersWindow(_session.CommonFolderLines), this);
            if (lines is not null) _session.SetCommonFolders(lines);
        }
        catch (Exception ex)
        {
            // Reported, not swallowed (UiTasks.Forget pattern): the failure shows on the window's status line.
            _status.Text = "Common folders failed: " + ex.Message;
        }
    }

    private Button AddButton(string text, string automationId, string tip, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 3) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetAutomationId(button, automationId);
        button.Name = automationId.Replace("Plugins.", "") + "Button";
        button.Click += (_, _) => click();
        _buttons.Children.Add(button);
        return button;
    }

    private Control BuildList()
    {
        var headers = new Grid { ColumnDefinitions = new ColumnDefinitions(ColumnWidths) };
        for (var column = 0; column < ColumnHeaders.Count; column++) headers.Children.Add(SortHeader(column));
        var headerRow = new Border { Child = headers, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerRow[!Border.BorderBrushProperty] = new DynamicResourceExtension("ChromeBorder");
        DockPanel.SetDock(headerRow, Dock.Top);

        AutomationProperties.SetAutomationId(_list, "Plugins.List");
        AutomationProperties.SetName(_list, "Plug-ins");
        _list[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        // Perf (H13/H15): an explicit virtualizing panel + a recycling template, so only the visible plug-in rows
        // build controls no matter how many folders were scanned.
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<PluginBrowserRow>((row, _) => BuildRow(row), supportsRecycling: true);
        // The themed row hover and selection look of the rows (look "ListRow"), with the header's cell padding so the columns line up.
        _list.ContainerPrepared += (_, e) =>
        {
            e.Container.Classes.Add("ListRow");
            if (e.Container is ListBoxItem item) item.Padding = CellPadding;
        };
        return new DockPanel { Children = { headerRow, _list } };
    }

    // A column header: a click sorts by its column ascending and a second click reverses.
    private Button SortHeader(int column)
    {
        var title = ColumnHeaders[column];
        var arrow = new TextBlock { FontSize = 9, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        arrow[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Accent");
        _sortArrows.Add(arrow);
        var header = new Button
        {
            Name = "SortBy" + title, Padding = CellPadding, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = title }, arrow } },
        };
        header[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        ToolTip.SetTip(header, $"Sort by {title.ToLowerInvariant()} (click again to reverse)");
        Grid.SetColumn(header, column);
        header.Click += (_, _) =>
        {
            _sortAscending = _sortColumn != column || !_sortAscending;
            _sortColumn = column;
            Filter();
        };
        return header;
    }

    private static Control BuildRow(PluginBrowserRow? row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ColumnWidths) };
        if (row is null) return grid;
        string[] cells = [row.Name, row.Vendor, row.Format, row.Role];
        for (var column = 0; column < cells.Length; column++)
        {
            var cell = new TextBlock { Text = cells[column], TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private void WireInput()
    {
        _list.DoubleTapped += (_, _) => Accept();
        _list.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        _search.AddHandler(KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
        // The property change filters at once (TextChanged is posted to the dispatcher), so Enter sees the new rows, as in the box.
        // A fast query pays the first pass at once and one more when typing rests (~100 ms), not one per keystroke.
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            if (!_filterDirty) return;
            _filterDirty = false;
            Filter();
        };
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (_filterDebounce.IsEnabled) { _filterDirty = true; return; }   // a trailing pass is already queued
            Filter();
            _filterDebounce.Start();
        };
    }

    // Down moves to the first row; Enter chooses the selected row, or the first row when none is selected.
    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _list.ItemCount > 0)
        {
            FlushFilter();
            _list.SelectedIndex = 0;
            _list.ContainerFromIndex(0)?.Focus();
            e.Handled = true;
        }
        if (e.Key == Key.Enter)
        {
            FlushFilter();
            if (_list.SelectedItem is null && _list.ItemCount > 0) _list.SelectedIndex = 0;
            Accept();
            e.Handled = true;
        }
    }

    /// <summary>Runs a queued trailing filter now (Enter, Down: the list shows the latest text).</summary>
    private void FlushFilter()
    {
        _filterDebounce.Stop();
        if (!_filterDirty) return;
        _filterDirty = false;
        Filter();
    }

    private void Accept()
    {
        if (_list.SelectedItem is not PluginBrowserRow row) return;
        if (row.Info.IsUnsupported32) { _status.Text = VstPluginInfo.Text32; return; }
        Chosen = row.Info;
        Close(true);
    }

    private void Filter()
    {
        _list.ItemsSource = PluginBrowserListPresenter.Rows(_session.All, _session.KnownRoles(), _session.NotIdentified, _search.Text ?? "", _sortColumn, _sortAscending);
        for (var column = 0; column < _sortArrows.Count; column++)
            _sortArrows[column].Text = column != _sortColumn ? "" : _sortAscending ? "▲" : "▼";
        if (_session.Scanning) return;
        _scanStandard.IsVisible = _session.All.Count == 0 && !_session.Settings.ScanStandardFolders;
        _status.Text = PluginBrowserListPresenter.StatusText(_session.All.Count, _session.FoldersChosen, _session.Settings.RememberScan);
    }
}
