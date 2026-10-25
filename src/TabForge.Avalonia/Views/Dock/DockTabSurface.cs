using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Platform;
using TabForge.Docking;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Docking;

// Owns: one tab group: the strip of pane titles, the selected pane's content, the node's SelectedPanel when a tab is clicked,
//   the tab context menu hook and the pointer gesture that starts a tab drag (press, move past a threshold, release).
// Does not own: creating pane content (the id -> Control factory), what a drag does (DockWorkspaceController) or the layout tree.
// Tests: TabForge.Avalonia.Tests.DockWorkspaceViewTests, DockWorkspaceControllerTests.
/// <summary>A tab group of the dock: tab strip above the selected pane's content.</summary>
public sealed class DockTabSurface : Border
{
    /// <summary>The pointer distance in DIPs from the press before a tab press becomes a drag.</summary>
    public const double DragThreshold = 6;
    /// <summary>The tab header row height, as the WPF dock host's header (36).</summary>
    public const double StripHeight = 36;

    private readonly IUiThread _ui = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();   // posts the layout save after a tab click
    private readonly DockNodeState _node;
    private readonly IReadOnlyDictionary<string, Control> _content;
    private readonly Func<string, ContextMenu?>? _menuOf;
    private readonly Action? _edited;
    private readonly StackPanel _strip = new() { Name = "DockTabs", Orientation = Orientation.Horizontal };
    private readonly Border _body = new() { Name = "DockTabBody" };
    private readonly Border _stripBar;
    private (string Id, Point At, bool Dragging)? _press;

    public DockTabSurface(DockNodeState node, IReadOnlyDictionary<string, Control> content,
        Func<string, ContextMenu?>? menuOf = null, Action? edited = null)
    {
        _node = node;
        _content = content;
        _menuOf = menuOf;
        _edited = edited;
        Name = "DockTabSurface";
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        var stripBar = _stripBar = new Border { Child = _strip, Height = StripHeight };
        stripBar[!BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        // A solo pane (the fretboard) has no tab to show: its strip hides and its content carries the pane menu, as in WPF.
        stripBar.IsVisible = !DockPanePresenter.HidesTabStrip(node.Panels);
        _body.ContextMenu = DockPanePresenter.ContentMenuPane(node.Panels) is { } menuPane ? menuOf?.Invoke(menuPane) : null;
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(stripBar, Dock.Top);
        dock.Children.Add(stripBar);
        dock.Children.Add(_body);
        Child = dock;
        Select(node.SelectedPanel ?? node.Panels.FirstOrDefault());
    }

    public DockNodeState Node => _node;

    public IReadOnlyList<Control> Tabs => _strip.Children;

    public string? Selected { get; private set; }

    /// <summary>The height of the header row a tab drop counts as, 0 when the strip is hidden (a solo pane).</summary>
    public double HeaderHeight => _stripBar.IsVisible ? StripHeight : 0;

    /// <summary>The tabs' left edges and widths in this surface's coordinates, for the insertion position of a tab drop.</summary>
    public IReadOnlyList<(double Left, double Width)> TabSpans() =>
        _strip.Children.Select(t => (t.TranslatePoint(default, this)?.X ?? 0, t.Bounds.Width)).ToList();

    /// <summary>Raised while a tab is dragged past <see cref="DragThreshold"/>, with the pane id and the pointer event.</summary>
    public event Action<string, PointerEventArgs>? TabDragMoved;

    /// <summary>Raised on release after a drag started.</summary>
    public event Action<string, PointerReleasedEventArgs>? TabDragEnded;

    /// <summary>Shows the pane with this id (or the first one when it is not in the group) and marks its tab.</summary>
    public void Select(string? id)
    {
        id = id is not null && _node.Panels.Contains(id) ? id : _node.Panels.FirstOrDefault();
        Selected = id;
        _node.SelectedPanel = id;
        if (_strip.Children.Count == 0) foreach (var panel in _node.Panels) _strip.Children.Add(Tab(panel));
        foreach (var tab in _strip.Children.OfType<Border>()) Restyle(tab, tab.Name == "DockTab_" + id);
        _body.Child = id is not null && _content.TryGetValue(id, out var control) ? control : null;
    }

    private Control Tab(string id)
    {
        var title = DockPaneTable.Rows.FirstOrDefault(r => r.Id == id)?.PaneTitle ?? id;
        // The WPF dock tab: a pill rounded on top, accent-tinted with an accent edge when selected, on the strip colour otherwise.
        var text = new TextBlock
        {
            Text = title, FontSize = 12.65, FontWeight = FontWeight.SemiBold, MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var tab = new Border
        {
            Name = "DockTab_" + id, Padding = new Thickness(9, 5), Margin = new Thickness(3, 3, 0, 3),
            CornerRadius = new CornerRadius(4, 4, 0, 0), BorderThickness = new Thickness(1), Child = text,
            Background = Brushes.Transparent,   // transparent, not null: the padding takes hover and presses
        };
        // The selected tab keeps its accent look; the others take the shared hover and press shade.
        TabForge.Avalonia.Platform.HoverShade.Wire(tab, fill => { if (id != Selected) tab.Background = fill; }, () => Brushes.Transparent);
        var movable = DockPanePresenter.CanMove(id);
        if (movable)
        {
            ToolTip.SetTip(tab, DockPanePresenter.TabToolTip(title));
            if (_menuOf?.Invoke(id) is { } menu) tab.ContextMenu = menu;
        }
        tab.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
            if (id != Selected)
            {
                Select(id);   // selection is immediate; saving the layout waits until the click path is done
                if (_edited is { } edited) _ui.Post(edited, UiPriority.Background);
            }
            _press = (id, e.GetPosition(this), false);
            e.Pointer.Capture(tab);
            e.Handled = true;
        };
        tab.PointerMoved += (_, e) =>
        {
            if (_press is not { } press || press.Id != id || !movable) return;
            var delta = e.GetPosition(this) - press.At;
            if (!press.Dragging && Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;
            _press = press with { Dragging = true };
            TabDragMoved?.Invoke(id, e);
        };
        tab.PointerReleased += (_, e) =>
        {
            if (_press is not { } press || press.Id != id) return;
            _press = null;
            e.Pointer.Capture(null);
            if (press.Dragging) TabDragEnded?.Invoke(id, e);
        };
        Restyle(tab, id == Selected);
        return tab;
    }

    private static void Restyle(Border tab, bool active)
    {
        if (active) tab[!Border.BackgroundProperty] = new DynamicResourceExtension("SelectedTintBrush");
        else tab.Background = Brushes.Transparent;
        tab[!Border.BorderBrushProperty] = new DynamicResourceExtension(active ? "SelectedOutlineBrush" : "Panel2Brush");
        if (tab.Child is TextBlock text) text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(active ? "TextStrongBrush" : "LegibleBrush");
    }
}
