using System.Windows;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>Snap settings for clips: changes apply at once.</summary>
public static class SnapSettingsDialog
{
    public static void Show(Window owner, SnapSettings snap, Action changed)
    {
        var w = new Window
        {
            Title = "Snap / grid settings", Owner = owner, Width = 430, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new StackPanel { Margin = new Thickness(16) };

        CheckBox Check(string text, bool value, Action<bool> set, string tip, double left = 0)
        {
            var box = new CheckBox { Content = text, IsChecked = value, ToolTip = tip, Margin = new Thickness(left, 4, 0, 4) };
            box.Click += (_, _) => { set(box.IsChecked == true); changed(); };
            root.Children.Add(box);
            return box;
        }

        Check("Enable snapping (Alt+S)", snap.Enabled, v => snap.Enabled = v, "Clips snap while you move or trim them. Hold Alt while dragging to bypass.");

        var gridRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        gridRow.Children.Add(new TextBlock { Text = "Grid size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var grid = new ComboBox { ItemsSource = SnapSettings.Grids, SelectedItem = snap.Grid, Width = 90, ToolTip = "Grid line spacing: a whole bar, or a note value (1/4 is a beat in 4/4)" };
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is string g) { snap.Grid = g; changed(); } };
        gridRow.Children.Add(grid);
        root.Children.Add(gridRow);

        root.Children.Add(new TextBlock { Text = "Snap to", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        Check("Grid", snap.ToGrid, v => snap.ToGrid = v, "The nearest grid line of the song's bars", 12);
        Check("Grid at any distance", snap.GridAtAnyDistance, v => snap.GridAtAnyDistance = v, "Snap to the grid however far away it is (off: only within the snap distance)", 30);
        Check("Media items (the edges of other clips)", snap.ToItems, v => snap.ToItems = v, "Clip start and end edges, on every track", 12);
        Check("Playhead", snap.ToPlayhead, v => snap.ToPlayhead = v, "The playback cursor", 12);

        var distanceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        distanceRow.Children.Add(new TextBlock { Text = "Snap distance", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var distance = new TextBox { Text = snap.DistancePx.ToString(), Width = 44, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "How near (in pixels) an edge must be to snap to a clip or the playhead" };
        void Commit()
        {
            if (!int.TryParse(distance.Text.Trim(), out var px)) { distance.Text = snap.DistancePx.ToString(); return; }
            snap.DistancePx = Math.Clamp(px, 1, 40);
            distance.Text = snap.DistancePx.ToString();
            changed();
        }
        distance.LostKeyboardFocus += (_, _) => Commit();
        distance.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
        distanceRow.Children.Add(distance);
        distanceRow.Children.Add(new TextBlock { Text = "pixels", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(distanceRow);

        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => w.Close();
        root.Children.Add(close);
        w.Content = root;
        w.Closed += (_, _) => Commit();
        w.ShowDialog();
    }
}
