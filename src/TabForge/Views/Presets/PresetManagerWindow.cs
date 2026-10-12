using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Plugins;

namespace TabForge.Views.Presets;

// Owns: the preset manager window: the tree of preset kinds with counts at the left, the toolbar (Rename, Duplicate, Move up, Move down,
//   Delete, Show in tree) and the list of the chosen node's presets at the right, the search across every kind with "Show in tree", the
//   extended selection (Ctrl / Shift click, Ctrl+A), the keys (F2, Delete, Alt+Up / Alt+Down, Enter, Esc) and the inline rename box.
// Does not own: what is listed (PresetCatalogue), the name rules and store changes of an action (PresetManagePanel, which every action
//   runs through), the delete question and its setting (PresetDeleteQuestion), the files (PresetFolderStore, DrumMapStore), or loading a preset.
// Tests: TestPresetManagerWindow. Capture step: preset-manager.
internal sealed class PresetManagerWindow : Window
{
    private const string SizeKey = "PresetManager.Size";
    internal const string BuiltInTip = "This preset comes with the app: it cannot be renamed, moved or deleted";
    private readonly ListBox _tree = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Extended };
    private readonly TextBox _search = new() { Width = 220, Height = 24, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _heading = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = new() { Margin = new Thickness(10, 10, 10, 10), TextWrapping = TextWrapping.Wrap };
    private readonly Button _rename, _duplicate, _up, _down, _delete, _show;
    private readonly HashSet<PresetKind> _expanded = new();
    private PresetNode _node;
    private TextBox? _editor;
    private bool _filling;

    /// <summary>Asks a yes / no question. Tests answer it directly.</summary>
    internal Func<string, bool> Confirm;
    /// <summary>Asks the delete question (text in; answer and "don't ask again" tick out). Tests answer it directly.</summary>
    internal Func<string, (bool Yes, bool Remember)> AskDelete;
    /// <summary>Shows a failure (a file could not be changed). Tests collect it.</summary>
    internal Action<string> ShowError;

    internal PresetManagerWindow(Window? owner, PresetKind kind, string? key)
    {
        Owner = owner;
        Title = "Preset manager";
        var size = Application.Current?.Properties[SizeKey] as Size?;
        Width = size?.Width ?? 900; Height = size?.Height ?? 500; MinWidth = 860; MinHeight = 320;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        Confirm = text => ThemedMessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        AskDelete = text => PresetDeleteQuestion.Ask(this, text, Title);
        ShowError = text => ThemedMessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        _node = PresetCatalogue.Node(kind, key);

        _rename = Tool("", "Rename", "Rename the selected preset", () => BeginRename());
        _duplicate = Tool("", "Duplicate", "Make a copy of the selected preset", DuplicateSelected);
        _up = Tool("", "Move up", "Move the selected preset up", () => MoveSelected(-1));
        _down = Tool("", "Move down", "Move the selected preset down", () => MoveSelected(1));
        _delete = Tool("", "Delete", "Delete the selected preset", DeleteSelected, danger: true);
        _show = Tool("", "Show in tree", "Go to the place of the selected preset (Enter)", ShowInTree);

        // Top: the heading of the shown node and the search box.
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var hint = new TextBlock { Text = "Search all presets", IsHitTestVisible = false, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var searchCell = new Grid();
        searchCell.Children.Add(_search); searchCell.Children.Add(hint);
        DockPanel.SetDock(searchCell, Dock.Right);
        top.Children.Add(searchCell); top.Children.Add(_heading);
        AutomationProperties.SetName(_search, "Search all presets");
        TooltipShortcuts.SetText(_search, "Search every kind of preset by name (Esc clears)");
        _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (!_filling) FillList(null); };

        // Left: the tree. Right: the toolbar over the list.
        AutomationProperties.SetName(_tree, "Preset kinds");
        AutomationProperties.SetName(_list, "Presets");
        _tree.SelectionChanged += (_, _) =>
        {
            if (_filling || _tree.SelectedItem is not ListBoxItem { Tag: PresetNode node }) return;
            _node = node;
            _filling = true;
            _search.Text = "";
            FillList(null, tree: false);   // the rows of the tree stay as they are, so the arrow keys keep their place
        };
        _tree.MouseDoubleClick += (_, _) => { if (_node.IsGroup) Toggle(_node.Kind, !_expanded.Contains(_node.Kind)); };
        _list.SelectionChanged += (_, _) => { if (!_filling) { EndEdit(); UpdateTools(); } };
        _list.MouseDoubleClick += (_, e) => { if (e.OriginalSource is TextBlock { Tag: "name" }) BeginRename(); };
        var left = Framed(_tree);
        left.Margin = new Thickness(0, 0, 8, 0);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 6) };
        foreach (var button in new[] { _rename, _duplicate, _up, _down, _delete, _show }) bar.Children.Add(button);
        var right = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); DockPanel.SetDock(_message, Dock.Bottom); DockPanel.SetDock(_empty, Dock.Top);
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        right.Children.Add(bar); right.Children.Add(_message); right.Children.Add(_empty); right.Children.Add(_list);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rightFrame = Framed(right);
        Grid.SetColumn(rightFrame, 1);
        body.Children.Add(left); body.Children.Add(rightFrame);
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top); root.Children.Add(body);
        root.SetResourceReference(LayoutTransformProperty, "UiScaleTransform");
        Content = root;
        KeyboardNavigation.SetTabIndex(_tree, 0); KeyboardNavigation.SetTabIndex(bar, 1); KeyboardNavigation.SetTabIndex(_list, 2); KeyboardNavigation.SetTabIndex(_search, 3);

        PreviewKeyDown += OnKey;
        Closed += (_, _) => { if (Application.Current is { } app && WindowState == WindowState.Normal) app.Properties[SizeKey] = new Size(Width, Height); };
        Show(kind, key);
    }


    // ---------- state the tests and the capture read ----------
    internal PresetNode SelectedNode => _node;
    internal IReadOnlyList<PresetEntry> Listed => _list.Items.Cast<ListBoxItem>().Select(i => (PresetEntry)i.Tag).ToList();
    internal PresetEntry? SelectedEntry => (_list.SelectedItem as ListBoxItem)?.Tag as PresetEntry;
    /// <summary>Every selected row, in list order (the list is extended-selection: Ctrl / Shift click, Ctrl+A).</summary>
    internal IReadOnlyList<PresetEntry> SelectedEntries => _list.Items.Cast<ListBoxItem>().Where(i => i.IsSelected).Select(i => (PresetEntry)i.Tag).ToList();
    internal string SearchText { get => _search.Text; set => _search.Text = value; }
    internal string Message => _message.Visibility == Visibility.Visible ? _message.Text : "";
    internal bool IsEditing => _editor is not null;
    /// <summary>The tree as shown: every visible row with its count.</summary>
    internal IReadOnlyList<(PresetNode Node, int Count)> TreeRows => _tree.Items.Cast<ListBoxItem>().Select(i => ((PresetNode)i.Tag, (int)((FrameworkElement)i.Content).Tag)).ToList();
    internal bool CanRename => _rename.IsEnabled;
    internal bool CanDuplicate => _duplicate.IsEnabled;
    internal bool CanDelete => _delete.IsEnabled;
    internal bool CanMove => _up.IsEnabled && _down.IsEnabled;
    internal bool CanMoveUp => _up.IsEnabled;
    internal bool CanMoveDown => _down.IsEnabled;
    internal bool CanShowInTree => _show.IsEnabled;
    internal string RenameTip => TooltipShortcuts.GetBaseText(_rename) ?? "";

    /// <summary>Jumps to the node of a kind and key (expanded and selected), leaving any search.</summary>
    internal void Show(PresetKind kind, string? key)
    {
        _node = PresetCatalogue.Node(kind, key);
        _expanded.Add(kind);
        if (_search.Text.Length > 0) _search.Text = ""; else FillList(null);
    }

    /// <summary>Selects the first listed preset of that name.</summary>
    internal bool Select(string name) => Listed.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) is { } entry && SelectIn(entry.Node, name);

    /// <summary>Selects exactly the listed presets of these names (what Ctrl+click does on each row).</summary>
    internal void SelectNames(params string[] names) =>
        SelectEntries(names.Select(n => Listed.First(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase))).Select(e => (e.Node, e.Name)).ToList());

    private bool SelectIn(PresetNode node, string name) => SelectEntries(new[] { (node, name) }) > 0;

    /// <summary>Selects exactly these rows (the others are cleared) and brings the first into view; returns how many were found.</summary>
    private int SelectEntries(IReadOnlyList<(PresetNode Node, string Name)> keep)
    {
        _list.UnselectAll();
        ListBoxItem? first = null;
        var found = 0;
        foreach (var (node, name) in keep)
        {
            var item = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag is PresetEntry e && e.Node.Same(node) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (item is null) continue;
            item.IsSelected = true;
            first ??= item;
            found++;
        }
        first?.BringIntoView();
        return found;
    }

    private void SelectRow(int index)
    {
        _list.UnselectAll();
        if (index >= 0 && index < _list.Items.Count) ((ListBoxItem)_list.Items[index]).IsSelected = true;
    }

    // ---------- the actions (each runs through the shared panel code: one set of rules and questions) ----------
    private PresetManagePanel? Actions(PresetEntry entry) =>
        PresetManager.StoreOf(entry.Node.Kind, entry.Node.Key) is { } store ? new PresetManagePanel(this, store, Title) { Confirm = Confirm, AskDelete = AskDelete, ShowError = ShowError } : null;

    /// <summary>The user's presets among the selected rows, grouped by the place they live in (each group is one store).</summary>
    private IEnumerable<IGrouping<(PresetKind, string?), PresetEntry>> UserGroups() =>
        SelectedEntries.Where(e => !e.BuiltIn).GroupBy(e => (e.Node.Kind, e.Node.Key));

    /// <summary>Applies a rename of the selected preset. False when the name is refused (the reason shows under the list).</summary>
    internal bool RenameSelected(string text)
    {
        if (SelectedEntries.Count != 1 || SelectedEntry is not { BuiltIn: false } entry || Actions(entry) is not { } actions) return false;
        var name = text.Trim();
        if (PresetCatalogue.BuiltIns(entry.Node).Contains(name, StringComparer.OrdinalIgnoreCase)) { Say(PresetRows.ReservedText); return false; }
        if (!actions.Rename(entry.Name, name)) { Say(actions.Message.Length > 0 ? actions.Message : null); return false; }
        FillList(new[] { (entry.Node, name) });
        return true;
    }

    /// <summary>Copies each selected preset that can be copied (built-in drum maps can too; other built-in presets are skipped).</summary>
    internal void DuplicateSelected()
    {
        if (!_duplicate.IsEnabled) return;
        var made = new List<(PresetNode Node, string Name)>();
        foreach (var entry in SelectedEntries.Where(e => e is { BuiltIn: false } or { Node.Kind: PresetKind.DrumMap }))
        {
            var copy = PresetRules.CopyName(entry.Name, PresetCatalogue.Entries(entry.Node).Select(e => e.Name).ToList());
            if (!entry.BuiltIn) Actions(entry)?.Duplicate(entry.Name);
            else
            {
                try { new DrumMapStore().DuplicateShipped(entry.Name, copy); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { ShowError("The preset could not be copied: " + ex.Message); }
            }
            made.Add((entry.Node, copy));
        }
        FillList(made);
    }

    /// <summary>Deletes the selected presets after one question ("Delete 3 presets?"); the row after the first deleted one is selected.</summary>
    internal void DeleteSelected()
    {
        var chosen = SelectedEntries.Where(e => !e.BuiltIn).ToList();
        if (chosen.Count == 0 || !PresetDeleteQuestion.Confirm(chosen.Select(e => e.Name).ToList(), AskDelete)) return;
        var at = _list.Items.Cast<ListBoxItem>().Select((item, i) => (item, i)).Where(p => p.item.IsSelected).Select(p => p.i).DefaultIfEmpty(0).Min();
        var deleted = false;
        foreach (var group in UserGroups()) deleted |= Actions(group.First())?.Remove(group.Select(e => e.Name).ToList()) == true;
        FillList(null);
        if (deleted) { SelectRow(Math.Clamp(at, 0, _list.Items.Count - 1)); }
        FocusList();
    }

    /// <summary>Moves the selected presets up (-1) or down (+1) as a block, each place on its own; not in search results, where order has no meaning.</summary>
    internal void MoveSelected(int delta)
    {
        if (_search.Text.Trim().Length > 0) return;
        var keep = SelectedEntries.Select(e => (e.Node, e.Name)).ToList();
        var moved = false;
        foreach (var group in UserGroups()) moved |= Actions(group.First())?.MoveBlock(group.Select(e => e.Name).ToList(), delta) == true;
        if (!moved) return;
        FillList(keep);
        FocusList();
    }

    /// <summary>Leaves the search and shows the selected hit in its own node (one selected preset).</summary>
    internal void ShowInTree()
    {
        if (SelectedEntries is not [{ } entry]) return;
        Show(entry.Node.Kind, entry.Node.Key);
        SelectIn(entry.Node, entry.Name);
        FocusList();
    }

    private void Say(string? text)
    {
        _message.Text = text ?? "";
        _message.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- the tree ----------
    private void FillTree()
    {
        _tree.Items.Clear();
        foreach (var group in PresetCatalogue.Groups())
        {
            _tree.Items.Add(TreeRow(group, 0));
            if (group.IsGroup && _expanded.Contains(group.Kind)) foreach (var child in PresetCatalogue.Children(group)) _tree.Items.Add(TreeRow(child, 1));
        }
        _tree.SelectedItem = _search.Text.Trim().Length > 0 ? null : _tree.Items.Cast<ListBoxItem>().FirstOrDefault(i => ((PresetNode)i.Tag).Same(_node));
    }

    private ListBoxItem TreeRow(PresetNode node, int level)
    {
        var count = PresetCatalogue.Entries(node).Count;
        var open = _expanded.Contains(node.Kind);
        var chevron = PresetManagePanel.Glyph(node.IsGroup ? open ? "" : "" : "");
        chevron.Width = 16; chevron.FontSize = 9; chevron.Background = Brushes.Transparent;
        if (node.IsGroup) chevron.MouseLeftButtonDown += (_, e) => { e.Handled = true; Toggle(node.Kind, !open); };
        var title = new TextBlock { Text = node.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
        var number = new TextBlock { Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        if (count == 0) title.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var row = new DockPanel { Tag = count, Margin = new Thickness(level * 16, 0, 0, 0), MinHeight = 22 };
        DockPanel.SetDock(chevron, Dock.Left); DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(chevron); row.Children.Add(number); row.Children.Add(title);
        var item = new ListBoxItem { Content = row, Tag = node, Padding = new Thickness(4, 2, 8, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, $"{node.Title}, {count} {(count == 1 ? "preset" : "presets")}{(node.IsGroup ? open ? ", expanded" : ", collapsed" : "")}");
        return item;
    }

    private void Toggle(PresetKind kind, bool open)
    {
        if (open) _expanded.Add(kind);
        else { _expanded.Remove(kind); if (_node.Kind == kind) _node = PresetCatalogue.Node(kind, null); }
        FillList(null);
        (_tree.SelectedItem as ListBoxItem)?.Focus();
    }

    // ---------- the list ----------
    /// <summary>Lists the node's presets (or the search hits) and the tree's counts again; <paramref name="select"/> picks the presets, else the selection is kept.</summary>
    private void FillList(IReadOnlyList<(PresetNode Node, string Name)>? select, bool tree = true)
    {
        var keep = select ?? SelectedEntries.Select(e => (e.Node, e.Name)).ToList();
        _filling = true;
        try
        {
            _editor = null;
            Say(null);
            if (tree) FillTree();
            var query = _search.Text.Trim();
            var entries = query.Length > 0 ? PresetCatalogue.Search(query) : PresetCatalogue.Entries(_node);
            var flat = query.Length > 0 || _node.IsGroup;
            _list.Items.Clear();
            foreach (var entry in entries) _list.Items.Add(Row(entry, flat));
            _heading.Text = query.Length > 0 ? $"{entries.Count} {(entries.Count == 1 ? "preset" : "presets")} found" : _node.Where;
            _empty.Text = query.Length > 0 ? "No preset has this in its name." : "No saved presets here yet. Presets are saved from the preset drop-downs of the FX chain and MIDI processing windows.";
            _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _show.Visibility = flat ? Visibility.Visible : Visibility.Collapsed;
            SelectEntries(keep);
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) SelectRow(0);
        }
        finally { _filling = false; }
        UpdateTools();
    }

    private static ListBoxItem Row(PresetEntry entry, bool flat)
    {
        var grid = new Grid { MinHeight = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = flat ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 76 });
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new TextBlock { Text = entry.Name, Tag = "name", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = entry.BuiltIn ? BuiltInTip : "Double-click to rename" });
        if (entry.BuiltIn) name.Children.Add(Muted("built-in", new Thickness(8, 0, 0, 0)));
        grid.Children.Add(name);
        if (flat) { var where = Muted(entry.Node.Where, new Thickness(10, 0, 0, 0)); where.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(where, 1); grid.Children.Add(where); }
        var date = Muted(PresetCatalogue.DateText(entry.Modified, DateTime.Now), new Thickness(10, 0, 0, 0));
        date.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(date, 2);
        grid.Children.Add(date);
        var item = new ListBoxItem { Content = grid, Tag = entry, Padding = new Thickness(8, 2, 8, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, string.Join(", ", new[] { entry.Name, entry.BuiltIn ? "built-in" : "", flat ? entry.Node.Where : "", date.Text }.Where(t => t.Length > 0)));
        return item;
    }

    private static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }

    /// <summary>Enables each tool for the selection: Rename and Show in tree need exactly one row; the moves need a user's preset that can move.</summary>
    private void UpdateTools()
    {
        var chosen = SelectedEntries;
        var user = chosen.Where(e => !e.BuiltIn).ToList();
        var searching = _search.Text.Trim().Length > 0;
        var none = "Select a preset first";
        var builtIn = chosen.Count > 0 && user.Count == 0 ? BuiltInTip : none;
        Set(_rename, chosen.Count == 1 && user.Count == 1, "Rename the selected preset", chosen.Count > 1 ? "Select one preset to rename" : builtIn);
        var canUp = user.Count > 0 && !searching && CanMoveBlock(user, -1);
        var canDown = user.Count > 0 && !searching && CanMoveBlock(user, 1);
        var noOrder = "Search results have no order to change";
        Set(_up, canUp, "Move the selected presets up", searching ? noOrder : user.Count == 0 ? builtIn : "The selected presets are already at the top");
        Set(_down, canDown, "Move the selected presets down", searching ? noOrder : user.Count == 0 ? builtIn : "The selected presets are already at the bottom");
        Set(_delete, user.Count > 0, user.Count == 1 ? "Delete the selected preset" : $"Delete the {user.Count} selected presets", builtIn);
        var copy = chosen.Any(e => e is { BuiltIn: false } or { Node.Kind: PresetKind.DrumMap });
        _duplicate.IsEnabled = copy;
        TooltipShortcuts.SetText(_duplicate, copy ? "Make a copy of the selected presets" : chosen.Count == 0 ? none : "This preset comes with the app; it is always in its drop-down and needs no copy");
        _show.IsEnabled = chosen.Count == 1;

        void Set(Button button, bool on, string tip, string off)
        {
            button.IsEnabled = on;
            TooltipShortcuts.SetText(button, on ? tip : off);
        }
    }

    /// <summary>True when the user's presets can move one row: per place, some of them have an unchosen row beside them.</summary>
    private static bool CanMoveBlock(IReadOnlyList<PresetEntry> user, int delta) => user.GroupBy(e => (e.Node.Kind, e.Node.Key)).Any(group =>
        PresetManagePanel.BlockSteps(PresetCatalogue.Entries(group.First().Node).Where(e => !e.BuiltIn).Select(e => e.Name).ToList(), group.Select(e => e.Name).ToList(), delta).Count > 0);

    // ---------- rename box, keys, small parts ----------
    /// <summary>The selected row's name becomes a text box: Enter applies, Esc cancels.</summary>
    internal void BeginRename()
    {
        EndEdit();
        if (SelectedEntries.Count != 1 || _list.SelectedItem is not ListBoxItem { Tag: PresetEntry { BuiltIn: false } entry, Content: Grid grid }) return;
        grid.Children[0].Visibility = Visibility.Collapsed;
        var editor = _editor = new TextBox { Text = entry.Name, MaxLength = PresetRules.MaxNameLength, VerticalContentAlignment = VerticalAlignment.Center, Height = 22, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(editor, $"New name for {entry.Name}");
        grid.Children.Add(editor);
        editor.Dispatcher.BeginInvoke(new Action(() => { editor.Focus(); Keyboard.Focus(editor); editor.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void EndEdit()
    {
        if (_editor is not { Parent: Grid grid } editor) { _editor = null; return; }
        _editor = null;
        grid.Children.Remove(editor);
        grid.Children[0].Visibility = Visibility.Visible;
        Say(null);
    }

    private void FocusList() => Dispatcher.BeginInvoke(new Action(() => { if (_editor is null) ((_list.SelectedItem as ListBoxItem) ?? (UIElement)_list).Focus(); }), System.Windows.Threading.DispatcherPriority.Input);

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_editor is { } editor)
        {
            if (key == Key.Enter) { e.Handled = true; if (RenameSelected(editor.Text)) FocusList(); else { editor.Focus(); editor.SelectAll(); } }
            else if (key == Key.Escape) { e.Handled = true; EndEdit(); FocusList(); }
            return;
        }
        var searching = _search.Text.Length > 0;
        var inList = _list.IsKeyboardFocusWithin;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        if (key == Key.Escape) { if (searching) { _search.Text = ""; FocusList(); } else Close(); }
        else if (key == Key.Enter && searching && (inList || _search.IsKeyboardFocusWithin)) ShowInTree();
        else if (key == Key.Down && _search.IsKeyboardFocusWithin) FocusList();
        else if (key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) _search.Focus();
        else if (inList && key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) _list.SelectAll();
        else if (inList && key == Key.F2) BeginRename();
        else if (inList && key == Key.Delete) DeleteSelected();
        else if (inList && alt && key == Key.Up) MoveSelected(-1);
        else if (inList && alt && key == Key.Down) MoveSelected(1);
        else if (_tree.IsKeyboardFocusWithin && _node.IsGroup && key is Key.Right or Key.Left && _expanded.Contains(_node.Kind) != (key == Key.Right)) Toggle(_node.Kind, key == Key.Right);
        else return;
        e.Handled = true;
    }

    private static Border Framed(UIElement child)
    {
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = child };
        frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return frame;
    }

    private static Button Tool(string glyph, string text, string tip, Action run, bool danger = false)
    {
        var icon = PresetManagePanel.Glyph(glyph);
        if (danger) icon.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0) };
        ToolTipService.SetShowOnDisabled(button, true);
        TooltipShortcuts.SetText(button, tip);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => run();
        return button;
    }
}
