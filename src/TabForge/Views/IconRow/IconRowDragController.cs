using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views.IconRow;

// Owns: the icon row's panel: restoring the saved order at load, the mouse drag that moves an icon (with its insertion marker), hiding dangling separators, saving the order, and restoring the default on "Reset panels".
// Does not own: the order rules (IconRowOrder.cs), the buttons, their commands and tooltips (MainWindow.xaml), or which buttons a narrow bar hides (ToolbarZoomSpeedFit).
// Tests: TestIconRowOrder (the order rules), TestToolbarZoomSpeedNarrow (the row in the bar).
public sealed class IconRowDragController
{
    private const double Threshold = 4;   // a press that moves less than this stays a click

    /// <summary>The stable id of a button in the row; a separator carries <see cref="IconRowOrder.Separator"/>.</summary>
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached("Id", typeof(string), typeof(IconRowDragController));
    public static string? GetId(DependencyObject d) => (string?)d.GetValue(IdProperty);
    public static void SetId(DependencyObject d, string? value) => d.SetValue(IdProperty, value);

    /// <summary>True on the row's panel: the panel gets a controller.</summary>
    public static readonly DependencyProperty ReorderProperty = DependencyProperty.RegisterAttached("Reorder", typeof(bool), typeof(IconRowDragController),
        new PropertyMetadata(false, (d, e) => { if (d is Panel panel && (bool)e.NewValue) _ = new IconRowDragController(panel); }));
    public static bool GetReorder(DependencyObject d) => (bool)d.GetValue(ReorderProperty);
    public static void SetReorder(DependencyObject d, bool value) => d.SetValue(ReorderProperty, value);

    /// <summary>Raised (bubbling) by whatever resets the window's panels; every icon row of that window returns to its default order.</summary>
    public static readonly RoutedEvent ResetEvent = EventManager.RegisterRoutedEvent("IconRowReset", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(IconRowDragController));

    private readonly Panel _panel;
    private IPaneHost? _host;
    private List<string>? _defaults;   // the order the panel is declared in
    private List<string> _order = new();
    private FrameworkElement? _pressed;
    private double _startX;
    private Marker? _marker;
    private int _target;

    private IconRowDragController(Panel panel)
    {
        _panel = panel;
        panel.Loaded += (_, _) => Load();
        panel.PreviewMouseLeftButtonDown += (_, e) => { _pressed = IconOf(e.OriginalSource as DependencyObject); _startX = e.GetPosition(_panel).X; };
        panel.PreviewMouseMove += (_, e) => Drag(e);
        panel.PreviewMouseLeftButtonUp += (_, e) => Drop(e);
        panel.LostMouseCapture += (_, _) => End();
    }

    private List<FrameworkElement> Items() => _panel.Children.OfType<FrameworkElement>().Where(c => GetId(c) is not null).ToList();

    private void Load()
    {
        if (_defaults is not null) return;
        _defaults = Items().Select(c => GetId(c)!).ToList();
        var window = Window.GetWindow(_panel);
        _host = window as IPaneHost;
        window?.AddHandler(ResetEvent, new RoutedEventHandler((_, _) => Set(_defaults.ToList(), save: true)));
        foreach (var item in Items()) item.IsVisibleChanged += (_, _) => ShowSeparators();
        Set(IconRowOrder.Apply(_host?.Settings.Appearance.IconRowOrder, _defaults), save: false);
    }

    /// <summary>Puts the panel's items in <paramref name="order"/> (only items out of place are touched) and stores it; the default order is stored as an empty list.</summary>
    private void Set(List<string> order, bool save)
    {
        var changed = !order.SequenceEqual(_order);
        _order = order;
        var items = Items();
        var separators = new Queue<FrameworkElement>(items.Where(c => GetId(c) == IconRowOrder.Separator));
        var first = items.Count == 0 ? 0 : _panel.Children.IndexOf(items[0]);
        for (var i = 0; i < order.Count; i++)
        {
            var item = order[i] == IconRowOrder.Separator ? separators.Dequeue() : items.First(c => GetId(c) == order[i]);
            if (ReferenceEquals(_panel.Children[first + i], item)) continue;
            _panel.Children.Remove(item);
            _panel.Children.Insert(first + i, item);
        }
        ShowSeparators();
        if (!save || !changed || _host is null) return;
        _host.Settings.Appearance.IconRowOrder = order.SequenceEqual(_defaults!) ? new() : order.ToList();
        _host.SaveSettings();
    }

    private void ShowSeparators()
    {
        var items = Items();
        var shown = IconRowOrder.SeparatorShown(items.Select(c => GetId(c)!).ToList(), i => items[i].Visibility != Visibility.Collapsed);
        for (var i = 0; i < items.Count; i++)
            if (GetId(items[i]) == IconRowOrder.Separator) items[i].Visibility = shown[i] ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The icon (not a separator) of the row that holds <paramref name="source"/>, or null.</summary>
    private FrameworkElement? IconOf(DependencyObject? source)
    {
        while (source is not null && !(source is FrameworkElement f && ReferenceEquals(f.Parent, _panel)))
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        return source is FrameworkElement item && GetId(item) is { } id && id != IconRowOrder.Separator ? item : null;
    }

    private void Drag(MouseEventArgs e)
    {
        if (_pressed is null || _defaults is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { End(); return; }
        var x = e.GetPosition(_panel).X;
        if (_marker is null)
        {
            if (Math.Abs(x - _startX) < Threshold) return;
            var pressed = _pressed;
            _panel.CaptureMouse();   // takes the press away from the button, so the drop does not click it
            _pressed = pressed;
            AdornerLayer.GetAdornerLayer(_panel)?.Add(_marker = new Marker(_panel));
        }
        // The landing place: before the first shown item whose middle is right of the pointer, else the end.
        var items = Items();
        var marker = _marker!;
        _target = items.Count;
        marker.X = _panel.ActualWidth;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Visibility == Visibility.Collapsed) continue;
            var slot = LayoutInformation.GetLayoutSlot(items[i]);
            if (x >= slot.Left + slot.Width / 2) continue;
            _target = i;
            marker.X = slot.Left;
            break;
        }
        marker.InvalidateVisual();
    }

    private void Drop(MouseButtonEventArgs e)
    {
        if (_marker is null) { _pressed = null; return; }
        var from = Items().IndexOf(_pressed!);
        var to = _target;
        End();
        e.Handled = true;
        Set(IconRowOrder.Move(_order, from, to), save: true);
    }

    private void End()
    {
        _pressed = null;
        if (_marker is not { } marker) return;
        _marker = null;
        AdornerLayer.GetAdornerLayer(_panel)?.Remove(marker);
        _panel.ReleaseMouseCapture();
    }

    /// <summary>The 2 px accent bar at the landing place.</summary>
    private sealed class Marker : Adorner
    {
        public double X;
        public Marker(UIElement row) : base(row) => IsHitTestVisible = false;

        protected override void OnRender(DrawingContext dc)
            => dc.DrawRectangle(TryFindResource("AccentBrush") as Brush ?? SystemColors.HighlightBrush, null, new Rect(Math.Max(0, X - 1), 0, 2, AdornedElement.RenderSize.Height));
    }
}
