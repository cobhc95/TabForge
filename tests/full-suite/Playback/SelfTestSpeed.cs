using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TabForge;

/// <summary>The single playback speed box in the top toolbar (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>The zoom and speed controls sit in the top toolbar and change the score zoom and the playback speed.</summary>
    private static void TestToolbarZoomAndSpeed() => RunInWindowFixture((window, _) =>
    {
        window.UpdateLayout();
        var toolbar = window.MainToolbar;
        Check("zoom and speed boxes are in the top toolbar",
            window.ZoomCombo.IsDescendantOf(toolbar) && window.SpeedCombo.IsDescendantOf(toolbar));
        var zoomIn = FindNamedButton(toolbar, "Zoom in");
        var zoomOut = FindNamedButton(toolbar, "Zoom out");
        Check("zoom in and out buttons are in the top toolbar", zoomIn is not null && zoomOut is not null);
        if (zoomIn is null || zoomOut is null) return;

        window.ZoomCombo.SelectedIndex = 6;   // 100%
        zoomIn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var afterIn = ParseLeadingPercent(window.ZoomCombo.Text);
        zoomOut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        zoomOut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var afterOut = ParseLeadingPercent(window.ZoomCombo.Text);
        Check("zoom in raises and zoom out lowers the toolbar zoom box",
            afterIn > 100 && afterOut < afterIn);

        window.SpeedCombo.SelectedIndex = 3;   // 125%
        Check("the toolbar speed box shows the chosen speed", window.SpeedCombo.Text == "125%");
    });

    /// <summary>A narrow window sheds the zoom / speed group in steps: zoom out / in stay visible at 900 px, and 1280 px shows the speed box.</summary>
    private static void TestToolbarZoomSpeedNarrow() => RunInWindowFixture((window, _) =>
    {
        var fit = new Views.ToolbarZoomSpeedFit(window.MainToolbar, window.MainMenu, window.PinnedToolStrip, window.ToolbarTempoGroup, window.ZoomSpeedGroup);
        window.MinWidth = 0;   // the 1080 px minimum at 125% UI scale leaves 864 px of layout
        foreach (var width in new[] { 864.0, 900, 1000, 1080, 1100, 1280 })
        {
            window.Width = width;
            window.UpdateLayout();
            fit.Update(window.MainToolbar.ActualWidth);
            window.UpdateLayout();
            var tempoLeft = window.ToolbarTempoGroup.TranslatePoint(new Point(0, 0), window.MainToolbar).X;
            var groupRight = window.ZoomSpeedGroup.TranslatePoint(new Point(window.ZoomSpeedGroup.ActualWidth, 0), window.MainToolbar).X;
            var menuRight = window.MainMenu.TranslatePoint(new Point(window.MainMenu.ActualWidth, 0), window.MainToolbar).X;
            Check($"{width} px: zoom out / in stay in the toolbar (step {fit.Step}, room {fit.LastFit.Room:0}, needed {fit.LastFit.Needed:0}, menu ends {menuRight:0}, group ends {groupRight:0}, tempo starts {tempoLeft:0})",
                window.ZoomSpeedGroup.IsVisible && FindNamedButton(window.ZoomSpeedGroup, "Zoom in")?.IsVisible == true && groupRight <= tempoLeft + 1);
            Check($"{width} px: Record video stays in the title bar left of Settings",
                window.RecordVideoButton.IsVisible && window.RecordVideoButton.IsDescendantOf(window.TitleBar) && !window.RecordVideoButton.IsDescendantOf(window.MainToolbar) &&
                window.RecordVideoButton.TranslatePoint(new Point(0, 0), window.TitleBar).X < window.TitleSettingsButton.TranslatePoint(new Point(0, 0), window.TitleBar).X);
            if (width >= 1280) Check("1280 px shows the speed box and its label", window.SpeedCombo.IsVisible && window.ZoomSpeedGroup.Children.OfType<TextBlock>().Single().IsVisible);
            if (width <= 864) Check("864 px hides the time and key signature readouts", !window.TimeSigLabel.IsVisible && !window.KeyLabelText.IsVisible);
            if (window.ZoomCombo.IsVisible)
            {
                var combo = window.ZoomCombo;
                var text = new FormattedText("Fit width", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight, combo.FontStretch), combo.FontSize, Brushes.Black, 1.0);
                Check($"{width} px: the zoom box shows \"Fit width\" whole (box {combo.ActualWidth:0}, text {text.Width:0} + arrow)", combo.ActualWidth >= text.Width + 26);
            }
        }
    });

    private static Button? FindNamedButton(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && System.Windows.Automation.AutomationProperties.GetName(button) == name) return button;
            if (FindNamedButton(child, name) is { } found) return found;
        }
        return null;
    }

    private static double ParseLeadingPercent(string text)
        => double.TryParse(text.TrimEnd('%', ' '), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

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
