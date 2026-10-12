using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TabForge.Services;

namespace TabForge.Views.Fx;

// Owns: the FX chain list's rows as a selection: click, Ctrl+click, Shift+click, the rubber band from the empty area, the focused row
//   (the one the plug-in pane shows) and its mark; and the drag of the selected rows (the others open a gap where the lifted rows would land and the
//   lifted rows sit in it, a line marks the drop position, the container the rows would go into lights up, the list scrolls at its edges, Esc cancels). Reports a drop; changes no chain.
// Does not own: what a row shows and what a drop does to the chain (FxChainWindow, FxChainTreeActions), the chain model (Plugins).
// Tests: TestFxChainListSelection.
internal sealed class FxChainList
{
    private readonly ListBox _list;
    private readonly Canvas _layer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Rectangle _band = new() { StrokeThickness = 1, Visibility = Visibility.Collapsed, RadiusX = 2, RadiusY = 2 };
    private object? _focused;
    private int _anchor = -1;
    private bool _applying;

    // Press, band and drag state. Row tops and heights are read once when a gesture starts (content coordinates of the list).
    private int _pressRow = -1;
    private bool _collapseOnRelease;
    private Point _press;
    private bool _banding, _dragging;
    private HashSet<int> _bandBase = new();
    private double[] _tops = Array.Empty<double>(), _heights = Array.Empty<double>();
    private List<int> _moving = new();
    private int _insert = -1, _into = -1, _lineDepth = -1, _gapRow = -1;
    private readonly List<(int Depth, string? DropName)> _info = new();
    private readonly FxChainDropTarget _marks;
    private System.Windows.Media.Effects.DropShadowEffect? _shadow;

    /// <summary>The list with the rubber band layer over it.</summary>
    public Grid Root { get; } = new();
    /// <summary>The focused row changed (the plug-in pane follows it).</summary>
    public event Action? FocusChanged;
    /// <summary>The selected rows were dropped: the rows that moved (ascending), the row they go in front of (the row count: the end),
    /// and the container row they were dropped onto (-1: none).</summary>
    public Action<IReadOnlyList<int>, int, int>? Dropped { get; set; }
    /// <summary>True when the row may take the dragged rows inside it (a container that is not one of them).</summary>
    public Func<int, IReadOnlyList<int>, bool>? CanDropInto { get; set; }

    public FxChainList(ListBox list)
    {
        _list = list;
        _list.SelectionMode = SelectionMode.Extended;
        ScrollViewer.SetCanContentScroll(_list, false);   // pixel scrolling: every row is laid out, so rows slide and the band measures them
        _band.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        _band.SetResourceReference(Shape.FillProperty, "AccentSoftBrush");
        _band.Opacity = 0.6;
        _layer.Children.Add(_band);
        _marks = new FxChainDropTarget(_layer);
        Root.Children.Add(_list);
        Root.Children.Add(_layer);
        _list.SelectionChanged += OnNativeSelection;
        _list.PreviewMouseLeftButtonDown += OnDown;
        _list.PreviewMouseMove += OnMove;
        _list.PreviewMouseLeftButtonUp += (_, e) => { if (Release()) e.Handled = true; };
        _list.LostMouseCapture += (_, _) => Cancel();
    }

    public int Count => _list.Items.Count;
    public object? Focused => _focused;
    public int FocusedIndex => IndexOfTag(_focused);
    public int IndexOf(object? tag) => IndexOfTag(tag);
    public bool IsDragging => _dragging;
    private ListBoxItem Row(int i) => (ListBoxItem)_list.Items[i]!;
    private static Border Chrome(ListBoxItem row) => (Border)row.Content;
    public object? TagAt(int i) => i >= 0 && i < Count ? Row(i).Tag : null;
    public IReadOnlyList<int> SelectedRows => Enumerable.Range(0, Count).Where(i => Row(i).IsSelected).ToList();
    public IReadOnlyList<object> SelectedTags => SelectedRows.Select(i => Row(i).Tag).ToList();

    /// <summary>The rows an action on <paramref name="tag"/>'s row applies to: the whole selection when that row is selected, else it alone.</summary>
    public IReadOnlyList<object> TargetsOf(object tag) => IndexOfTag(tag) is var i and >= 0 && Row(i).IsSelected ? SelectedTags : new[] { tag };

    private int IndexOfTag(object? tag)
    {
        if (tag is null) return -1;
        for (var i = 0; i < Count; i++) if (ReferenceEquals(Row(i).Tag, tag)) return i;
        return -1;
    }

    public void Clear() { Cancel(); _info.Clear(); _applying = true; try { _list.Items.Clear(); } finally { _applying = false; } }

    /// <summary>Adds a row: its content sits in a frame that carries the focus mark and the lifted look. <paramref name="depth"/> is how many
    /// containers it sits in; <paramref name="dropName"/> is a container's name for "Drop into …" (null: a plug-in).</summary>
    public void Add(UIElement content, object tag, string toolTip, string name, int depth = 0, string? dropName = null)
    {
        _info.Add((depth, dropName));
        var chrome = new Border { Child = content, Padding = new Thickness(2, 3, 5, 3), BorderThickness = new Thickness(3, 1, 1, 1), CornerRadius = new CornerRadius(3), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent };
        var row = new ListBoxItem { Content = chrome, Tag = tag, ToolTip = toolTip, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 1) };
        System.Windows.Automation.AutomationProperties.SetName(row, name);
        _list.Items.Add(row);
    }

    /// <summary>After the rows were rebuilt: selects the rows of <paramref name="tags"/> that still exist and focuses <paramref name="focus"/>
    /// (alone, when it is not one of them). Raises no focus event: the caller shows the pane.</summary>
    public void Restore(IReadOnlyCollection<object> tags, int focus)
    {
        if (Count == 0) { _focused = null; _anchor = -1; return; }
        focus = Math.Clamp(focus, 0, Count - 1);
        var keep = Enumerable.Range(0, Count).Where(i => tags.Contains(Row(i).Tag, ReferenceEqualityComparer.Instance)).ToHashSet();
        if (!keep.Contains(focus)) keep = new HashSet<int> { focus };
        Apply(keep, focus, raise: false);
        _anchor = focus;
    }

    // ---------- selection ----------
    /// <summary>A click on a row: alone selects it, Ctrl toggles it, Shift selects from the anchor to it (Ctrl+Shift adds that range).</summary>
    public void Click(int index, bool ctrl = false, bool shift = false)
    {
        if (index < 0 || index >= Count) return;
        var set = ctrl ? SelectedRows.ToHashSet() : new HashSet<int>();
        if (shift && _anchor >= 0 && _anchor < Count) for (var i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) set.Add(i);
        else
        {
            if (!ctrl || !set.Remove(index)) set.Add(index);
            _anchor = index;
        }
        if (set.Count == 0) set.Add(index);   // the pane always shows a plug-in: the last selected row stays
        Apply(set, set.Contains(index) ? index : set.Contains(FocusedIndex) ? FocusedIndex : set.Min());
    }

    public void SelectAll() => Apply(Enumerable.Range(0, Count).ToHashSet(), Math.Max(0, FocusedIndex));

    /// <summary>Esc: cancels a drag or a band, else clears a multi-selection to the focused row. False when there was nothing to do.</summary>
    public bool HandleEscape()
    {
        if (_dragging || _banding) { Cancel(); return true; }
        if (SelectedRows.Count < 2) return false;
        Apply(new HashSet<int> { Math.Max(0, FocusedIndex) }, Math.Max(0, FocusedIndex));
        return true;
    }

    private void Apply(HashSet<int> set, int focus, bool raise = true)
    {
        if (Count == 0) return;
        _applying = true;
        try { for (var i = 0; i < Count; i++) if (Row(i).IsSelected != set.Contains(i)) Row(i).IsSelected = set.Contains(i); }
        finally { _applying = false; }
        SetFocused(focus, raise);
    }

    private void SetFocused(int index, bool raise)
    {
        var tag = TagAt(index);
        for (var i = 0; i < Count; i++)
        {
            // The focused row carries an accent bar on its left edge, also among other selected rows.
            if (i == index) Chrome(Row(i)).SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else Chrome(Row(i)).BorderBrush = Brushes.Transparent;
            Chrome(Row(i)).BorderThickness = new Thickness(3, 1, 1, 1);
        }
        if (ReferenceEquals(tag, _focused)) return;
        _focused = tag;
        if (raise) FocusChanged?.Invoke();
    }

    /// <summary>The list changed its own selection (arrow keys, Ctrl+A, a caller setting the index): the focus follows.</summary>
    private void OnNativeSelection(object sender, SelectionChangedEventArgs e)
    {
        if (_applying) return;
        var selected = SelectedRows;
        var added = e.AddedItems.Count == 1 ? _list.Items.IndexOf(e.AddedItems[0]) : -1;
        var focus = added >= 0 ? added : selected.Contains(FocusedIndex) ? FocusedIndex : selected.Count > 0 ? selected[0] : -1;
        if (selected.Count <= 1) _anchor = focus;
        SetFocused(focus, raise: true);
    }

    // ---------- pointer ----------
    private ScrollViewer? _scroll;
    private ScrollViewer? Scroll => _scroll ??= FindChild<ScrollViewer>(_list);
    private double Offset => Scroll?.VerticalOffset ?? 0;

    private static T? FindChild<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T hit) return hit;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private static T? Parent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T) node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    private void Measure()
    {
        _list.UpdateLayout();
        _tops = new double[Count]; _heights = new double[Count];
        for (var i = 0; i < Count; i++)
        {
            _tops[i] = Row(i).TranslatePoint(new Point(0, 0), _list).Y + Offset;
            _heights[i] = Row(i).ActualHeight + Row(i).Margin.Bottom;
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (Parent<ButtonBase>(source) is not null || Parent<ScrollBar>(source) is not null || Parent<TextBoxBase>(source) is not null) return;   // ticks, chevrons, the scroll bar and a name box keep their own clicks
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _press = e.GetPosition(_list);
        _press.Y += Offset;
        var row = Parent<ListBoxItem>(source);
        var index = row is null ? -1 : _list.Items.IndexOf(row);
        _list.Focus();
        if (index < 0)
        {
            BeginBand(ctrl);
            _list.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (e.ClickCount > 1) return;   // a double-click floats the plug-in window
        row!.Focus();
        // A press on a selected row keeps the selection (it may be the start of a drag); a plain click then selects it alone on release.
        _collapseOnRelease = !ctrl && !shift && row.IsSelected && SelectedRows.Count > 1;
        if (_collapseOnRelease) SetFocused(index, raise: true); else Click(index, ctrl, shift);
        _pressRow = index;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || (!_banding && _pressRow < 0)) return;
        var at = e.GetPosition(_list);
        // The list scrolls while the pointer is at its top or bottom edge.
        if (Scroll is { } scroll && (_banding || _dragging))
        {
            if (at.Y < 14 && scroll.VerticalOffset > 0) scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - 12));
            else if (at.Y > _list.ActualHeight - 14 && scroll.VerticalOffset < scroll.ScrollableHeight) scroll.ScrollToVerticalOffset(Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + 12));
        }
        var y = at.Y + Offset;
        if (_banding) { Band(_press.Y, y, at.X); return; }
        if (!_dragging)
        {
            if (Math.Abs(y - _press.Y) < 3 || !Row(_pressRow).IsSelected) return;
            BeginDrag();
            _list.CaptureMouse();
        }
        DragTo(y, at.X);
        e.Handled = true;
    }

    /// <summary>The button came up: ends the band, drops the dragged rows, or finishes a plain click. True when a gesture ended.</summary>
    private bool Release()
    {
        var had = _banding || _dragging;
        if (_banding) EndBand();
        else if (_dragging) EndDrag();
        else if (_collapseOnRelease && _pressRow >= 0) Click(_pressRow);
        _pressRow = -1; _collapseOnRelease = false;
        if (_list.IsMouseCaptured) _list.ReleaseMouseCapture();
        return had;
    }

    private void Cancel()
    {
        if (_banding) { _banding = false; _band.Visibility = Visibility.Collapsed; }
        if (_dragging) { _dragging = false; _insert = _into = _lineDepth = _gapRow = -1; ShowDrag(); }
        _pressRow = -1; _collapseOnRelease = false;
        if (_list.IsMouseCaptured) _list.ReleaseMouseCapture();
    }

    // ---------- rubber band ----------
    private void BeginBand(bool add)
    {
        Measure();
        _banding = true;
        _bandBase = add ? SelectedRows.ToHashSet() : new HashSet<int>();
    }

    /// <summary>Selects the rows the band between two heights touches (content coordinates), added to the rows it started with.</summary>
    private void Band(double y0, double y1, double x = 0)
    {
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var set = _bandBase.ToHashSet();
        for (var i = 0; i < Count; i++) if (_tops[i] < bottom && _tops[i] + _heights[i] > top) set.Add(i);
        if (set.Count > 0) Apply(set, set.Contains(FocusedIndex) ? FocusedIndex : set.Min());
        Canvas.SetLeft(_band, Math.Min(_press.X, x)); Canvas.SetTop(_band, top - Offset);
        _band.Width = Math.Abs(x - _press.X); _band.Height = bottom - top;
        _band.Visibility = Visibility.Visible;
    }

    private void EndBand() { _banding = false; _band.Visibility = Visibility.Collapsed; }

    // ---------- drag ----------
    private double _pointerY = double.NaN;   // the pointer in content coordinates while rows are lifted; NaN: the lifted rows sit in the gap (tests, captures)

    private void BeginDrag()
    {
        Measure();
        _moving = SelectedRows.ToList();
        _dragging = true;
        _insert = _into = _lineDepth = _gapRow = -1;
        foreach (var i in Enumerable.Range(0, Count)) Row(i).RenderTransform = new TranslateTransform();
    }

    /// <summary>The height of the lifted rows above a row: how far it moves up while they are out of the list.</summary>
    private double Above(int row) => _moving.Where(m => m < row).Sum(m => _heights[m]);
    /// <summary>Where a row shows while the lifted rows are out of the list and no gap is open (the row count: the end of the list).</summary>
    private double ClosedTop(int row) => Count == 0 ? 0 : (row < Count ? _tops[row] : _tops[^1] + _heights[^1]) - Above(row);

    /// <summary>Finds the drop for the pointer (content coordinates; FxChainDropTarget.At) and shows it.</summary>
    private void DragTo(double y, double x)
    {
        // The rows below the open gap show one lifted block lower than their place among the rows that stay: the pointer is read there.
        _pointerY = y;
        var lifted = _moving.Sum(i => _heights[i]);
        if (_gapRow >= 0 && ClosedTop(_gapRow) is var gap && y > gap) y = Math.Max(gap, y - lifted);
        var rows = new DropRow[Count];
        for (var i = 0; i < Count; i++) rows[i] = new DropRow(_tops[i] - Above(i), _heights[i], _info[i].Depth, CanDropInto?.Invoke(i, _moving) == true, _moving.Contains(i));
        (_insert, _into, _lineDepth) = FxChainDropTarget.At(rows, x, y);
        _gapRow = _into >= 0 && _lineDepth < 0 ? _into + 1 : _insert;   // onto a container: the gap opens right under its row
        ShowDrag();
    }

    /// <summary>The track list's drag look: lifted rows in the soft accent with an accent edge and a shadow, the others slide to open a gap.
    /// The lifted rows sit in that gap, where they would land, moved a little to the right: they never cover the row under the pointer.</summary>
    private void ShowDrag()
    {
        var lifted = _moving.Sum(i => _heights[i]);
        for (var i = 0; i < Count; i++)
        {
            var row = Row(i);
            var chrome = Chrome(row);
            if (row.RenderTransform is not TranslateTransform shift) continue;
            if (!_dragging)
            {
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                row.RenderTransform = Transform.Identity;
                Panel.SetZIndex(row, 0);
                row.Effect = null;
                chrome.Background = Brushes.Transparent;
                chrome.BorderBrush = Brushes.Transparent;
                if (_info[i].DropName is not null) FxChainRows.ShowOpen(chrome.Child, false);
                continue;
            }
            var moving = _moving.Contains(i);
            var to = _gapRow < 0 ? 0 : moving ? ClosedTop(_gapRow) + Above(i) - _tops[i] : -Above(i) + (i >= _gapRow ? lifted : 0);
            if (moving && _gapRow >= 0 && !double.IsNaN(_pointerY))
            {
                // The lifted block follows the pointer itself (no steps, no animation), hanging just under it so the row pointed at stays readable.
                var top = Math.Clamp(_pointerY + 10, 0, Math.Max(0, _tops[^1] + _heights[^1] - lifted));
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.Y = top + Above(i) - _tops[i];
            }
            else if (Math.Abs((double)shift.GetAnimationBaseValue(TranslateTransform.YProperty) - to) > 0.1)
            {
                shift.Y = to;
                var duration = UiMotion.DurationMilliseconds(105);
                if (duration <= 0) shift.BeginAnimation(TranslateTransform.YProperty, null);
                else shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
            }
            // Lit: the lifted rows, and the container the rows would go into (accent outline, tint, an open folder).
            var target = i == _into;
            if (moving)
            {
                shift.X = 18;
                Panel.SetZIndex(row, 1000);
                row.Effect = _shadow ??= new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 3, Direction = 270, Opacity = 0.6, Color = Colors.Black, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
            }
            if (moving || target) { chrome.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); chrome.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); }
            else
            {
                if (!ReferenceEquals(row.Tag, _focused)) chrome.BorderBrush = Brushes.Transparent;
                chrome.Background = Brushes.Transparent;
            }
            if (_info[i].DropName is not null) FxChainRows.ShowOpen(chrome.Child, target);
        }
        // Inside a container: the line starts at its plug-ins' indent and the container is lit. Outside: a full-width line and no container lit.
        var line = _dragging && _lineDepth >= 0;
        _marks.Line(line ? ClosedTop(_insert) - Offset : 0, line ? _lineDepth : -1, _list.ActualWidth);
        var into = _dragging && _into >= 0 ? _into : -1;
        _marks.Badge(into >= 0 ? FxChainRows.NameRight(Chrome(Row(into)).Child, _list) : -1, into >= 0 ? ClosedTop(into) - Offset : 0, into >= 0 ? _heights[into] : 0, _list.ActualWidth);
        if (!_dragging) SetFocused(FocusedIndex, raise: false);
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

    /// <summary>The rows in their order after the <paramref name="moving"/> rows (ascending) went together in front of row <paramref name="insert"/>.</summary>
    internal static List<T> Reorder<T>(IReadOnlyList<T> rows, IReadOnlyList<int> moving, int insert)
    {
        var result = new List<T>(rows.Count);
        for (var i = 0; i <= rows.Count; i++)
        {
            if (i == Math.Min(insert, rows.Count)) result.AddRange(moving.Select(m => rows[m]));
            if (i < rows.Count && !moving.Contains(i)) result.Add(rows[i]);
        }
        return result;
    }

    // ---------- test and capture seams (no pressed button) ----------
    internal void SimulateBand(double y0, double y1, bool add = false) { BeginBand(add); _press = new Point(4, y0); Band(y0, y1, 120); }
    internal void SimulateBandEnd() => EndBand();
    // Where a row shows: during a drag the rows that stay are closed up.
    internal double RowTop(int index) { if (!_dragging) Measure(); return _tops[index] - (_dragging ? Above(index) : 0); }
    internal double RowHeight(int index) => _heights[index];
    internal double RowCentre(int index) => RowTop(index) + _heights[index] / 2;
    internal double RowBottom(int index) => RowTop(index) + _heights[index];
    internal void SimulateDragStart(int index) { if (!Row(index).IsSelected) Click(index); _pressRow = index; _press = new Point(20, RowCentre(index)); BeginDrag(); }
    /// <summary>A pointer among the rows as they show closed up (RowTop), at the left edge by default: where an open container ends, that is behind it.</summary>
    internal void SimulateDragMove(double y, double x = 0) { _gapRow = -1; DragTo(y, x); }
    /// <summary>The insertion line's depth (-1: none) and left edge, and the badge's text.</summary>
    internal (int Depth, double Left, string Badge) DropMarks => (_lineDepth, _marks.LineLeft, _marks.BadgeText);
    internal void SimulateDragEnd() => EndDrag();
    internal (int Insert, int Into) DragTarget => (_insert, _into);
}
