using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Custom drum map: for every GM percussion sound choose the TAB line, the text written on the TAB,
/// the staff position and the notehead. Starts from the track's current mapping.
/// </summary>
public static class DrumMapWindow
{
    public static List<DrumMapEntry>? Show(Window owner, TrackModel track)
    {
        var entries = Enumerable.Range(27, 61).Select(m => DrumMaps.For(track, m).Clone()).ToList();
        var w = new Window
        {
            Title = "Custom drum map", Width = 720, Height = 640, MinWidth = 560, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(14) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = "Choose how each drum sound is written. TAB line 1 is the top line; staff position counts half-spaces down from the top staff line (0 = top line, 7 = kick space, negative = above the staff)." };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var header = new Grid { Margin = new Thickness(0, 0, 18, 4) };
        foreach (var width in new[] { 220.0, 90, 90, 110, 1 })
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        string[] titles = { "Sound", "TAB line", "TAB text", "Staff position", "Notehead" };
        for (var c = 0; c < titles.Length; c++)
        {
            var t = new TextBlock { Text = titles[c], FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(t, c); header.Children.Add(t);
        }
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var basePreset = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Reset every row from a preset" };
        foreach (var p in DrumMaps.Presets.Where(p => p != DrumMaps.Custom)) basePreset.Items.Add(new ComboBoxItem { Content = DrumMaps.DisplayName(p), Tag = p });
        var reset = new Button { Content = "Reset from", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 0, 10, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(20, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(reset); buttons.Children.Add(basePreset); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var list = new StackPanel();
        void Build()
        {
            list.Children.Clear();
            foreach (var entry in entries)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                foreach (var width in new[] { 220.0, 90, 90, 110, 1 })
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
                var name = new TextBlock { Text = $"{entry.Midi} - {InstrumentPanel.PercussionNames[entry.Midi - 27]}", VerticalAlignment = VerticalAlignment.Center };
                var line = new ComboBox { Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
                for (var l = 1; l <= 8; l++) line.Items.Add(l);
                line.SelectedItem = entry.TabLine + 1;
                line.SelectionChanged += (_, _) => { if (line.SelectedItem is int v) entry.TabLine = v - 1; };
                var text = new TextBox { Text = entry.Label, Width = 70, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 4 };
                text.TextChanged += (_, _) => entry.Label = text.Text.Trim();
                var step = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
                for (var s = -4; s <= 10; s++) step.Items.Add(s);
                step.SelectedItem = Math.Clamp(entry.StaffStep, -4, 10);
                step.SelectionChanged += (_, _) => { if (step.SelectedItem is int v) entry.StaffStep = v; };
                var head = new ComboBox { Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var h in new[] { "normal", "x", "circle", "diamond" }) head.Items.Add(h);
                head.SelectedItem = entry.Head;
                head.SelectionChanged += (_, _) => { if (head.SelectedItem is string v) entry.Head = v; };
                UIElement[] cells = { name, line, text, step, head };
                for (var c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); row.Children.Add(cells[c]); }
                list.Children.Add(row);
            }
        }
        Build();
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        reset.Click += (_, _) =>
        {
            if ((basePreset.SelectedItem as ComboBoxItem)?.Tag is not string preset) return;
            entries = Enumerable.Range(27, 61).Select(m => DrumMaps.Default(preset, m)).ToList();
            Build();
        };
        List<DrumMapEntry>? result = null;
        ok.Click += (_, _) => { result = entries; w.DialogResult = true; };
        w.Content = root;
        return DialogHost.ShowModal(w) == true ? result : null;
    }
}
