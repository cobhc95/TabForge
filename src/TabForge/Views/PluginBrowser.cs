using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Add plug-in: every plug-in in your folders with its vendor, format and role; type to filter, double-click (or
/// Enter) to add. The folders are scanned when the window opens (progress below), or once and remembered when
/// Settings > Audio &amp; VST > Remember the plug-in list is on (then Rescan updates it).
/// </summary>
internal static class PluginBrowser
{
    private sealed record Row(string Name, string Vendor, string Format, string Role, VstPluginInfo Info);

    public static VstPluginInfo? Choose(Window owner, IFxChainHost host)
    {
        VstPluginInfo? result = null;
        var settings = host.PluginSettings;
        var w = new Window
        {
            Title = "Add plug-in", Owner = owner, Width = 760, Height = 560, MinWidth = 520, MinHeight = 320, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(10) };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Type to filter by name or vendor" };
        DockPanel.SetDock(search, Dock.Top);
        root.Children.Add(search);

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        var progress = new ProgressBar { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        var status = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        bottom.Children.Add(progress);
        bottom.Children.Add(status);
        bottom.Children.Add(buttons);
        root.Children.Add(bottom);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Name", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Vendor", Width = 190, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Vendor)) });
        view.Columns.Add(new GridViewColumn { Header = "Format", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Format)) });
        view.Columns.Add(new GridViewColumn { Header = "Role", Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Role)) });
        // Themed, sortable headers (click to sort, again to reverse) and theme row hover/selection.
        var sortColumn = 0; var sortAscending = true;
        var list = new ListView { View = view };
        ThemedList.StyleRows(list);
        list.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        root.Children.Add(list);

        IReadOnlyList<VstPluginInfo> all = settings.RememberScan && settings.ScanCache.Count > 0
            ? settings.ScanCache.Select(k => new VstPluginInfo(k.Name, k.Path, k.Format, k.Vendor, k.Role)).ToList()
            : VstScannerService.LastScan;
        var scanning = false;
        var notIdentified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // not approved: its role is not read by running it
        ThemedList.MakeSortable(view, (column, ascending) => { sortColumn = column; sortAscending = ascending; Filter(); });

        VstPluginInfo WithKnown(VstPluginInfo p)
        {
            var known = settings.Probed.FirstOrDefault(k => string.Equals(k.Path, p.Path, StringComparison.OrdinalIgnoreCase));
            return known is null ? p : p with { Role = p.Role.Length > 0 ? p.Role : known.Role, Vendor = p.Vendor.Length > 0 ? p.Vendor : known.Vendor };
        }

        void Filter()
        {
            var q = search.Text.Trim();
            var rows = all.Select(WithKnown)
                .Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Vendor.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(p => new Row(p.Name, p.Vendor, p.Format, p.Role.Length > 0 ? p.Role : notIdentified.Contains(p.Path) ? "approve to identify" : "…", p));
            Func<Row, string> key = sortColumn switch { 1 => r => r.Vendor, 2 => r => r.Format, 3 => r => r.Role, _ => r => r.Name };
            var sorted = (sortAscending ? rows.OrderBy(key, StringComparer.CurrentCultureIgnoreCase) : rows.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase))
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            list.ItemsSource = sorted;
            if (scanning) return;
            status.Text = all.Count == 0
                ? (settings.Folders.Count == 0 && !settings.ScanStandardFolders
                    ? "No plug-in folders yet: click Add folder… (or turn on the standard folders in Folder settings…), or browse to a plug-in file."
                    : "No plug-ins found in your folders.")
                : $"{all.Count} plug-ins{(settings.RememberScan ? " (remembered list)" : "")}. Double-click one, or select it and press Enter, to add it.";
        }

        async Task Scan(List<string>? commonRoots = null)
        {
            if (scanning) return;
            scanning = true;
            progress.Visibility = Visibility.Visible;
            var roots = commonRoots ?? VstScannerService.RootsFor(settings).ToList();
            var report = new Progress<(int Found, string Folder)>(p => status.Text = $"Scanning… {p.Found} found · {p.Folder}");
            var before = all.Count;
            try
            {
                var found = await Task.Run(() => VstScannerService.Scan(roots, CancellationToken.None, report));
                // Common-folder scans merge into the remembered list instead of replacing it.
                all = commonRoots is null ? found
                    : all.Concat(found).GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
            scanning = false;
            if (commonRoots is not null) status.Text = $"Scanned {roots.Count} common folder(s): {all.Count} plug-ins in the list ({all.Count - before} new).";
            progress.Visibility = Visibility.Collapsed;
            if (settings.RememberScan)
            {
                settings.ScanCache = all.Select(p => new KnownPlugin { Name = p.Name, Path = p.Path, Format = p.Format, Vendor = p.Vendor, Role = p.Role }).ToList();
                host.SaveSettings();
            }
            var keep = commonRoots is null ? null : status.Text;
            Filter();
            if (keep is not null) status.Text = keep;
            _ = Identify();
        }

        // Plug-ins whose role / vendor the files do not tell are asked when they are approved (several at a time, each in a
        // throwaway process), while this window is open. Results are remembered, so each is checked only once.
        var identifying = false;
        async Task Identify()
        {
            if (identifying) return;
            identifying = true;
            var unknown = all.Where(p => WithKnown(p).Role.Length == 0).ToList();
            var done = 0;
            // Several at once (each in its own throwaway process): about a third of the machine's cores, 2 to 8.
            using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 3, 2, 8));
            await Task.WhenAll(unknown.Select(async plugin =>
            {
                await gate.WaitAsync();
                try
                {
                    if (!w.IsLoaded) return;
                    var described = await Task.Run(() => PluginCatalog.Describe(plugin.Path, settings, 10000));
                    if (described.Role.Length == 0) notIdentified.Add(plugin.Path);   // unapproved or changed: never executed to find out
                }
                finally { gate.Release(); }
                done++;   // back on the UI thread here
                if (!w.IsLoaded) return;
                if (!scanning) status.Text = $"Identifying plug-ins… {done} of {unknown.Count} (instrument or effect, vendor)";
                if (done % 8 == 0 || done == unknown.Count) { host.SaveSettings(); var selected = list.SelectedItem as Row; Filter(); if (selected is not null) list.SelectedItem = (list.ItemsSource as List<Row>)?.FirstOrDefault(r => r.Info.Path == selected.Info.Path); }
            }));
            identifying = false;
        }

        void Add(string text, Action click, string tip)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 3, 10, 3), ToolTip = tip };
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
        }
        void Accept() { if (list.SelectedItem is Row row) { result = row.Info; w.DialogResult = true; } }
        Add("Add folder…", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add plug-in folders", Multiselect = true };
            if (dialog.ShowDialog(w) != true) return;
            foreach (var folder in dialog.FolderNames)
                if (!settings.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) settings.Folders.Add(folder);
            host.SaveSettings();
            await Scan();
        }, "Add one or more folders that hold plug-ins; they are kept in Settings > Audio & VST.");
        Add("Browse for a plug-in file…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a VST plug-in", Filter = "VST plug-ins (*.vst3;*.dll)|*.vst3;*.dll" };
            if (dialog.ShowDialog(w) != true) return;
            var path = dialog.FileName;
            var vst3 = path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);
            if (!vst3 && !PeExports.IsVst2Plugin(path)) { status.Text = "That file is not a 64-bit VST plug-in."; return; }
            var (vendor, role) = vst3 ? VstScannerService.Vst3ModuleInfo(path) : (VstScannerService.Vst2Vendor(path), "");
            result = new VstPluginInfo(Path.GetFileNameWithoutExtension(path), path, vst3 ? "VST3" : "VST2", vendor, role);
            w.DialogResult = true;
        }, "Add a single plug-in file without adding its folder.");
        Add("Rescan", async () => await Scan(), "Scan the folders again (after installing or removing plug-ins).");
        Add("Scan common folders", async () =>
        {
            var roots = VstScannerService.CommonRoots(settings);
            if (roots.Count == 0) { status.Text = "None of the common plug-in folders exist on this computer."; return; }
            await Scan(roots);
        }, "Scan the usual VST locations (Steinberg VstPlugins, Common Files VST3, VST2 ...); found plug-ins are merged into the list. Edit the folder list with Edit list.");
        Add("Edit list…", () =>
        {
            var box = new TextBox { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 8) };
            box.Text = string.Join("\r\n", settings.CommonFolders.Count > 0 ? settings.CommonFolders : VstScannerService.DefaultCommonFolders);
            var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var reset = new Button { Content = "Reset to defaults", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(ok); row.Children.Add(reset); row.Children.Add(cancel);
            var dock = new DockPanel { Margin = new Thickness(12) };
            var caption = new TextBlock { Text = "Common plug-in folders, one per line (%ENV% variables work; missing folders are skipped)" };
            DockPanel.SetDock(caption, Dock.Top); DockPanel.SetDock(row, Dock.Bottom);
            dock.Children.Add(caption); dock.Children.Add(row); dock.Children.Add(box);
            var dlg = new Window { Title = "Common plug-in folders", Content = dock, Width = 560, Height = 380, Owner = w, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            dlg.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
            dlg.SetResourceReference(Window.ForegroundProperty, "TextBrush");
            reset.Click += (_, _) => box.Text = string.Join("\r\n", VstScannerService.DefaultCommonFolders);
            ok.Click += (_, _) => dlg.DialogResult = true;
            if (dlg.ShowDialog() != true) return;
            var lines = box.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            settings.CommonFolders = lines.SequenceEqual(VstScannerService.DefaultCommonFolders) ? new List<string>() : lines;
            host.SaveSettings();
        }, "Edit the list of common folders (also in Settings > Audio & VST), with Reset to defaults.");
        // Remember: the list is kept and shown at once next time, without scanning the folders again.
        var remember = new CheckBox
        {
            Content = "Remember plug-ins (load from the saved list, no scan each time)", IsChecked = settings.RememberScan,
            Margin = new Thickness(0, 8, 0, 0), ToolTip = "On: the plug-ins found are saved and this window opens instantly from that list; use Rescan after installing new plug-ins. Off: the folders are scanned each time this window opens."
        };
        remember.Click += (_, _) =>
        {
            settings.RememberScan = remember.IsChecked == true;
            settings.ScanCache = settings.RememberScan
                ? all.Select(p => new KnownPlugin { Name = p.Name, Path = p.Path, Format = p.Format, Vendor = p.Vendor, Role = p.Role }).ToList()
                : new List<KnownPlugin>();
            host.SaveSettings();
            Filter();
        };
        bottom.Children.Insert(2, remember);
        Add("Folder settings…", () => { w.DialogResult = false; host.OpenAudioSettings(); }, "Plug-in folders, remembered scans and plug-in window options (Settings > Audio & VST).");
        Add("Cancel", () => w.DialogResult = false, "Close without adding");

        list.MouseDoubleClick += (_, _) => Accept();
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && list.Items.Count > 0) { list.SelectedIndex = 0; (list.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem)?.Focus(); e.Handled = true; }
            if (e.Key == Key.Enter) { if (list.SelectedItem is null && list.Items.Count > 0) list.SelectedIndex = 0; Accept(); }
        };
        search.TextChanged += (_, _) => Filter();
        w.Content = root;
        Filter();
        // Scan when opening, unless the remembered list is used.
        var hasFolders = settings.Folders.Count > 0 || settings.ScanStandardFolders;
        if (hasFolders && !(settings.RememberScan && settings.ScanCache.Count > 0)) w.Loaded += async (_, _) => await Scan();
        else w.Loaded += async (_, _) => await Identify();
        search.Focus();
        return DialogHost.ShowModal(w) == true ? result : null;
    }
}
