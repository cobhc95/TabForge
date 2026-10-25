using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TabForge.Avalonia.Platform;

// Owns: the double-click reset of a slider to its default value (the mixer faders, the popups' sliders). It is the second left press of a double-click, caught
//     before the slider's own press handling: a slider takes the pointer on a press, so the Tapped/DoubleTapped gesture never completes on it.
// Does not own: the stored value or the change callbacks. Setting Value raises ValueChanged, so the slider's own handler stores and refreshes as for a drag.
// Threads: UI thread only.
public static class AvaloniaSliderReset
{
    /// <summary>A double-click sets the slider to <paramref name="defaultValue"/> and starts no drag; <paramref name="beforeReset"/> runs first (an undo step, say).</summary>
    public static void OnDoubleTap(Slider slider, double defaultValue, Action? beforeReset = null) =>
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed) return;
            beforeReset?.Invoke();
            slider.Value = defaultValue;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
}
