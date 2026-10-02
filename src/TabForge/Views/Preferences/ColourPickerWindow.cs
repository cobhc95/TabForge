using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>The full colour picker opened from a colour row: hue, shade, optional alpha, hex code, recent colours and the default.</summary>
internal sealed class ColourPickerWindow : Window
{
    private readonly string _default;
    private readonly Slider _hue = new() { Minimum = 0, Maximum = 359, TickFrequency = 1 };
    private readonly Slider _saturation = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
    private readonly Slider _value = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
    private readonly Slider _alpha = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
    private readonly CheckBox _useAlpha = new() { Content = "Include alpha" };
    private readonly Border _preview = new() { Height = 44, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
    private readonly TextBox _hex = new() { Width = 130 };
    private readonly Window _owner;
    private bool _updating;
    private Color _color;

    public string? SelectedColour { get; private set; }

    public ColourPickerWindow(string initial, string defaultValue, IReadOnlyList<string> recent, Window owner)
    {
        _default = defaultValue;
        _owner = owner;
        _color = TryParse(initial, out var parsed) ? parsed : Colors.White;
        Width = 490;
        Height = 630;
        MinWidth = 490;
        MinHeight = 630;
        MaxWidth = 490;
        MaxHeight = 630;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;
        Title = "Choose colour";
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        Foreground = (Brush)Application.Current.FindResource("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        foreach (var key in owner.Resources.Keys)
            Resources[key] = owner.Resources[key];

        _hex.Text = ColourHex(_color);
        _useAlpha.IsChecked = _color.A != byte.MaxValue;
        _alpha.Value = _color.A * 100.0 / 255;
        LoadHsv(_color);

        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = "Colour", FontSize = 20, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10) });
        _preview.Margin = new Thickness(0, 0, 0, 12);
        content.Children.Add(_preview);

        var recentPanel = new WrapPanel();
        foreach (var item in recent.Where(value => TryParse(value, out _)).Take(12))
        {
            var recentButton = new Button
            {
                Style = (Style)owner.FindResource("ColourSwatchButton"),
                Width = 30,
                Height = 28,
                Margin = new Thickness(2),
                Background = SafeBrush(item)
            };
            ToolTipService.SetToolTip(recentButton, item);
            recentButton.Click += (_, _) => SetColor(Parse(item));
            recentPanel.Children.Add(recentButton);
        }
        content.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 8), Children =
            { new TextBlock { Text = "Recent colours", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }, recentPanel } });
        content.Children.Add(SliderRow("Hue", _hue));
        content.Children.Add(SliderRow("Saturation", _saturation));
        content.Children.Add(SliderRow("Brightness", _value));
        var alphaRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4) };
        alphaRow.Children.Add(_useAlpha);
        _alpha.Width = 250;
        _alpha.Margin = new Thickness(12, 0, 0, 0);
        alphaRow.Children.Add(_alpha);
        content.Children.Add(alphaRow);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12) };
        hexRow.Children.Add(new TextBlock { Text = "Hex", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _hex.VerticalContentAlignment = VerticalAlignment.Center;
        hexRow.Children.Add(_hex);
        content.Children.Add(hexRow);

        var cancel = DialogButton("Cancel", "SecondaryActionButton");
        var reset = DialogButton("Default", "SecondaryActionButton");
        var use = DialogButton("Use colour", "PrimaryActionButton");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        reset.Margin = new Thickness(0, 0, 8, 0);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(reset);
        actions.Children.Add(cancel);
        actions.Children.Add(use);
        content.Children.Add(actions);
        Content = content;

        _hue.ValueChanged += (_, _) => UpdateFromHsv();
        _saturation.ValueChanged += (_, _) => UpdateFromHsv();
        _value.ValueChanged += (_, _) => UpdateFromHsv();
        _alpha.ValueChanged += (_, _) => UpdateFromHsv();
        _useAlpha.Checked += (_, _) => UpdateFromHsv();
        _useAlpha.Unchecked += (_, _) => UpdateFromHsv();
        _hex.TextChanged += (_, _) =>
        {
            if (_updating || !TryParse(_hex.Text, out var value)) return;
            _updating = true;
            _color = value;
            _useAlpha.IsChecked = value.A != byte.MaxValue;
            _alpha.Value = value.A * 100.0 / 255;
            LoadHsv(value);
            _hex.Text = ColourHex(value);
            _updating = false;
            UpdatePreview();
        };
        cancel.Click += (_, _) => { SelectedColour = null; DialogResult = false; };
        reset.Click += (_, _) => { SelectedColour = _default; DialogResult = true; };
        use.Click += (_, _) => { SelectedColour = ColourHex(_color, _useAlpha.IsChecked == true); DialogResult = true; };
        UpdatePreview();
    }

    private Button DialogButton(string text, string key)
    {
        var button = new Button { Content = text, Style = (Style)_owner.FindResource(key) };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static FrameworkElement SliderRow(string label, Slider slider)
    {
        slider.Margin = new Thickness(0, 3, 0, 3);
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        slider.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        return grid;
    }

    private void UpdateFromHsv()
    {
        if (_updating) return;
        _color = HsvColor(_hue.Value, _saturation.Value / 100, _value.Value / 100,
            _useAlpha.IsChecked == true ? (byte)Math.Round(_alpha.Value * 255 / 100) : byte.MaxValue);
        _updating = true;
        _hex.Text = ColourHex(_color, _useAlpha.IsChecked == true);
        _updating = false;
        UpdatePreview();
    }

    private void SetColor(Color color)
    {
        _updating = true;
        _color = color;
        _useAlpha.IsChecked = color.A != byte.MaxValue;
        _alpha.Value = color.A * 100.0 / 255;
        LoadHsv(color);
        _hex.Text = ColourHex(color);
        _updating = false;
        UpdatePreview();
    }

    private void LoadHsv(Color color)
    {
        var red = color.R / 255.0;
        var green = color.G / 255.0;
        var blue = color.B / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var hue = delta == 0 ? 0 : maximum == red ? 60 * (((green - blue) / delta) % 6) :
            maximum == green ? 60 * ((blue - red) / delta + 2) : 60 * ((red - green) / delta + 4);
        if (hue < 0) hue += 360;
        var wasUpdating = _updating;
        _updating = true;
        _hue.Value = hue;
        _saturation.Value = maximum == 0 ? 0 : delta / maximum * 100;
        _value.Value = maximum * 100;
        _updating = wasUpdating;
    }

    private void UpdatePreview()
    {
        _preview.Background = new SolidColorBrush(_color);
        _preview.BorderBrush = Brush("#53606B");
    }

    private static bool TryParse(string? text, out Color color) => TabForge.Visualization.ColourText.TryParseSetting(text, out color);

    private static Color Parse(string value) => TabForge.Visualization.ColourText.ParseOr(value, Colors.Transparent);
    private static Brush SafeBrush(string value) => TryParse(value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
    private static Brush Brush(string value) => TabForge.Visualization.ColourText.BrushOr(value);

    private static Color HsvColor(double hue, double saturation, double value, byte alpha)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0.0), < 120 => (x, chroma, 0.0), < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma), < 300 => (x, 0.0, chroma), _ => (chroma, 0.0, x)
        };
        return Color.FromArgb(alpha, ToByte(red + m), ToByte(green + m), ToByte(blue + m));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    private static string ColourHex(Color color, bool includeAlpha = false) => TabForge.Visualization.ColourText.Hex(color, includeAlpha);
}
