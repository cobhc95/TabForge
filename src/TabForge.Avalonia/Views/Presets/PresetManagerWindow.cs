using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: the preset manager window: the tree of preset kinds and their children with counts at the left, the toolbar (Rename, Duplicate,
//   Move up, Move down, Delete, Show in tree) and the list of the chosen node's presets at the right, the search across every kind, the
//   extended selection, the inline rename box, the keys and the message line.
// Does not own: what is listed (IPresetCatalogue), the changes and their questions (PresetManagerActions, PresetManagerDialogs), the name
//   rules (PresetRules, PresetManagePresenter), the stores (IPresetStoreFactory) or loading a preset.
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>One row of the manager's tree: the node and how many presets it lists.</summary>
public sealed record PresetTreeRow(PresetNode Node, int Count);

/// <summary>The program-wide preset manager: one window that organises every kind of saved preset.</summary>
public sealed partial class PresetManagerWindow : Window
{
    public const string Caption = "Preset manager";
    private const string BuiltInLabel = "built-in";
    private const string BuiltInTip = "This preset comes with the app: it cannot be renamed, moved or deleted";
    private const string NoneSelectedTip = "Select a preset first";
    private const string EmptyText = "No saved presets here yet. Presets are saved from the preset drop-downs of the FX chain and MIDI processing windows.";
    private const string NoMatchText = "No preset has this in its name.";
    private const string SearchOrderTip = "Search results have no order to change";
    private const string RenameMultipleTip = "Select one preset to rename";
    private const string AtTopTip = "The selected presets are already at the top";
    private const string AtBottomTip = "The selected presets are already at the bottom";
    private const string DuplicateBuiltInTip = "This preset comes with the app; it is always in its drop-down and needs no copy";
    private const string ChevronOpen = "▾";
    private const string ChevronClosed = "▸";
    private const string NameTag = "name";
    private const string AutomationSearch = "Search all presets", AutomationTree = "Preset kinds", AutomationList = "Presets";
    private const double TreeWidth = 230, WindowWidth = 900, WindowHeight = 500, MinWindowWidth = 860, MinWindowHeight = 320;
    private const double SearchWidth = 220, SearchHeight = 24, IndentPerLevel = 16, ChevronWidth = 18, DateWidth = 76;
    private const double ToolHeight = 26, ToolMinWidth = 72, ToolGap = 6, FramePadding = 1, RowPadding = 8;
    private const double WindowFontSize = 13, BodyMargin = 10, TopGap = 8, BarMargin = 6, MessageMarginY = 4, EmptyMargin = 10;
    private const double FrameCornerRadius = 4, NameGap = 8, WhereGap = 10, ChevronFontSize = 10, ToolPaddingX = 10;
    private const double RowMinHeight = 24, RowPaddingY = 2, TreeTitleGapX = 2, TreeTitleGapRight = 6, TreeCountGap = 6;
    private const double TreePaddingX = 4, TreePaddingY = 2, TreePaddingRight = 8;

    private readonly IPresetCatalogue _catalogue;
    private readonly PresetManagerActions _actions;
    private readonly ListBox _tree = new() { BorderThickness = new Thickness(0), Name = "PresetCatalogKinds" };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Multiple, Name = "PresetList" };
    private readonly TextBox _search = new() { Width = SearchWidth, Height = SearchHeight, Watermark = "Search all presets", VerticalContentAlignment = VerticalAlignment.Center, Name = "SearchPresets" };
    private readonly TextBlock _heading = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Name = "Heading" };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(RowPadding, MessageMarginY), IsVisible = false, Name = "Message" };
    private readonly TextBlock _empty = new() { Margin = new Thickness(EmptyMargin), TextWrapping = TextWrapping.Wrap, Name = "Empty" };
    private readonly Button _rename = ToolButton("Rename");
    private readonly Button _duplicate = ToolButton("Duplicate");
    private readonly Button _up = ToolButton("Move up");
    private readonly Button _down = ToolButton("Move down");
    private readonly Button _delete = ToolButton("Delete");
    private readonly Button _show = ToolButton("Show in tree");
    private readonly HashSet<PresetCatalogKind> _expanded = new();
    private PresetNode _node;
    private PresetEntry? _editing;
    private TextBox? _editor;
    private StackPanel? _editorHost;
    private TextBlock? _editorName;
    private bool _filling;
    // Perf: typing filters at once on the first keystroke and once more when it rests (~100 ms).
    private readonly TabForge.Platform.IUiTimer _searchDebounce = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication()
        .CreateTimer(System.TimeSpan.FromMilliseconds(TabForge.Avalonia.Platform.UiTiming.FilterDebounceMs));
    private bool _searchDirty;

    /// <summary>The questions and the delete-setting seam; a test replaces the members before it acts.</summary>
    public PresetManagerDialogs Dialogs { get; }

    /// <param name="host">Shows the replace and delete questions.</param>
    /// <param name="startKind">The kind the window opens on.</param>
    /// <param name="startKey">The plug-in folder or processor type the window opens on; null for the kind's group.</param>
    /// <param name="catalogue">What the window lists; the real preset folders by default.</param>
    /// <param name="stores">The stores the actions change; the real preset folders by default.</param>
    /// <param name="confirmDeletePresets">Reads General.ConfirmDeletePresets at each delete; true by default.</param>
    /// <param name="rememberDeletePresets">Turns that setting off and saves it; a no-op by default (tests/the preview).</param>
    public PresetManagerWindow(AvaloniaDialogHost host,
        PresetCatalogKind startKind = PresetCatalogKind.PluginPreset, string? startKey = null,
        IPresetCatalogue? catalogue = null, IPresetStoreFactory? stores = null,
        Func<bool>? confirmDeletePresets = null, Action<bool>? rememberDeletePresets = null)
    {
        _catalogue = catalogue ?? new PresetCataloguePresenter();
        Dialogs = PresetManagerDialogs.For(host, this, confirmDeletePresets, rememberDeletePresets);
        _actions = new PresetManagerActions(stores ?? PresetStoreFactory.Default, Dialogs);
        _node = _catalogue.Node(startKind, startKey);

        Title = Caption;
        Width = WindowWidth;
        Height = WindowHeight;
        MinWidth = MinWindowWidth;
        MinHeight = MinWindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = WindowFontSize;

        _tree[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _list[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _empty[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        _delete[!ForegroundProperty] = new DynamicResourceExtension("Danger");
        AutomationProperties.SetName(_search, AutomationSearch);
        AutomationProperties.SetName(_tree, AutomationTree);
        AutomationProperties.SetName(_list, AutomationList);
        ToolTip.SetTip(_search, "Search every kind of preset by name (Esc clears)");
        WireTool(_rename, "Rename the selected preset", () => BeginRename());
        WireTool(_duplicate, "Make a copy of the selected preset", DuplicateSelected);
        WireTool(_up, "Move the selected preset up", () => MoveSelected(-1));
        WireTool(_down, "Move the selected preset down", () => MoveSelected(1));
        WireTool(_delete, "Delete the selected preset", () => Platform.UiTasks.Forget(DeleteSelectedAsync(), "Delete preset", Say));
        WireTool(_show, "Go to the place of the selected preset (Enter)", ShowInTree);

        _search.TextChanged += (_, _) =>
        {
            if (_filling) return;
            if (_searchDebounce.IsEnabled) { _searchDirty = true; return; }   // a trailing pass is already queued
            FillList(null);
            _searchDebounce.Start();
        };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            if (!_searchDirty) return;
            _searchDirty = false;
            FillList(null);
        };
        Closed += (_, _) => _searchDebounce.Stop();
        _tree.SelectionChanged += (_, _) =>
        {
            if (_filling || _tree.SelectedItem is not ListBoxItem { Tag: PresetNode node }) return;
            _node = node;
            _filling = true;
            try { _search.Text = ""; }
            finally { _filling = false; }
            FillList(null, rebuildTree: false);   // the tree rows stay, so Left/Right and the arrow keys keep their place
        };
        _tree.DoubleTapped += (_, _) => { if (_node.IsGroup) Toggle(_node.Kind, !_expanded.Contains(_node.Kind)); };
        _list.SelectionChanged += (_, _) => { if (!_filling) { EndEdit(); UpdateTools(); } };
        _list.DoubleTapped += (_, e) => { if (e.Source is TextBlock { Tag: NameTag }) BeginRename(); };

        Content = BuildRoot();
        AddHandler(KeyDownEvent, OnKey);
        _expanded.Add(_node.Kind);
        ConfigureListVirtualization();
        FillList(null);
    }

    /// <summary>The node the list shows.</summary>
    public PresetNode SelectedNode => _node;
    /// <summary>The rows the list shows, in order.</summary>
    public IReadOnlyList<PresetEntry> Listed => _entries.ToList();
    /// <summary>The one selected row, or null.</summary>
    public PresetEntry? SelectedEntry => _list.SelectedItem as PresetEntry;
    /// <summary>Every selected row, in list order.</summary>
    public IReadOnlyList<PresetEntry> SelectedEntries => _list.Selection.SelectedIndexes.OrderBy(i => i).Where(i => i < _entries.Count)
        .Select(i => _entries[i]).ToList();
    /// <summary>The tree as shown (expanded children included) with each node's count.</summary>
    public IReadOnlyList<PresetTreeRow> TreeRows => _tree.Items.OfType<ListBoxItem>()
        .Select(item => new PresetTreeRow((PresetNode)item.Tag!, (int)((Grid)item.Content!).Tag!)).ToList();
    public string Message => _message.IsVisible ? _message.Text ?? "" : "";
    public bool IsEditing => _editor is not null;
    public bool CanRename => _rename.IsEnabled;
    public bool CanDuplicate => _duplicate.IsEnabled;
    public bool CanDelete => _delete.IsEnabled;
    public bool CanMoveUp => _up.IsEnabled;
    public bool CanMoveDown => _down.IsEnabled;
    public bool CanShowInTree => _show.IsVisible && _show.IsEnabled;
    public string RenameTip => ToolTip.GetTip(_rename) as string ?? "";

    public string SearchText
    {
        get => _search.Text?.Trim() ?? "";
        set
        {
            _filling = true;
            try { _search.Text = value; }
            finally { _filling = false; }
            FillList(null);
        }
    }

    /// <summary>Jumps to a kind (and plug-in folder or processor type), expands its group and clears any search.</summary>
    public void Show(PresetCatalogKind kind, string? key)
    {
        _node = _catalogue.Node(kind, key);
        _expanded.Add(kind);
        _filling = true;
        try { _search.Text = ""; }
        finally { _filling = false; }
        FillList(null);
    }

    /// <summary>Selects the first listed preset with that name.</summary>
    public bool Select(string name) =>
        Listed.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) is { } entry && SelectEntries(new[] { entry }) > 0;

    /// <summary>Selects exactly these listed presets (what Ctrl+click does on each row).</summary>
    public void SelectNames(params string[] names) =>
        SelectEntries(names.Select(name => Listed.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<PresetEntry>().ToList());

    /// <summary>Starts the in-place rename of the one selected user preset.</summary>
    public void BeginRename()
    {
        EndEdit();
        if (SelectedEntries is not [{ BuiltIn: false } entry]) return;
        var index = IndexOf(entry);
        if (index < 0) return;
        _list.UpdateLayout();
        if (_list.ContainerFromIndex(index) is not ListBoxItem container) return;
        var grid = container.GetVisualDescendants().OfType<Grid>().FirstOrDefault()
            ?? container.GetLogicalDescendants().OfType<Grid>().FirstOrDefault();
        var host = grid?.Children.OfType<StackPanel>().FirstOrDefault();
        var name = host?.Children.OfType<TextBlock>().FirstOrDefault(t => (t.Tag as string) == NameTag);
        if (grid is null || host is null || name is null) return;
        _editing = entry;
        _editorHost = host;
        _editorName = name;
        name.IsVisible = false;
        var editor = _editor = new TextBox { Text = entry.Name, MaxLength = PresetRules.MaxNameLength, VerticalAlignment = VerticalAlignment.Center, Height = 22, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(editor, $"New name for {entry.Name}");
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Platform.UiTasks.Forget(CommitRenameAsync(editor.Text ?? ""), "Rename preset", Say); }
            else if (e.Key == Key.Escape) { e.Handled = true; EndEdit(); UpdateTools(); }
        };
        host.Children.Add(editor);
        editor.Focus();
        editor.SelectAll();
    }

    /// <summary>Renames the preset being edited; a refused name stays in the box and says why.</summary>
    public async Task CommitRenameAsync(string text)
    {
        if (_editing is not { } entry) return;
        var result = await _actions.RenameAsync(entry, text, _catalogue.BuiltIns(entry.Node));
        if (!result.Ok) { Say(result.Error); return; }
        EndEdit();
        FillList(new[] { entry with { Name = result.Select ?? entry.Name } });
    }

    /// <summary>Copies each selected preset that can be copied (built-in drum maps can too; other built-ins are skipped).</summary>
    public void DuplicateSelected()
    {
        if (!_duplicate.IsEnabled) return;
        var made = new List<PresetEntry>();
        foreach (var entry in SelectedEntries.Where(e => !e.BuiltIn || e.Node.Kind == PresetCatalogKind.DrumMap))
        {
            var result = _actions.Duplicate(entry, _catalogue.Entries(entry.Node).Select(e => e.Name).ToList());
            Say(result.Error);
            if (result.Ok && result.Select is { } copy) made.Add(entry with { Name = copy });
        }
        if (made.Count > 0) FillList(made);
    }

    /// <summary>Deletes the selected user presets after one question; the row after the first deleted one is selected.</summary>
    public async Task DeleteSelectedAsync()
    {
        var at = _list.SelectedIndex;
        var result = await _actions.DeleteAsync(SelectedEntries);
        if (result.Error is { } error) Say(error);
        if (!result.Ok) return;
        FillList(null);
        if (_entries.Count > 0) _list.SelectedIndex = Math.Clamp(at, 0, _entries.Count - 1);
        UpdateTools();
    }

    /// <summary>Moves the selected user presets one row up (-1) or down (+1) as a block; not while searching.</summary>
    public void MoveSelected(int delta)
    {
        if (SearchText.Length > 0) return;
        var keep = SelectedEntries.ToList();
        var result = _actions.Move(keep, delta);
        if (result.Error is { } error) Say(error);
        if (result.Ok) FillList(keep);
    }

    /// <summary>Leaves the search and shows the one selected hit in its own node.</summary>
    public void ShowInTree()
    {
        if (SelectedEntries is not [{ } entry]) return;
        Show(entry.Node.Kind, entry.Node.Key);
        Select(entry.Name);
    }


    private void UpdateTools()
    {
        var chosen = SelectedEntries;
        var user = chosen.Where(entry => !entry.BuiltIn).ToList();
        var searching = SearchText.Length > 0;
        var builtInTip = chosen.Count > 0 && user.Count == 0 ? BuiltInTip : NoneSelectedTip;
        SetTool(_rename, chosen.Count == 1 && user.Count == 1, "Rename the selected preset", chosen.Count > 1 ? RenameMultipleTip : builtInTip);
        var canUp = user.Count > 0 && !searching && _actions.CanMove(user, -1);
        var canDown = user.Count > 0 && !searching && _actions.CanMove(user, 1);
        SetTool(_up, canUp, "Move the selected presets up", searching ? SearchOrderTip : user.Count == 0 ? builtInTip : AtTopTip);
        SetTool(_down, canDown, "Move the selected presets down", searching ? SearchOrderTip : user.Count == 0 ? builtInTip : AtBottomTip);
        SetTool(_delete, user.Count > 0, user.Count == 1 ? "Delete the selected preset" : $"Delete the {user.Count} selected presets", builtInTip);
        var copyable = chosen.Any(entry => !entry.BuiltIn || entry.Node.Kind == PresetCatalogKind.DrumMap);
        SetTool(_duplicate, copyable, "Make a copy of the selected presets", chosen.Count == 0 ? NoneSelectedTip : DuplicateBuiltInTip);
        _show.IsEnabled = chosen.Count == 1;
    }

    private static void SetTool(Button button, bool on, string onTip, string offTip)
    {
        button.IsEnabled = on;
        ToolTip.SetTip(button, on ? onTip : offTip);
    }

    private void Say(string? text)
    {
        _message.Text = text ?? "";
        _message.IsVisible = text is not null;
    }

    private void Toggle(PresetCatalogKind kind, bool open)
    {
        if (open) _expanded.Add(kind);
        else
        {
            _expanded.Remove(kind);
            if (_node.Kind == kind) _node = _catalogue.Node(kind, null);
        }
        FillList(null);
        (_tree.SelectedItem as ListBoxItem)?.Focus();
    }

    private int SelectEntries(IReadOnlyList<PresetEntry> keep)
    {
        _list.Selection.Clear();
        var found = 0;
        var first = -1;
        for (var i = 0; i < _entries.Count; i++)
        {
            var listed = _entries[i];
            var wanted = false;
            foreach (var entry in keep)
            {
                if (listed.Node.Same(entry.Node) && string.Equals(listed.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) { wanted = true; break; }
            }
            if (!wanted) continue;
            _list.Selection.Select(i);
            if (first < 0) first = i;
            found++;
        }
        if (first >= 0 && _list.ContainerFromIndex(first) is ListBoxItem container) container.BringIntoView();
        return found;
    }

    private void EndEdit()
    {
        if (_editor is { } editor && _editorHost is { } host) host.Children.Remove(editor);
        if (_editorName is { } name) name.IsVisible = true;
        _editor = null;
        _editorHost = null;
        _editorName = null;
        _editing = null;
    }

    private Control BuildRoot()
    {
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, TopGap), LastChildFill = true };
        DockPanel.SetDock(_search, Dock.Right);
        top.Children.Add(_search);
        top.Children.Add(_heading);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(BarMargin) };
        foreach (var button in new[] { _rename, _duplicate, _up, _down, _delete, _show }) bar.Children.Add(button);
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_message, Dock.Bottom);
        DockPanel.SetDock(_empty, Dock.Top);
        var right = new DockPanel();
        right.Children.Add(bar);
        right.Children.Add(_message);
        right.Children.Add(_empty);
        right.Children.Add(_list);

        var rightFrame = Framed(right);
        Grid.SetColumn(rightFrame, 1);
        var leftFrame = Framed(_tree);
        leftFrame.Margin = new Thickness(0, 0, 8, 0);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{TreeWidth},*") };
        body.Children.Add(leftFrame);
        body.Children.Add(rightFrame);

        var root = new DockPanel { Margin = new Thickness(BodyMargin) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        return root;
    }

    private static Border Framed(Control content)
    {
        var frame = new Border { BorderThickness = new Thickness(FramePadding), CornerRadius = new CornerRadius(FrameCornerRadius), Child = content };
        frame[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("ChromeBorder");
        return frame;
    }

    private static Button ToolButton(string text)
    {
        var button = new Button
        {
            Content = text, Height = ToolHeight, MinWidth = ToolMinWidth, Margin = new Thickness(0, 0, ToolGap, 0), Padding = new Thickness(ToolPaddingX, 0),
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static void WireTool(Button button, string tip, Action click)
    {
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
    }
}
