using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Features.LeftStrip;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the Customize strip window: the Available icons list with its search box, the add / remove / move up / move down buttons, the On the strip
//   list (reordered by the buttons or by dragging), Position (Left vertical / Beside menus / Right vertical), Size (Normal / Large), Tooltips and
//   Show labels, and the footer (Reset to default, Cancel, Apply). Apply writes the choices into the saved state and rebuilds the live strip.
// Does not own: the catalog (LeftStripItems), the strip itself (LeftStripFeature), saving the settings file (LeftStripFeature through MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests.
public sealed class LeftStripCustomizeWindow : Window
{
    private const double WindowWidth = 720, WindowHeight = 480, ListWidth = 220, Gap = 6, ColumnGap = 12;

    private readonly LeftStripSettings _state;
    private readonly Action _applied;
    private readonly ObservableCollection<string> _on = new();
    private readonly ListBox _available = new() { Name = "AvailableList", Width = ListWidth };
    private readonly ListBox _strip = new() { Name = "StripList", Width = ListWidth };
    private readonly TextBox _search = new() { Name = "SearchBox", Watermark = "Search", Margin = new Thickness(0, 0, 0, Gap) };
    private readonly RadioButton _left = new() { Content = "Left vertical", GroupName = "StripPosition" };
    private readonly RadioButton _beside = new() { Content = "Beside menus", GroupName = "StripPosition" };
    private readonly RadioButton _right = new() { Content = "Right vertical", GroupName = "StripPosition" };
    private readonly RadioButton _normal = new() { Content = "Normal", GroupName = "StripSize" };
    private readonly RadioButton _large = new() { Content = "Large", GroupName = "StripSize" };
    private readonly CheckBox _tooltips = new() { Content = "Tooltips" };
    private readonly CheckBox _labels = new() { Content = "Show labels" };

    public LeftStripCustomizeWindow(LeftStripSettings state, Action applied)
    {
        _state = state;
        _applied = applied;
        Title = "Customize strip";
        Width = WindowWidth; Height = WindowHeight;
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = ChromeMetrics.TextDialog;

        _available.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<string>((id, _) => new TextBlock { Text = Label(id) });
        _strip.ItemTemplate = _available.ItemTemplate;
        _strip.ItemsSource = _on;
        _search.TextChanged += (_, _) => FillAvailable();
        _available.DoubleTapped += (_, _) => Add();
        _strip.DoubleTapped += (_, _) => Remove();
        AttachDrag();

        var add = Action("Add >", Add);
        var remove = Action("< Remove", Remove);
        var up = Action("Move up", () => Move(-1));
        var down = Action("Move down", () => Move(1));
        var middle = new StackPanel { Spacing = Gap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(ColumnGap, 0), Children = { add, remove, up, down } };

        var availableColumn = Column("Available icons", _available, _search);
        var stripColumn = Column("On the strip", _strip, null);
        var options = new StackPanel
        {
            Spacing = Gap, Margin = new Thickness(ColumnGap, 0, 0, 0),
            Children = { Heading("Position"), _left, _beside, _right, Heading("Size"), _normal, _large, Heading("Show"), _tooltips, _labels },
        };
        var body = new DockPanel { LastChildFill = false };
        foreach (var part in new Control[] { availableColumn, middle, stripColumn, options }) { DockPanel.SetDock(part, Dock.Left); body.Children.Add(part); }

        var reset = Action("Reset to default", () => Load(new LeftStripSettings()));
        reset.Name = "ResetButton";
        var cancel = Action("Cancel", () => Close());
        cancel.IsCancel = true;
        var apply = Action("Apply", Apply);
        apply.IsDefault = true; apply.Name = "ApplyButton";
        cancel.Margin = new Thickness(0, 0, Gap, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } };
        var footer = new DockPanel { Margin = DialogMetrics.FooterGap, Children = { reset, buttons } };
        DockPanel.SetDock(reset, Dock.Left);

        var dock = new DockPanel { Margin = DialogMetrics.ContentInset };
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(body);
        Content = dock;
        Load(state);
    }

    private static string Label(string id) => id == LeftStripItems.SeparatorId ? "Separator" : LeftStripItems.Find(id) is { } item ? $"{item.Label}  ({item.Group})" : id;

    private static Button Action(string text, Action run)
    {
        var button = new Button { Content = text, Padding = DialogMetrics.ActionButtonPadding, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => run();
        return button;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = global::Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, Gap, 0, 0) };

    private static DockPanel Column(string heading, ListBox list, Control? above)
    {
        var column = new DockPanel();
        var title = Heading(heading);
        title.Margin = new Thickness(0, 0, 0, Gap);
        DockPanel.SetDock(title, Dock.Top);
        column.Children.Add(title);
        if (above is not null) { DockPanel.SetDock(above, Dock.Top); column.Children.Add(above); }
        list[!ListBox.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        list.BorderThickness = new Thickness(1);
        column.Children.Add(list);
        return column;
    }

    private void Load(LeftStripSettings from)
    {
        _on.Clear();
        foreach (var id in LeftStripItems.Resolve(from.Items)) _on.Add(id);
        _left.IsChecked = !from.BesideMenus && !from.RightSide;
        _beside.IsChecked = from.BesideMenus;
        _right.IsChecked = !from.BesideMenus && from.RightSide;
        _normal.IsChecked = !from.Large;
        _large.IsChecked = from.Large;
        _tooltips.IsChecked = from.Tooltips;
        _labels.IsChecked = from.Labels;
        FillAvailable();
    }

    // The icons not on the strip yet that match the search, then the Separator (always available: the strip may hold several).
    private void FillAvailable()
    {
        var term = (_search.Text ?? "").Trim();
        var ids = LeftStripItems.All.Where(i => !_on.Contains(i.Id))
            .Where(i => term.Length == 0 || i.Label.Contains(term, StringComparison.OrdinalIgnoreCase) || i.Group.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Id).ToList();
        if (term.Length == 0 || "Separator".Contains(term, StringComparison.OrdinalIgnoreCase)) ids.Add(LeftStripItems.SeparatorId);
        _available.ItemsSource = ids;
    }

    private void Add()
    {
        if (_available.SelectedItem is not string id) return;
        var at = _strip.SelectedIndex >= 0 ? _strip.SelectedIndex + 1 : _on.Count;
        _on.Insert(at, id);
        _strip.SelectedIndex = at;
        FillAvailable();
    }

    private void Remove()
    {
        var i = _strip.SelectedIndex;
        if (i < 0) return;
        _on.RemoveAt(i);
        _strip.SelectedIndex = Math.Min(i, _on.Count - 1);
        FillAvailable();
    }

    private void Move(int by)
    {
        var i = _strip.SelectedIndex;
        var to = i + by;
        if (i < 0 || to < 0 || to >= _on.Count) return;
        _on.Move(i, to);
        _strip.SelectedIndex = to;
    }

    // Dragging a row of the On the strip list drops it where the pointer is released.
    private void AttachDrag()
    {
        int? from = null;
        _strip.AddHandler(PointerPressedEvent, (_, e) => from = _strip.SelectedIndex >= 0 && e.GetCurrentPoint(_strip).Properties.IsLeftButtonPressed ? IndexAt(e) : null,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _strip.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (from is not { } start) return;
            from = null;
            var end = IndexAt(e);
            if (end is not { } to || to == start || start < 0 || start >= _on.Count) return;
            _on.Move(start, to);
            _strip.SelectedIndex = to;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private int? IndexAt(PointerEventArgs e)
    {
        var hit = _strip.InputHitTest(e.GetPosition(_strip)) as Visual;
        while (hit is not null && hit is not ListBoxItem) hit = hit.GetVisualParent();
        return hit is ListBoxItem item ? _strip.IndexFromContainer(item) : null;
    }

    private void Apply()
    {
        _state.Items = _on.SequenceEqual(LeftStripItems.Default) ? null : _on.ToList();
        _state.BesideMenus = _beside.IsChecked == true;
        _state.RightSide = _right.IsChecked == true;
        _state.Large = _large.IsChecked == true;
        _state.Tooltips = _tooltips.IsChecked == true;
        _state.Labels = _labels.IsChecked == true;
        _applied();
        Close();
    }
}
