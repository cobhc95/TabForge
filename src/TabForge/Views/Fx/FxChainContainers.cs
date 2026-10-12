using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Services.Features;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Views.Fx;

// Owns: containers in the FX chain window: the container pane on the right (name, bypass, the mix that is not applied yet, the
//   count, the routing note, the preset bar), the actions (new, put selected in a new container, dissolve, delete with its plug-ins,
//   rename, bypass, collapse), their keys and right-click menu, and what a drop of dragged rows does to the chain's tree.
// Does not own: the container model and its rules (Plugins/ChainContainers.cs), the rows (FxChainRows, FxChainList), the undo step
//   and the engine (FxChainWindow, through the actions it passes), container preset files (FxContainerPresets).
// Tests: TestFxChainContainerRows.
internal sealed class FxChainContainers
{
    public const string MixTip = "Container mix is kept with the chain and is not applied yet";
    private readonly Window _owner;
    private readonly FxChainList _rows;
    private readonly Func<RigPreset> _rig;
    private readonly Action<Action<List<ChainItem>>, bool> _edit;
    private readonly Action<object?> _refresh;
    private readonly Action<IReadOnlyList<PluginSlot>> _leaving;
    private readonly TextBox _name = new() { MinWidth = 180, MaxLength = 80, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
    private readonly CheckBox _on = new() { Content = "On", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Untick to bypass every plug-in in this container; their own switches stay as they are" };
    private readonly KnobControl _mix = new() { Minimum = 0, Maximum = 100, DefaultValue = 100, Width = 30, Height = 30, Label = "Mix", IsEnabled = false, Opacity = 0.5, Format = v => $"{v:0}%", ToolTip = MixTip };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _presetBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private ChainContainer? _shown;

    /// <summary>The container pane; hidden unless a container row is focused.</summary>
    public StackPanel Pane { get; } = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    /// <summary>The preset bar's place in the pane (filled by the container presets).</summary>
    public StackPanel PresetBar => _presetBar;
    /// <summary>The container presets of the pane.</summary>
    public FxContainerPresets Presets { get; }
    public ChainContainer? Shown => _shown;

    /// <param name="edit">Runs a change as one undo step and refreshes the list; true: only bypass switches changed.</param>
    /// <param name="refresh">Builds the list again and focuses the given row's item (null: keeps the focus).</param>
    /// <param name="leaving">Plug-ins that are about to leave the chain (their open editor is closed first).</param>
    /// <param name="trusted">True when a plug-in may be loaded here (PluginTrust).</param>
    /// <param name="collectStates">Reads the plug-ins' current settings into their slots (before a preset is saved).</param>
    public FxChainContainers(Window owner, FxChainList rows, Func<RigPreset> rig, Action<Action<List<ChainItem>>, bool> edit, Action<object?> refresh, Action<IReadOnlyList<PluginSlot>> leaving,
        Func<PluginSlot, bool> trusted, Func<Task> collectStates)
    {
        (_owner, _rows, _rig, _edit, _refresh, _leaving) = (owner, rows, rig, edit, refresh, leaving);
        Presets = new FxContainerPresets(owner, _presetBar, () => _shown, rig, change => edit(change, false), leaving, trusted, collectStates);
        rows.Dropped = Drop;
        rows.CanDropInto = (row, moving) => rows.TagAt(row) is ChainContainer && !moving.Contains(row);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = "Container", VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var mixLabel = new TextBlock { Text = "Mix", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = MixTip };
        mixLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        ToolTipService.SetShowOnDisabled(_mix, true);
        UiIds.Id(_name, "Fx.ContainerName", "Container name");
        TooltipShortcuts.SetText(_name, "The container's name (Enter applies)");
        foreach (var child in new UIElement[] { label, _name, _on, mixLabel, _mix, _count }) line.Children.Add(child);
        Pane.Children.Add(line);
        Pane.Children.Add(_presetBar);
        Pane.Children.Add(_note);
        _name.LostKeyboardFocus += (_, _) => ApplyName();
        _name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyName(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _name.Text = _shown?.Name ?? ""; e.Handled = true; }
        };
        _on.Click += (_, _) => { if (_shown is { } c) SetEnabled(c, _on.IsChecked == true); };
        var menu = new ContextMenu();
        rows.Root.ContextMenu = menu;
        menu.Opened += (_, _) => { menu.Items.Clear(); foreach (var item in MenuItems()) menu.Items.Add(item); };
    }

    /// <summary>Shows the pane for a container (true), or hides it.</summary>
    public bool Show(ChainContainer? container)
    {
        _shown = container;
        Pane.Visibility = container is null ? Visibility.Collapsed : Visibility.Visible;
        if (container is null) return false;
        var count = _rig().Containers?.Contains(container) == true ? container.Count : 0;
        _name.Text = container.Name;
        _on.IsChecked = container.Enabled;
        _mix.Value = container.Mix;
        _count.Text = FxChainRows.Count(count);
        _note.Text = "This container came with its own internal routing; its plug-ins run in series here. The routing is kept and written back on export.";
        _note.Visibility = container.OwnRouting ? Visibility.Visible : Visibility.Collapsed;
        Presets.Fill();
        return true;
    }

    private void ApplyName()
    {
        var name = _name.Text.Trim();
        if (_shown is not { } c || name.Length == 0 || name == c.Name) { _name.Text = _shown?.Name ?? ""; return; }
        _edit(_ => c.Name = name, false);
    }

    private ChainContainer? Focused => _rows.Focused as ChainContainer;

    /// <summary>The container an action on the focused row means: that container, or the one the focused plug-in sits in.</summary>
    private ChainContainer? Target()
    {
        if (Focused is { } c) return c;
        if (_rows.Focused is not { } tag) return null;
        ChainContainer? Owner(List<ChainItem> items, ChainContainer? parent) =>
            items.Any(i => ReferenceEquals(i.Tag, tag)) ? parent : items.Select(i => i.Container is null ? null : Owner(i.Children, i.Container)).FirstOrDefault(o => o is not null);
        return Owner(ChainTree.Tree(_rig()), null);
    }

    private void Change(Func<List<ChainItem>, object?> change)
    {
        object? focus = null;
        _edit(tree => focus = change(tree), false);
        if (focus is not null) _refresh(focus);
    }

    /// <summary>An empty container behind the focused row (at the end without one).</summary>
    public void New() => Change(tree =>
    {
        var item = new ChainItem { Container = new ChainContainer() };
        if (_rows.Focused is { } tag && ChainTree.Find(tree, tag) is { } at) at.List.Insert(at.Index + 1, item); else tree.Add(item);
        return item.Container;
    });

    /// <summary>Rows of one container (or all at the top level) can be grouped: no plug-in is pulled out of a container others stay in.</summary>
    private bool CanGroup => ChainTree.SameParent(ChainTree.Tree(_rig()), _rows.SelectedTags);

    public void GroupSelected() { if (CanGroup) Change(tree => ChainTree.GroupItems(tree, _rows.SelectedTags)); }

    public void Dissolve() { if (Target() is { } c) Change(tree => { ChainTree.Remove(tree, c, keepItems: true); return null; }); }

    /// <summary>Removes the selected containers with everything inside, and the selected plug-ins; asks once when plug-ins go with a container.</summary>
    public void DeleteSelected()
    {
        var tags = _rows.SelectedTags;
        var tree = ChainTree.Tree(_rig());
        var boxes = tags.OfType<ChainContainer>().ToList();
        var inside = boxes.SelectMany(c => ChainTree.Find(tree, c) is { } at ? ChainTree.Plugins(at.List[at.Index].Children) : Array.Empty<PluginSlot>()).Distinct().ToList();
        if (inside.Count > 0 && ThemedMessageBox.Show(_owner, boxes.Count == 1
                ? $"Delete the container “{boxes[0].Name}” with its {FxChainRows.Count(inside.Count)}?" : $"Delete {boxes.Count} containers with their {FxChainRows.Count(inside.Count)}?",
                "Delete container", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _leaving(inside.Concat(tags.OfType<PluginSlot>()).ToList());
        Change(t =>
        {
            foreach (var c in boxes) ChainTree.Remove(t, c, keepItems: false);
            foreach (var p in tags.OfType<PluginSlot>()) if (ChainTree.Find(t, p) is { } at) at.List.RemoveAt(at.Index);
            var stay = ChainTree.Plugins(t).ToHashSet<PluginSlot>(ReferenceEqualityComparer.Instance);
            _rig().Plugins.RemoveAll(p => !stay.Contains(p));   // gone from the tree: gone from the chain
            return null;
        });
    }

    public void Rename()
    {
        if (Focused is null) return;
        _name.Focus();
        _name.SelectAll();
    }

    public void SetEnabled(ChainContainer container, bool on) => _edit(_ => container.Enabled = on, true);

    /// <summary>Collapsed is how the list looks: no undo step.</summary>
    public void ToggleCollapsed(ChainContainer container)
    {
        container.Collapsed = !container.Collapsed;
        _refresh(container);
    }

    /// <summary>The dragged rows were dropped: onto a container (to its end), else in front of a row (inside what holds that row), else at the end.</summary>
    private void Drop(IReadOnlyList<int> moving, int insert, int into)
    {
        var rig = _rig();
        var tags = moving.Select(i => _rows.TagAt(i)!).ToList();
        string Shape(List<ChainItem> items) => string.Concat(items.Select(i => i.Plugin is { } p ? p.Id + "," : $"{i.Container!.Id}[{Shape(i.Children)}]"));
        var tree = ChainTree.Tree(rig);
        if (!ChainTree.Move(tree, tags, into >= 0 ? null : _rows.TagAt(insert), _rows.TagAt(into) as ChainContainer) || Shape(tree) == Shape(ChainTree.Tree(rig))) return;
        _edit(live => ChainTree.Move(live, tags, into >= 0 ? null : _rows.TagAt(insert), _rows.TagAt(into) as ChainContainer), false);
    }

    /// <summary>Runs a container command by its catalogue id (FxChainIds). False when it does not apply now (a rename with no container focused).</summary>
    public bool Run(string id)
    {
        switch (id)
        {
            case FxChainIds.NewContainer: New(); return true;
            case FxChainIds.GroupSelected: GroupSelected(); return true;
            case FxChainIds.Dissolve: Dissolve(); return true;
            case FxChainIds.Rename: if (Focused is null) return false; Rename(); return true;
            case FxChainIds.DeleteContainer: DeleteSelected(); return true;
            default: return false;
        }
    }

    /// <summary>The container entries of the right-click menu and of the Add… menu, for the rows as they are now.</summary>
    public IEnumerable<MenuItem> MenuItems()
    {
        MenuItem Item(string header, string id, bool enabled, string tip)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, ToolTip = tip, InputGestureText = HotkeyCatalog.DisplayAll(TooltipShortcuts.Hotkeys, id) };
            MenuHotkey.SetId(item, id);
            item.Click += (_, _) => Run(id);
            return item;
        }
        var target = Target();
        yield return Item("New container", FxChainIds.NewContainer, true, "An empty container behind the focused row: drag plug-ins onto it");
        yield return Item("Put selected in a new container", FxChainIds.GroupSelected, CanGroup, "The selected rows go into a new container, in their order (rows of one container, or all at the top level)");
        yield return Item(target is null ? "Dissolve container" : $"Dissolve container “{target.Name}”", FxChainIds.Dissolve, target is not null, "Removes the container; its plug-ins stay in the chain");
        yield return Item("Rename container", FxChainIds.Rename, Focused is not null, "Type a new name for the focused container");
        yield return Item("Delete container with its plug-ins…", FxChainIds.DeleteContainer, Focused is not null, "Removes the selected containers and every plug-in inside them");
    }
}
