using System.Windows;

namespace TabForge.Views;

/// <summary>
/// Owns: fitting the score zoom and speed group in the top toolbar beside the menu, the pinned tools and the tempo box. A narrow
/// window sheds it in steps: a tighter menu bar, then the "Speed" label, then a narrower speed box, then the speed box, then the
/// time and key signature readouts, then the zoom box (zoom out / in stay), and only then the whole group.
/// Does not own: the zoom and speed controls (ScoreZoomController, TransportControlsController), their hotkeys or menu items,
/// which keep working while any part is hidden; the menu item template (App.xaml spaces top-level items by their Padding).
/// Tests: TestToolbarZoomAndSpeed, TestToolbarZoomSpeedNarrow.
/// </summary>
internal sealed class ToolbarZoomSpeedFit
{
    private const double Gap = 8;            // breathing room between the group and its neighbours
    private const double ComboWide = 76, ComboNarrow = 62;
    internal const int Collapsed = 7;       // 0 = everything shown ... 7 = the group collapsed

    private readonly FrameworkElement _menu, _pinned, _tempo, _group;

    // The group's parts and the tempo group's readouts, found by name or kind in the toolbar markup.
    private readonly FrameworkElement? _speedLabel, _speedSeparator, _zoomCombo, _speedCombo;
    private readonly FrameworkElement[] _readouts;

    /// <summary>The room and the menu + group width at the last step tried (for tests).</summary>
    internal (double Room, double Needed) LastFit { get; private set; }

    /// <summary>Re-lays the group out whenever the toolbar's width changes.</summary>
    public ToolbarZoomSpeedFit(FrameworkElement bar, FrameworkElement menu, FrameworkElement pinned, FrameworkElement tempo, FrameworkElement group)
    {
        _menu = menu;
        _pinned = pinned;
        _tempo = tempo;
        _group = group;
        FrameworkElement? Named(FrameworkElement root, string name) => LogicalTreeHelper.FindLogicalNode(root, name) as FrameworkElement;
        var parts = (group as System.Windows.Controls.Panel)?.Children.OfType<FrameworkElement>().ToList() ?? new();
        _speedLabel = parts.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
        _speedSeparator = parts.OfType<System.Windows.Controls.Border>().FirstOrDefault();
        _zoomCombo = Named(group, "ZoomCombo");
        _speedCombo = Named(group, "SpeedCombo");
        _readouts = new[] { Named(tempo, "TimeSigLabel"), Named(tempo, "KeyLabelText") }.OfType<FrameworkElement>().ToArray();
        bar.SizeChanged += (_, e) => Update(e.NewSize.Width);
    }

    /// <summary>The step last applied (0 = everything shown, <see cref="Collapsed"/> = the group hidden).</summary>
    public int Step { get; private set; }

    /// <summary>Applies the first step whose menu and group fit in <paramref name="barWidth"/> beside the pinned tools and tempo box.</summary>
    public void Update(double barWidth)
    {
        for (var step = 0; step <= Collapsed; step++)
        {
            Apply(step);
            LastFit = (barWidth - _pinned.ActualWidth - _pinned.Margin.Left - Natural(_tempo) - Gap, Natural(_menu) + Natural(_group));
            if (step == Collapsed || LastFit.Needed <= LastFit.Room) break;
        }
    }

    private static readonly Thickness CompactMenuPadding = new(-5, 5, 3, 5);   // with the 8 px icon slot: 3 px each side

    private static void Invalidate(DependencyObject d)
    {
        (d as UIElement)?.InvalidateMeasure();
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) Invalidate(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
    }

    private static double Natural(FrameworkElement e)
    {
        if (e.Visibility == Visibility.Collapsed) return 0;
        Invalidate(e);   // a changed descendant does not dirty its ancestors until the next layout pass
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return e.DesiredSize.Width;   // includes the margin
    }

    private void Apply(int step)
    {
        Step = step;
        static void Show(FrameworkElement? e, bool on) { if (e is not null) e.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
        Show(_group, step < Collapsed);
        Show(_speedLabel, step < 2);
        if (_speedCombo is not null) _speedCombo.Width = step >= 3 ? ComboNarrow : ComboWide;   // the zoom box keeps its width: "Fit width" needs it
        if (_menu is System.Windows.Controls.ItemsControl menu)
            foreach (var item in menu.Items.OfType<System.Windows.Controls.Control>())
                if (step >= 1) item.Padding = CompactMenuPadding;
                else item.ClearValue(System.Windows.Controls.Control.PaddingProperty);
        Show(_speedSeparator, step < 4);
        Show(_speedCombo, step < 4);
        foreach (var readout in _readouts) Show(readout, step < 5);
        Show(_zoomCombo, step < 6);
    }
}
