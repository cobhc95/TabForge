using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the MIDI chain list's multi-selection: click, Ctrl+click, Shift+click, Ctrl+A, the rubber band from the empty area,
//   the focused row (the one the right-hand pane shows) and the whole-selection Remove/Up/Down/Delete. The click/band/focus
//   rules live in ChainSelection, shared with the FX chain list (FxChainList); this window only maps rows to indexes.
// Does not own: what a row shows, the parameter pane, the undo step (the edit delegate), the list rules (MidiProcessingListPresenter).
// Tests: MidiProcessingChainMultiSelectTests.
public sealed partial class MidiProcessingWindow
{
    private readonly ChainSelection _chainSel = new();
    private bool _syncing;
    private int _pressRow = -1;
    private bool _collapseOnRelease;
    private Point _press;
    private readonly Border _chainBand = new()
    {
        BorderThickness = DialogMetrics.MidiFrameBorder, CornerRadius = new CornerRadius(DialogMetrics.MidiFrameCorner), IsVisible = false, Opacity = 0.6, IsHitTestVisible = false,
    };
    private Canvas? _chainLayer;

    /// <summary>The selected chain rows, ascending (the whole selection Remove/Up/Down act on).</summary>
    internal IReadOnlyList<int> SelectedChainIndices => _chainSel.SelectedRows;

    /// <summary>The focused chain row (the pane follows it, like the FX window); -1 with no rows.</summary>
    internal int FocusedChainIndex => _chainSel.FocusedIndex;

    private PluginMidiProcessor? FocusedProc => _chainSel.FocusedIndex >= 0 && _chainSel.FocusedIndex < Procs.Count ? Procs[_chainSel.FocusedIndex] : null;

    private void InitChainSelect()
    {
        _list.SelectionMode = SelectionMode.Multiple;
        _chainBand[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        _chainBand[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
    }

    private Control WrapChainList(Control frame)
    {
        var wrap = new Grid { ClipToBounds = true };
        wrap.Children.Add(frame);
        _chainLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        _chainLayer.Children.Add(_chainBand);
        wrap.Children.Add(_chainLayer);
        return wrap;
    }

    private void WireChainSelect()
    {
        _list.AddHandler(PointerPressedEvent, OnChainDown, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, OnChainMove, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerReleasedEvent, OnChainUp, RoutingStrategies.Tunnel);
        _list.PointerCaptureLost += (_, _) => CancelChainBand();
    }

    private static bool ChainKeepsOwnClick(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
            if (node is Button or TextBox or ScrollBar or ToggleButton) return true;
        return false;
    }

    private int ChainRowOf(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
            if (node is ListBoxItem item)
            {
                var index = _list.IndexFromContainer(item);
                if (index >= 0) return index;
            }
        return -1;
    }

    private void OnChainDown(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_list).Properties.IsRightButtonPressed)
        {
            var hit = ChainRowOf(e.Source);
            if (hit >= 0 && !_chainSel.IsSelected(hit))
            {
                _list.Focus();
                ClickChainRow(hit);
            }
            return;
        }
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || ChainKeepsOwnClick(e.Source)) return;
        var ctrl = e.KeyModifiers.HasPrimary();
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _press = e.GetPosition(_list);
        var index = ChainRowOf(e.Source);
        _list.Focus();
        if (index < 0)
        {
            _chainSel.BeginBand(ctrl);
            e.Pointer.Capture(_list);
            e.Handled = true;
            return;
        }
        _collapseOnRelease = !ctrl && !shift && _chainSel.IsSelected(index) && _chainSel.SelectedRows.Count > 1;
        if (_collapseOnRelease) { _chainSel.SetFocused(index, Procs.Count); SyncChainFromHelper(); ShowSelected(); }
        else ClickChainRow(index, ctrl, shift);
        _pressRow = index;
        e.Handled = true;
    }

    private void OnChainMove(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        if (!_chainSel.IsBanding) return;
        var at = e.GetPosition(_list);
        UpdateChainBand(_press.Y, at.Y);
        e.Handled = true;
    }

    private void OnChainUp(object? sender, PointerReleasedEventArgs e)
    {
        var had = _chainSel.IsBanding;
        if (had) EndChainBand();
        else if (_collapseOnRelease && _pressRow >= 0) ClickChainRow(_pressRow);
        _pressRow = -1; _collapseOnRelease = false;
        if (e.Pointer.Captured == _list) e.Pointer.Capture(null);
        if (had) e.Handled = true;
    }

    private void CancelChainBand()
    {
        if (!_chainSel.IsBanding) { _pressRow = -1; _collapseOnRelease = false; return; }
        _chainSel.EndBand();
        _chainBand.IsVisible = false;
        _pressRow = -1; _collapseOnRelease = false;
    }

    private void UpdateChainBand(double y0, double y1)
    {
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var hits = new List<int>();
        for (var i = 0; i < Procs.Count; i++)
        {
            if (_list.ContainerFromIndex(i) is not Control container) continue;
            var origin = container.TranslatePoint(new Point(0, 0), _list);
            if (origin is null) continue;
            var cTop = origin.Value.Y;
            var cBottom = cTop + container.Bounds.Height;
            if (cTop < bottom && cBottom > top) hits.Add(i);
        }
        _chainSel.UpdateBand(hits);
        SyncChainFromHelper();
        ShowSelected();
        if (_chainLayer is not null)
        {
            var topInLayer = _list.TranslatePoint(new Point(0, top), _chainLayer)?.Y ?? top;
            Canvas.SetLeft(_chainBand, 0);
            Canvas.SetTop(_chainBand, topInLayer);
            _chainBand.Width = Math.Max(8, _chainLayer.Bounds.Width);
            _chainBand.Height = Math.Max(0, bottom - top);
            _chainBand.IsVisible = _chainBand.Height > 0;
        }
    }

    private void EndChainBand()
    {
        _chainSel.EndBand();
        _chainBand.IsVisible = false;
    }

    /// <summary>A click on a chain row for tests and pointer handling: alone selects it, Ctrl toggles, Shift extends from the anchor.</summary>
    internal void ClickChainRow(int index, bool ctrl = false, bool shift = false)
    {
        _chainSel.Click(index, Procs.Count, ctrl, shift);
        SyncChainFromHelper();
        ShowSelected();
    }

    /// <summary>Selects every chain row (Ctrl+A).</summary>
    internal void SelectAllChain()
    {
        _chainSel.SelectAll(Procs.Count);
        SyncChainFromHelper();
        ShowSelected();
    }

    /// <summary>Selects the chain rows a band between two list heights touches (tests).</summary>
    internal void SimulateChainBand(double y0, double y1, bool add = false)
    {
        _chainSel.BeginBand(add);
        var tops = new List<double>();
        var heights = new List<double>();
        for (var i = 0; i < Procs.Count; i++)
        {
            if (_list.ContainerFromIndex(i) is not Control container) continue;
            var origin = container.TranslatePoint(new Point(0, 0), _list);
            if (origin is null) continue;
            tops.Add(origin.Value.Y);
            heights.Add(container.Bounds.Height);
        }
        while (tops.Count < Procs.Count) { tops.Add(double.MaxValue); heights.Add(0); }
        _chainSel.UpdateBand(ChainSelection.BandHits(tops, heights, y0, y1));
        SyncChainFromHelper();
        ShowSelected();
        _chainSel.EndBand();
    }

    private void SyncChainFromHelper()
    {
        _syncing = true;
        try
        {
            _list.SelectedItems!.Clear();
            foreach (var i in _chainSel.SelectedRows)
                if (i >= 0 && i < Procs.Count) _list.SelectedItems!.Add(Procs[i]);
        }
        finally { _syncing = false; }
    }

    private void SyncChainFromList()
    {
        if (_syncing || _building) return;
        var items = _list.SelectedItems!.OfType<PluginMidiProcessor>().ToList();
        if (items.Count == 0)
        {
            if (_chainSel.SelectedRows.Count == 0) return;
            _chainSel.Clear();
            ShowSelected();
            return;
        }
        var indices = items.Select(p => Procs.IndexOf(p)).Where(i => i >= 0).OrderBy(i => i).ToList();
        if (indices.SequenceEqual(_chainSel.SelectedRows)) return;
        var selIndex = _list.SelectedIndex;
        int focus;
        if (indices.Contains(selIndex)) focus = selIndex;
        else if (indices.Contains(_chainSel.FocusedIndex)) focus = _chainSel.FocusedIndex;
        else focus = indices[0];
        _chainSel.Replace(indices, focus, Procs.Count);
        ShowSelected();
    }

    private void RemoveChainSelected()
    {
        var selected = _chainSel.SelectedRows.Select(i => i >= 0 && i < Procs.Count ? Procs[i] : null).Where(p => p is not null).Cast<PluginMidiProcessor>().ToList();
        if (selected.Count == 0)
        {
            if (Selected is not { } single) return;
            selected = new List<PluginMidiProcessor> { single };
        }
        var first = Procs.IndexOf(selected[0]);
        _edit(() => { foreach (var p in selected) Procs.Remove(p); });
        RefreshList(Math.Clamp(first, 0, Math.Max(0, Procs.Count - 1)));
    }

    private void MoveChain(int delta)
    {
        var selected = _chainSel.SelectedRows.ToList();
        if (selected.Count == 0)
        {
            if (Selected is not { } single) return;
            selected = new List<int> { Procs.IndexOf(single) };
        }
        if (MidiProcessingListPresenter.MoveMany(selected, _chainSel.FocusedIndex, delta, Procs.Count) is not { } moved) return;
        _edit(() =>
        {
            var ordered = moved.Order.Select(old => Procs[old]).ToList();
            Procs.Clear();
            Procs.AddRange(ordered);
        });
        RefreshListKeep(moved.Selected, moved.Focus);
    }

    /// <summary>Rebuilds the rows and selects <paramref name="select"/> (clamped into the list).</summary>
    private void RefreshList(int select)
    {
        _building = true;
        try
        {
            _list.ItemsSource = Procs.ToList();
            if (Procs.Count == 0) { _chainSel.Clear(); _syncing = true; try { _list.SelectedItems!.Clear(); _list.SelectedIndex = -1; } finally { _syncing = false; } }
            else
            {
                var clamped = ScoreEditorGeometry.ClampIndex(select, Procs.Count);
                _chainSel.Replace(new[] { clamped }, clamped, Procs.Count);
                SyncChainFromHelper();
            }
        }
        finally { _building = false; }
        ShowSelected();
        UpdateWatch();
    }

    private void RefreshListKeep(IReadOnlyList<int> selected, int focus)
    {
        _building = true;
        try
        {
            _list.ItemsSource = Procs.ToList();
            _chainSel.Replace(selected, focus, Procs.Count);
            SyncChainFromHelper();
        }
        finally { _building = false; }
        ShowSelected();
        UpdateWatch();
    }
}
