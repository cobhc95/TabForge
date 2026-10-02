using System.Windows.Controls;

namespace TabForge;

/// <summary>The single playback speed box in the Zoom &amp; speed pane (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestSpeedControl()
    {
        foreach (var (text, expected) in new (string, double?)[]
                 { ("90%", 0.9), ("90", 0.9), ("0.9", 0.9), ("1,25", 1.25), ("150 %", 1.5), ("2x", 2.0), ("abc", null), ("", null) })
        {
            var v = Controllers.TransportControlsController.ParseSpeedText(text);
            Check($"speed text '{text}' parses to {expected?.ToString() ?? "nothing"}",
                expected is null ? v is null : v is { } got && Math.Abs(got - expected.Value) < 1e-9);
        }
        Check("speed clamps to 25..200 %",
            Controllers.TransportControlsController.ClampSpeed(0.1) == 0.25 && Controllers.TransportControlsController.ClampSpeed(5) == 2.0 && Controllers.TransportControlsController.ClampSpeed(0.9) == 0.9 &&
            Controllers.TransportControlsController.ClampSpeed(double.NaN) == 1.0 && Controllers.TransportControlsController.ClampSpeed(Controllers.TransportControlsController.ParseSpeedText("500%")!.Value) == 2.0 &&
            Controllers.TransportControlsController.ClampSpeed(Controllers.TransportControlsController.ParseSpeedText("10%")!.Value) == 0.25);

        var combo = new ComboBox { IsEditable = true };
        foreach (var t in new[] { "50%", "75%", "100%", "125%", "150%", "200%" })
            combo.Items.Add(new ComboBoxItem { Content = t });
        foreach (var (speed, expected) in new[] { (1.0, "100%"), (0.9, "90%"), (0.25, "25%"), (3.0, "200%"), (0.5, "50%") })
        {
            var shown = Controllers.TransportControlsController.ShowSpeedOn(combo, speed);
            var box = combo.Template?.FindName("PART_EditableTextBox", combo) as TextBox;
            Check($"speed box shows {expected} for speed {speed}",
                shown == expected && combo.Text == expected && box is not null && box.Text == expected);
        }
    }
}
