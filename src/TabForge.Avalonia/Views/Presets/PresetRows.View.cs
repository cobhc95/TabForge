using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;

// Owns: what one preset row renders in the open list: the group header above the first row of a group, the grip, the label, the pencil and bin that
//   show only under the pointer or on the selected row, and the error line; and the item-template view that shows plain text in the closed box.
// Does not own: what the buttons do (PresetRows), the icons' drawing (PresetRowIcons) or the drag (PresetRows.Drag).
// Tests: PresetRowsTests.
namespace TabForge.Avalonia.Views.Presets;

public sealed partial class PresetRows
{
    private const int HeaderRow = 0, ContentRow = 1, ErrorRow = 2, GripColumn = 0, LabelColumn = 1, ToolsColumn = 2;
    private const string BuiltInHeader = "Built-in", YoursHeader = "Yours", StarPrefix = "★ ", SelectedFill = "AccentSoftBrush", HoverFill = "HoverBrush", FramePart = "bd";

    /// <summary>The group an item belongs to: Yours for a user's preset, BuiltIn for a row that comes with the app, otherwise None.</summary>
    public PresetGroup GroupOf(object? item)
    {
        if (item is null || _isManage(item)) return PresetGroup.None;
        if (_userName(item) is not null) return PresetGroup.Yours;
        return _isBuiltIn(item) ? PresetGroup.BuiltIn : PresetGroup.None;
    }

    /// <summary>True when no earlier row of the list is in the same group (the row that carries the group's header).</summary>
    private bool IsFirstOfGroup(object? item, PresetGroup group)
    {
        foreach (var other in _host.Items)
        {
            if (ReferenceEquals(other, item) || Equals(other, item)) return true;
            if (GroupOf(other) == group) return false;
        }
        return true;
    }

    /// <summary>What a row renders: the organisable row for a user's preset or a built-in one, plain text otherwise.</summary>
    public Control View(object? item)
    {
        var text = _text(item);
        var group = GroupOf(item);
        if (group == PresetGroup.None) return Plain(text);
        var name = _userName(item!);
        return Row(name, name is not null && text.StartsWith(StarPrefix, StringComparison.Ordinal) ? text[StarPrefix.Length..] : text, group, IsFirstOfGroup(item, group));
    }

    private Control Row(string? name, string text, PresetGroup group, bool header)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions { new(new GridLength(DialogMetrics.PresetGripWidth)), new(GridLength.Star), new(GridLength.Auto) } };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto) { MinHeight = DialogMetrics.PresetRowMinHeight });
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (header) AddHeader(grid, group == PresetGroup.Yours ? YoursHeader : BuiltInHeader);
        var label = RowLabel(text);
        Grid.SetColumn(label, LabelColumn);
        Grid.SetRow(label, ContentRow);
        grid.Children.Add(label);
        var border = new Border { Child = grid, Tag = name ?? "", Background = AvaloniaPalette.TransparentBrush }.WithPresetInset();
        if (name is null) return border;
        var grip = PresetRowIcons.Grip(name);
        Grid.SetColumn(grip, GripColumn);
        Grid.SetRow(grip, ContentRow);
        grid.Children.Add(grip);
        BeginDrag(grip, name);
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = PresetRowIcons.ButtonGap, VerticalAlignment = VerticalAlignment.Center,
            Children = { PresetRowIcons.Rename(name, () => BeginRename(name)), PresetRowIcons.Delete(name, () => Delete(name)) },
        }.Spaced(DialogMetrics.PresetToolsGap);
        Grid.SetColumn(tools, ToolsColumn);
        Grid.SetRow(tools, ContentRow);
        grid.Children.Add(tools);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false }.Spaced(DialogMetrics.PresetErrorGap);
        error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
        Grid.SetRow(error, ErrorRow);
        Grid.SetColumnSpan(error, ToolsColumn + 1);
        grid.Children.Add(error);
        _rows[name] = (grid, label, error);
        ShowToolsWhenNeeded(border, tools);
        return border;
    }

    private static void AddHeader(Grid grid, string text)
    {
        var header = new TextBlock { Text = text, FontSize = DialogMetrics.PresetHeaderSize, FontWeight = FontWeight.SemiBold, IsHitTestVisible = false }.Spaced(DialogMetrics.PresetHeaderGap);
        header[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        Grid.SetRow(header, HeaderRow);
        Grid.SetColumnSpan(header, ToolsColumn + 1);
        grid.Children.Add(header);
    }

    // The pencil and bin are there only while the pointer is over the row or the row is the selected one; they stay in the layout (so the label never
    // jumps) but are invisible and ignore the pointer otherwise.
    private static void ShowToolsWhenNeeded(Border row, Panel tools)
    {
        bool over = false, selected = false;
        ListBoxItem? container = null;
        void Sync()
        {
            var on = over || selected;
            tools.Opacity = on ? 1 : 0;
            tools.IsHitTestVisible = on;
            Paint(container, selected ? SelectedFill : over ? HoverFill : null);
        }
        void OnProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ListBoxItem.IsSelectedProperty) return;
            selected = container?.IsSelected == true;
            Sync();
        }
        row.PointerEntered += (_, _) => { over = true; Sync(); };
        row.PointerExited += (_, _) => { over = false; Sync(); };
        row.AttachedToVisualTree += (_, _) =>
        {
            container = row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (container is null) return;
            selected = container.IsSelected;
            container.PropertyChanged += OnProperty;
            Sync();
        };
        row.DetachedFromVisualTree += (_, _) =>
        {
            if (container is not null) container.PropertyChanged -= OnProperty;
            container = null;
        };
        Sync();
    }

    // The selected row takes the theme's accent fill and a hovered one the theme's hover shade, so the two never look alike; the fill goes on the
    // row's own frame (the container, and the template's "bd" part when it has one), above the shared look's fills. Null gives the look back.
    private static void Paint(ListBoxItem? container, string? brush)
    {
        if (container is null) return;
        var bd = container.GetVisualDescendants().OfType<Border>().FirstOrDefault(part => part.Name == FramePart);
        if (brush is null) { container.ClearValue(TemplatedControl.BackgroundProperty); bd?.ClearValue(Border.BackgroundProperty); return; }
        container[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension(brush);
        if (bd is not null) bd[!Border.BackgroundProperty] = new DynamicResourceExtension(brush);
    }

    private static TextBlock Plain(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    // A label inside the open list, a little right of the grip column.
    private static TextBlock RowLabel(string text) => Plain(text).Spaced(DialogMetrics.PresetLabelGap);

    /// <summary>The row content: the organisable row inside the open list, plain text in the closed box.</summary>
    private sealed class PresetRowView : Decorator
    {
        private readonly PresetRows _rows;
        public PresetRowView(PresetRows rows)
        {
            _rows = rows;
            Loaded += (_, _) => Build();
            DataContextChanged += (_, _) => { if (IsLoaded) Build(); };
        }

        private void Build()
        {
            var text = DataContext?.ToString() ?? "";
            var container = this.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            var inList = _rows._host is not ComboBox || container is not null;
            // A ready-made ComboBoxItem is its own item: its row is the item, not the content the template is given.
            var item = container is not null && _rows._host.IndexFromContainer(container) is var at and >= 0 && ReferenceEquals(_rows._host.Items[at], container) ? container : DataContext;
            Child = inList ? _rows.View(item) : Plain(text);
        }
    }
}
