using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TabForge.Views.ViewTempo;

// Owns: the fine-tune popup of each View and tempo button: a slider, a box for an exact value and one or two buttons
//       (zoom: Fit width; speed: 100%; tempo: step down / up), or the two signature buttons for time / key.
// Does not own: opening or placing the popup (ViewTempoBar), applying a value (IViewTempoHost), the preset menus (ViewTempoMenus).
// Tests: TestToolbarZoomAndSpeed.
public static class ViewTempoPopups
{
    public static Popup Build(int kind, IViewTempoHost host, UIElement target)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 6, 8, 6), Child = panel };
        frame.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        frame.SetResourceReference(FrameworkElement.LayoutTransformProperty, "UiScaleTransform");
        var popup = new Popup { Child = frame, PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        frame.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; popup.IsOpen = false; } };

        switch (kind)
        {
            case ViewTempoBar.Zoom:
                Tuner(panel, 50, 200, "%", () => ViewTempoControlsHost.Percent(host.ZoomText, 100), v => host.SetZoom($"{v:0}%"), false);
                panel.Children.Add(Push("Fit width", "Fit the score to the width of its pane", () => { host.SetZoom("Fit width"); popup.IsOpen = false; }));
                break;
            case ViewTempoBar.Speed:
                var reset = Tuner(panel, 25, 200, "%", () => host.SpeedPercent, host.SetSpeedPercent, false);
                panel.Children.Add(Push("100%", "Back to normal speed", () => reset(100)));
                break;
            case ViewTempoBar.Tempo:
                var set = Tuner(panel, 30, 300, "", () => host.Tempo, v => host.SetTempo((int)Math.Round(v)), true);
                panel.Children.Add(Push("−", "One beat per minute slower", () => set(host.Tempo - 1)));
                panel.Children.Add(Push("+", "One beat per minute faster", () => set(host.Tempo + 1)));
                break;
            default:
                panel.Children.Add(Push("Time signature…", "Change the time signature", () => { popup.IsOpen = false; host.Run("Bar.TimeSignature"); }));
                panel.Children.Add(Push("Key signature…", "Change the key signature", () => { popup.IsOpen = false; host.Run("Bar.KeySignature"); }));
                break;
        }
        return popup;
    }

    // A slider and a box showing one value. The slider applies when it is released (or a key is let go), the box on Enter.
    // Returns a setter that applies a value and shows it in both.
    private static Action<double> Tuner(Panel panel, double min, double max, string unit, Func<double> get, Action<double> apply, bool boxFirst)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = 130, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), SmallChange = 1, LargeChange = 5 };
        if (Application.Current?.TryFindResource("ArrangementSlider") is Style style) slider.Style = style;
        var box = new TextBox { Width = 46, Height = 24, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = unit == "%" ? "Type a percentage, then Enter" : "Type the tempo, then Enter" };
        var syncing = false;
        void Show(double v)
        {
            syncing = true;
            slider.Value = Math.Clamp(v, min, max);
            box.Text = Math.Round(Math.Clamp(v, min, max)).ToString(CultureInfo.InvariantCulture) + unit;
            syncing = false;
        }
        void Set(double v) { apply(Math.Clamp(v, min, max)); Show(get()); }
        Show(get());
        slider.ValueChanged += (_, e) => { if (!syncing) box.Text = Math.Round(e.NewValue).ToString(CultureInfo.InvariantCulture) + unit; };
        slider.LostMouseCapture += (_, _) => Set(slider.Value);
        slider.KeyUp += (_, _) => Set(slider.Value);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Set(ViewTempoControlsHost.Percent(box.Text, get()));
            box.SelectAll();
        };
        if (boxFirst)
        {
            panel.Children.Add(box);
            panel.Children.Add(slider);
            box.Loaded += (_, _) => box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); Keyboard.Focus(box); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            panel.Children.Add(slider);
            panel.Children.Add(box);
        }
        return Set;
    }

    private static Button Push(string text, string tip, Action run)
    {
        var button = new Button { Content = text, Height = 24, MinWidth = 24, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
        button.Click += (_, _) => run();
        return button;
    }
}
