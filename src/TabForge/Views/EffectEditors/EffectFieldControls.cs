using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TabForge.Views.EffectEditors;

// Owns: the two small controls the value-based effect editors share: a number box with - / + and a row of radio choices (text and/or a note symbol).
// Does not own: what a value means (the editor classes), presets (ValuesEffectDialog) or the frame (ThemedEditorDialog).
// Tests: TestOrnamentEditors.
internal sealed class NumberField : StackPanel
{
    private readonly TextBox _box = new() { Width = 46, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Height = 26 };
    private readonly int _min, _max;

    public NumberField(string label, int min, int max, int value, int tab)
    {
        _min = min; _max = max;
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 4, 0, 4);
        Children.Add(new TextBlock { Text = label, Width = 96, VerticalAlignment = VerticalAlignment.Center });
        var down = Step("-", -1);
        var up = Step("+", 1);
        _box.TabIndex = tab;
        _box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Up) { Value++; e.Handled = true; } else if (e.Key == Key.Down) { Value--; e.Handled = true; } };
        _box.LostFocus += (_, _) => Value = Value;
        Children.Add(down); Children.Add(_box); Children.Add(up);
        Value = value;
        System.Windows.Automation.AutomationProperties.SetName(_box, label);
    }

    public int Value
    {
        get => int.TryParse(_box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, _min, _max) : _min;
        set => _box.Text = Math.Clamp(value, _min, _max).ToString(CultureInfo.InvariantCulture);
    }

    public bool Enabled { set { foreach (UIElement child in Children) child.IsEnabled = value; } }

    private Button Step(string text, int delta)
    {
        var b = new Button { Content = text, Width = 26, Height = 26, Margin = new Thickness(4, 0, 4, 0), IsTabStop = false };
        b.Click += (_, _) => Value += delta;
        return b;
    }
}

internal sealed class ChoiceField : StackPanel
{
    private readonly List<(RadioButton Button, int Value)> _items = new();

    public event Action? Changed;

    /// <param name="options">Text, value and an optional tool-icon id (for example "Duration/sixteenth_note") shown beside the text.</param>
    public ChoiceField(string label, (string Text, int Value, string? Icon)[] options, int value, int tab)
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 4, 0, 4);
        Children.Add(new TextBlock { Text = label, Width = 96, VerticalAlignment = VerticalAlignment.Center });
        var group = Guid.NewGuid().ToString("N");
        foreach (var (text, v, icon) in options)
        {
            object content = text;
            if (icon is not null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new SvgIconView { Icon = "tool:" + icon, ShowFrame = false, Width = 20, Height = 20, IsHitTestVisible = false, IconColor = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("TextBrush")).Color });
                row.Children.Add(new TextBlock { Text = " " + text, VerticalAlignment = VerticalAlignment.Center });
                content = row;
            }
            var radio = new RadioButton { Content = content, GroupName = group, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center, TabIndex = tab, ToolTip = text };
            radio.Checked += (_, _) => Changed?.Invoke();
            System.Windows.Automation.AutomationProperties.SetName(radio, label + " " + text);
            Children.Add(radio);
            _items.Add((radio, v));
        }
        Value = value;
    }

    public int Value
    {
        get => _items.FirstOrDefault(i => i.Button.IsChecked == true).Value;
        set
        {
            foreach (var (button, v) in _items) button.IsChecked = v == value;
            if (_items.Count > 0 && _items.All(i => i.Button.IsChecked != true)) _items[0].Button.IsChecked = true;
        }
    }
}
