using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// Compact colour chooser used outside the settings window: a preset dropdown (the same named colours as
/// Settings), a swatch grid, hue / saturation / brightness sliders and a hex box, with a live preview.
/// </summary>
public static class ColourChooser
{
    public static Color? Show(Window? owner, string title, Color initial, Color? defaultColour = null, Action<Color>? preview = null)
    {
        var color = initial;
        var w = new Window
        {
            Title = title, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("WindowBrush"), Foreground = (Brush)Application.Current.FindResource("TextBrush"),
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        var previewBox = new Border { Height = 40, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x60, 0x6B)), Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(previewBox);

        var presets = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        var list = new List<(string Name, string Hex)>();
        if (defaultColour is { } d) list.Add(("Default", Hex(d)));
        list.AddRange(PreferencesWindow.ColourPresets);
        foreach (var (name, hex) in list)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = hex };
            row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Parse(hex)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            presets.Items.Add(row);
        }
        root.Children.Add(presets);

        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(swatches);

        Slider MakeSlider(double max) => new() { Minimum = 0, Maximum = max, Width = 250, Margin = new Thickness(0, 2, 0, 2) };
        var hue = MakeSlider(359); var sat = MakeSlider(100); var val = MakeSlider(100);
        UIElement Row(string label, Slider slider)
        {
            var p = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            p.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(slider);
            return p;
        }
        root.Children.Add(Row("Hue", hue));
        root.Children.Add(Row("Saturation", sat));
        root.Children.Add(Row("Brightness", val));
        var hexRow = new DockPanel { Margin = new Thickness(0, 6, 0, 12) };
        hexRow.Children.Add(new TextBlock { Text = "Hex", Width = 80, VerticalAlignment = VerticalAlignment.Center });
        var hexBox = new TextBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        hexRow.Children.Add(hexBox);
        root.Children.Add(hexRow);

        var updating = false;
        void SetColor(Color c, bool fromSliders = false)
        {
            color = c;
            updating = true;
            previewBox.Background = new SolidColorBrush(c);
            if (!fromSliders)
            {
                var (h, s, v) = ToHsv(c);
                hue.Value = h; sat.Value = s * 100; val.Value = v * 100;
            }
            hexBox.Text = Hex(c);
            var match = presets.Items.OfType<FrameworkElement>().FirstOrDefault(i => string.Equals((string)i.Tag, Hex(c), StringComparison.OrdinalIgnoreCase));
            presets.SelectedItem = match;
            updating = false;
            preview?.Invoke(c);
        }

        foreach (var (name, hex) in list)
        {
            var b = new Button { Width = 26, Height = 22, Margin = new Thickness(2), Background = new SolidColorBrush(Parse(hex)), ToolTip = name,
                BorderThickness = new Thickness(1) };
            b.Click += (_, _) => SetColor(Parse(hex));
            swatches.Children.Add(b);
        }
        presets.SelectionChanged += (_, _) => { if (!updating && presets.SelectedItem is FrameworkElement { Tag: string hex }) SetColor(Parse(hex)); };
        void FromSliders(object? s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updating) return;
            SetColor(FromHsv(hue.Value, sat.Value / 100, val.Value / 100), fromSliders: true);
        }
        hue.ValueChanged += FromSliders; sat.ValueChanged += FromSliders; val.ValueChanged += FromSliders;
        hexBox.LostFocus += (_, _) => { if (!updating && TryParse(hexBox.Text, out var c)) SetColor(c); };
        hexBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && TryParse(hexBox.Text, out var c)) SetColor(c); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        w.Content = root;
        SetColor(initial);
        var accepted = DialogHost.ShowModal(w) == true;
        if (!accepted) preview?.Invoke(initial);
        return accepted ? color : null;
    }

    public static string Hex(Color c) => TabForge.Visualization.ColourText.HexAuto(c);

    /// <summary>Hex colour as typed in the chooser; <paramref name="color"/> is white when invalid.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        if (TabForge.Visualization.ColourText.TryParseHex(text, out color)) return true;
        color = Colors.White;
        return false;
    }

    private static Color Parse(string hex) => TryParse(hex, out var c) ? c : Colors.White;

    private static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var d = max - min;
        var h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
