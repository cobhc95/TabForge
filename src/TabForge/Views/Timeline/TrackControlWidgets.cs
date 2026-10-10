using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: the stateless track-control widgets shared by the track list and the mixer: the colour palette, volume / pan
// steps, slider wheel and drag gestures, the M / S toggle buttons, colour parsing.
// Does not own: where the widgets sit or what their values change (ArrangementPanel, MixerWindow).
// Tests: TestMuteSoloIsInstant, TestClips (steps), TestSliderMappingAndGroupValues (slider mapping), TestTrackRowMenu (palette).
public static class TrackControlWidgets
{
    /// <summary>Named, slightly muted track colours (readable on dark and light themes).</summary>
    public static readonly (string Name, string Hex)[] TrackColourPalette =
    {
        ("Red", "#B8403A"), ("Orange", "#C46A2B"), ("Amber", "#B8902A"), ("Yellow", "#A89A2E"), ("Olive", "#7A8A34"),
        ("Green", "#3F8F4E"), ("Teal", "#2E8A7E"), ("Turquoise", "#2A96A6"), ("Blue", "#3A6FB8"), ("Indigo", "#5357B0"),
        ("Purple", "#7A4FB0"), ("Magenta", "#A4468E"), ("Pink", "#B8577A"), ("Brown", "#8A5E3C"), ("Grey", "#6E7580"), ("Slate", "#4A5563"),
    };

    public static Color ParseColour(string? hex) =>
        ColourChooser.TryParse(hex, out var c) ? c : Color.FromRgb(0x80, 0x80, 0x80);

    public static int PanStep(int midiPan) => Math.Clamp((int)Math.Round((midiPan - 64) / 8.0), -8, 8);

    public static int VolumeStep(int midiVolume) => Math.Clamp((int)Math.Round(midiVolume / 8.0), 0, 16);

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

    internal static Button ToggleIconButton(string iconResource, bool active, Action toggle, string tooltip)
    {
        // "M" (grey box, red when muted) and "S" (solo): the box restyles on the click, before any follow-up work runs.
        var mute = iconResource == "IconMute";
        var label = new TextBlock
        {
            Text = mute ? "M" : "S", FontSize = 11, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Button
        {
            Width = 23, MinWidth = 23, Height = 22, Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Content = label
        };
        static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
        void Restyle()
        {
            button.Background = active ? Res(mute ? "TrackMutedBrush" : "TrackSoloBrush") : Res("TrackToggleIdleBrush");
            button.BorderBrush = active ? Res(mute ? "TrackMutedBrush" : "TrackSoloActiveBorderBrush") : Res("BorderSoftBrush");
            if (mute && active)
            {
                // Readable on the red box in both themes (the light theme's red is pale).
                var c = Res("TrackMutedBrush") is SolidColorBrush solid ? solid.Color : Colors.Red;
                label.Foreground = Draw.Solid(0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Color.FromRgb(0x2A, 0x08, 0x0E) : Colors.White);
            }
            else label.Foreground = !mute && active ? Res("TrackSoloTextBrush") : Res("MutedBrush");
            var stateWord = mute ? (active ? "Muted" : "Not muted") : (active ? "Soloed" : "Not soloed");
            button.ToolTip = $"{tooltip} ({stateWord})";
            System.Windows.Automation.AutomationProperties.SetHelpText(button, stateWord);
        }
        Restyle();
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) =>
        {
            active = !active;
            Restyle();
            toggle();
        };
        return button;
    }

    public static Brush ParseBrush(string hex, Brush fallback) =>
        ColourText.TryParse(hex, out var colour) ? Draw.Solid(Draw.Tame(colour)) : fallback;
}
