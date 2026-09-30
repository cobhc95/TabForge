using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>Audio clip properties: name, volume (dB), pitch (semitones) and speed. Changes apply on OK.</summary>
public static class ClipPropertiesDialog
{
    public static bool Show(Window owner, AudioClip clip)
    {
        var w = new Window
        {
            Title = "Audio clip properties", Owner = owner, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new StackPanel { Margin = new Thickness(16) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        root.Children.Add(grid);

        void Row(string label, UIElement editor, UIElement? value = null)
        {
            var r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 8, 5) };
            Grid.SetRow(text, r); grid.Children.Add(text);
            Grid.SetRow(editor, r); Grid.SetColumn(editor, 1); grid.Children.Add(editor);
            if (value is not null) { Grid.SetRow(value, r); Grid.SetColumn(value, 2); grid.Children.Add(value); }
        }

        var name = new TextBox { Text = clip.Name, Margin = new Thickness(0, 4, 0, 4) };
        Row("Name", name);
        Slider Slide(string label, double min, double max, double value, double step, Func<double, string> format, double reset)
        {
            var s = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 4, IsSnapToTickEnabled = true, TickFrequency = step, VerticalAlignment = VerticalAlignment.Center };
            var shown = new TextBlock { Text = format(value), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            s.ValueChanged += (_, _) => shown.Text = format(s.Value);
            s.MouseDoubleClick += (_, _) => s.Value = reset;   // double-click resets, like the knobs
            s.ToolTip = "Double-click to reset";
            Row(label, s, shown);
            return s;
        }
        var gain = Slide("Volume", -36, 12, clip.GainDb, 0.5, v => $"{v:+0.0;-0.0;0.0} dB", 0);
        var pitch = Slide("Pitch", -12, 12, clip.Pitch, 1, v => $"{v:+0;-0;0} st", 0);
        var speed = Slide("Speed", 0.5, 2, clip.Speed, 0.05, v => $"{v.ToString("0.00", CultureInfo.CurrentCulture)}×", 1);
        var muted = new CheckBox { Content = "Muted", IsChecked = clip.Muted, Margin = new Thickness(0, 6, 0, 0) };
        Row("", muted);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 10), Text = $"{clip.File}\nPitch and speed change independently; the file itself is not changed." };
        info.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(info);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 });
        root.Children.Add(buttons);
        w.Content = root;
        if (DialogHost.ShowModal(w) != true) return false;

        clip.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : clip.Name;
        clip.GainDb = gain.Value;
        clip.Pitch = pitch.Value;
        clip.Speed = speed.Value;
        clip.Muted = muted.IsChecked == true;
        return true;
    }
}
