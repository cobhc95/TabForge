using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: the preset manager's two lists: the tree rows (chevron, title, count, indentation) and the list rows (name, built-in label,
//   "where" in a flat list, date), and filling them from the injected catalogue.
// Does not own: the catalogue, the selection and the tools (the window), or the actions.
// Perf (H13/H15): the preset list is virtualized (ItemsSource + data template over a VirtualizingStackPanel): only the
//   visible rows build controls, so a large preset folder no longer builds the whole list on open, search or Show.
//   The tree stays direct items (a handful of group rows; virtualizing it buys nothing).
// Tests: PresetManagerWindowTests, IconArtPreloadTests (list readiness).
namespace TabForge.Avalonia.Views.Presets;

public sealed partial class PresetManagerWindow
{
    // The entries behind the virtualized list's ItemsSource; Listed reads these, never the containers.
    private IReadOnlyList<PresetEntry> _entries = Array.Empty<PresetEntry>();
    // True while the list shows the flat search/group layout (name + where + date); false for the plain node layout.
    private bool _flat;

    /// <summary>Switches the preset list to virtualization; called once from the window's constructor before the first fill.</summary>
    private void ConfigureListVirtualization()
    {
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<PresetEntry>((entry, _) => BuildRowContent(entry, _flat), supportsRecycling: true);
        _list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item) return;
            item.Padding = new Thickness(RowPadding, RowPaddingY, RowPadding, RowPaddingY);
            if (item.Content is PresetEntry entry)
                AutomationProperties.SetName(item, RowAutomationName(entry, _flat));
        };
    }

    private void FillList(IReadOnlyList<PresetEntry>? select, bool rebuildTree = true)
    {
        var keep = select ?? SelectedEntries;
        _filling = true;
        try
        {
            EndEdit();
            Say(null);
            if (rebuildTree) FillTree();
            var query = SearchText;
            var entries = query.Length > 0 ? _catalogue.Search(query) : _catalogue.Entries(_node);
            _flat = query.Length > 0 || _node.IsGroup;
            _entries = entries;
            // A fresh template per fill binds the current flat layout; recycling still reuses containers within the fill,
            // so only the visible rows build controls no matter how many presets the folder holds.
            var flat = _flat;
            _list.ItemTemplate = new FuncDataTemplate<PresetEntry>((entry, _) => BuildRowContent(entry, flat), supportsRecycling: true);
            _list.ItemsSource = _entries;
            _heading.Text = query.Length > 0 ? $"{entries.Count} {(entries.Count == 1 ? "preset" : "presets")} found" : _node.Where;
            _empty.Text = query.Length > 0 ? NoMatchText : EmptyText;
            _empty.IsVisible = entries.Count == 0;
            _show.IsVisible = _flat;
            SelectEntries(keep);
            if (_list.Selection.SelectedIndexes.Count == 0 && _entries.Count > 0) _list.SelectedIndex = 0;
        }
        finally { _filling = false; }
        UpdateTools();
    }

    private void FillTree()
    {
        _tree.Items.Clear();
        foreach (var group in _catalogue.Groups())
        {
            _tree.Items.Add(TreeItem(group, 0));
            if (group.IsGroup && _expanded.Contains(group.Kind))
                foreach (var child in _catalogue.Children(group)) _tree.Items.Add(TreeItem(child, 1));
        }
        _tree.SelectedItem = SearchText.Length > 0 ? null : _tree.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag is PresetNode node && node.Same(_node));
    }

    private ListBoxItem TreeItem(PresetNode node, int level)
    {
        var count = _catalogue.Entries(node).Count;
        var open = _expanded.Contains(node.Kind);
        var chevron = new Button
        {
            Content = node.IsGroup ? (open ? ChevronOpen : ChevronClosed) : "", Width = ChevronWidth, Padding = new Thickness(0),
            IsVisible = node.IsGroup, Background = AvaloniaPalette.TransparentBrush, BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, FontSize = ChevronFontSize,
        };
        if (node.IsGroup) chevron.Click += (_, e) => { e.Handled = true; Toggle(node.Kind, !open); };
        var title = new TextBlock { Text = node.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(TreeTitleGapX, 0, TreeTitleGapRight, 0) };
        var number = new TextBlock { Text = count.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(TreeCountGap, 0, 0, 0) };
        number[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        if (count == 0) title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), Tag = count };
        grid.Children.Add(chevron);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(number, 2);
        grid.Children.Add(number);
        var item = new ListBoxItem { Tag = node, Content = grid, Padding = new Thickness(TreePaddingX, TreePaddingY, TreePaddingRight, TreePaddingY), Margin = new Thickness(level * IndentPerLevel, 0, 0, 0) };
        AutomationProperties.SetName(item, $"{node.Title}, {count} {(count == 1 ? "preset" : "presets")}{(node.IsGroup ? open ? ", expanded" : ", collapsed" : "")}");
        return item;
    }

    /// <summary>The index of <paramref name="entry"/> in the virtualized list, or -1.</summary>
    private int IndexOf(PresetEntry entry)
    {
        for (var i = 0; i < _entries.Count; i++)
            if (_entries[i].Node.Same(entry.Node) && string.Equals(_entries[i].Name, entry.Name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>The row content the virtualized template builds for a visible entry (the container is supplied by the list).</summary>
    private Control BuildRowContent(PresetEntry? entry, bool flat)
    {
        if (entry is null) return new Grid();
        var grid = new Grid { MinHeight = RowMinHeight, ColumnDefinitions = ColumnDefinitions.Parse(flat ? "*,*,Auto" : "*,Auto") };
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        var text = new TextBlock { Text = entry.Name, Tag = NameTag, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(text, entry.BuiltIn ? BuiltInTip : "Double-click to rename");
        name.Children.Add(text);
        if (entry.BuiltIn) name.Children.Add(Muted(BuiltInLabel, new Thickness(NameGap, 0, 0, 0)));
        grid.Children.Add(name);
        if (flat)
        {
            var where = Muted(entry.Node.Where, new Thickness(WhereGap, 0, 0, 0));
            where.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(where, 1);
            grid.Children.Add(where);
        }
        var date = Muted(PresetCataloguePresenter.DateText(entry.Modified, DateTime.Now), new Thickness(WhereGap, 0, 0, 0));
        date.HorizontalAlignment = HorizontalAlignment.Right;
        date.MinWidth = DateWidth;
        Grid.SetColumn(date, flat ? 2 : 1);
        grid.Children.Add(date);
        return grid;
    }

    /// <summary>The automation name of a list row (set on the realized container, so tests and readers see it while only visible rows exist).</summary>
    private static string RowAutomationName(PresetEntry entry, bool flat) =>
        string.Join(", ", new[] { entry.Name, entry.BuiltIn ? BuiltInLabel : "", flat ? entry.Node.Where : "",
            PresetCataloguePresenter.DateText(entry.Modified, DateTime.Now) }.Where(t => !string.IsNullOrEmpty(t)));

    private static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return block;
    }
}
