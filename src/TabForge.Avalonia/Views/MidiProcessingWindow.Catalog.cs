using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the catalog column of the MIDI processing window: the rows (group headings with their icon and collapse chevron, processor and
//   parameter matches with the processor's icon), collapsing a group, the search (all groups' matches while text is typed), adding the selected
//   match (Add, Enter, double-click, the add slot) and dragging a match into the chain.
// Does not own: the groups and the search index (MidiProcessorCatalog), the chain's selection and drop line (ChainSelect), the layout.
// Tests: MidiProcessingWindowTests.
public sealed partial class MidiProcessingWindow
{
    /// <summary>A group heading row of the catalog.</summary>
    internal sealed record CatalogHeader(MidiProcessorGroup Group, bool Collapsed);

    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private Button? _addSlot;
    private MidiProcessorCatalog.SearchHit? _catalogPressHit, _lastCatalogHit;
    private Point _catalogPress;
    private bool _catalogDragging, _fillingCatalog;

    /// <summary>The catalog's processor and parameter rows in order (headings left out).</summary>
    internal IReadOnlyList<MidiProcessorCatalog.SearchHit> CatalogHits => _catalog.Items.OfType<MidiProcessorCatalog.SearchHit>().ToList();

    /// <summary>Collapses or expands a catalog group (a click on its heading does the same).</summary>
    internal void ToggleCatalogGroup(string key)
    {
        if (!_collapsed.Remove(key)) _collapsed.Add(key);
        FillCatalog(_lastCatalogHit);
    }

    private void FillCatalog(MidiProcessorCatalog.SearchHit? keep = null)
    {
        var query = _search.Text ?? "";
        var items = new List<object>();
        if (query.Trim().Length == 0)
        {
            foreach (var group in MidiProcessorCatalog.Groups)
            {
                var collapsed = _collapsed.Contains(group.Key);
                items.Add(new CatalogHeader(group, collapsed));
                if (collapsed) continue;
                foreach (var type in group.Types)
                    if (MidiProcessorCatalog.Find(type) is { } info) items.Add(new MidiProcessorCatalog.SearchHit(info, null, info.Name));
            }
        }
        else items.AddRange(MidiProcessorCatalog.SearchAll(query));
        _fillingCatalog = true;
        try
        {
            _catalog.ItemsSource = items;
            var again = keep is null ? null : items.OfType<MidiProcessorCatalog.SearchHit>().FirstOrDefault(h => h.Info.Type == keep.Info.Type && h.Param == keep.Param);
            _catalog.SelectedItem = again ?? items.OfType<MidiProcessorCatalog.SearchHit>().FirstOrDefault();
        }
        finally { _fillingCatalog = false; }
        CatalogSelectionChanged();
    }

    private void CatalogSelectionChanged()
    {
        if (_fillingCatalog) return;
        if (_catalog.SelectedItem is CatalogHeader)
        {
            // A heading is not a choice: the selection stays on the last processor (a click on the heading folds the group).
            _fillingCatalog = true;
            try { _catalog.SelectedItem = _lastCatalogHit is { } last && _catalog.Items.Contains(last) ? last : null; }
            finally { _fillingCatalog = false; }
        }
        if (_catalog.SelectedItem is MidiProcessorCatalog.SearchHit hit) _lastCatalogHit = hit;
        if (_catalog.SelectedItem is MidiProcessorCatalog.SearchHit shown) MidiSharedRows.Tip(_catalog, shown.Param?.Tip is { Length: > 0 } tip ? tip : shown.Info.Description);
        _add.IsEnabled = _catalog.SelectedItem is MidiProcessorCatalog.SearchHit;
    }

    private Control CatalogRow(object? item)
    {
        var row = new DockPanel { ClipToBounds = true, Background = AvaloniaPalette.TransparentBrush };
        row.WithMargin(DialogMetrics.MidiChainRowPad);
        if (item is CatalogHeader header)
        {
            row.Name = "CatalogGroupHeader";
            row.WithMargin(DialogMetrics.MidiGroupHeadPad);
            var chevron = MidiIcons.Icon(header.Collapsed ? IconGeometry.ChevronRight : IconGeometry.ChevronDown, DialogMetrics.MidiIconSize, "MutedBrush");
            var icon = MidiIcons.Icon(MidiIcons.ForGroup(header.Group.Key), DialogMetrics.MidiIconSize, "AccentBrush");
            chevron.WithMargin(DialogMetrics.MidiIconGap); icon.WithMargin(DialogMetrics.MidiIconGap);
            DockPanel.SetDock(chevron, Dock.Left); DockPanel.SetDock(icon, Dock.Left);
            var text = new TextBlock { Text = header.Group.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
            row.Children.Add(chevron); row.Children.Add(icon); row.Children.Add(text);
            MidiSharedRows.Tip(row, header.Collapsed ? "Show this group's processors" : "Hide this group's processors");
            global::Avalonia.Automation.AutomationProperties.SetName(row, header.Group.Name);
            var key = header.Group.Key;
            row.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                AvaloniaUiThread.ForApplication().Post(() => ToggleCatalogGroup(key), UiPriority.Normal);
            };
            return row;
        }
        var hit = item as MidiProcessorCatalog.SearchHit;
        var glyph = MidiIcons.Icon(MidiIcons.ForType(hit?.Info.Type ?? ""), DialogMetrics.MidiIconSize, hit?.Param is null ? "TextPrimary" : "MutedBrush");
        glyph.WithMargin(DialogMetrics.MidiIconGap);
        DockPanel.SetDock(glyph, Dock.Left);
        var name = MidiSmallLayout.CatalogText(hit?.Display ?? "");
        name.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(glyph); row.Children.Add(name);
        return row;
    }

    private void SearchKey(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddSelected(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up && _catalog.ItemCount > 0) { StepCatalog(e.Key == Key.Down ? 1 : -1); e.Handled = true; }
    }

    // Moves the catalog selection to the next processor or parameter row, past the headings.
    private void StepCatalog(int delta)
    {
        var items = _catalog.Items.Cast<object?>().ToList();
        for (var i = _catalog.SelectedIndex + delta; i >= 0 && i < items.Count; i += delta)
            if (items[i] is MidiProcessorCatalog.SearchHit) { _catalog.SelectedIndex = i; _catalog.ScrollIntoView(i); return; }
    }

    /// <summary>Adds the selected catalog match: after the selected chain step, or at <paramref name="at"/> (a drop or the add slot).</summary>
    private void AddSelected(int? at = null)
    {
        FlushSearch();
        if (_catalog.SelectedItem is not MidiProcessorCatalog.SearchHit hit) return;
        AddHit(hit, at);
    }

    private void AddHit(MidiProcessorCatalog.SearchHit hit, int? at)
    {
        var info = hit.Info;
        if (hit.Param is not null)
        {
            // A parameter match: reuse the selected processor of that type, else any of that type, else add one.
            var existing = Selected is { } s && s.Type == info.Type ? s : Procs.FirstOrDefault(p => p.Type == info.Type);
            _jumpKey = hit.Param.Key;
            if (existing is not null)
            {
                if (Selected == existing) ShowSelected(); else ClickChainRow(Procs.IndexOf(existing));
                return;
            }
        }
        if (!MidiProcessingListPresenter.CanAdd(Procs.Count)) { _jumpKey = null; return; }
        int? selectedIndex = Selected is { } sel ? Procs.IndexOf(sel) : null;
        var index = at is int drop ? Math.Clamp(drop, 0, Procs.Count) : MidiProcessingListPresenter.InsertIndex(selectedIndex, Procs.Count);
        var created = MidiProcessorCatalog.Create(info);
        _edit(() => Procs.Insert(index, created));
        RefreshList(Procs.IndexOf(created));
    }

    /// <summary>Applies the search text to the catalog and the parameter highlight now.</summary>
    private void ApplySearch()
    {
        _queryWords = (_search.Text ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        FillCatalog();
        ApplyFilter();
    }

    /// <summary>Runs a queued trailing search now (Enter, Add: the list shows the latest text).</summary>
    private void FlushSearch()
    {
        _searchDebounce.Stop();
        if (!_searchDirty) return;
        _searchDirty = false;
        ApplySearch();
    }

    // ---------- drag from the catalog into the chain ----------

    private void WireCatalogDrag()
    {
        _catalog.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _catalogPressHit = null;
            if (!e.GetCurrentPoint(_catalog).Properties.IsLeftButtonPressed) return;
            _catalogPress = e.GetPosition(this);
            _catalogPressHit = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as MidiProcessorCatalog.SearchHit;
        }, RoutingStrategies.Tunnel);
        _catalog.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_catalogPressHit is null || !e.GetCurrentPoint(_catalog).Properties.IsLeftButtonPressed) return;
            var at = e.GetPosition(this);
            if (!_catalogDragging && Math.Abs(at.X - _catalogPress.X) + Math.Abs(at.Y - _catalogPress.Y) < DialogMetrics.MidiDragThreshold) return;
            if (!_catalogDragging) { _catalogDragging = true; e.Pointer.Capture(_catalog); }
            ShowDropLine(DropTarget(e.GetPosition(_list)));
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        _catalog.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var hit = _catalogPressHit;
            _catalogPressHit = null;
            if (!_catalogDragging) return;
            _catalogDragging = false;
            HideDropLine();
            if (e.Pointer.Captured == _catalog) e.Pointer.Capture(null);
            e.Handled = true;
            if (hit is not null && DropTarget(e.GetPosition(_list)) is int target) AddHit(hit, target);
        }, RoutingStrategies.Tunnel);
        _catalog.PointerCaptureLost += (_, _) => { _catalogDragging = false; _catalogPressHit = null; HideDropLine(); };
    }

    /// <summary>Drops a catalog match into the chain before the step at <paramref name="target"/> (tests; a drag does the same).</summary>
    internal void DropCatalogHit(MidiProcessorCatalog.SearchHit hit, int target) => AddHit(hit, target);

    // The chain index a pointer over the chain (or its add slot) would insert at; null when the pointer is elsewhere.
    private int? DropTarget(Point inList)
    {
        if (_addSlot is { } slot && _list.TranslatePoint(inList, slot) is { } inSlot && new Rect(slot.Bounds.Size).Contains(inSlot)) return Procs.Count;
        if (!new Rect(_list.Bounds.Size).Contains(inList)) return null;
        return InsertIndexAt(inList.Y);
    }
}
