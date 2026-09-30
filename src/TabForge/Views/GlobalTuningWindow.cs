using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace TabForge.Views;

/// <summary>
/// Global tuning for every pitched track at once: pick a preset or set each string of a six-string
/// reference (high to low). Other string counts follow by aligning from the lowest string (a bass takes
/// the four lowest strings' changes; extra low strings on 7/8-strings follow the lowest string).
/// </summary>
public static class GlobalTuningWindow
{
    public static readonly int[] StandardE = { 64, 59, 55, 50, 45, 40 };

    /// <summary>Returns the chosen six-string tuning (high to low, MIDI), or null when cancelled.</summary>
    public static int[]? Show(Window? owner, int[] current)
    {
        var tuning = (int[])current.Clone();
        var w = new Window
        {
            Title = "Global tuning (all instruments)", Width = 640, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Owner = owner, ShowInTaskbar = false,
        };
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = "Retunes every instrument track. Fret numbers stay; the sounding pitch of each string moves.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.8,
        });

        var presetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        presetRow.Children.Add(new TextBlock { Text = "Preset", Width = 60, VerticalAlignment = VerticalAlignment.Center });
        var presets = new ComboBox { IsEditable = false };
        var presetList = TrackPropertiesWindow.SixStringPresets.ToList();
        foreach (var (name, _) in presetList) presets.Items.Add(name);
        presets.Items.Add("Custom");
        presetRow.Children.Add(presets);
        root.Children.Add(presetRow);

        var strings = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        // Same look as the Track properties tuning editor: number, the drawn string, − / note pill / +.
        foreach (var width in new[] { 26.0, 1, 34, 64, 34, 120 })
            strings.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        var noteLabels = new TextBlock[6];
        var stringLines = new System.Windows.Shapes.Rectangle[6];
        var offsetLabels = new TextBlock[6];
        var syncing = false;

        void Refresh()
        {
            syncing = true;
            for (var s = 0; s < 6; s++)
            {
                noteLabels[s].Text = TrackPropertiesWindow.NoteName(tuning[s]);
                // Thicker for lower pitches, like real wound strings.
                var thickness = Math.Clamp(1 + (70 - tuning[s]) / 9.0, 1, 5);
                stringLines[s].Height = thickness;
                stringLines[s].RadiusX = stringLines[s].RadiusY = thickness / 2;
                var d = tuning[s] - StandardE[s];
                offsetLabels[s].Text = d == 0 ? "standard" : d > 0 ? $"+{d} from standard" : $"{d} from standard";
            }
            var match = presetList.FindIndex(p => p.HighToLow.SequenceEqual(tuning));
            presets.SelectedIndex = match >= 0 ? match : presetList.Count;
            syncing = false;
        }

        Button Step(string text, Action action)
        {
            var b = new Button { Content = text, Width = 28, Height = 24, Margin = new Thickness(2) };
            b.Click += (_, _) => { action(); Refresh(); };
            return b;
        }

        for (var s = 0; s < 6; s++)
        {
            var index = s;
            strings.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            var label = new TextBlock { Text = (s + 1).ToString(), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
            stringLines[s] = new System.Windows.Shapes.Rectangle
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                Fill = new LinearGradientBrush(Color.FromRgb(0xE8, 0xE0, 0xC8), Color.FromRgb(0x9A, 0x8C, 0x6A), 90)
            };
            var down = Step("−", () => tuning[index] = Math.Max(0, tuning[index] - 1));
            noteLabels[s] = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                Width = 56, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1C)), BorderThickness = new Thickness(1),
                Child = noteLabels[s],
            };
            pill.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var up = Step("+", () => tuning[index] = Math.Min(127, tuning[index] + 1));
            offsetLabels[s] = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7 };
            UIElement[] cells = { label, stringLines[s], down, pill, up, offsetLabels[s] };
            for (var c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], s);
                Grid.SetColumn(cells[c], c);
                strings.Children.Add(cells[c]);
            }
        }
        root.Children.Add(strings);

        var shiftRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        shiftRow.Children.Add(new TextBlock { Text = "All strings", Width = 70, VerticalAlignment = VerticalAlignment.Center });
        shiftRow.Children.Add(Step("−", () => { for (var s = 0; s < 6; s++) tuning[s] = Math.Max(0, tuning[s] - 1); }));
        shiftRow.Children.Add(Step("+", () => { for (var s = 0; s < 6; s++) tuning[s] = Math.Min(127, tuning[s] + 1); }));
        var reset = new Button { Content = "Standard E", Margin = new Thickness(12, 2, 0, 2), Padding = new Thickness(8, 0, 8, 0) };
        reset.Click += (_, _) => { tuning = (int[])StandardE.Clone(); Refresh(); };
        shiftRow.Children.Add(reset);
        root.Children.Add(shiftRow);

        var manualRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        manualRow.Children.Add(new TextBlock { Text = "Shift all by (semitones)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var shiftBox = new TextBox { Width = 56, Text = "0", VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
        var shiftApply = new Button { Content = "Shift", Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 2, 0, 2) };
        void ApplyManualShift()
        {
            if (!int.TryParse(shiftBox.Text.Trim().TrimStart('+'), out var n)) return;
            n = Math.Clamp(n, -24, 24);
            for (var s = 0; s < 6; s++) tuning[s] = Math.Clamp(tuning[s] + n, 0, 127);
            shiftBox.Text = "0";
            Refresh();
        }
        shiftApply.Click += (_, _) => ApplyManualShift();
        shiftBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyManualShift(); e.Handled = true; } };
        manualRow.Children.Add(shiftBox);
        manualRow.Children.Add(shiftApply);
        root.Children.Add(manualRow);

        presets.SelectionChanged += (_, _) =>
        {
            if (syncing || presets.SelectedIndex < 0 || presets.SelectedIndex >= presetList.Count) return;
            tuning = (int[])presetList[presets.SelectedIndex].HighToLow.Clone();
            Refresh();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Apply", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        int[]? result = null;
        ok.Click += (_, _) => { result = tuning; w.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        w.Content = root;
        w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) w.DialogResult = false; };
        Refresh();
        return DialogHost.ShowModal(w) == true ? result : null;
    }
}
