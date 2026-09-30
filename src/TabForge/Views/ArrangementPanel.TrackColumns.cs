using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// ArrangementPanel: track-control columns (ordered, resizable, re-orderable) and the controls in them.
public sealed partial class ArrangementPanel
{
    // ---------- track-control columns: ordered, resizable, re-orderable ----------
    // "name" is the flexible column; every other column has a stored width.
    public static readonly string[] DefaultColumnOrder = { "settings", "colour", "number", "name", "fx", "mute", "solo", "volume", "pan", "instrument" };
    private static readonly Dictionary<string, (string label, double width, double min, double max)> ColumnSpecs = new()
    {
        ["colour"] = ("●", 22, 18, 40), ["settings"] = ("⚙", 24, 20, 40), ["number"] = ("#", 28, 22, 50),
        ["name"] = ("TRACK", 0, 0, 0), ["fx"] = ("FX", 56, 50, 80), ["mute"] = ("M", 26, 22, 44), ["solo"] = ("S", 26, 22, 44),
        ["volume"] = ("VOLUME", 82, 40, 240), ["pan"] = ("PAN", 82, 28, 240), ["instrument"] = ("INSTRUMENT", 132, 80, 280)
    };
    private const double RowGridLeft = 6, RowGridRight = 10;
    private List<string> _columnOrder = DefaultColumnOrder.ToList();
    private readonly Dictionary<string, double> _columnWidths = ColumnSpecs.ToDictionary(p => p.Key, p => p.Value.width);
    private readonly List<Grid> _rowGrids = new();
    private Grid? _columnHeader;
    /// <summary>Raised after the user finishes resizing or reordering track columns.</summary>
    public event EventHandler? ColumnLayoutChanged;

    /// <summary>Column order, widths and the whole controls-area width, for saving in settings.</summary>
    public (List<string> order, Dictionary<string, double> widths, double area) ColumnState
    {
        get => (_columnOrder.ToList(), new Dictionary<string, double>(_columnWidths), ColumnDefinitions[0].Width.Value);
        set
        {
            // Layouts saved before the FX column existed: put it just before Mute.
            if (value.order is { } saved && !saved.Contains("fx") && saved.Count == DefaultColumnOrder.Length - 1)
            {
                saved = saved.ToList();
                saved.Insert(Math.Max(0, saved.IndexOf("mute")), "fx");
                value = (saved, value.widths, value.area);
            }
            if (value.order is { Count: > 0 } order && order.Count == DefaultColumnOrder.Length &&
                DefaultColumnOrder.All(order.Contains))
                _columnOrder = order.ToList();
            if (value.widths is not null)
                foreach (var (id, width) in value.widths)
                    if (ColumnSpecs.TryGetValue(id, out var spec) && id != "name" && double.IsFinite(width))
                        _columnWidths[id] = Math.Clamp(width, spec.min, spec.max);
            if (double.IsFinite(value.area) && value.area > 0)
                ColumnDefinitions[0].Width = new GridLength(Math.Clamp(value.area, 420, 900));
            RebuildColumnHeader();
            RebuildControls();
        }
    }

    public void ResetColumnLayout()
    {
        _columnOrder = DefaultColumnOrder.ToList();
        foreach (var (id, spec) in ColumnSpecs) _columnWidths[id] = spec.width;
        ColumnDefinitions[0].Width = new GridLength(ControlsWidth);
        RebuildColumnHeader();
        RebuildControls();
        ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private GridLength ColumnWidth(string id) =>
        id == "name" ? new GridLength(1, GridUnitType.Star) : new GridLength(_hiddenColumns.Contains(id) ? 0 : _columnWidths[id]);

    private readonly HashSet<string> _hiddenColumns = new(StringComparer.OrdinalIgnoreCase);

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
            RebuildColumnHeader();
            RebuildControls();
        }
    }

    private void AddColumnDefinitions(Grid grid)
    {
        foreach (var id in _columnOrder)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = ColumnWidth(id), MinWidth = id == "name" ? 40 : 0 });
    }

    /// <summary>Puts each row element in its column and draws faint separators between columns.</summary>
    private void PlaceCells(Grid grid, Dictionary<string, FrameworkElement> cells)
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
                SetColumn(wrap, c);
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
        SetColumn(line, column);
        return line;
    }

    /// <summary>Resizes every row live; no rebuild, so playback and scrolling are untouched.</summary>
    private void ApplyColumnWidths()
    {
        foreach (var grid in _rowGrids.Append(_columnHeader).OfType<Grid>())
            for (var c = 0; c < _columnOrder.Count && c < grid.ColumnDefinitions.Count; c++)
                grid.ColumnDefinitions[c].Width = ColumnWidth(_columnOrder[c]);
    }

    /// <summary>
    /// The slim label strip above the tracks: drag a label to move that column, drag a label's
    /// right edge to resize it, right-click to reset.
    /// </summary>
    private UIElement BuildColumnHeader()
    {
        _columnHeader = new Grid { Height = ColumnHeaderHeight, Margin = new Thickness(RowGridLeft, 0, RowGridRight, 0), Background = Brushes.Transparent };
        RebuildColumnHeader();
        _columnHeader.ContextMenuOpening += (_, _) => _columnHeader!.ContextMenu = BuildEmptyAreaMenu(new Control[] { ResetColumnsItem() });
        _columnHeader.ContextMenu = BuildEmptyAreaMenu(new Control[] { ResetColumnsItem() });   // rebuilt on each opening (checked state)
        return _columnHeader;
    }

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
    private static readonly IEasingFunction SlideEase = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

    private void RebuildColumnHeader()
    {
        if (_columnHeader is null) return;
        _columnHeader.Children.Clear();
        _columnHeader.ColumnDefinitions.Clear();
        _headerCells.Clear();
        _headerDecor.Clear();
        _columnTransforms.Clear();
        foreach (var _ in _columnOrder) _columnTransforms.Add(new TranslateTransform());
        AddColumnDefinitions(_columnHeader);
        for (var c = 0; c < _columnOrder.Count; c++)
        {
            var id = _columnOrder[c];
            var index = c;
            var label = new TextBlock
            {
                Text = ColumnSpecs[id].label, FontSize = 9, FontWeight = FontWeights.SemiBold, Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = id == "name" ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                Margin = new Thickness(id == "name" ? 4 : 0, 0, 0, 0), IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
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
                _colDragStartX = e.GetPosition(_columnHeader).X;
                _colDragArmed = true;
                _colDragActive = false;
                cell.CaptureMouse();
                e.Handled = true;
            };
            cell.MouseMove += (_, e) =>
            {
                if (!_colDragArmed || _colDragFrom != index || !cell.IsMouseCaptured) return;
                var x = e.GetPosition(_columnHeader).X;
                if (!_colDragActive)
                {
                    if (Math.Abs(x - _colDragStartX) < 4) return;
                    BeginColumnDrag(cell);
                }
                UpdateColumnDrag(cell, x - _colDragStartX);
            };
            cell.MouseLeftButtonUp += (_, _) => EndColumnDrag(cell);
            cell.LostMouseCapture += (_, _) => { if (_colDragActive || _colDragArmed) EndColumnDrag(cell); };
            SetColumn(cell, c);
            _columnHeader.Children.Add(cell);
            _headerCells.Add(cell);
            if (c < _columnOrder.Count - 1)
            {
                var line = ColumnSeparatorLine(c);
                _columnHeader.Children.Add(line);
                _headerDecor.Add(line);
                var grip = ColumnBoundaryGrip(c);
                _columnHeader.Children.Add(grip);
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
            _colDragWidths[c] = _columnHeader!.ColumnDefinitions[c].ActualWidth;
            left += _colDragWidths[c];
        }
        foreach (var decor in _headerDecor.Concat(_rowSeparators)) decor.Visibility = Visibility.Hidden;
        SetZIndex(cell, 100);
        // Lift the whole column: tinted, outlined cells drawn above their neighbours.
        if (_colDragFrom < _columnRowCells.Count)
            foreach (var wrap in _columnRowCells[_colDragFrom])
            {
                SetZIndex(wrap, 100);
                wrap.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                wrap.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                wrap.BorderThickness = new Thickness(1, 0, 1, 0);
            }
        cell.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        cell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        cell.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.85 };
        if (cell.Child is TextBlock text) text.Opacity = 1;
    }

    private void UpdateColumnDrag(Border cell, double offset)
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
                new System.Windows.Media.Animation.DoubleAnimation(shift, SlideDuration) { EasingFunction = SlideEase });
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
        var settle = new System.Windows.Media.Animation.DoubleAnimation(slotLeft - _colDragLefts[from], SlideDuration) { EasingFunction = SlideEase };
        settle.Completed += (_, _) =>
        {
            if (target != from)
            {
                var id = _columnOrder[from];
                _columnOrder.RemoveAt(from);
                _columnOrder.Insert(target, id);
            }
            // Header first: it creates the column transforms the rebuilt rows share.
            RebuildColumnHeader();
            RebuildControls();
            if (target != from) ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
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
        SetZIndex(thumb, 5);
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
            ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        SetColumn(thumb, left);
        return thumb;
    }

    /// <summary>Master (all tracks) volume knob in the arrangement header, 0–100 %.</summary>
    public KnobControl MasterVolumeKnob { get; private set; } = null!;
    /// <summary>Global tuning button in the arrangement header; the window builds its menu.</summary>
    public Button TuningButton { get; private set; } = null!;

    public void SetTuningLabel(int semitones) =>
        _tuningText.Text = semitones == 0 ? "0" : semitones > 0 ? $"+{semitones}" : $"{semitones}";
    public void SetTuningLabel(string text) => _tuningText.Text = text;

    /// <summary>Left click on the tuning-fork icon (opens the global tuning window when enabled).</summary>
    public event EventHandler? TuningIconClicked;
    /// <summary>Left click on the shift number (type-in dialog).</summary>
    public event EventHandler? TuningNumberClicked;
    /// <summary>Right click anywhere on the tuning button (quick menu).</summary>
    public event EventHandler? TuningMenuRequested;
    /// <summary>Inline edit committed: the new absolute semitone shift.</summary>
    public event EventHandler<int>? TuningShiftEdited;

    private System.Windows.Shapes.Path? _tuningFork;
    private DispatcherTimer? _tuningClickTimer;
    private bool _tuningClickOnIcon;
    private TextBox? _tuningEditor;

    private void AttachTuningGestures()
    {
        TuningButton.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // single vs double click is resolved here, not by Button.Click
            if (_tuningEditor is not null) return;
            _tuningClickTimer ??= new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime())
            };
            _tuningClickTimer.Stop();
            if (e.ClickCount >= 2) { BeginTuningInlineEdit(); return; }
            _tuningClickOnIcon = _tuningFork is not null && _tuningFork.IsMouseOver;
            _tuningClickTimer.Tick -= TuningSingleClick;
            _tuningClickTimer.Tick += TuningSingleClick;
            _tuningClickTimer.Start();
        };
        TuningButton.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; TuningMenuRequested?.Invoke(this, EventArgs.Empty); };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void TuningSingleClick(object? sender, EventArgs e)
    {
        _tuningClickTimer?.Stop();
        if (_tuningClickOnIcon) TuningIconClicked?.Invoke(this, EventArgs.Empty);
        else TuningNumberClicked?.Invoke(this, EventArgs.Empty);
    }

    // Double-click: the number becomes a text box with its value selected; Enter or clicking away saves.
    private void BeginTuningInlineEdit()
    {
        if (TuningButton.Content is not StackPanel content) return;
        var index = content.Children.IndexOf(_tuningText);
        var editor = new TextBox
        {
            Text = _tuningText.Text.TrimStart('+'), MinWidth = 30, Margin = _tuningText.Margin, Padding = new Thickness(1, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
        };
        var done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            content.Children.Remove(editor);
            content.Children.Insert(index, _tuningText);
            _tuningEditor = null;
            if (commit && int.TryParse(editor.Text.Trim().TrimStart('+'), out var value))
                TuningShiftEdited?.Invoke(this, Math.Clamp(value, -24, 24));
        }
        editor.KeyDown += (_, k) =>
        {
            if (k.Key == Key.Enter) { Finish(true); k.Handled = true; }
            else if (k.Key == Key.Escape) { Finish(false); k.Handled = true; }
        };
        editor.LostKeyboardFocus += (_, _) => Finish(true);
        editor.PreviewTextInput += (_, t) => t.Handled = !t.Text.All(c => char.IsDigit(c) || c is '-' or '+');
        content.Children.RemoveAt(index);
        content.Children.Insert(index, editor);
        _tuningEditor = editor;
        editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { editor.Focus(); Keyboard.Focus(editor); editor.SelectAll(); }));
    }

    private readonly TextBlock _tuningText = new() { Text = "0", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 3, 0) };

    // Tuning-fork icon + signed semitone shift (e.g. -2, 0, +1).
    private StackPanel TuningButtonContent()
    {
        var fork = _tuningFork = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,1 V7 A3,3 0 0 0 9,7 V1 M6,10 V15"),
            Stroke = (Brush)Application.Current.FindResource("TextBrush"),
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 12, Height = 16, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center,
            Fill = Brushes.Transparent, ToolTip = "Global tuning window",
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(fork);
        content.Children.Add(_tuningText);
        return content;
    }

    /// <summary>Show pan as rotary knobs (default) or as the older horizontal sliders.</summary>
    public bool PanKnobs { get; set; } = true;
    /// <summary>The user switched the pan control style from a pan control's context menu.</summary>
    public event EventHandler<bool>? PanStyleChanged;

    private static string PanText(double value)
    {
        var offset = (int)Math.Round(value) - 64;
        return offset == 0 ? "Centre" : offset < 0 ? $"L {-offset}" : $"R {offset}";
    }

    private ContextMenu PanContextMenu(TrackModel track, FrameworkElement control)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)) };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header, Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
            item.Click += (_, _) => action();
            return item;
        }
        void SetPan(int value)
        {
            MixEditStarting?.Invoke(this, EventArgs.Empty);
            if (control is KnobControl knob) knob.Value = value;
            else if (control is Slider slider) slider.Value = Math.Clamp(value, 0, 127) - 64;
            MixEditEnded?.Invoke(this, EventArgs.Empty);
        }
        menu.Items.Add(Item("Centre pan", () => SetPan(64)));
        menu.Items.Add(Item("Set exact pan…", () =>
        {
            var text = GpDialogs.Prompt("Pan", "Pan from -63 (left) to +63 (right), 0 = centre:", (track.Pan - 64).ToString());
            if (int.TryParse(text, out var offset)) SetPan(Math.Clamp(offset + 64, 0, 127));
        }));
        menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource(typeof(Separator)) });
        var knobs = Item("Knob style", () => PanStyleChanged?.Invoke(this, true));
        knobs.IsCheckable = true; knobs.IsChecked = PanKnobs;
        var sliders = Item("Slider style", () => PanStyleChanged?.Invoke(this, false));
        sliders.IsCheckable = true; sliders.IsChecked = !PanKnobs;
        menu.Items.Add(knobs);
        menu.Items.Add(sliders);

        return menu;
    }

    private void AttachMixEditGestures(Slider slider)
    {
        slider.PreviewMouseLeftButtonDown += (_, _) => MixEditStarting?.Invoke(this, EventArgs.Empty);
        slider.PreviewMouseLeftButtonUp += (_, _) => MixEditEnded?.Invoke(this, EventArgs.Empty);
        slider.PreviewMouseWheel += (_, _) =>
        {
            MixEditStarting?.Invoke(this, EventArgs.Empty);
            slider.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(() => MixEditEnded?.Invoke(this, EventArgs.Empty)));
        };
        slider.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.PageUp or Key.PageDown or Key.Home or Key.End)
                MixEditStarting?.Invoke(this, EventArgs.Empty);
        };
        slider.PreviewKeyUp += (_, _) => MixEditEnded?.Invoke(this, EventArgs.Empty);
        slider.LostMouseCapture += (_, _) => MixEditEnded?.Invoke(this, EventArgs.Empty);
        slider.LostKeyboardFocus += (_, _) => MixEditEnded?.Invoke(this, EventArgs.Empty);
    }

    // standard instrument selector: the current sound as a button; click opens six quick picks, a
    // separator, then every GM family as a hover submenu (with the instrument badges), and VST plug-ins.
    private Button InstrumentButton(TrackModel track, Action<string> choose)
    {
        var label = InstrumentLabel(track);
        var entry = TabForge.Services.InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, track.MidiChannel == 9);
        var content = new DockPanel { LastChildFill = true };
        var arrow = new TextBlock { Text = "▾", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(arrow, Dock.Right);
        content.Children.Add(arrow);
        var icon = TabForge.Views.InstrumentIcon.Element(entry, 16); // family colour, not the track colour
        icon.Margin = new Thickness(0, 0, 5, 0);
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content, Margin = new Thickness(4, 2, 4, 2), FontSize = 11, Padding = new Thickness(5, 1, 5, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Instrument / VST for this track",
        };
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)), PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            MenuItem Item(TabForge.Services.InstrumentEntry e)
            {
                var item = new MenuItem
                {
                    Header = e.Name, Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
                    Icon = TabForge.Views.InstrumentIcon.Element(e, 18), IsCheckable = true,
                    IsChecked = string.Equals(e.Name, label, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) => choose(e.Name);
                return item;
            }
            // Top: every sound in the current instrument's family; then a clear divider and all families.
            var currentFamily = entry?.Category;
            var siblings = currentFamily is null ? new List<TabForge.Services.InstrumentEntry>()
                : TabForge.Services.InstrumentCatalog.All.Where(e => e.Category == currentFamily).ToList();
            if (siblings.Count > 0)
            {
                menu.Items.Add(MenuHeader(currentFamily!.ToUpperInvariant()));
                foreach (var e in siblings) menu.Items.Add(Item(e));
                menu.Items.Add(MenuDivider());
            }
            menu.Items.Add(MenuHeader("ALL INSTRUMENT FAMILIES"));
            foreach (var family in TabForge.Services.InstrumentCatalog.Categories)
            {
                var members = TabForge.Services.InstrumentCatalog.All.Where(e => e.Category == family).ToList();
                var sub = new MenuItem
                {
                    Header = family, Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
                    Icon = TabForge.Views.InstrumentIcon.Element(members[0], 18),
                };
                // Built on first hover so opening the menu stays instant.
                sub.Items.Add(new MenuItem());
                sub.SubmenuOpened += (_, _) =>
                {
                    if (sub.Items.Count == members.Count) return;
                    sub.Items.Clear();
                    foreach (var e in members) sub.Items.Add(Item(e));
                };
                menu.Items.Add(sub);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    private static MenuItem MenuHeader(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 1) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return new MenuItem { Header = label, IsEnabled = false, IsHitTestVisible = false, Focusable = false,
            Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
    }

    // A clearly visible divider: a full-width 2px line with space above and below.
    private static Separator MenuDivider()
    {
        var line = new Separator { Margin = new Thickness(0, 6, 0, 6), Height = 2, MinHeight = 2 };
        line.Template = new ControlTemplate(typeof(Separator))
        {
            VisualTree = CreateDividerVisual()
        };
        return line;
    }

    private static FrameworkElementFactory CreateDividerVisual()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(FrameworkElement.HeightProperty, 2.0);
        border.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
        border.SetValue(UIElement.OpacityProperty, 0.7);
        return border;
    }

    private static Color ParseColour(string? hex) =>
        TabForge.Views.ColourChooser.TryParse(hex, out var c) ? c : Color.FromRgb(0x80, 0x80, 0x80);

    /// <summary>Named, slightly muted track colours (readable on dark and light themes).</summary>
    public static readonly (string Name, string Hex)[] TrackColourPalette =
    {
        ("Red", "#B8403A"), ("Orange", "#C46A2B"), ("Amber", "#B8902A"), ("Yellow", "#A89A2E"), ("Olive", "#7A8A34"),
        ("Green", "#3F8F4E"), ("Teal", "#2E8A7E"), ("Turquoise", "#2A96A6"), ("Blue", "#3A6FB8"), ("Indigo", "#5357B0"),
        ("Purple", "#7A4FB0"), ("Magenta", "#A4468E"), ("Pink", "#B8577A"), ("Brown", "#8A5E3C"), ("Grey", "#6E7580"), ("Slate", "#4A5563"),
    };

    public static int PanStep(int midiPan) => Math.Clamp((int)Math.Round((midiPan - 64) / 8.0), -8, 8);

    public static int VolumeStep(int midiVolume) => Math.Clamp((int)Math.Round(midiVolume / 8.0), 0, 16);

    /// <summary>Reads the "auto-resize track list to fit" setting (owned by the main window's settings).</summary>
    public Func<bool>? AutoFitState { get; set; }
    public event Action? AutoFitToggleRequested;

    /// <summary>Show track volume as rotary knobs instead of the standard slider (default slider).</summary>
    public bool VolumeKnobs { get; set; }

    /// <summary>Mouse wheel over a slider: 1 unit per notch (Ctrl: 8), and the wheel never scrolls the panel behind it.</summary>
    internal static void AttachWheelStep(Slider slider)
    {
        slider.PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta == 0) return;
            var step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 8 : 1;
            slider.Value = Math.Clamp(Math.Round(slider.Value) + (e.Delta > 0 ? step : -step), slider.Minimum, slider.Maximum);
            e.Handled = true;
        };
    }

    // Press anywhere on the slider and drag: the value follows the pointer continuously (no jump-then-stop).
    internal static void AttachSmoothDrag(Slider slider, double defaultValue)
    {
        slider.Focusable = true;            // arrow keys (1 step), Home / End
        slider.FocusVisualStyle = null;     // the track outline (style trigger) shows keyboard focus
        var dragged = false;      // the previous press moved the value: the next press is a new drag, not half of a double-click
        Point downAt = default;
        // The handle's centre travels the track minus the handle's own width (pointer x -> value, so the handle stays under the pointer).
        void Follow(MouseEventArgs e)
        {
            // A slider removed from its window mid-drag has no pointer mapping: let go instead of pinning it to an end.
            if (PresentationSource.FromVisual(slider) is null) { if (slider.IsMouseCaptured) slider.ReleaseMouseCapture(); return; }
            slider.Value = SliderValueAt(slider, PointerSource.Position(e, slider).X);
            if ((PointerSource.Position(e, slider) - downAt).Length > 3) dragged = true;
        }
        slider.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (slider.Focusable) slider.Focus();   // keyboard: arrows step by SmallChange (1)
            if (e.ClickCount == 2 && !dragged) { slider.Value = defaultValue; return; } // double-click restores the default
            downAt = PointerSource.Position(e, slider);
            dragged = false;
            slider.CaptureMouse();
            Follow(e);
        };
        slider.PreviewMouseMove += (_, e) => { if (slider.IsMouseCaptured) Follow(e); };
        slider.PreviewMouseLeftButtonUp += (_, _) => { if (slider.IsMouseCaptured) slider.ReleaseMouseCapture(); };
    }

    /// <summary>Slider value for a pointer x (in slider coordinates), using the handle's real width.</summary>
    internal static double SliderValueAt(Slider slider, double x)
    {
        var thumbWidth = 18.0;
        if (slider.Template?.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track { Thumb: { ActualWidth: > 0 } thumb })
            thumbWidth = thumb.ActualWidth;
        var usable = Math.Max(1, slider.ActualWidth - thumbWidth);
        var ratio = Math.Clamp((x - thumbWidth / 2) / usable, 0, 1);
        if (slider.IsDirectionReversed) ratio = 1 - ratio;
        // Volume, pan and group levels are whole numbers: snap, so a pointer that lands exactly between two values
        // (whole-pixel mouse positions at 100% scale) picks one instead of leaving a half value.
        return Math.Clamp(Math.Round(slider.Minimum + ratio * (slider.Maximum - slider.Minimum), MidpointRounding.AwayFromZero),
            slider.Minimum, slider.Maximum);
    }

    private static void SetSliderFromTrackClick(Slider slider, MouseButtonEventArgs e)
    {
        if (IsSliderThumb(e.OriginalSource as DependencyObject)) return;
        var usableWidth = Math.Max(1, slider.ActualWidth - 12);
        var ratio = Math.Clamp((e.GetPosition(slider).X - 6) / usableWidth, 0, 1);
        if (slider.IsDirectionReversed) ratio = 1 - ratio;
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        e.Handled = true;
    }

    private static bool IsSliderThumb(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.Thumb) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    internal static Button ToggleIconButton(string iconResource, bool active, Action toggle, string tooltip)
    {
        var button = new Button
        {
            Width = 23, MinWidth = 23, Height = 22, Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0), ToolTip = tooltip,
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Background = iconResource == "IconMute"
                ? active ? (Brush)Application.Current.FindResource("TrackMutedBrush") : (Brush)Application.Current.FindResource("TrackAudibleBrush")
                : active ? (Brush)Application.Current.FindResource("TrackSoloBrush") : (Brush)Application.Current.FindResource("TrackToggleIdleBrush"),
            BorderBrush = iconResource == "IconMute"
                ? active ? (Brush)Application.Current.FindResource("TrackMutedBrush") : (Brush)Application.Current.FindResource("TrackAudibleBrush")
                : active ? (Brush)Application.Current.FindResource("TrackSoloActiveBorderBrush") : (Brush)Application.Current.FindResource("BorderSoftBrush")
        };
        if (iconResource == "IconSolo")
        {
            button.Content = new TextBlock
            {
                Text = "S", FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = active ? (Brush)Application.Current.FindResource("TrackSoloTextBrush") : (Brush)Application.Current.FindResource("MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            button.Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)Application.Current.FindResource(iconResource == "IconMute" && !active ? "IconSpeaker" : iconResource),
                Style = (Style)Application.Current.FindResource("IconPath")
            };
        }
        var stateWord = iconResource == "IconMute" ? (active ? "Muted" : "Not muted") : iconResource == "IconSolo" ? (active ? "Soloed" : "Not soloed") : (active ? "On" : "Off");
        button.ToolTip = $"{tooltip} ({stateWord})";
        System.Windows.Automation.AutomationProperties.SetHelpText(button, stateWord);
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => toggle();
        return button;
    }

    private static readonly string[] Palette =
    {
        "#F61A16", "#ED2224", "#F4E014", "#2248E8", "#35B954", "#D850C6", "#FF8C00", "#00B7C3", "#8B5CF6", "#64748B"
    };

    private static string NextColor(string current)
    {
        var i = Array.IndexOf(Palette, current);
        return Palette[(i + 1 + Palette.Length) % Palette.Length];
    }

    public static Brush ParseBrush(string hex, Brush fallback) =>
        ColourText.TryParse(hex, out var colour) ? Draw.Solid(Draw.Tame(colour)) : fallback;
}
