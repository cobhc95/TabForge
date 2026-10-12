using System.Windows;

namespace TabForge.Views;

/// <summary>
/// Owns: fitting the menu bar in the top toolbar beside the pinned tools and the icon buttons. A narrow window sheds in steps:
/// a tighter menu bar, then the Keyboard mode button.
/// Does not own: the zoom, speed and tempo controls (the View and tempo dock pane), the Keyboard mode command, its menu row and
/// hotkey, which keep working while the button is hidden; the menu item template (App.xaml spaces top-level items by their Padding).
/// Tests: TestToolbarZoomSpeedNarrow.
/// </summary>
internal sealed class ToolbarZoomSpeedFit
{
    private const double Gap = 8;            // breathing room between the menu and its neighbours
    internal const int KeyboardModeButtonSteps = 2;
    internal const int Collapsed = 2;       // 0 = everything shown, 1 = tight menu bar, 2 = no Keyboard mode button

    private readonly FrameworkElement _menu, _pinned, _tempo;
    private readonly FrameworkElement? _learnButton;

    /// <summary>The room and the menu width at the last step tried (for tests).</summary>
    internal (double Room, double Needed) LastFit { get; private set; }

    /// <summary>Re-fits the menu bar whenever the toolbar's width changes; <paramref name="tempo"/> is the icon button group.</summary>
    public ToolbarZoomSpeedFit(FrameworkElement bar, FrameworkElement menu, FrameworkElement pinned, FrameworkElement tempo)
    {
        _menu = menu;
        _pinned = pinned;
        _tempo = tempo;
        _learnButton = LogicalTreeHelper.FindLogicalNode(tempo, "KeyboardModeButton") as FrameworkElement;
        bar.SizeChanged += (_, e) => Update(e.NewSize.Width);
    }

    /// <summary>The step last applied (0 = everything shown, <see cref="Collapsed"/> = the last one).</summary>
    public int Step { get; private set; }

    /// <summary>Applies the first step whose menu fits in <paramref name="barWidth"/> beside the pinned tools and the icon buttons.</summary>
    public void Update(double barWidth)
    {
        for (var step = 0; step <= Collapsed; step++)
        {
            Apply(step);
            LastFit = (barWidth - _pinned.ActualWidth - _pinned.Margin.Left - Natural(_tempo) - Gap, Natural(_menu));
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
        if (_menu is System.Windows.Controls.ItemsControl menu)
            foreach (var item in menu.Items.OfType<System.Windows.Controls.Control>())
                if (step >= 1) item.Padding = CompactMenuPadding;
                else item.ClearValue(System.Windows.Controls.Control.PaddingProperty);
        Show(_learnButton, step < KeyboardModeButtonSteps);   // the Keyboard mode button goes first: the menu row and the hotkey stay
    }
}
