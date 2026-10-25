using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace TabForge.Avalonia.Views;

// Owns: the fine-tune popup content of the View and tempo bar's buttons: a slider, a box for an exact value and one or two buttons
//   (zoom: Fit width; speed: 100%; tempo: step down / up), and its frame.
// Does not own: opening or placing the popup (ViewTempoBarView), applying a value (the setter the caller passes), the preset menus (ViewTempoMenus).
// Tests: TabForge.Avalonia.Tests.ViewTempoMenusTests.
public static class ViewTempoPopups
{
    public const double MinimumSpeedPercent = 25;
    public const double MaximumSpeedPercent = 200;
    public const double MinimumZoomPercent = 50;
    public const double MaximumZoomPercent = 200;
    public const double MinimumTempo = 30;
    public const double MaximumTempo = 300;
    public const double WheelSpeedStep = 5;
    private const double SliderWidth = 130;
    private const double BoxWidth = 46;
    private const double RowHeight = 24;

    /// <summary>The speed popup: slider and box in percent, then a 100% button.</summary>
    public static Control Speed(Func<double> getPercent, Action<double> setPercent)
    {
        var panel = Row();
        var set = Tuner(panel, MinimumSpeedPercent, MaximumSpeedPercent, "%", getPercent, setPercent, false, 100);
        panel.Children.Add(Push("100%", "Back to normal speed", () => set(100)));
        return Frame(panel);
    }

    /// <summary>The tempo popup: box and slider in beats per minute, then one-beat step down and up buttons.</summary>
    public static Control Tempo(Func<int> getTempo, Action<int> setTempo)
    {
        var panel = Row();
        var set = Tuner(panel, MinimumTempo, MaximumTempo, "", () => getTempo(), v => setTempo((int)Math.Round(v)), true);
        panel.Children.Add(Push("−", "One beat per minute slower", () => set(getTempo() - 1)));
        panel.Children.Add(Push("+", "One beat per minute faster", () => set(getTempo() + 1)));
        return Frame(panel);
    }

    /// <summary>The zoom popup: slider and box in percent, then a Fit width button when the caller has one.</summary>
    public static Control Zoom(Func<double> getPercent, Action<double> setPercent, Action? fitWidth)
    {
        var panel = Row();
        Tuner(panel, MinimumZoomPercent, MaximumZoomPercent, "%", getPercent, setPercent, false, 100);
        if (fitWidth is not null) panel.Children.Add(Push("Fit width", "Fit the score to the width of its pane", fitWidth));
        return Frame(panel);
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal };

    private static Border Frame(Control child)
    {
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 6), Child = child };
        frame[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        return frame;
    }

    // A slider and a box showing one value: the slider applies when released, the box on Enter. Returns a setter that applies and shows a value.
    // A double-click on the slider applies defaultValue (when given), as the box's Enter would.
    private static Action<double> Tuner(Panel panel, double min, double max, string unit, Func<double> get, Action<double> apply, bool boxFirst, double? defaultValue = null)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = SliderWidth, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), SmallChange = 1, LargeChange = 5 };
        var box = new TextBox { Width = BoxWidth, Height = RowHeight, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(box, unit == "%" ? "Type a percentage, then Enter" : "Type the tempo, then Enter");
        var syncing = false;
        string Text(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture) + unit;
        void Show(double v)
        {
            syncing = true;
            slider.Value = Math.Clamp(v, min, max);
            box.Text = Text(Math.Clamp(v, min, max));
            syncing = false;
        }
        void Set(double v) { apply(Math.Clamp(v, min, max)); Show(get()); }
        Show(get());
        slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && !syncing) box.Text = Text(slider.Value); };
        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Set(slider.Value), global::Avalonia.Interactivity.RoutingStrategies.Tunnel | global::Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        slider.KeyUp += (_, _) => Set(slider.Value);
        if (defaultValue is { } reset) slider.DoubleTapped += (_, _) => Set(reset);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var typed = (box.Text ?? "").Trim().TrimEnd('%');
            Set(double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : get());
            box.SelectAll();
        };
        if (boxFirst) { panel.Children.Add(box); panel.Children.Add(slider); }
        else { panel.Children.Add(slider); panel.Children.Add(box); }
        return Set;
    }

    private static Button Push(string text, string tip, Action run)
    {
        var button = new Button { Content = text, Height = RowHeight, MinWidth = RowHeight, Padding = new Thickness(6, 0), Margin = new Thickness(0, 0, 4, 0) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => run();
        return button;
    }
}
