using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the FX chain list's rows as a selection: click, Ctrl+click, Shift+click, Ctrl+A, the arrow keys, the rubber band from the empty area, the focused
//   row (the one the plug-in pane shows) and its mark; and the drag of the selected rows (the others open a gap where the lifted rows would land and the
//   lifted rows sit in it, a line marks the drop position, the container the rows would go into lights up, the list scrolls at its edges, Esc cancels).
//   Reports a drop and a double-click; changes no chain.
// Does not own: what a row shows (FxChainRows), what a drop does to the chain (FxChainContainers), the drop rules (FxChainDropMath), the chain model.
// Tests: TabForge.Avalonia.Tests.FxChainListTests.
// Threads: UI thread only. No timers: nothing moves unless the pointer does.
public sealed class FxChainList : Grid
{
    /// <summary>The pointer must move this far (DIPs) from the press before a press on a selected row starts a drag.</summary>
    public const double DragStartDistance = 3;
    /// <summary>The list scrolls while the pointer is within this distance (DIPs) of its top or bottom edge during a drag or a band.</summary>
    public const double EdgeScrollZone = 14, EdgeScrollStep = 12;
    /// <summary>The lifted rows sit this far (DIPs) right of the gap and this far under the pointer.</summary>
    public const double LiftedOffsetX = 18, LiftedBelowPointer = 10;
    private const double FocusBarWidth = 3, ItemGap = 1, BandOpacity = 0.6, LineHeight = 2, LiveRowCornerRadius = 3, LineRightMargin = 22, LineLeftMargin = 4;
    private const double LiftedOpacity = 0.9, BadgeRightMargin = 24, BadgeGap = 6, BadgeMinRoom = 24, BadgePaddingX = 6, BadgePaddingY = 1, BadgeRadius = 8;
    private const int LiftedZ = 1000;
    private const string DropInsideText = "Drop inside";

    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Name = "ChainScroll" };
    private readonly StackPanel _rows = new() { Name = "ChainRows" };
    private readonly Canvas _layer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Border _band = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), IsVisible = false, Opacity = BandOpacity };
    private readonly Border _line = new() { Height = LineHeight, CornerRadius = new CornerRadius(1), IsVisible = false };
    private readonly TextBlock _badgeText = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Text = DropInsideText };
    private readonly Border _badge;
    private readonly List<(Border Chrome, object Tag, int Depth, string? DropName)> _items = new();
    private readonly HashSet<int> _selected = new();
    private object? _focused;
    private int _anchor = -1;
    private bool _applying;

    private int _pressRow = -1;
    private bool _collapseOnRelease, _banding, _dragging;
    private Point _press;
    private HashSet<int> _bandBase = new();
    private double[] _tops = Array.Empty<double>(), _heights = Array.Empty<double>();
    private List<int> _moving = new();
    private int _insert = -1, _into = -1, _lineDepth = -1, _gapRow = -1;
    private double _pointerY = double.NaN;

    /// <summary>The focused row changed (the plug-in pane follows it).</summary>
    public event Action? FocusChanged;
    /// <summary>A row was double-clicked (the plug-in's own window floats).</summary>
    public event Action<object>? RowActivated;
    /// <summary>The selected rows were dropped: the rows that moved (ascending), the row they go in front of (the row count: the end), and the container row they were dropped onto (-1: none).</summary>
    public Action<IReadOnlyList<int>, int, int>? Dropped { get; set; }
    /// <summary>True when the row may take the dragged rows inside it (a container that is not one of them).</summary>
    public Func<int, IReadOnlyList<int>, bool>? CanDropInto { get; set; }

    public FxChainList()
    {
        Name = "ChainList";
        Focusable = true;
        _scroll.Content = _rows;
        _band[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        _band[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
        _line[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        _badgeText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextStrongBrush");
        _badge = new Border
        {
            Child = _badgeText, Padding = new Thickness(BadgePaddingX, BadgePaddingY), CornerRadius = new CornerRadius(BadgeRadius), BorderThickness = new Thickness(1), IsVisible = false,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush"), [!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush"),
        };
        _layer.Children.Add(_band);
        _layer.Children.Add(_line);
        _layer.Children.Add(_badge);
        Children.Add(_scroll);
        Children.Add(_layer);
        AddHandler(PointerPressedEvent, OnDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnUp, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => Cancel();
        KeyDown += OnKey;
    }

    public int Count => _items.Count;
    public object? Focused => _focused;
    public int FocusedIndex => IndexOf(_focused);
    public bool IsDragging => _dragging;
    public bool IsBanding => _banding;
    public object? TagAt(int i) => i >= 0 && i < Count ? _items[i].Tag : null;
    public IReadOnlyList<int> SelectedRows => _selected.OrderBy(i => i).ToList();
    public IReadOnlyList<object> SelectedTags => SelectedRows.Select(i => _items[i].Tag).ToList();
    public Control RowAt(int i) => _items[i].Chrome;
    public ScrollViewer Scroller => _scroll;

    public int IndexOf(object? tag)
    {
        if (tag is null) return -1;
        for (var i = 0; i < Count; i++) if (ReferenceEquals(_items[i].Tag, tag)) return i;
        return -1;
    }

    /// <summary>The rows an action on <paramref name="tag"/>'s row applies to: the whole selection when that row is selected, else it alone.</summary>
    public IReadOnlyList<object> TargetsOf(object tag) => IndexOf(tag) is var i and >= 0 && _selected.Contains(i) ? SelectedTags : new[] { tag };

    public void Clear()
    {
        Cancel();
        _applying = true;
        try { _rows.Children.Clear(); _items.Clear(); _selected.Clear(); }
        finally { _applying = false; }
    }

    /// <summary>Adds a row: its content sits in a frame that carries the focus mark and the lifted look. <paramref name="depth"/> is how many
    /// containers it sits in; <paramref name="dropName"/> is a container's name for "Drop into …" (null: a plug-in).</summary>
    public void Add(Control content, object tag, string toolTip, string name, int depth = 0, string? dropName = null)
    {
        var chrome = new Border
        {
            Child = content, Padding = new Thickness(2, 3, 5, 3), BorderThickness = new Thickness(FocusBarWidth, 1, 1, 1), CornerRadius = new CornerRadius(LiveRowCornerRadius),
            BorderBrush = AvaloniaPalette.TransparentBrush, Background = AvaloniaPalette.TransparentBrush, Margin = new Thickness(0, 0, 0, ItemGap), Tag = tag,
            RenderTransform = new TranslateTransform(),
        };
        ToolTip.SetTip(chrome, toolTip);
        global::Avalonia.Automation.AutomationProperties.SetName(chrome, name);
        _items.Add((chrome, tag, depth, dropName));
        _rows.Children.Add(chrome);
    }

    /// <summary>After the rows were rebuilt: selects the rows of <paramref name="tags"/> that still exist and focuses row <paramref name="focus"/>
    /// (alone, when it is not one of them). Raises no focus event: the caller shows the pane.</summary>
    public void Restore(IReadOnlyCollection<object> tags, int focus)
    {
        if (Count == 0) { _focused = null; _anchor = -1; return; }
        focus = Math.Clamp(focus, 0, Count - 1);
        var keep = Enumerable.Range(0, Count).Where(i => tags.Contains(_items[i].Tag, ReferenceEqualityComparer.Instance)).ToHashSet();
        if (!keep.Contains(focus)) keep = new HashSet<int> { focus };
        Apply(keep, focus, raise: false);
        _anchor = focus;
    }

    // ---------- selection ----------

    /// <summary>A click on a row: alone selects it, Ctrl toggles it, Shift selects from the anchor to it (Ctrl+Shift adds that range).</summary>
    public void Click(int index, bool ctrl = false, bool shift = false)
    {
        if (index < 0 || index >= Count) return;
        var set = ctrl ? _selected.ToHashSet() : new HashSet<int>();
        if (shift && _anchor >= 0 && _anchor < Count) for (var i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) set.Add(i);
        else
        {
            if (!ctrl || !set.Remove(index)) set.Add(index);
            _anchor = index;
        }
        if (set.Count == 0) set.Add(index);   // the pane always shows a row: the last selected row stays
        Apply(set, set.Contains(index) ? index : set.Contains(FocusedIndex) ? FocusedIndex : set.Min());
    }

    public void SelectAll() => Apply(Enumerable.Range(0, Count).ToHashSet(), Math.Max(0, FocusedIndex));

    /// <summary>Esc: cancels a drag or a band, else clears a multi-selection to the focused row. False when there was nothing to do.</summary>
    public bool HandleEscape()
    {
        if (_dragging || _banding) { Cancel(); return true; }
        if (_selected.Count < 2) return false;
        Apply(new HashSet<int> { Math.Max(0, FocusedIndex) }, Math.Max(0, FocusedIndex));
        return true;
    }

    private void Apply(HashSet<int> set, int focus, bool raise = true)
    {
        if (Count == 0) return;
        _applying = true;
        try
        {
            _selected.Clear();
            foreach (var i in set) _selected.Add(i);
            for (var i = 0; i < Count; i++) PaintRow(i);
        }
        finally { _applying = false; }
        SetFocused(focus, raise);
    }

    private void SetFocused(int index, bool raise)
    {
        var tag = TagAt(index);
        for (var i = 0; i < Count; i++) PaintRow(i, focusedIndex: index);
        if (ReferenceEquals(tag, _focused)) return;
        _focused = tag;
        if (raise && !_applying) FocusChanged?.Invoke();
    }

    // A selected row takes the soft accent fill; the focused row an accent bar on its left edge, also among other selected rows.
    private void PaintRow(int i, int? focusedIndex = null)
    {
        var chrome = _items[i].Chrome;
        var focused = i == (focusedIndex ?? FocusedIndex);
        if (focused) chrome[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        else { chrome.ClearValue(Border.BorderBrushProperty); chrome.BorderBrush = AvaloniaPalette.TransparentBrush; }   // a bound brush is dropped first
        if (_selected.Contains(i)) chrome[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
        else { chrome.ClearValue(Border.BackgroundProperty); chrome.Background = AvaloniaPalette.TransparentBrush; }
    }

    // ---------- keys ----------

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.A when ctrl: SelectAll(); e.Handled = true; break;
            case Key.Up: MoveFocus(-1, shift); e.Handled = true; break;
            case Key.Down: MoveFocus(1, shift); e.Handled = true; break;
            case Key.Home: Move(0, shift); e.Handled = true; break;
            case Key.End: Move(Count - 1, shift); e.Handled = true; break;
        }
    }

    private void MoveFocus(int step, bool extend) => Move(Math.Clamp(Math.Max(0, FocusedIndex) + step, 0, Math.Max(0, Count - 1)), extend);

    private void Move(int index, bool extend)
    {
        if (Count == 0) return;
        Click(index, ctrl: false, shift: extend);
        _scroll.Offset = new Vector(0, Math.Clamp(ScrollToShow(index), 0, ScrollMax));
    }

    private double ScrollMax => Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);

    // The offset that brings row index into view with the least movement.
    private double ScrollToShow(int index)
    {
        var chrome = _items[index].Chrome;
        var top = chrome.Bounds.Top;
        var bottom = chrome.Bounds.Bottom;
        if (top < _scroll.Offset.Y) return top;
        if (bottom > _scroll.Offset.Y + _scroll.Viewport.Height) return bottom - _scroll.Viewport.Height;
        return _scroll.Offset.Y;
    }

    // ---------- pointer ----------

    private double Offset => _scroll.Offset.Y;

    private void Measure()
    {
        _rows.UpdateLayout();
        _tops = new double[Count]; _heights = new double[Count];
        for (var i = 0; i < Count; i++)
        {
            _tops[i] = _items[i].Chrome.Bounds.Top;
            _heights[i] = _items[i].Chrome.Bounds.Height + ItemGap;
        }
    }

    private static bool KeepsOwnClick(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
            if (node is Button or TextBox or ScrollBar or ToggleButton) return true;
        return false;
    }

    private int RowOf(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
            if (node is Border { Tag: { } tag } && IndexOf(tag) is var index and >= 0) return index;
        return -1;
    }

    private void OnDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || KeepsOwnClick(e.Source)) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _press = e.GetPosition(_rows);
        var index = RowOf(e.Source);
        Focus();
        if (index < 0)
        {
            BeginBand(ctrl);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (e.ClickCount > 1) { RowActivated?.Invoke(_items[index].Tag); e.Handled = true; return; }
        // A press on a selected row keeps the selection (it may be the start of a drag); a plain click then selects it alone on release.
        _collapseOnRelease = !ctrl && !shift && _selected.Contains(index) && _selected.Count > 1;
        if (_collapseOnRelease) SetFocused(index, raise: true); else Click(index, ctrl, shift);
        _pressRow = index;
        e.Handled = true;
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || (!_banding && _pressRow < 0)) return;
        var viewport = e.GetPosition(_scroll);
        if (_banding || _dragging) ScrollAtEdges(viewport.Y);
        var at = e.GetPosition(_rows);
        if (_banding) { Band(_press.Y, at.Y, at.X); return; }
        if (!_dragging)
        {
            if (Math.Abs(at.Y - _press.Y) < DragStartDistance || !_selected.Contains(_pressRow)) return;
            BeginDrag();
            e.Pointer.Capture(this);
        }
        DragTo(at.Y, at.X);
        e.Handled = true;
    }

    private void ScrollAtEdges(double viewportY)
    {
        if (viewportY < EdgeScrollZone && Offset > 0) _scroll.Offset = new Vector(0, Math.Max(0, Offset - EdgeScrollStep));
        else if (viewportY > _scroll.Viewport.Height - EdgeScrollZone && Offset < ScrollMax) _scroll.Offset = new Vector(0, Math.Min(ScrollMax, Offset + EdgeScrollStep));
    }

    private void OnUp(object? sender, PointerReleasedEventArgs e)
    {
        var had = _banding || _dragging;
        if (_banding) EndBand();
        else if (_dragging) EndDrag();
        else if (_collapseOnRelease && _pressRow >= 0) Click(_pressRow);
        _pressRow = -1; _collapseOnRelease = false;
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        if (had) e.Handled = true;
    }

    /// <summary>Ends any band or drag without applying it (Esc, a lost capture, a rebuild of the rows).</summary>
    private void Cancel()
    {
        if (_banding) { _banding = false; _band.IsVisible = false; }
        if (_dragging) { _dragging = false; _insert = _into = _lineDepth = _gapRow = -1; ShowDrag(); }
        _pressRow = -1; _collapseOnRelease = false;
    }

    // ---------- rubber band ----------

    private void BeginBand(bool add)
    {
        Measure();
        _banding = true;
        _bandBase = add ? _selected.ToHashSet() : new HashSet<int>();
    }

    /// <summary>Selects the rows the band between two heights touches (content coordinates), added to the rows it started with.</summary>
    private void Band(double y0, double y1, double x)
    {
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var set = _bandBase.ToHashSet();
        for (var i = 0; i < Count; i++) if (_tops[i] < bottom && _tops[i] + _heights[i] > top) set.Add(i);
        if (set.Count > 0) Apply(set, set.Contains(FocusedIndex) ? FocusedIndex : set.Min());
        Canvas.SetLeft(_band, Math.Min(_press.X, x)); Canvas.SetTop(_band, top - Offset);
        _band.Width = Math.Abs(x - _press.X); _band.Height = bottom - top;
        _band.IsVisible = true;
    }

    private void EndBand() { _banding = false; _band.IsVisible = false; }

    // ---------- drag ----------

    private void BeginDrag()
    {
        Measure();
        _moving = SelectedRows.ToList();
        _dragging = true;
        _insert = _into = _lineDepth = _gapRow = -1;
    }

    /// <summary>The height of the lifted rows above a row: how far it moves up while they are out of the list.</summary>
    private double Above(int row) => _moving.Where(m => m < row).Sum(m => _heights[m]);

    /// <summary>Where a row shows while the lifted rows are out of the list and no gap is open (the row count: the end of the list).</summary>
    private double ClosedTop(int row) => Count == 0 ? 0 : (row < Count ? _tops[row] : _tops[^1] + _heights[^1]) - Above(row);

    private void DragTo(double y, double x)
    {
        // The rows below the open gap show one lifted block lower than their place among the rows that stay: the pointer is read there.
        _pointerY = y;
        var lifted = _moving.Sum(i => _heights[i]);
        if (_gapRow >= 0 && ClosedTop(_gapRow) is var gap && y > gap) y = Math.Max(gap, y - lifted);
        var rows = new DropRow[Count];
        for (var i = 0; i < Count; i++) rows[i] = new DropRow(_tops[i] - Above(i), _heights[i], _items[i].Depth, CanDropInto?.Invoke(i, _moving) == true, _moving.Contains(i));
        var target = FxChainDropMath.At(rows, x, y);
        (_insert, _into, _lineDepth) = (target.Insert, target.Into, target.LineDepth);
        _gapRow = _into >= 0 && _lineDepth < 0 ? _into + 1 : _insert;   // onto a container: the gap opens right under its row
        ShowDrag();
    }

    // The lifted rows sit in the gap where they would land, a little to the right and under the pointer; the others slide to open it.
    private void ShowDrag()
    {
        var lifted = _moving.Sum(i => _heights[i]);
        for (var i = 0; i < Count; i++)
        {
            var chrome = _items[i].Chrome;
            if (chrome.RenderTransform is not TranslateTransform shift) continue;
            if (!_dragging)
            {
                shift.X = 0; shift.Y = 0;
                chrome.ZIndex = 0;
                chrome.Opacity = 1;
                PaintRow(i);
                if (_items[i].DropName is not null) FxChainRows.ShowOpen(chrome.Child, false);
                continue;
            }
            var moving = _moving.Contains(i);
            var to = _gapRow < 0 ? 0 : moving ? ClosedTop(_gapRow) + Above(i) - _tops[i] : -Above(i) + (i >= _gapRow ? lifted : 0);
            if (moving && _gapRow >= 0 && !double.IsNaN(_pointerY))
            {
                var top = Math.Clamp(_pointerY + LiftedBelowPointer, 0, Math.Max(0, _tops[^1] + _heights[^1] - lifted));
                to = top + Above(i) - _tops[i];
            }
            shift.Y = to;
            shift.X = moving ? LiftedOffsetX : 0;
            var target = i == _into;
            if (moving) { chrome.ZIndex = LiftedZ; chrome.Opacity = LiftedOpacity; }
            if (moving || target)
            {
                chrome[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
                chrome[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
            }
            else PaintRow(i);
            if (_items[i].DropName is not null) FxChainRows.ShowOpen(chrome.Child, target);
        }
        // Inside a container: the line starts at its plug-ins' indent and the container is lit. Outside: a full-width line and no container lit.
        var line = _dragging && _lineDepth >= 0;
        ShowLine(line ? ClosedTop(_insert) - Offset : 0, line ? _lineDepth : -1);
        var into = _dragging && _into >= 0 ? _into : -1;
        ShowBadge(into >= 0, into >= 0 ? ClosedTop(into) - Offset : 0, into >= 0 ? _heights[into] : 0);
    }

    private void ShowLine(double y, int depth)
    {
        _line.IsVisible = depth >= 0;
        if (depth < 0) return;
        var left = LineLeftMargin + depth * FxChainDropMath.Indent;
        Canvas.SetLeft(_line, left); Canvas.SetTop(_line, y - LineHeight / 2);
        _line.Width = Math.Max(8, Bounds.Width - left - LineRightMargin);
    }

    // "Drop inside", right-aligned in the container row, never wider than the room behind the row's name.
    private void ShowBadge(bool show, double rowTop, double rowHeight)
    {
        var width = Bounds.Width;
        var nameRight = show ? FxChainRows.NameRight(_items[_into].Chrome.Child, this) : -1;
        var room = width - BadgeRightMargin - nameRight - BadgeGap;
        _badge.IsVisible = show && room >= BadgeMinRoom;
        if (!_badge.IsVisible) return;
        _badge.MaxWidth = room;
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_badge, width - _badge.DesiredSize.Width - BadgeRightMargin);
        Canvas.SetTop(_badge, rowTop + Math.Max(0, (rowHeight - _badge.DesiredSize.Height) / 2));
    }

    private void EndDrag()
    {
        var (moving, insert, into) = (_moving, _insert, _into);
        _dragging = false;
        _pointerY = double.NaN;
        _insert = _into = _lineDepth = _gapRow = -1;
        _pressRow = -1;
        ShowDrag();
        if (insert >= 0) Dropped?.Invoke(moving, insert, into);
    }

    // ---------- test and capture seams (no pressed button) ----------

    internal void SimulateBand(double y0, double y1, bool add = false) { BeginBand(add); _press = new Point(4, y0); Band(y0, y1, 120); }
    internal void SimulateBandEnd() => EndBand();
    /// <summary>Where a row shows: during a drag the rows that stay are closed up.</summary>
    internal double RowTop(int index) { if (!_dragging) Measure(); return _tops[index] - (_dragging ? Above(index) : 0); }
    internal double RowHeight(int index) => _heights[index];
    internal double RowCentre(int index) => RowTop(index) + _heights[index] / 2;
    internal void SimulateDragStart(int index) { if (!_selected.Contains(index)) Click(index); _pressRow = index; _press = new Point(20, RowCentre(index)); BeginDrag(); }
    internal void SimulateDragMove(double y, double x = 0) { _gapRow = -1; DragTo(y, x); }
    /// <summary>The insertion line's depth (-1: none) and left edge, and the badge's text ("" while hidden).</summary>
    internal (int Depth, double Left, string Badge) DropMarks => (_lineDepth, _line.IsVisible ? Canvas.GetLeft(_line) : -1, _badge.IsVisible ? _badgeText.Text ?? "" : "");
    internal void SimulateDragEnd() => EndDrag();
    internal (int Insert, int Into) DragTarget => (_insert, _into);
}
