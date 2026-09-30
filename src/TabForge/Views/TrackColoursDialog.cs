using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>
/// Colour tracks by hand: tick tracks (or give a range, e.g. 1 to 4), then click a colour; repeat for other
/// ranges. Changes show at once; <paramref name="changed"/> is called after each (the caller refreshes, and
/// <paramref name="beforeFirstChange"/> captures undo once).
/// </summary>
public static class TrackColoursDialog
{
    public static void Show(Window owner, SongProject project, Action beforeFirstChange, Action changed)
    {
        var w = new Window
        {
            Title = "Colour tracks", Owner = owner, Width = 460, Height = 540, MinHeight = 320, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(14) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "Tick the tracks to colour (or choose a range), then click a colour. Repeat for other tracks." };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        // Range: tracks [from] to [to] -> ticks them.
        var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var from = new ComboBox { Width = 60, ItemsSource = Enumerable.Range(1, project.Tracks.Count).ToList(), SelectedIndex = 0 };
        var to = new ComboBox { Width = 60, ItemsSource = Enumerable.Range(1, project.Tracks.Count).ToList(), SelectedIndex = Math.Max(0, project.Tracks.Count - 1) };
        var tick = new Button { Content = "Select range", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        range.Children.Add(new TextBlock { Text = "Tracks", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        range.Children.Add(from);
        range.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        range.Children.Add(to);
        range.Children.Add(tick);
        DockPanel.SetDock(range, Dock.Top);
        root.Children.Add(range);

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        var palette = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        bottom.Children.Add(palette);
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => w.Close();
        bottom.Children.Add(close);
        root.Children.Add(bottom);

        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, Border Swatch, TrackModel Track)>();
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var track = project.Tracks[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Margin = new Thickness(6, 0, 8, 0), Background = Brush(track.ColorHex) };
            row.Children.Add(box);
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock { Text = $"{i + 1}. {track.Name}", VerticalAlignment = VerticalAlignment.Center });
            list.Children.Add(row);
            boxes.Add((box, swatch, track));
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tick.Click += (_, _) =>
        {
            var a = Math.Min(from.SelectedIndex, to.SelectedIndex); var b = Math.Max(from.SelectedIndex, to.SelectedIndex);
            for (var i = 0; i < boxes.Count; i++) boxes[i].Box.IsChecked = i >= a && i <= b;
        };

        var captured = false;
        foreach (var (name, hex) in ArrangementPanel.TrackColourPalette)
        {
            var button = new Button { Width = 30, Height = 24, Margin = new Thickness(0, 0, 4, 4), ToolTip = $"{name}: colour the ticked tracks", Padding = new Thickness(0) };
            button.Content = new Border { Width = 22, Height = 16, CornerRadius = new CornerRadius(3), Background = Brush(hex) };
            button.Click += (_, _) =>
            {
                var ticked = boxes.Where(b => b.Box.IsChecked == true).ToList();
                if (ticked.Count == 0) return;
                if (!captured) { beforeFirstChange(); captured = true; }
                foreach (var (_, swatch, track) in ticked) { track.ColorHex = hex; swatch.Background = Brush(hex); }
                changed();
            };
            palette.Children.Add(button);
        }
        w.Content = root;
        DialogHost.ShowModal(w);
    }

    private static Brush Brush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { return Brushes.Gray; }
    }
}
