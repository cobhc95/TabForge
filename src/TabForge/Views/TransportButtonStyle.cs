using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: how a transport toggle button (metronome, count-in, loop) looks when it is on or off: fill, border, glow, automation name.
// Does not own: the toggle state (TransportControlsController) or what the toggle does.
// Tests: TestWindowLifetime.
internal static class TransportButtonStyle
{
    public static void Apply(Button button, bool active)
    {
        var (fill, accent) = button.Name switch
        {
            "MetronomeButton" => ("PlaybarMetroBrush", Color.FromRgb(0xFF, 0x95, 0x56)),
            "CountInButton" => ("PlaybarBeatBrush", Color.FromRgb(0xC1, 0x9B, 0xFF)),
            _ => ("PlaybarLoopBrush", Color.FromRgb(0x38, 0xE8, 0x89))
        };
        button.Background = active
            ? (Brush)Application.Current.FindResource(fill)
            : (Brush)Application.Current.FindResource("PlaybarSurfaceBrush");
        var light = VisualTheme.IsLight;
        if (light) accent = Color.FromRgb((byte)(accent.R * 0.62), (byte)(accent.G * 0.62), (byte)(accent.B * 0.62)); // neon reads harsh on grey
        button.BorderBrush = active
            ? new SolidColorBrush(accent)
            : (Brush)Application.Current.FindResource("PlaybarBorderBrush");
        button.Opacity = active || light ? 1.0 : 0.78;
        button.Effect = active
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = light ? 8 : 13,
                ShadowDepth = 0,
                Opacity = light ? 0.35 : 0.86
            }
            : null;
        if (button.Name is "MetronomeButton" or "LoopButton")
        {
            var label = button.Name == "LoopButton" ? "Loop" : "Metronome";
            System.Windows.Automation.AutomationProperties.SetName(button, $"{label} {(active ? "on" : "off")}");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "Active" : "Inactive");
        }
        if (button.Content is SvgIconView icon)
            icon.IconColor = active ? accent : Color.FromRgb(0x9A, 0xA6, 0xB2);
    }
}
