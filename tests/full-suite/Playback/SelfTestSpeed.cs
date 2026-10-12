using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TabForge;

/// <summary>The single playback speed box in the View and tempo pane (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>The zoom and speed controls sit in the View and tempo pane and change the score zoom and the playback speed.</summary>
    private static void TestToolbarZoomAndSpeed() => RunInWindowFixture((window, _) =>
    {
        window.UpdateLayout();
        var toolbar = window.ViewTempoPanelContent;
        Check("zoom and speed boxes are in the View and tempo pane",
            window.ZoomCombo.IsDescendantOf(toolbar) && window.SpeedCombo.IsDescendantOf(toolbar));
        var zoomIn = FindNamedButton(toolbar, "Zoom in");
        var zoomOut = FindNamedButton(toolbar, "Zoom out");
        Check("zoom in and out buttons are in the View and tempo pane", zoomIn is not null && zoomOut is not null);
        if (zoomIn is null || zoomOut is null) return;

        window.ZoomCombo.SelectedIndex = 6;   // 100%
        zoomIn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var afterIn = ParseLeadingPercent(window.ZoomCombo.Text);
        zoomOut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        zoomOut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var afterOut = ParseLeadingPercent(window.ZoomCombo.Text);
        Check("zoom in raises and zoom out lowers the zoom box",
            afterIn > 100 && afterOut < afterIn);

        window.SpeedCombo.SelectedIndex = 3;   // 125%
        Check("the speed box shows the chosen speed", window.SpeedCombo.Text == "125%");

        // The bar's buttons drive those controls: a menu row applies the value and the caption follows.
        var bar = FindViewTempoBar(toolbar);
        Check("the View and tempo pane holds the four-button bar with its host", bar?.Host is not null);
        if (bar?.Host is null) return;
        var transport = LtField<Controllers.TransportControlsController>(window, "_transport")!;
        void Pick(int kind, string header) => bar!.BuildMenu(kind)!.Items.OfType<MenuItem>().First(i => (string)i.Header == header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pick(Views.ViewTempo.ViewTempoBar.Speed, "75%");
        Check($"choosing 75% on the speed button sets the playback speed (speed {transport.Speed}, caption {bar.Caption(1)})",
            Math.Abs(transport.Speed - 0.75) < 1e-9 && bar.Caption(1) == "75%" && bar.Caption(1) != window.TempoBox.Text);
        Check("the speed menu checks the current speed", bar.BuildMenu(1)!.Items.OfType<MenuItem>().Single(i => i.IsChecked).Header as string == "75%");
        bar.Host.SetSpeedPercent(85);
        Check($"a speed between presets applies (speed {transport.Speed}, caption {bar.Caption(1)})", Math.Abs(transport.Speed - 0.85) < 1e-9 && bar.Caption(1) == "85%");
        Pick(Views.ViewTempo.ViewTempoBar.Speed, "100%");
        Check("choosing 100% restores the speed", Math.Abs(transport.Speed - 1.0) < 1e-9 && bar.Caption(1) == "100%");

        var zoom = LtField<Views.ScoreZoomController>(window, "_scoreZoom")!;
        Pick(Views.ViewTempo.ViewTempoBar.Zoom, "125%");
        Check($"choosing 125% on the zoom button sets the score zoom (factor {zoom.Factor}, caption {bar.Caption(0)})", Math.Abs(zoom.Factor - 1.25) < 1e-9 && bar.Caption(0) == "125%");
        Pick(Views.ViewTempo.ViewTempoBar.Zoom, "Fit width");
        Check($"choosing Fit width shows Fit (factor {zoom.Factor}, caption {bar.Caption(0)})", zoom.Factor <= 0 && bar.Caption(0) == "Fit");
        Check("the zoom menu's Zoom in and Zoom out rows reach their commands, as do the signature rows",
            bar.Host is Views.ViewTempo.ViewTempoControlsHost found && new[] { "View.ZoomIn", "View.ZoomOut", "Bar.TimeSignature", "Bar.KeySignature" }.All(id => found.CommandSource(id) is not null));

        bar.Host.SetTempo(180);
        Check($"setting 180 through the bar sets the song tempo (box {window.TempoBox.Text}, caption {bar.Caption(2)})", window.TempoBox.Text == "180" && bar.Caption(2) == "180");
        Check("the tempo menu checks the current tempo", bar.BuildMenu(2)!.Items.OfType<MenuItem>().Single(i => i.IsChecked).Header as string == "180");

        window.KeyLabelText.Text = "Am";
        Check($"the time / key button shows the short key and its tooltip the full key (caption {bar.Caption(3)}, tooltip {bar.Buttons[3].ToolTip})",
            bar.Caption(3) == window.TimeSigLabel.Text + " Am" && (bar.Buttons[3].ToolTip as string ?? "").StartsWith($"Time signature and key: {window.TimeSigLabel.Text}, A minor", StringComparison.Ordinal));
        Check("each button has a tooltip", bar.Buttons.All(b => b.ToolTip is string { Length: > 0 }));
    });

    private static Views.ViewTempo.ViewTempoBar? FindViewTempoBar(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Views.ViewTempo.ViewTempoBar bar) return bar;
            if (FindViewTempoBar(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>At every window width the menu bar fits beside the icon buttons and the View and tempo pane shows its controls whole.</summary>
    private static void TestToolbarZoomSpeedNarrow() => RunInWindowFixture((window, _) =>
    {
        var fit = new Views.ToolbarZoomSpeedFit(window.MainToolbar, window.MainMenu, window.PinnedToolStrip, window.ToolbarTempoGroup);
        window.MinWidth = 0;   // the 1080 px minimum at 125% UI scale leaves 864 px of layout
        foreach (var width in new[] { 864.0, 900, 1000, 1080, 1100, 1280 })
        {
            window.Width = width;
            window.UpdateLayout();
            fit.Update(window.MainToolbar.ActualWidth);
            window.UpdateLayout();
            var tempoLeft = window.ToolbarTempoGroup.TranslatePoint(new Point(0, 0), window.MainToolbar).X;
            var menuRight = window.MainMenu.TranslatePoint(new Point(window.MainMenu.ActualWidth, 0), window.MainToolbar).X;
            Check($"{width} px: the menu bar ends left of the icon buttons (step {fit.Step}, room {fit.LastFit.Room:0}, needed {fit.LastFit.Needed:0}, menu ends {menuRight:0}, buttons start {tempoLeft:0})", menuRight <= tempoLeft + 1);
            var pane = window.ViewTempoPanelContent;
            Check($"{width} px: zoom, speed, tempo and the time / key readouts are whole inside the View and tempo pane",
                new FrameworkElement[] { window.ZoomCombo, FindNamedButton(pane, "Zoom in")!, window.SpeedCombo, window.TempoBox, window.TimeSigLabel, window.KeyLabelText }.All(e =>
                    e.IsVisible && !e.IsDescendantOf(window.MainToolbar) && e.TranslatePoint(new Point(e.ActualWidth, 0), pane).X <= pane.ActualWidth + 0.5));
            Check($"{width} px: Record video stays in the title bar left of Settings",
                window.RecordVideoButton.IsVisible && window.RecordVideoButton.IsDescendantOf(window.TitleBar) && !window.RecordVideoButton.IsDescendantOf(window.MainToolbar) &&
                window.RecordVideoButton.TranslatePoint(new Point(0, 0), window.TitleBar).X < window.TitleSettingsButton.TranslatePoint(new Point(0, 0), window.TitleBar).X);
            if (window.ZoomCombo.IsVisible)
            {
                var combo = window.ZoomCombo;
                var text = new FormattedText("Fit width", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight, combo.FontStretch), combo.FontSize, Brushes.Black, 1.0);
                Check($"{width} px: the zoom box shows \"Fit width\" whole (box {combo.ActualWidth:0}, text {text.Width:0} + padding)", combo.ActualWidth >= text.Width + 6);
            }
        }
        // The four buttons share one row at a 250 px content width (the pane's 4 px padding on each side), equal and whole.
        var pills = window.ViewTempoPanelContent;
        var savedPaneWidth = pills.Width;
        pills.Width = 258;
        window.UpdateLayout();
        var bar = FindViewTempoBar(pills)!;
        var tops = bar.Buttons.Select(b => b.TranslatePoint(new Point(0, 0), pills).Y).ToArray();
        var widths = bar.Buttons.Select(b => b.ActualWidth).ToArray();
        var whole = bar.Buttons.All(b => b.Content is FrameworkElement c && c.DesiredSize.Width <= b.ActualWidth - 2 &&
            b.TranslatePoint(new Point(b.ActualWidth, 0), pills).X <= pills.ActualWidth - 4 + 0.5 && b.TranslatePoint(new Point(0, 0), pills).X >= 4 - 0.5);
        Check($"250 px content: the four buttons share one row, equal and whole (tops {string.Join(", ", tops.Select(y => y.ToString("0")))}; widths {string.Join(", ", widths.Select(x => x.ToString("0.#")))}; content {string.Join(", ", bar.Buttons.Select(b => ((FrameworkElement)b.Content).DesiredSize.Width.ToString("0")))})",
            tops.Max() - tops.Min() <= 1 && widths.Max() - widths.Min() <= 1 && widths.Min() >= 50 && whole);
        Check("the key caption shows the tonic, with m for minor (C, Am, F♯m, B♭)",
            Services.KeyNames.Short(0, false) == "C" && Services.KeyNames.Short(0, true) == "Am" &&
            Services.KeyNames.Short(3, true) == "F♯m" && Services.KeyNames.Short(-2, false) == "B♭");
        pills.Width = savedPaneWidth;
        window.UpdateLayout();
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
