using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// The Add plug-in window, built section by section: search box, list, scan progress/status, action buttons and the
/// remember option; scanning and identifying run while the window is open.
/// </summary>
internal sealed class PluginBrowserDialog
{
    private sealed record Row(string Name, string Vendor, string Format, string Role, VstPluginInfo Info);

    private readonly IFxChainHost _host;
    private readonly PluginSettings _settings;
    private readonly Window _w;
    private VstPluginInfo? _result;

    private TextBox _search = null!;
    private StackPanel _bottom = null!;
    private ProgressBar _progress = null!;
    private TextBlock _status = null!;
    private WrapPanel _buttons = null!;
    private ListView _list = null!;
    private GridView _view = null!;
    private Button? _scanStandard;   // the empty-state button; shown only while the list is empty and the standard folders are off

    private IReadOnlyList<VstPluginInfo> _all;
    private bool _scanning;
    private bool _identifying;
    private int _sortColumn;
    private bool _sortAscending = true;
    private readonly HashSet<string> _notIdentified = new(StringComparer.OrdinalIgnoreCase);   // not approved: its role is not read by running it

    public PluginBrowserDialog(Window owner, IFxChainHost host)
    {
        _host = host;
        _settings = host.PluginSettings;
        _w = new Window
        {
            Title = "Add plug-in", Owner = owner, Width = 760, Height = 560, MinWidth = 520, MinHeight = 320, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        _w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        _all = _settings.RememberScan && _settings.ScanCache.Count > 0
            ? _settings.ScanCache.Select(k => new VstPluginInfo(k.Name, k.Path, k.Format, k.Vendor, k.Role)).ToList()
            : _settings.LastScan;

        var root = new DockPanel { Margin = new Thickness(10) };
        BuildSearchBox(root);
        BuildBottom(root);
        BuildList(root);
        BuildButtons();
        WireInput();
        _w.Content = root;
        Filter();
        // Scan when opening, unless the remembered list is used.
        var hasFolders = _settings.Folders.Count > 0 || _settings.ScanStandardFolders;
        if (hasFolders && !(_settings.RememberScan && _settings.ScanCache.Count > 0)) _w.Loaded += async (_, _) => await Scan();
        else _w.Loaded += async (_, _) => await Identify();
        _search.Focus();
    }

    /// <summary>Shows the window modally; the chosen plug-in, or null.</summary>
    public VstPluginInfo? Run() => DialogHost.ShowModal(_w) == true ? _result : null;

    // ---------- sections ----------

    private void BuildSearchBox(DockPanel root)
    {
        _search = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Type to filter by name or vendor" };
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_search);
    }

    private void BuildBottom(DockPanel root)
    {
        _bottom = new StackPanel();
        DockPanel.SetDock(_bottom, Dock.Bottom);
        _progress = new ProgressBar { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        _status = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _bottom.Children.Add(_progress);
        _bottom.Children.Add(_status);
        _bottom.Children.Add(_buttons);
        root.Children.Add(_bottom);
    }

    private void BuildList(DockPanel root)
    {
        _view = new GridView();
        _view.Columns.Add(new GridViewColumn { Header = "Name", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Name)) });
        _view.Columns.Add(new GridViewColumn { Header = "Vendor", Width = 190, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Vendor)) });
        _view.Columns.Add(new GridViewColumn { Header = "Format", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Format)) });
        _view.Columns.Add(new GridViewColumn { Header = "Role", Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Role)) });
        // Themed, sortable headers (click to sort, again to reverse) and theme row hover/selection.
        _list = new ListView { View = _view };
        UiIds.Id(_search, "Plugins.Search", "Search plug-ins");
        UiIds.Id(_list, "Plugins.List", "Plug-ins");
        ThemedList.StyleRows(_list);
        _list.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        root.Children.Add(_list);
        ThemedList.MakeSortable(_view, (column, ascending) => { _sortColumn = column; _sortAscending = ascending; Filter(); });
    }

    private void BuildButtons()
    {
        _scanStandard = AddButton("Scan the standard VST folders", async () =>
        {
            VstScannerService.EnableStandardFolders(_settings);   // the user's choice: Preferences > Audio & Plug-ins > Also scan the standard VST folders
            _host.SaveSettings();
            _scanStandard!.Visibility = Visibility.Collapsed;
            await Scan();
        }, "Turns on Settings > Audio & Plug-ins > Also scan the standard VST folders, then scans: "
            + string.Join(", ", VstScannerService.StandardFolderLabels) + ", and the common folders list (Edit list…).");
        AddButton("Add folder…", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add plug-in folders", Multiselect = true };
            if (dialog.ShowDialog(_w) != true) return;
            foreach (var folder in dialog.FolderNames)
                if (!_settings.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _settings.Folders.Add(folder);
            _host.SaveSettings();
            await Scan();
        }, "Add one or more folders that hold plug-ins; they are kept in Settings > Audio & Plug-ins.");
        AddButton("Browse for a plug-in file…", BrowseForFile, "Add a single plug-in file without adding its folder.");
        AddButton("Rescan", async () => await Scan(), "Scan the folders again (after installing or removing plug-ins).");
        AddButton("Scan common folders", async () =>
        {
            var roots = VstScannerService.CommonRoots(_settings);
            if (roots.Count == 0) { _status.Text = "None of the common plug-in folders exist on this computer."; return; }
            await Scan(roots);
        }, "Scan the usual VST locations (Steinberg VstPlugins, Common Files VST3, VST2 ...); found plug-ins are merged into the list. Edit the folder list with Edit list.");
        AddButton("Edit list…", EditCommonFolders, "Edit the list of common folders (also in Settings > Audio & Plug-ins), with Reset to defaults.");
        _bottom.Children.Insert(2, BuildRememberOption());
        AddButton("Folder settings…", () => { _w.DialogResult = false; _host.OpenAudioSettings(); }, "Plug-in folders, remembered scans and plug-in window options (Settings > Audio & Plug-ins).");
        AddButton("Cancel", () => _w.DialogResult = false, "Close without adding");
    }

    private Button AddButton(string text, Action click, string tip)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 3, 10, 3), ToolTip = tip };
        b.Click += (_, _) => click();
        var id = text switch
        {
            "Add folder…" => "Plugins.AddFolder", "Browse for a plug-in file…" => "Plugins.BrowseFile", "Rescan" => "Plugins.Rescan",
            "Scan common folders" => "Plugins.ScanCommon", "Edit list…" => "Plugins.EditList", "Folder settings…" => "Plugins.FolderSettings",
            "Cancel" => "Plugins.Cancel", "Scan the standard VST folders" => "Plugins.ScanStandard", _ => null
        };
        if (id is not null) UiIds.Id(b, id);
        _buttons.Children.Add(b);
        return b;
    }

    private void BrowseForFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a VST plug-in", Filter = "VST plug-ins (*.vst3;*.dll)|*.vst3;*.dll" };
        if (dialog.ShowDialog(_w) != true) return;
        var path = dialog.FileName;
        var vst3 = path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);
        if (!vst3 && !PeExports.IsVst2Plugin(path)) { _status.Text = "That file is not a 64-bit VST plug-in."; return; }
        var (vendor, role) = vst3 ? VstScannerService.Vst3ModuleInfo(path) : (VstScannerService.Vst2Vendor(path), "");
        _result = new VstPluginInfo(Path.GetFileNameWithoutExtension(path), path, vst3 ? "VST3" : "VST2", vendor, role);
        _w.DialogResult = true;
    }

    private void EditCommonFolders()
    {
        var box = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 8) };
        box.Text = string.Join("\r\n", _settings.CommonFolders.Count > 0 ? _settings.CommonFolders : VstScannerService.DefaultCommonFolders);
        var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var reset = new Button { Content = "Reset to defaults", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok); row.Children.Add(reset); row.Children.Add(cancel);
        var dock = new DockPanel { Margin = new Thickness(12) };
        var caption = new TextBlock { Text = "Common plug-in folders, one per line (%ENV% variables work; missing folders are skipped)" };
        DockPanel.SetDock(caption, Dock.Top); DockPanel.SetDock(row, Dock.Bottom);
        dock.Children.Add(caption); dock.Children.Add(row); dock.Children.Add(box);
        var dlg = new Window { Title = "Common plug-in folders", Content = dock, Width = 560, Height = 380, Owner = _w, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        dlg.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        dlg.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        reset.Click += (_, _) => box.Text = string.Join("\r\n", VstScannerService.DefaultCommonFolders);
        ok.Click += (_, _) => dlg.DialogResult = true;
        if (dlg.ShowDialog() != true) return;
        var lines = box.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _settings.CommonFolders = lines.SequenceEqual(VstScannerService.DefaultCommonFolders) ? new List<string>() : lines;
        _host.SaveSettings();
    }

    /// <summary>Remember: the list is kept and shown at once next time, without scanning the folders again.</summary>
    private CheckBox BuildRememberOption()
    {
        var remember = new CheckBox
        {
            Content = "Remember plug-ins (load from the saved list, no scan each time)", IsChecked = _settings.RememberScan,
            Margin = new Thickness(0, 8, 0, 0), ToolTip = "On: the plug-ins found are saved and this window opens instantly from that list; use Rescan after installing new plug-ins. Off: the folders are scanned each time this window opens."
        };
        remember.Click += (_, _) =>
        {
            _settings.RememberScan = remember.IsChecked == true;
            _settings.ScanCache = _settings.RememberScan ? CacheOfAll() : new List<KnownPlugin>();
            _host.SaveSettings();
            Filter();
        };
        return remember;
    }

    private List<KnownPlugin> CacheOfAll() =>
        _all.Select(p => new KnownPlugin { Name = p.Name, Path = p.Path, Format = p.Format, Vendor = p.Vendor, Role = p.Role }).ToList();

    private void WireInput()
    {
        _list.MouseDoubleClick += (_, _) => Accept();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list.Items.Count > 0) { _list.SelectedIndex = 0; (_list.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem)?.Focus(); e.Handled = true; }
            if (e.Key == Key.Enter) { if (_list.SelectedItem is null && _list.Items.Count > 0) _list.SelectedIndex = 0; Accept(); }
        };
        _search.TextChanged += (_, _) => Filter();
    }

    private void Accept()
    {
        if (_list.SelectedItem is Row row) { _result = row.Info; _w.DialogResult = true; }
    }

    // ---------- list contents, scanning, identifying ----------

    private VstPluginInfo WithKnown(VstPluginInfo p)
    {
        var known = _settings.Probed.FirstOrDefault(k => string.Equals(k.Path, p.Path, StringComparison.OrdinalIgnoreCase));
        return known is null ? p : p with { Role = p.Role.Length > 0 ? p.Role : known.Role, Vendor = p.Vendor.Length > 0 ? p.Vendor : known.Vendor };
    }

    private void Filter()
    {
        var q = _search.Text.Trim();
        var rows = _all.Select(WithKnown)
            .Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Vendor.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(p => new Row(p.Name, p.Vendor, p.Format, p.Role.Length > 0 ? p.Role : _notIdentified.Contains(p.Path) ? "approve to identify" : "…", p));
        Func<Row, string> key = _sortColumn switch { 1 => r => r.Vendor, 2 => r => r.Format, 3 => r => r.Role, _ => r => r.Name };
        var sorted = (_sortAscending ? rows.OrderBy(key, StringComparer.CurrentCultureIgnoreCase) : rows.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase))
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _list.ItemsSource = sorted;
        if (_scanning) return;
        if (_scanStandard is not null) _scanStandard.Visibility = _all.Count == 0 && !_settings.ScanStandardFolders ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = _all.Count == 0
            ? (_settings.Folders.Count == 0 && !_settings.ScanStandardFolders
                ? "No plug-in folders yet: click Scan the standard VST folders, or Add folder…, or browse to a plug-in file."
                : "No plug-ins found in your folders.")
            : $"{_all.Count} plug-ins{(_settings.RememberScan ? " (remembered list)" : "")}. Double-click one, or select it and press Enter, to add it.";
    }

    private async Task Scan(List<string>? commonRoots = null)
    {
        if (_scanning) return;
        _scanning = true;
        _progress.Visibility = Visibility.Visible;
        var roots = commonRoots ?? VstScannerService.RootsFor(_settings).ToList();
        var report = new Progress<(int Found, string Folder)>(p => _status.Text = $"Scanning… {p.Found} found · {p.Folder}");
        var before = _all.Count;
        try
        {
            var found = await Task.Run(() => VstScannerService.Scan(roots, CancellationToken.None, report, _settings));
            // Common-folder scans merge into the remembered list instead of replacing it.
            _all = commonRoots is null ? found
                : _all.Concat(found).GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        _scanning = false;
        if (commonRoots is not null) _status.Text = $"Scanned {roots.Count} common folder(s): {_all.Count} plug-ins in the list ({_all.Count - before} new).";
        _progress.Visibility = Visibility.Collapsed;
        if (_settings.RememberScan)
        {
            _settings.ScanCache = CacheOfAll();
            _host.SaveSettings();
        }
        var keep = commonRoots is null ? null : _status.Text;
        Filter();
        if (keep is not null) _status.Text = keep;
        _ = Identify();
    }

    /// <summary>
    /// Plug-ins whose role / vendor the files do not tell are asked when they are approved (several at a time, each in a
    /// throwaway process), while this window is open. Results are remembered, so each is checked only once.
    /// </summary>
    private async Task Identify()
    {
        if (_identifying) return;
        _identifying = true;
        var unknown = _all.Where(p => WithKnown(p).Role.Length == 0).ToList();
        var done = 0;
        // Several at once (each in its own throwaway process): about a third of the machine's cores, 2 to 8.
        using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 3, 2, 8));
        await Task.WhenAll(unknown.Select(async plugin =>
        {
            await gate.WaitAsync();
            try
            {
                if (!_w.IsLoaded) return;
                var described = await Task.Run(() => PluginCatalog.Describe(plugin.Path, _settings, 10000));
                if (described.Role.Length == 0) _notIdentified.Add(plugin.Path);   // unapproved or changed: never executed to find out
            }
            finally { gate.Release(); }
            done++;   // back on the UI thread here
            if (!_w.IsLoaded) return;
            if (!_scanning) _status.Text = $"Identifying plug-ins… {done} of {unknown.Count} (instrument or effect, vendor)";
            if (done % 8 == 0 || done == unknown.Count) { _host.SaveSettings(); var selected = _list.SelectedItem as Row; Filter(); if (selected is not null) _list.SelectedItem = (_list.ItemsSource as List<Row>)?.FirstOrDefault(r => r.Info.Path == selected.Info.Path); }
        }));
        _identifying = false;
    }
}
