using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TabForge.Views;

/// <summary>What the track-column layout needs from the arrangement panel that hosts it (implemented by <see cref="ArrangementPanel"/>).</summary>
internal interface ITrackColumnHost
{
    /// <summary>Width of the whole track-controls area (the panel's first column).</summary>
    double ControlsAreaWidth { get; set; }
    void RebuildControls();
    ContextMenu BuildColumnHeaderMenu();
    object? TryFindResource(object key);
    void RaiseColumnLayoutChanged();
}

// Owns: the track-control columns: order, widths, hidden columns, the header strip, the per-row grids and cells
// placed in columns, and the column drag / resize gestures.
// Does not own: the cells themselves (ArrangementPanel.TrackRows / Groups build them), saving the layout (MainWindow via ColumnState).
// Tests: TestTrackListFit, TestTrackRowsEndFlush, TestAddTrackLane.
internal sealed class TrackColumnLayout
{
    // "name" is the flexible column; every other column has a stored width.
    public static readonly string[] DefaultOrder = { "settings", "colour", "number", "name", "fx", "mute", "solo", "volume", "pan", "instrument" };
    private static readonly Dictionary<string, (string label, double width, double min, double max)> ColumnSpecs = WithHeaderMinimums(new()
    {
        ["colour"] = ("●", 22, 18, 40), ["settings"] = ("⚙", 24, 20, 40), ["number"] = ("#", 28, 22, 50),
        ["name"] = ("TRACK", 0, 0, 0), ["fx"] = ("FX", 56, 50, 80), ["mute"] = ("M", 26, 22, 44), ["solo"] = ("S", 26, 22, 44),
        ["volume"] = ("VOLUME", 82, 40, 240), ["pan"] = ("PAN", 82, 28, 240), ["instrument"] = ("INSTRUMENT", 132, 80, 280)
    });

    /// <summary>Header label font size (DIPs); the minimum column widths are measured at it.</summary>
    private const double HeaderFontSize = Services.ThemeService.MinFontSize * 1.2;
    private const double HeaderCellPadding = 8;   // cell margin + border + a little air, both sides
    public const double RowGridLeft = 6, RowGridRight = 10;

    /// <summary>Width (DIPs) a column's header label needs at the header font, so no header ever trims.</summary>
    public static double HeaderNeed(string label)
    {
        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), HeaderFontSize, Brushes.Black, 1.0);
        return Math.Ceiling(text.WidthIncludingTrailingWhitespace + HeaderCellPadding);
    }

    private static Dictionary<string, (string label, double width, double min, double max)> WithHeaderMinimums(
        Dictionary<string, (string label, double width, double min, double max)> specs)
    {
        foreach (var id in specs.Keys.ToList())
        {
            if (id == "name") continue;
            var (label, width, min, max) = specs[id];
            min = Math.Max(min, HeaderNeed(label));
            specs[id] = (label, Math.Max(width, min), min, Math.Max(max, min));
        }
        return specs;
    }

    /// <summary>Each column's minimum width and the width its header needs (self-test: minimum >= need).</summary>
    public static IEnumerable<(string id, double min, double need)> MinimumsForTest() =>
        ColumnSpecs.Where(p => p.Key != "name").Select(p => (p.Key, p.Value.min, HeaderNeed(p.Value.label)));

    private readonly ITrackColumnHost _host;
    private List<string> _columnOrder = DefaultOrder.ToList();
    private readonly Dictionary<string, double> _columnWidths = ColumnSpecs.ToDictionary(p => p.Key, p => p.Value.width);
    private readonly HashSet<string> _hiddenColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Grid> _rowGrids = new();

    // Column drag state. Only the header animates during a drag (cheap transforms); the track rows
    // are rebuilt once, when the column is dropped.
    private readonly List<Border> _headerCells = new();
    private readonly List<TranslateTransform> _columnTransforms = new();
    private readonly List<List<Border>> _columnRowCells = new();
    private readonly List<UIElement> _rowSeparators = new();
    private readonly List<UIElement> _headerDecor = new();
    private int _colDragFrom = -1, _colDragTarget = -1;
    private double _colDragStartX;
    private bool _colDragArmed, _colDragActive;
    private double[] _colDragLefts = Array.Empty<double>(), _colDragWidths = Array.Empty<double>();
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(150));
    private static readonly IEasingFunction SlideEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    public TrackColumnLayout(ITrackColumnHost host) => _host = host;

    /// <summary>The header strip (null until built).</summary>
    public Grid? Header { get; private set; }

    /// <summary>Column order, widths and the whole controls-area width, for saving in settings.</summary>
    public (List<string> order, Dictionary<string, double> widths, double area) State
    {
        get => (_columnOrder.ToList(), new Dictionary<string, double>(_columnWidths), _host.ControlsAreaWidth);
        set
        {
            // Layouts saved before the FX column existed: put it just before Mute.
            if (value.order is { } saved && !saved.Contains("fx") && saved.Count == DefaultOrder.Length - 1)
            {
                saved = saved.ToList();
                saved.Insert(Math.Max(0, saved.IndexOf("mute")), "fx");
                value = (saved, value.widths, value.area);
            }
            if (value.order is { Count: > 0 } order && order.Count == DefaultOrder.Length &&
                DefaultOrder.All(order.Contains))
                _columnOrder = order.ToList();
            if (value.widths is not null)
                foreach (var (id, width) in value.widths)
                    if (ColumnSpecs.TryGetValue(id, out var spec) && id != "name" && double.IsFinite(width))
                        _columnWidths[id] = Math.Clamp(width, spec.min, spec.max);
            if (double.IsFinite(value.area) && value.area > 0)
                _host.ControlsAreaWidth = Math.Clamp(value.area, 420, 900);
            RebuildHeader();
            _host.RebuildControls();
        }
    }

    public void Reset(double areaWidth)
    {
        _columnOrder = DefaultOrder.ToList();
        foreach (var (id, spec) in ColumnSpecs) _columnWidths[id] = spec.width;
        _host.ControlsAreaWidth = areaWidth;
        RebuildHeader();
        _host.RebuildControls();
        _host.RaiseColumnLayoutChanged();
    }

    private GridLength ColumnWidth(string id) =>
        id == "name" ? new GridLength(1, GridUnitType.Star) : new GridLength(_hiddenColumns.Contains(id) ? 0 : _columnWidths[id]);

    /// <summary>Track columns the user hid (Mixer: "Show volume / pan in the track list"). They keep their place.</summary>
    public IReadOnlyCollection<string> HiddenColumns
    {
        get => _hiddenColumns;
        set
        {
            var next = new HashSet<string>(value.Where(id => id is "volume" or "pan"), StringComparer.OrdinalIgnoreCase);
            if (next.SetEquals(_hiddenColumns)) return;
            _hiddenColumns.Clear();
            _hiddenColumns.UnionWith(next);
            RebuildHeader();
            _host.RebuildControls();
        }
    }

    /// <summary>Forgets the row grids and cells before the track rows are rebuilt.</summary>
    public void ClearRows()
    {
        _rowGrids.Clear();
        _columnRowCells.Clear();
        _rowSeparators.Clear();
    }

    /// <summary>Gives a row grid the current columns; the grid then resizes with the header.</summary>
    public void AddRowGrid(Grid grid)
    {
        AddColumnDefinitions(grid);
        _rowGrids.Add(grid);
    }

    private void AddColumnDefinitions(Grid grid)
    {
        foreach (var id in _columnOrder)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = ColumnWidth(id), MinWidth = id == "name" ? 40 : 0 });
    }

    /// <summary>Puts each row element in its column and draws faint separators between columns.</summary>
    public void PlaceCells(Grid grid, Dictionary<string, FrameworkElement> cells)
    {
        for (var c = 0; c < _columnOrder.Count; c++)
        {
            if (cells.TryGetValue(_columnOrder[c], out var element) && !_hiddenColumns.Contains(_columnOrder[c]))
            {
                // Each cell shares its column's transform with the header label, so dragging a column
                // slides every track's cell with one animation.
                var wrap = new Border
                {
                    Child = element, Background = Brushes.Transparent,
                    RenderTransform = c < _columnTransforms.Count ? _columnTransforms[c] : Transform.Identity
                };
                Grid.SetColumn(wrap, c);
                grid.Children.Add(wrap);
                while (_columnRowCells.Count <= c) _columnRowCells.Add(new List<Border>());
                _columnRowCells[c].Add(wrap);
            }
            if (c < _columnOrder.Count - 1 && !_hiddenColumns.Contains(_columnOrder[c]))
            {
                var line = ColumnSeparatorLine(c);
                grid.Children.Add(line);
                _rowSeparators.Add(line);
            }
        }
    }

    private static Border ColumnSeparatorLine(int column)
    {
        var line = new Border
        {
            Width = 1, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 5),
            Opacity = 0.22, IsHitTestVisible = false
        };
        line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        Grid.SetColumn(line, column);
        return line;
    }

    /// <summary>Resizes every row live; no rebuild, so playback and scrolling are untouched.</summary>
    private void ApplyColumnWidths()
    {
        foreach (var grid in _rowGrids.Append(Header).OfType<Grid>())
            for (var c = 0; c < _columnOrder.Count && c < grid.ColumnDefinitions.Count; c++)
                grid.ColumnDefinitions[c].Width = ColumnWidth(_columnOrder[c]);
    }

    /// <summary>
    /// The slim label strip above the tracks: drag a label to move that column, drag a label's
    /// right edge to resize it, right-click to reset.
    /// </summary>
    public UIElement BuildHeader()
    {
        var header = Header = new Grid { Height = ArrangementPanel.ColumnHeaderHeight, Margin = new Thickness(RowGridLeft, 0, RowGridRight, 0), Background = Brushes.Transparent };
        RebuildHeader();
        header.ContextMenuOpening += (_, _) => header.ContextMenu = _host.BuildColumnHeaderMenu();
        header.ContextMenu = _host.BuildColumnHeaderMenu();   // rebuilt on each opening (checked state)
        return header;
    }

    public void RebuildHeader()
    {
        if (Header is not { } header) return;
        header.Children.Clear();
        header.ColumnDefinitions.Clear();
        _headerCells.Clear();
        _headerDecor.Clear();
        _columnTransforms.Clear();
        foreach (var _ in _columnOrder) _columnTransforms.Add(new TranslateTransform());
        AddColumnDefinitions(header);
        for (var c = 0; c < _columnOrder.Count; c++)
        {
            var id = _columnOrder[c];
            var index = c;
            var label = new TextBlock
            {
                Text = ColumnSpecs[id].label, FontSize = HeaderFontSize, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = id == "name" ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                Margin = new Thickness(id == "name" ? 4 : 0, 0, 0, 0), IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
            var cell = new Border
            {
                Background = Brushes.Transparent, Child = label, CornerRadius = new CornerRadius(3), Margin = new Thickness(1, 1, 1, 1),
                RenderTransform = _columnTransforms[c], BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
                ToolTip = "Press and drag to move this column · drag a divider to resize · right-click to reset",
                Visibility = _hiddenColumns.Contains(id) ? Visibility.Collapsed : Visibility.Visible
            };
            cell.MouseEnter += (_, _) => { if (!_colDragActive) cell.SetResourceReference(Border.BackgroundProperty, "HoverBrush"); };
            cell.MouseLeave += (_, _) => { if (!_colDragActive) cell.Background = Brushes.Transparent; };
            cell.MouseLeftButtonDown += (_, e) =>
            {
                _colDragFrom = index;
                _colDragTarget = index;
                _colDragStartX = e.GetPosition(Header).X;
                _colDragArmed = true;
                _colDragActive = false;
                cell.CaptureMouse();
                e.Handled = true;
            };
            cell.MouseMove += (_, e) =>
            {
                if (!_colDragArmed || _colDragFrom != index || !cell.IsMouseCaptured) return;
                var x = e.GetPosition(Header).X;
                if (!_colDragActive)
                {
                    if (Math.Abs(x - _colDragStartX) < 4) return;
                    BeginColumnDrag(cell);
                }
                UpdateColumnDrag(x - _colDragStartX);
            };
            cell.MouseLeftButtonUp += (_, _) => EndColumnDrag(cell);
            cell.LostMouseCapture += (_, _) => { if (_colDragActive || _colDragArmed) EndColumnDrag(cell); };
            Grid.SetColumn(cell, c);
            header.Children.Add(cell);
            _headerCells.Add(cell);
            if (c < _columnOrder.Count - 1)
            {
                var line = ColumnSeparatorLine(c);
                header.Children.Add(line);
                _headerDecor.Add(line);
                var grip = ColumnBoundaryGrip(c);
                header.Children.Add(grip);
                _headerDecor.Add(grip);
            }
        }
    }

    private void BeginColumnDrag(Border cell)
    {
        _colDragActive = true;
        var count = _headerCells.Count;
        _colDragLefts = new double[count];
        _colDragWidths = new double[count];
        var left = 0.0;
        for (var c = 0; c < count; c++)
        {
            _colDragLefts[c] = left;
            _colDragWidths[c] = Header!.ColumnDefinitions[c].ActualWidth;
            left += _colDragWidths[c];
        }
        foreach (var decor in _headerDecor.Concat(_rowSeparators)) decor.Visibility = Visibility.Hidden;
        Panel.SetZIndex(cell, 100);
        // Lift the whole column: tinted, outlined cells drawn above their neighbours.
        if (_colDragFrom < _columnRowCells.Count)
            foreach (var wrap in _columnRowCells[_colDragFrom])
            {
                Panel.SetZIndex(wrap, 100);
                wrap.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                wrap.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                wrap.BorderThickness = new Thickness(1, 0, 1, 0);
            }
        cell.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        cell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var accent = (_host.TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        cell.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.85 };
        if (cell.Child is TextBlock text) text.Opacity = 1;
    }

    private void UpdateColumnDrag(double offset)
    {
        var from = _colDragFrom;
        // Keep the dragged label inside the strip.
        var min = -_colDragLefts[from];
        var max = _colDragLefts[^1] + _colDragWidths[^1] - (_colDragLefts[from] + _colDragWidths[from]);
        offset = Math.Clamp(offset, min, max);
        _columnTransforms[from].X = offset;

        // Target slot: the column whose midpoint the dragged label's centre has passed.
        var centre = _colDragLefts[from] + _colDragWidths[from] / 2 + offset;
        var target = from;
        for (var c = 0; c < _colDragLefts.Length; c++)
        {
            var mid = _colDragLefts[c] + _colDragWidths[c] / 2;
            if (c < from && centre < mid) { target = c; break; }
            if (c > from && centre > mid) target = c;
        }
        if (target == _colDragTarget) return;
        _colDragTarget = target;
        for (var c = 0; c < _headerCells.Count; c++)
        {
            if (c == from) continue;
            var shift = from < target && c > from && c <= target ? -_colDragWidths[from]
                      : from > target && c >= target && c < from ? _colDragWidths[from] : 0;
            _columnTransforms[c].BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(shift, SlideDuration) { EasingFunction = SlideEase });
        }
    }

    private void EndColumnDrag(Border cell)
    {
        var wasActive = _colDragActive;
        _colDragArmed = false;
        _colDragActive = false;
        if (cell.IsMouseCaptured) cell.ReleaseMouseCapture();
        if (!wasActive) { cell.Background = Brushes.Transparent; return; }

        var from = _colDragFrom;
        var target = _colDragTarget;
        // Settle the dragged label into its slot, then commit the new order once.
        var slotLeft = target >= from
            ? _colDragLefts[target] + _colDragWidths[target] - _colDragWidths[from]
            : _colDragLefts[target];
        var settle = new DoubleAnimation(slotLeft - _colDragLefts[from], SlideDuration) { EasingFunction = SlideEase };
        settle.Completed += (_, _) =>
        {
            if (target != from)
            {
                var id = _columnOrder[from];
                _columnOrder.RemoveAt(from);
                _columnOrder.Insert(target, id);
            }
            // Header first: it creates the column transforms the rebuilt rows share.
            RebuildHeader();
            _host.RebuildControls();
            if (target != from) _host.RaiseColumnLayoutChanged();
        };
        _columnTransforms[from].BeginAnimation(TranslateTransform.XProperty, settle);
    }

    /// <summary>
    /// Word-table style divider between column <paramref name="left"/> and the next: dragging moves only
    /// this boundary, so the left column grows exactly as much as its right neighbour shrinks.
    /// The flexible name column absorbs the change when it is on either side.
    /// </summary>
    private Thumb ColumnBoundaryGrip(int left)
    {
        var line = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
        line.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var hostFactory = new FrameworkElementFactory(typeof(Border));
        hostFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var thumb = new Thumb
        {
            Width = 7, Cursor = Cursors.SizeWE, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, -4, 0), ToolTip = "Drag to resize",
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hostFactory }
        };
        Panel.SetZIndex(thumb, 5);
        thumb.Loaded += (_, _) =>
        {
            if (VisualTreeHelper.GetChildrenCount(thumb) > 0 && VisualTreeHelper.GetChild(thumb, 0) is Border host) host.Child = line;
        };
        thumb.MouseEnter += (_, _) => line.Opacity = 0.9;
        thumb.MouseLeave += (_, _) => { if (!thumb.IsDragging) line.Opacity = 0; };
        thumb.DragDelta += (_, e) =>
        {
            var leftId = _columnOrder[left];
            var rightId = _columnOrder[left + 1];
            var delta = e.HorizontalChange;
            // Limit the move so neither neighbour passes its minimum or maximum.
            if (leftId != "name")
            {
                var spec = ColumnSpecs[leftId];
                delta = Math.Clamp(delta, spec.min - _columnWidths[leftId], spec.max - _columnWidths[leftId]);
            }
            if (rightId != "name")
            {
                var spec = ColumnSpecs[rightId];
                delta = Math.Clamp(delta, _columnWidths[rightId] - spec.max, _columnWidths[rightId] - spec.min);
            }
            if (Math.Abs(delta) < 0.01) return;
            if (leftId != "name") _columnWidths[leftId] += delta;
            if (rightId != "name") _columnWidths[rightId] -= delta;
            ApplyColumnWidths();
        };
        thumb.DragCompleted += (_, _) =>
        {
            if (!thumb.IsMouseOver) line.Opacity = 0;
            _host.RaiseColumnLayoutChanged();
        };
        Grid.SetColumn(thumb, left);
        return thumb;
    }
}
